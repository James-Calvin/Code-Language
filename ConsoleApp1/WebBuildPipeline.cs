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
        bool emitWebBytecode = false)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        var options = new ModuleCompileOptions
        {
            Target = CompileTarget.VmWeb,
            TraceLinker = traceLinker,
            TraceWriter = traceWriter,
            EnableGraphicalAppProfile = true,
            EnableImpliedEngineImports = true
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
        string html = BuildIndexHtml(
            manifest?.Name ?? Path.GetFileNameWithoutExtension(fullSourcePath),
            result.WebScene,
            result.CallableNames,
            Convert.ToBase64String(result.Bytecode),
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
        IReadOnlyDictionary<int, string> callableNames,
        string bytecodeBase64,
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
            callableNames,
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

{{runtimeScript}}

const runtime = new CanvasSceneRuntime({
  width: APP_METADATA.virtualWidth,
  height: APP_METADATA.virtualHeight,
  title: APP_TITLE
});

runtime.attach(document.body);

try {
  const bytecode = decodeBase64Bytes(APP_BYTECODE_BASE64);
  const profileEnabled = new URLSearchParams(window.location.search).get("code-profile") === "1";
  const vm = new WebVm(bytecode, {
    output: line => console.log(line),
    sceneHost: runtime,
    functionNames: APP_METADATA.callableNames,
    profileEnabled
  });
  window.CodeRuntime = {
    vm,
    runtime,
    profile: {
      start: () => vm.profiler.start(),
      stop: () => vm.profiler.stop(),
      reset: () => vm.profiler.reset(),
      report: () => vm.profiler.print(),
      json: () => JSON.stringify(vm.profiler.report(), null, 2)
    }
  };
  runtime.runScene(vm, APP_METADATA.scene);
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
