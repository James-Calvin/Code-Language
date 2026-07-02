using System;
using System.IO;
using System.Reflection;
using ConsoleApp1.Compiler;

namespace ConsoleApp1;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 1 && IsHelpFlag(args[0]))
        {
            PrintHelp();
            return;
        }

        if (args.Length == 1 && args[0] == "--version")
        {
            Console.WriteLine(GetVersion());
            return;
        }

        if (args.Length == 0)
        {
            PrintHelp();
            return;
        }

        string? disasmPath = null;
        string? bytecodePath = null;
        string? libraryPath = null;
        string? codePath = null;
        string? outPath = null;
        bool skipTests = false;
        bool runTests = false;
        bool compileOnly = false;
        string? dumpTokensPath = null;
        bool dumpModuleGraph = false;
        string? moduleGraphOutputPath = null;
        string? moduleGraphFormat = null;
        bool traceLinker = false;
        bool buildWeb = false;
        bool nativeMode = false;
        bool emitWebBytecode = false;
        bool targetSpecified = false;
        bool directWasmBackend = false;
        bool disableGarbageCollection = false;
        CompileTarget compileTarget = CompileTarget.VmNative;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--disasm":
                    if (i + 1 >= args.Length) Fail("Usage: --disasm <file.bytecode>");
                    disasmPath = args[++i];
                    break;
                case "--dump-tokens":
                    if (i + 1 >= args.Length) Fail("Usage: --dump-tokens <file.code>");
                    dumpTokensPath = args[++i];
                    break;
                case "-o":
                case "--output":
                case "--out":
                    if (i + 1 >= args.Length) Fail("Usage: -o <output-folder-or-bytecode-file>");
                    outPath = args[++i];
                    break;
                case "--build-web":
                    buildWeb = true;
                    break;
                case "--native":
                    nativeMode = true;
                    break;
                case "--emit-web-bytecode":
                    emitWebBytecode = true;
                    break;
                case "--skip-tests":
                    skipTests = true;
                    break;
                case "--run-tests":
                    runTests = true;
                    break;
                case "--compile-only":
                    compileOnly = true;
                    break;
                case "--dump-module-graph":
                    dumpModuleGraph = true;
                    if (i + 1 < args.Length &&
                        !args[i + 1].StartsWith("--", StringComparison.Ordinal) &&
                        !args[i + 1].EndsWith(".code", StringComparison.OrdinalIgnoreCase) &&
                        !args[i + 1].EndsWith(".bytecode", StringComparison.OrdinalIgnoreCase))
                    {
                        moduleGraphOutputPath = args[++i];
                    }
                    break;
                case "--module-graph-format":
                    if (i + 1 >= args.Length) Fail("Usage: --module-graph-format <text|json|dot>");
                    moduleGraphFormat = args[++i];
                    break;
                case "--trace-linker":
                    traceLinker = true;
                    break;
                case "--target":
                    if (i + 1 >= args.Length) Fail("Usage: --target <vm-native|vm-web>");
                    string targetArg = args[++i];
                    if (!CompileTargetExtensions.TryParse(targetArg, out compileTarget))
                        Fail($"Unsupported target '{targetArg}'. Use vm-native or vm-web.");
                    targetSpecified = true;
                    break;
                case "--web-backend":
                    if (i + 1 >= args.Length) Fail("Usage: --web-backend <wasm-vm|direct-wasm>");
                    string backend = args[++i];
                    if (backend == "direct-wasm") directWasmBackend = true;
                    else if (backend != "wasm-vm") Fail($"Unsupported web backend '{backend}'. Use wasm-vm or direct-wasm.");
                    break;
                case "--disable-garbage-collection":
                    disableGarbageCollection = true;
                    break;
                default:
                    if (args[i].EndsWith(".bytecode", StringComparison.OrdinalIgnoreCase))
                        bytecodePath = args[i];
                    else if (args[i].EndsWith(".codelib", StringComparison.OrdinalIgnoreCase))
                        libraryPath = args[i];
                    else if (args[i].EndsWith(".code", StringComparison.OrdinalIgnoreCase))
                        codePath = args[i];
                    else
                        Fail($"Unrecognized argument '{args[i]}'");
                    break;
            }
        }

        if (runTests)
        {
            TestHarness.RunAll();
            return;
        }

        if (moduleGraphFormat is not null && !dumpModuleGraph)
            Fail("--module-graph-format requires --dump-module-graph.");

        if (buildWeb && nativeMode)
            Fail("--build-web cannot be combined with --native.");

        if (nativeMode && targetSpecified && compileTarget != CompileTarget.VmNative)
            Fail("--native requires target vm-native.");

        if (buildWeb)
        {
            if (targetSpecified && compileTarget != CompileTarget.VmWeb)
                Fail("--build-web requires target vm-web.");
            compileTarget = CompileTarget.VmWeb;
        }
        else if (nativeMode)
        {
            compileTarget = CompileTarget.VmNative;
        }
        else if (targetSpecified && compileTarget == CompileTarget.VmNative)
        {
            nativeMode = true;
        }

        bool shouldBuildWeb = codePath is not null &&
            (buildWeb || (!nativeMode && !compileOnly && !targetSpecified && !dumpModuleGraph));

        if (emitWebBytecode && !shouldBuildWeb && !buildWeb)
            Fail("--emit-web-bytecode can only be used when building a web app.");
        if (directWasmBackend && codePath is null)
            Fail("--web-backend direct-wasm requires a .code input.");
        if (ValidateDirectWasmGarbageCollectionFlag(disableGarbageCollection, directWasmBackend) is { } directGcError)
            Fail(directGcError);

        if (disasmPath != null)
        {
            var bytes = LoadBytecodePayload(disasmPath);
            Console.Write(Disassembler.Disassemble(bytes));
            return;
        }

        if (dumpTokensPath != null)
        {
            DumpTokens(dumpTokensPath);
            return;
        }

        if (buildWeb && codePath is null)
            Fail("--build-web requires a .code input.");

        if (codePath != null)
        {
            if (shouldBuildWeb)
            {
                if (dumpModuleGraph || moduleGraphOutputPath is not null || moduleGraphFormat is not null)
                    Fail("Module graph options are not supported with web builds yet.");

                string outputDirectory = ResolveCliWebOutputDirectory(codePath, outPath);
                BuildWebApp(codePath, outputDirectory, traceLinker, emitWebBytecode, directWasmBackend, disableGarbageCollection);
                return;
            }

            string outputPath = outPath ?? ResolveCliNativeOutputPath(codePath);
            CompileToFile(codePath, outputPath, dumpModuleGraph, moduleGraphOutputPath, moduleGraphFormat, traceLinker, compileTarget, directWasmBackend, disableGarbageCollection);
            if (!compileOnly)
                RunBytecode(outputPath, MapHostTarget(compileTarget));
            return;
        }

        if (dumpModuleGraph || moduleGraphOutputPath is not null || moduleGraphFormat is not null)
            Fail("Module graph options require a .code input.");

        if (bytecodePath != null)
        {
            RunBytecode(bytecodePath, MapHostTarget(compileTarget));
            return;
        }

        if (libraryPath != null)
        {
            RunBytecode(libraryPath, MapHostTarget(compileTarget));
            return;
        }

        if (!skipTests)
            PrintHelp();
    }

    private static void CompileToFile(
        string sourcePath,
        string outputPath,
        bool dumpModuleGraph,
        string? moduleGraphOutputPath,
        string? moduleGraphFormat,
        bool traceLinker,
        CompileTarget target,
        bool directWasmBackend,
        bool disableGarbageCollection)
    {
        var source = File.ReadAllText(sourcePath);
        try
        {
            var options = new ModuleCompileOptions
            {
                Target = target,
                TraceLinker = traceLinker,
                TraceWriter = traceLinker ? message => Console.Error.WriteLine($"[linker] {message}") : null,
                EnableGraphicalAppProfile = directWasmBackend && target == CompileTarget.VmWeb,
                EnableImpliedEngineImports = directWasmBackend && target == CompileTarget.VmWeb,
                EmitDirectWasm = directWasmBackend,
                DisableDirectWasmGarbageCollection = disableGarbageCollection
            };
            var result = ModuleCompiler.CompileFromFileWithMetadata(sourcePath, options);
            string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
            var output = directWasmBackend
                ? result.DirectWasm?.Module ?? throw new InvalidOperationException("Direct-Wasm compilation did not produce a module.")
                : result.Bytecode;
            File.WriteAllBytes(outputPath, output);
            Console.WriteLine($"Compiled {sourcePath} -> {outputPath} (target={target.ToCliValue()}, backend={(directWasmBackend ? "direct-wasm" : "bytecode")})");
            if (dumpModuleGraph)
            {
                string graphBody = FormatModuleGraph(
                    result.Graph,
                    Path.GetDirectoryName(sourcePath),
                    moduleGraphOutputPath,
                    moduleGraphFormat);

                if (moduleGraphOutputPath is null)
                {
                    Console.WriteLine("Module graph:");
                    Console.Write(graphBody);
                }
                else
                {
                    string fullGraphPath = Path.GetFullPath(moduleGraphOutputPath);
                    string? dir = Path.GetDirectoryName(fullGraphPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(fullGraphPath, graphBody);
                    Console.WriteLine($"Module graph written to {fullGraphPath}");
                }
            }
        }
        catch (CompilerException ce)
        {
            Console.Error.WriteLine($"{sourcePath}:{ce.Line}:{ce.Column}: error: {ce.Message}");
            DiagnosticPrinter.PrintSnippet(sourcePath, source, ce.Line, ce.Column);
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Compile failed: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static void BuildWebApp(
        string sourcePath,
        string? outputDirectory,
        bool traceLinker,
        bool emitWebBytecode,
        bool directWasmBackend,
        bool disableGarbageCollection)
    {
        var source = File.ReadAllText(sourcePath);
        try
        {
            var result = WebBuildPipeline.Build(
                sourcePath,
                outputDirectory,
                traceLinker,
                traceLinker ? message => Console.Error.WriteLine($"[linker] {message}") : null,
                emitWebBytecode,
                directWasmBackend,
                disableGarbageCollection);

            Console.WriteLine($"Built web app {sourcePath} -> {result.OutputDirectory}");
            Console.WriteLine($"Entry page: {result.IndexHtmlPath}");
            if (result.BytecodePath is not null)
                Console.WriteLine($"Bytecode: {result.BytecodePath}");
        }
        catch (CompilerException ce)
        {
            Console.Error.WriteLine($"{sourcePath}:{ce.Line}:{ce.Column}: error: {ce.Message}");
            DiagnosticPrinter.PrintSnippet(sourcePath, source, ce.Line, ce.Column);
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Web build failed: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static string FormatModuleGraph(ModuleGraph graph, string? sourceDir, string? outputPath, string? formatOverride)
    {
        string? format = NormalizeModuleGraphFormat(outputPath, formatOverride);
        return format switch
        {
            "json" => graph.ToJsonString(sourceDir),
            "dot" => graph.ToDotString(sourceDir),
            _ => graph.ToDisplayString(sourceDir)
        };
    }

    private static string? NormalizeModuleGraphFormat(string? outputPath, string? formatOverride)
    {
        if (!string.IsNullOrWhiteSpace(formatOverride))
        {
            string normalized = formatOverride.Trim().ToLowerInvariant();
            if (normalized is "text" or "json" or "dot")
                return normalized;
            Fail($"Unsupported module graph format '{formatOverride}'. Use text, json, or dot.");
        }

        if (string.IsNullOrWhiteSpace(outputPath))
            return null;

        string extension = Path.GetExtension(outputPath).ToLowerInvariant();
        return extension switch
        {
            ".json" => "json",
            ".dot" or ".gv" => "dot",
            _ => null
        };
    }

    private static void RunBytecode(string path, VmHostTarget hostTarget)
    {
        if (!path.EndsWith(".bytecode", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".codelib", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Expected a .bytecode or .codelib file.");
            Environment.Exit(1);
        }
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Bytecode file not found: {path}");
            Environment.Exit(1);
        }

        var bytes = LoadBytecodePayload(path);
        var vm = new Vm(bytes, hostTarget: hostTarget);
        try
        {
            vm.Run();
        }
        catch (VmRuntimeException vex)
        {
            Console.Error.WriteLine($"Runtime error: {vex.Message}");
            PrintRuntimeLocation(path, vex.Line, vex.Column);
            if (vex.CallStack.Length > 0)
            {
                Console.Error.WriteLine("Stack trace (most recent call first):");
                foreach (var frame in vex.CallStack)
                {
                    var locText = FormatLocation(path, frame.Line, frame.Column);
                    Console.Error.WriteLine($"  at ip {frame.Ip}{locText}");
                }
            }
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Runtime error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static void Fail(string message)
    {
        Console.Error.WriteLine(message);
        Environment.Exit(1);
    }

    private static bool IsHelpFlag(string arg) =>
        arg is "--help" or "-h" or "/?";

    internal static string? ValidateDirectWasmGarbageCollectionFlag(bool disableGarbageCollection, bool directWasmBackend)
    {
        if (!disableGarbageCollection)
            return null;

        return directWasmBackend
            ? null
            : "--disable-garbage-collection requires --web-backend direct-wasm.";
    }

    private static string ResolveCliWebOutputDirectory(string sourcePath, string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
            return Path.GetFullPath(outputPath);

        string sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceName))
            sourceName = "app";

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), sourceName));
    }

    private static string ResolveCliNativeOutputPath(string sourcePath)
    {
        string sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceName))
            sourceName = "app";

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), sourceName + ".bytecode"));
    }

    private static string GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
            return info;

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static void PrintHelp()
    {
        Console.WriteLine($$"""
compiler {{GetVersion()}}

Usage:
  compiler <file.code> [-o output-folder]
  compiler --native <file.code>

Options:
  -o, --output <folder>      Set the web output folder.
  --native                   Compile and run with native host bindings.
  --version                  Show the compiler version.
  --help, -h, /?             Show this help.

Examples:
  compiler source.code
  compiler source.code -o MyApp
  compiler --native ConsoleApp1/examples/arithmetic.code
""");
    }

    private static byte[] LoadBytecodePayload(string path)
    {
        if (path.EndsWith(".codelib", StringComparison.OrdinalIgnoreCase))
            return CodeLibraryArtifactFormat.Read(path).Bytecode;
        return File.ReadAllBytes(path);
    }

    private static VmHostTarget MapHostTarget(CompileTarget target) => target switch
    {
        CompileTarget.VmNative => VmHostTarget.Native,
        CompileTarget.VmWeb => VmHostTarget.Web,
        _ => VmHostTarget.Native
    };

    private static void DumpTokens(string path)
    {
        var source = File.ReadAllText(path);
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        foreach (var t in tokens)
        {
            Console.WriteLine($"{t.Line}:{t.Column} {t.Type} '{t.Lexeme}'");
        }
    }

    private static void PrintRuntimeLocation(string bytecodePath, int line, int column)
    {
        if (line <= 0 || column <= 0) return;
        string sourcePath = Path.ChangeExtension(bytecodePath, ".code");
        if (File.Exists(sourcePath))
        {
            var source = File.ReadAllText(sourcePath);
            DiagnosticPrinter.PrintSnippet(sourcePath, source, line, column);
        }
        else
        {
            Console.Error.WriteLine($"  at {sourcePath}:{line}:{column}");
        }
    }

    private static string FormatLocation(string bytecodePath, int line, int column)
    {
        if (line <= 0 || column <= 0) return string.Empty;
        string sourcePath = Path.ChangeExtension(bytecodePath, ".code");
        string displayPath = File.Exists(sourcePath) ? sourcePath : Path.GetFileName(sourcePath);
        return $" ({displayPath}:{line}:{column})";
    }
}
