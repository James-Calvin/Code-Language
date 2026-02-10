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
        string? codePath = null;
        string? outPath = null;
        bool skipTests = false;
        bool compileOnly = false;
        string? dumpTokensPath = null;

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
                    if (i + 1 >= args.Length) Fail("Usage: --out <output.bytecode>");
                    outPath = args[++i];
                    break;
                case "--skip-tests":
                    skipTests = true;
                    break;
                case "--compile-only":
                    compileOnly = true;
                    break;
                default:
                    if (args[i].EndsWith(".bytecode", StringComparison.OrdinalIgnoreCase))
                        bytecodePath = args[i];
                    else if (args[i].EndsWith(".code", StringComparison.OrdinalIgnoreCase))
                        codePath = args[i];
                    else
                        Fail($"Unrecognized argument '{args[i]}'");
                    break;
            }
        }

        if (disasmPath != null)
        {
            var bytes = File.ReadAllBytes(disasmPath);
            Console.Write(Disassembler.Disassemble(bytes));
            return;
        }

        if (dumpTokensPath != null)
        {
            DumpTokens(dumpTokensPath);
            return;
        }

        if (codePath != null)
        {
            string outputPath = outPath ?? Path.ChangeExtension(codePath, ".bytecode");
            CompileToFile(codePath, outputPath);
            if (!compileOnly)
                RunBytecode(outputPath);
            return;
        }

        if (bytecodePath != null)
        {
            RunBytecode(bytecodePath);
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

    private static void CompileToFile(string sourcePath, string outputPath)
    {
        try
        {
            var source = File.ReadAllText(sourcePath);
            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            var ast = parser.Parse();
            var generator = new CodeGenerator();
            var bytes = generator.Generate(ast);
            File.WriteAllBytes(outputPath, bytes);
            Console.WriteLine($"Compiled {sourcePath} -> {outputPath}");
        }
        catch (CompilerException ce)
        {
            Console.Error.WriteLine($"{sourcePath}:{ce.Line}:{ce.Column}: error: {ce.Message}");
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Compile failed: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static void RunBytecode(string path)
    {
        if (!path.EndsWith(".bytecode", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Expected a .bytecode file.");
            Environment.Exit(1);
        }
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Bytecode file not found: {path}");
            Environment.Exit(1);
        }

        var bytes = File.ReadAllBytes(path);
        var vm = new Vm(bytes);
        vm.Run();
    }

    private static void Fail(string message)
    {
        Console.Error.WriteLine(message);
        Environment.Exit(1);
    }

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
}
