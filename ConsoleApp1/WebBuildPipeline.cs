using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConsoleApp1.Compiler;

namespace ConsoleApp1;

sealed class WebBuildResult
{
    public string OutputDirectory { get; }
    public string IndexHtmlPath { get; }
    public string? BytecodePath { get; }

    public WebBuildResult(string outputDirectory, string indexHtmlPath, string? bytecodePath)
    {
        OutputDirectory = outputDirectory;
        IndexHtmlPath = indexHtmlPath;
        BytecodePath = bytecodePath;
    }
}

internal static class WebBuildPipeline
{
    private const int VirtualWidth = 640;
    private const int VirtualHeight = 360;

    public static WebBuildResult Build(
        string sourcePath,
        string? outputDirectory,
        bool traceLinker = false,
        Action<string>? traceWriter = null,
        bool emitWebBytecode = false,
        bool directWasmBackend = false)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        var options = new ModuleCompileOptions
        {
            Target = CompileTarget.VmWeb,
            TraceLinker = traceLinker,
            TraceWriter = traceWriter,
            EnableGraphicalAppProfile = true,
            EnableImpliedEngineImports = true,
            EmitDirectWasm = directWasmBackend
        };

        var result = ModuleCompiler.CompileFromFileWithMetadata(fullSourcePath, options);
        if (result.WebScene is null)
        {
            throw new CompilerException(
                "Web build requires either an explicit object 'MainScene' with a zero-argument constructor and zero-argument start(), update(), and draw() methods, or a top-level web app entry with start(), update(), and draw() functions.",
                1,
                1);
        }

        var manifest = PackageManifestLoader.TryLoadNearest(fullSourcePath, CompileTarget.VmWeb);
        string resolvedOutputDirectory = ResolveOutputDirectory(fullSourcePath, outputDirectory, manifest);
        Directory.CreateDirectory(resolvedOutputDirectory);

        string bytecodePath = Path.Combine(resolvedOutputDirectory, "app.bytecode");
        if (emitWebBytecode)
            File.WriteAllBytes(bytecodePath, result.Bytecode);
        else if (File.Exists(bytecodePath))
            File.Delete(bytecodePath);

        CopyAssets(fullSourcePath, resolvedOutputDirectory, manifest);

        string runtimeScript = PrepareRuntimeScriptForInlineModule(File.ReadAllText(ResolveWebRuntimeScriptPath(fullSourcePath)));
        byte[] wasmRuntime = File.ReadAllBytes(ResolveWasmRuntimePath(fullSourcePath));
        File.WriteAllBytes(Path.Combine(resolvedOutputDirectory, "code-runtime.wasm"), wasmRuntime);
        byte[]? appWasm = result.DirectWasm?.Module;
        string appWasmPath = Path.Combine(resolvedOutputDirectory, "code-app.wasm");
        if (directWasmBackend)
            File.WriteAllBytes(appWasmPath, appWasm ?? throw new InvalidOperationException("Direct-Wasm compilation did not produce an application module."));
        else if (File.Exists(appWasmPath))
            File.Delete(appWasmPath);
        string html = BuildIndexHtml(
            manifest?.Name ?? Path.GetFileNameWithoutExtension(fullSourcePath),
            result.WebScene,
            Convert.ToBase64String(result.Bytecode),
            Convert.ToBase64String(wasmRuntime),
            appWasm is null ? null : Convert.ToBase64String(appWasm),
            directWasmBackend,
            runtimeScript);

        string indexHtmlPath = Path.Combine(resolvedOutputDirectory, "index.html");
        WriteUtf8(indexHtmlPath, html);

