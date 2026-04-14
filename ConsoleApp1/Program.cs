using System;
using System.IO;
using ConsoleApp1.Compiler;

namespace ConsoleApp1;

internal static class Program
{
    public static void Main(string[] args)
    {
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
        bool emitWebBytecode = false;
        bool targetSpecified = false;
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
                case "--out":
                    if (i + 1 >= args.Length) Fail("Usage: --out <output.bytecode|output-directory>");
                    outPath = args[++i];
                    break;
                case "--build-web":
                    buildWeb = true;
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

        if (emitWebBytecode && !buildWeb)
            Fail("--emit-web-bytecode requires --build-web.");

        if (buildWeb)
        {
            if (targetSpecified && compileTarget != CompileTarget.VmWeb)
                Fail("--build-web requires target vm-web.");
            compileTarget = CompileTarget.VmWeb;
        }

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
            if (buildWeb)
            {
                if (dumpModuleGraph || moduleGraphOutputPath is not null || moduleGraphFormat is not null)
                    Fail("Module graph options are not supported with --build-web yet.");

                BuildWebApp(codePath, outPath, traceLinker, emitWebBytecode);
                return;
            }

            string outputPath = outPath ?? Path.ChangeExtension(codePath, ".bytecode");
            CompileToFile(codePath, outputPath, dumpModuleGraph, moduleGraphOutputPath, moduleGraphFormat, traceLinker, compileTarget);
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
            TestHarness.RunAll();

        Console.WriteLine();
        Console.WriteLine("Demo: (2 + 3) * 4");
        var program = BytecodeBuilder.New()
            .PushInt(2).PushInt(3).Add()
            .PushInt(4).Mul()
            .Print().Halt()
            .ToArray();
        new Vm(program).Run();
    }

    private static void CompileToFile(
        string sourcePath,
        string outputPath,
        bool dumpModuleGraph,
        string? moduleGraphOutputPath,
        string? moduleGraphFormat,
        bool traceLinker,
        CompileTarget target)
    {
        var source = File.ReadAllText(sourcePath);
        try
        {
            var options = new ModuleCompileOptions
            {
                Target = target,
                TraceLinker = traceLinker,
                TraceWriter = traceLinker ? message => Console.Error.WriteLine($"[linker] {message}") : null
            };
            var result = ModuleCompiler.CompileFromFileWithMetadata(sourcePath, options);
            File.WriteAllBytes(outputPath, result.Bytecode);
            Console.WriteLine($"Compiled {sourcePath} -> {outputPath} (target={target.ToCliValue()})");
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
        bool emitWebBytecode)
    {
        var source = File.ReadAllText(sourcePath);
        try
        {
            var result = WebBuildPipeline.Build(
                sourcePath,
                outputDirectory,
                traceLinker,
                traceLinker ? message => Console.Error.WriteLine($"[linker] {message}") : null,
                emitWebBytecode);

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
