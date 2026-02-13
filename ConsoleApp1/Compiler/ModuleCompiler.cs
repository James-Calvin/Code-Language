using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConsoleApp1.Compiler;

static class ModuleCompiler
{
    public static byte[] CompileFromSource(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        var typeChecker = new TypeChecker();
        typeChecker.Check(ast);
        var generator = new CodeGenerator();
        return generator.Generate(ast);
    }

    public static byte[] CompileFromFile(string entryPath)
    {
        string fullEntryPath = Path.GetFullPath(entryPath);
        string projectRoot = Directory.GetCurrentDirectory();
        var linker = new ModuleLinker(projectRoot);
        var ast = linker.Link(fullEntryPath);
        var typeChecker = new TypeChecker();
        typeChecker.Check(ast);
        var generator = new CodeGenerator();
        return generator.Generate(ast);
    }

    private sealed class ModuleLinker
    {
        private readonly string _projectRoot;
        private readonly Dictionary<string, ModuleInfo> _modules = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _visiting = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _order = new();

        public ModuleLinker(string projectRoot)
        {
            _projectRoot = Path.GetFullPath(projectRoot);
        }

        public IList<Stmt> Link(string entryPath)
        {
            _modules.Clear();
            _visiting.Clear();
            _order.Clear();

            Visit(entryPath);

            var linked = new List<Stmt>();
            for (int i = 0; i < _order.Count; i++)
            {
                var module = _modules[_order[i]];
                linked.AddRange(module.LinkedStatements);
            }
            return linked;
        }

        private ModuleInfo Visit(string modulePath)
        {
            modulePath = Path.GetFullPath(modulePath);
            if (_modules.TryGetValue(modulePath, out var cached))
                return cached;
            if (_visiting.Contains(modulePath))
                throw new CompilerException($"Circular import detected at '{Path.GetFileName(modulePath)}'", 1, 1);

            _visiting.Add(modulePath);
            var module = ParseModule(modulePath);
            foreach (var import in module.Imports)
            {
                string dependencyPath = ResolveImportPath(module.Path, import.SourcePath, import.Source);
                var dependency = Visit(dependencyPath);
                if (!dependency.ExportedDeclarations.TryGetValue(import.Name.Lexeme, out var exported))
                {
                    throw new CompilerException(
                        $"Module '{Path.GetFileName(dependencyPath)}' does not export '{import.Name.Lexeme}'",
                        import.Name.Line,
                        import.Name.Column);
                }

                if (import.Alias is not null)
                {
                    module.LinkedStatements.Add(BuildAliasWrapper(import, exported));
                }
            }

            module.LinkedStatements.AddRange(module.LocalStatements);
            _visiting.Remove(modulePath);
            _modules[modulePath] = module;
            _order.Add(modulePath);
            return module;
        }

        private static ModuleInfo ParseModule(string modulePath)
        {
            if (!File.Exists(modulePath))
                throw new CompilerException($"Module file not found: '{modulePath}'", 1, 1);

            IList<Stmt> statements;
            try
            {
                string source = File.ReadAllText(modulePath);
                var lexer = new Lexer(source);
                var tokens = lexer.ScanTokens();
                var parser = new Parser(tokens);
                statements = parser.Parse();
            }
            catch (CompilerException ex)
            {
                throw new CompilerException($"{Path.GetFileName(modulePath)}: {ex.Message}", ex.Line, ex.Column);
            }

            var imports = new List<ImportDecl>();
            var locals = new List<Stmt>();
            var exports = new Dictionary<string, Stmt>(StringComparer.Ordinal);
            foreach (var stmt in statements)
            {
                switch (stmt)
                {
                    case ImportDecl imp:
                        imports.Add(imp);
                        break;
                    case ExportDecl exp:
                    {
                        string exportName = GetExportName(exp.Declaration);
                        if (exports.ContainsKey(exportName))
                        {
                            throw new CompilerException(
                                $"Module export '{exportName}' is already declared",
                                GetDeclLine(exp.Declaration),
                                GetDeclColumn(exp.Declaration));
                        }
                        exports[exportName] = exp.Declaration;
                        locals.Add(exp.Declaration);
                        break;
                    }
                    default:
                        locals.Add(stmt);
                        break;
                }
            }

            return new ModuleInfo(modulePath, imports, locals, exports, new List<Stmt>());
        }

