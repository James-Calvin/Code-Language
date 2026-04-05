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
    public string BytecodePath { get; }

    public WebBuildResult(string outputDirectory, string indexHtmlPath, string bytecodePath)
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
        Action<string>? traceWriter = null)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        var options = new ModuleCompileOptions
        {
            Target = CompileTarget.VmWeb,
            TraceLinker = traceLinker,
            TraceWriter = traceWriter
        };

        var result = ModuleCompiler.CompileFromFileWithMetadata(fullSourcePath, options);
        if (result.WebScene is null)
        {
            throw new CompilerException(
                "Web build requires object 'MainScene' with a zero-argument constructor and zero-argument start(), update(), and draw() methods.",
                1,
                1);
        }

        var manifest = PackageManifestLoader.TryLoadNearest(fullSourcePath, CompileTarget.VmWeb);
        string resolvedOutputDirectory = ResolveOutputDirectory(fullSourcePath, outputDirectory, manifest);
        Directory.CreateDirectory(resolvedOutputDirectory);

        string bytecodePath = Path.Combine(resolvedOutputDirectory, "app.bytecode");
        File.WriteAllBytes(bytecodePath, result.Bytecode);

        string runtimeScript = PrepareRuntimeScriptForInlineModule(File.ReadAllText(ResolveWebRuntimeScriptPath(fullSourcePath)));
        string html = BuildIndexHtml(
            manifest?.Name ?? Path.GetFileNameWithoutExtension(fullSourcePath),
            result.WebScene,
            Convert.ToBase64String(result.Bytecode),
            runtimeScript);

        string indexHtmlPath = Path.Combine(resolvedOutputDirectory, "index.html");
        WriteUtf8(indexHtmlPath, html);

        return new WebBuildResult(resolvedOutputDirectory, indexHtmlPath, bytecodePath);
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

    private static string PrepareRuntimeScriptForInlineModule(string runtimeScript)
    {
        return Regex.Replace(runtimeScript, @"(?m)^\s*export\s+", string.Empty);
    }

    private static string BuildIndexHtml(
        string title,
        WebSceneMetadata webScene,
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
            scene = new
            {
                typeName = webScene.SceneTypeName,
                constructor = new { targetIp = webScene.Constructor.TargetIp, frameSize = webScene.Constructor.FrameSize },
                start = new { targetIp = webScene.Start.TargetIp, frameSize = webScene.Start.FrameSize },
                update = new { targetIp = webScene.Update.TargetIp, frameSize = webScene.Update.FrameSize },
                draw = new { targetIp = webScene.Draw.TargetIp, frameSize = webScene.Draw.FrameSize }
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
  const vm = new WebVm(bytecode, {
    output: line => runtime.appendOutput(line),
    sceneHost: runtime
  });
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