        return new WebBuildResult(
            resolvedOutputDirectory,
            indexHtmlPath,
            emitWebBytecode ? bytecodePath : null);
    }

    private static string ResolveOutputDirectory(string sourcePath, string? outputDirectory, PackageManifest? manifest)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            return Path.GetFullPath(outputDirectory);

        if (manifest is not null)
            return Path.Combine(manifest.PackageRoot, "dist");

        string? sourceDir = Path.GetDirectoryName(sourcePath);
        return Path.Combine(sourceDir ?? Directory.GetCurrentDirectory(), "dist");
    }

    private static string ResolveWebRuntimeScriptPath(string sourcePath)
    {
        foreach (var candidateRoot in EnumerateCandidateRoots(sourcePath))
        {
            string candidate = Path.Combine(candidateRoot, "web-runtime", "code-vm-web.js");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate web-runtime/code-vm-web.js required for web build.");
    }

    private static string ResolveWasmRuntimePath(string sourcePath)
    {
        foreach (var candidateRoot in EnumerateCandidateRoots(sourcePath))
        {
            string candidate = Path.Combine(candidateRoot, "web-runtime", "code-runtime.wasm");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate web-runtime/code-runtime.wasm required for web build. Build the Rust runtime first.");
    }

    private static IEnumerable<string> EnumerateCandidateRoots(string sourcePath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var start in new[]
        {
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(Path.GetFullPath(sourcePath)),
            AppContext.BaseDirectory
        })
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;

            string? cursor = Path.GetFullPath(start);
            while (!string.IsNullOrWhiteSpace(cursor))
            {
                if (seen.Add(cursor))
                    yield return cursor;
                cursor = Directory.GetParent(cursor)?.FullName;
            }
        }
    }

    private static void CopyAssets(string sourcePath, string outputDirectory, PackageManifest? manifest)
    {
        string assetsSourceDirectory = ResolveAssetsSourceDirectory(sourcePath, manifest);
        if (!Directory.Exists(assetsSourceDirectory))
            return;

        string assetsOutputDirectory = Path.Combine(outputDirectory, "assets");
        foreach (var sourceFile in Directory.GetFiles(assetsSourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(assetsSourceDirectory, sourceFile);
            string destinationPath = Path.Combine(assetsOutputDirectory, relativePath);
            string? destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDir))
                Directory.CreateDirectory(destinationDir);
            File.Copy(sourceFile, destinationPath, overwrite: true);
        }
    }

    private static string ResolveAssetsSourceDirectory(string sourcePath, PackageManifest? manifest)
    {
        if (manifest is not null)
            return Path.Combine(manifest.PackageRoot, "assets");

        string? sourceDir = Path.GetDirectoryName(sourcePath);
        return Path.Combine(sourceDir ?? Directory.GetCurrentDirectory(), "assets");
    }

    private static string PrepareRuntimeScriptForInlineModule(string runtimeScript)
    {
        return Regex.Replace(runtimeScript, @"(?m)^\s*export\s+", string.Empty);
    }

    private static string BuildIndexHtml(
        string title,
        WebSceneMetadata webScene,
        string bytecodeBase64,
        string wasmRuntimeBase64,
        string? appWasmBase64,
        bool directWasmBackend,
        string runtimeScript)
    {
        string pageTitle = string.IsNullOrWhiteSpace(title) ? "Code App" : title.Trim();
        string titleHtml = WebUtility.HtmlEncode(pageTitle);
        string titleJson = JsonSerializer.Serialize(pageTitle);
        string metadataJson = JsonSerializer.Serialize(new
        {
            title = pageTitle,
            virtualWidth = VirtualWidth,
            virtualHeight = VirtualHeight,
            scene = new
            {
                typeName = webScene.SceneTypeName,
                constructor = new { targetIp = webScene.Constructor.TargetIp, frameSize = webScene.Constructor.FrameSize },
                start = new { targetIp = webScene.Start.TargetIp, frameSize = webScene.Start.FrameSize },
                update = new { targetIp = webScene.Update.TargetIp, frameSize = webScene.Update.FrameSize },
                draw = new { targetIp = webScene.Draw.TargetIp, frameSize = webScene.Draw.FrameSize },
                drawHud = webScene.DrawHud is null
                    ? null
                    : new { targetIp = webScene.DrawHud.TargetIp, frameSize = webScene.DrawHud.FrameSize }
            }
        }, new JsonSerializerOptions { WriteIndented = true });
        string bytecodeJson = JsonSerializer.Serialize(bytecodeBase64);
        string wasmRuntimeJson = JsonSerializer.Serialize(wasmRuntimeBase64);
        string appWasmJson = JsonSerializer.Serialize(appWasmBase64 ?? string.Empty);
        string backendJson = JsonSerializer.Serialize(directWasmBackend ? "direct-wasm" : "wasm-vm");
        string workerRuntimeJson = JsonSerializer.Serialize(runtimeScript + "\ninstallCodeWorkerRuntime();\n");

        return
$$"""
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>{{titleHtml}}</title>
  </head>
  <body>
    <script type="module">
const APP_TITLE = {{titleJson}};
const APP_METADATA = {{metadataJson}};
const APP_BYTECODE_BASE64 = {{bytecodeJson}};
const CODE_RUNTIME_WASM_BASE64 = {{wasmRuntimeJson}};
const CODE_APP_WASM_BASE64 = {{appWasmJson}};
const CODE_WEB_BACKEND = {{backendJson}};
const CODE_WORKER_SOURCE = {{workerRuntimeJson}};

{{runtimeScript}}

const runtime = new CanvasSceneRuntime({
  width: APP_METADATA.virtualWidth,
  height: APP_METADATA.virtualHeight,
  title: APP_TITLE
});

runtime.attach(document.body);

try {
  const bytecode = decodeBase64Bytes(APP_BYTECODE_BASE64);
  let wasmBytes;
  try {
    if (window.location.protocol === "file:") throw new Error("Use embedded Wasm for direct-file execution.");
    const response = await fetch("code-runtime.wasm");
    if (!response.ok) throw new Error("Wasm request failed with status " + response.status + ".");
    wasmBytes = new Uint8Array(await response.arrayBuffer());
  } catch {
    wasmBytes = decodeBase64Bytes(CODE_RUNTIME_WASM_BASE64);
  }
  let appWasmBytes = null;
  if (CODE_WEB_BACKEND === "direct-wasm") {
    try {
      if (window.location.protocol === "file:") throw new Error("Use embedded application Wasm for direct-file execution.");
      const response = await fetch("code-app.wasm");
      if (!response.ok) throw new Error("Application Wasm request failed with status " + response.status + ".");
      appWasmBytes = new Uint8Array(await response.arrayBuffer());
    } catch {
      appWasmBytes = decodeBase64Bytes(CODE_APP_WASM_BASE64);
    }
  }
  const profileEnabled = new URLSearchParams(window.location.search).get("code-profile") === "1";
  const controller = new WorkerCodeRuntimeController(runtime, CODE_WORKER_SOURCE, bytecode, wasmBytes, APP_METADATA.scene, profileEnabled, CODE_WEB_BACKEND, appWasmBytes);
  window.CodeRuntime = {
    vm: null,
    runtime,
    controller,
    profile: {
      start: () => controller.profileStart(),
      stop: () => controller.profileStop(),
      reset: () => controller.profileReset(),
      report: () => controller.profileReport(true),
      json: () => controller.profileJson()
    }
  };
} catch (error) {
  runtime.showFatal(error);
  console.error(error);
}
    </script>
  </body>
</html>
""";
    }

    private static void WriteUtf8(string path, string content)
    {
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