        private string ResolveImportPath(string importerPath, string sourcePath, Token sourceToken)
        {
            string normalized = sourcePath.EndsWith(".code", StringComparison.OrdinalIgnoreCase)
                ? sourcePath
                : sourcePath + ".code";

            var candidates = new List<string>();
            if (Path.IsPathRooted(normalized))
            {
                candidates.Add(Path.GetFullPath(normalized));
            }
            else
            {
                string importerDir = Path.GetDirectoryName(importerPath) ?? _projectRoot;
                candidates.Add(Path.GetFullPath(Path.Combine(importerDir, normalized)));
                candidates.Add(Path.GetFullPath(Path.Combine(_projectRoot, "lib", normalized)));

                string? cursor = importerDir;
                while (!string.IsNullOrEmpty(cursor))
                {
                    candidates.Add(Path.GetFullPath(Path.Combine(cursor, "lib", normalized)));
                    cursor = Directory.GetParent(cursor)?.FullName;
                }
            }

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new CompilerException(
                $"Could not resolve import '{sourcePath}'",
                sourceToken.Line,
                sourceToken.Column);
        }

        private static Stmt BuildAliasWrapper(ImportDecl import, Stmt exportedDecl)
        {
            if (import.Alias is null)
                throw new InvalidOperationException("Alias token is required.");

            if (exportedDecl is not FunctionDecl fn)
            {
                throw new CompilerException(
                    $"Alias import for '{import.Name.Lexeme}' is only supported for functions",
                    import.Alias.Line,
                    import.Alias.Column);
            }
            if (fn.ReturnType is null)
            {
                throw new CompilerException(
                    $"Cannot alias function '{fn.Name.Lexeme}' without an explicit return type",
                    import.Alias.Line,
                    import.Alias.Column);
            }

            var aliasName = new Token(TokenType.Identifier, import.Alias.Lexeme, null, import.Alias.Line, import.Alias.Column);
            var callName = new Token(TokenType.Identifier, fn.Name.Lexeme, null, import.Name.Line, import.Name.Column);
            var parameters = new List<Parameter>(fn.Parameters.Count);
            var callArgs = new List<Expr>(fn.Parameters.Count);
            for (int i = 0; i < fn.Parameters.Count; i++)
            {
                var sourceParam = fn.Parameters[i];
                var paramToken = new Token(TokenType.Identifier, sourceParam.Name.Lexeme, null, import.Alias.Line, import.Alias.Column);
                parameters.Add(new Parameter(sourceParam.Type, paramToken));
                callArgs.Add(new Variable(paramToken));
            }

            var call = new Call(callName, callArgs);
            var bodyStmts = new List<Stmt> { new ReturnStmt(call) };
            var body = new Block(bodyStmts);
            return new FunctionDecl(aliasName, fn.ReturnType, parameters, body);
        }

        private static string GetExportName(Stmt declaration) => declaration switch
        {
            FunctionDecl fn => fn.Name.Lexeme,
            ObjectDecl obj => obj.Name.Lexeme,
            InterfaceDecl iface => iface.Name.Lexeme,
            _ => throw new CompilerException(
                "Only function/object/interface declarations can be exported",
                GetDeclLine(declaration),
                GetDeclColumn(declaration))
        };

        private static int GetDeclLine(Stmt declaration) => declaration switch
        {
            FunctionDecl fn => fn.Name.Line,
            ObjectDecl obj => obj.Name.Line,
            InterfaceDecl iface => iface.Name.Line,
            _ => 1
        };

        private static int GetDeclColumn(Stmt declaration) => declaration switch
        {
            FunctionDecl fn => fn.Name.Column,
            ObjectDecl obj => obj.Name.Column,
            InterfaceDecl iface => iface.Name.Column,
            _ => 1
        };
    }

    private sealed record ModuleInfo(
        string Path,
        List<ImportDecl> Imports,
        List<Stmt> LocalStatements,
        Dictionary<string, Stmt> ExportedDeclarations,
        List<Stmt> LinkedStatements);
}
