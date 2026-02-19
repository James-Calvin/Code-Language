using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ConsoleApp1.Compiler;

sealed class ModuleCompileOptions
{
    public CompileTarget Target { get; init; } = CompileTarget.VmNative;
    public PackageManifest? PackageManifest { get; init; }
    public bool TraceLinker { get; init; }
    public Action<string>? TraceWriter { get; init; }
}

sealed class ModuleCompileResult
{
    public byte[] Bytecode { get; }
    public ModuleGraph Graph { get; }
    public CompileTarget Target { get; }
    public IReadOnlyList<string> RequiredCapabilities { get; }

    public ModuleCompileResult(byte[] bytecode, ModuleGraph graph, CompileTarget target, IReadOnlyList<string> requiredCapabilities)
    {
        Bytecode = bytecode;
        Graph = graph;
        Target = target;
        RequiredCapabilities = requiredCapabilities;
    }
}

sealed class ModuleGraph
{
    public string EntryPath { get; }
    public CompileTarget Target { get; }
    public IReadOnlyList<string> RequiredCapabilities { get; }
    public IReadOnlyList<ModuleGraphModule> Modules { get; }
    public IReadOnlyList<ModuleGraphEdge> Edges { get; }

    public ModuleGraph(
        string entryPath,
        CompileTarget target,
        IReadOnlyList<string> requiredCapabilities,
        IReadOnlyList<ModuleGraphModule> modules,
        IReadOnlyList<ModuleGraphEdge> edges)
    {
        EntryPath = entryPath;
        Target = target;
        RequiredCapabilities = requiredCapabilities;
        Modules = modules;
        Edges = edges;
    }

    public string ToDisplayString(string? displayRoot = null)
    {
        var sb = new StringBuilder();
        sb.Append("Entry: ").Append(FormatPath(EntryPath, displayRoot)).AppendLine();
        sb.Append("Target: ").Append(Target.ToCliValue()).AppendLine();
        string capabilitiesText = RequiredCapabilities.Count == 0 ? "(none)" : string.Join(", ", RequiredCapabilities);
        sb.Append("Capabilities: ").Append(capabilitiesText).AppendLine();
        sb.AppendLine("Modules:");
        for (int i = 0; i < Modules.Count; i++)
        {
            var module = Modules[i];
            string exports = module.Exports.Count == 0 ? "(none)" : string.Join(", ", module.Exports);
            string package = string.IsNullOrWhiteSpace(module.PackageName) ? string.Empty : $" package={module.PackageName}";
            sb.Append("  - ")
                .Append(FormatPath(module.Path, displayRoot))
                .Append(package)
                .Append(" exports=")
                .Append(exports)
                .AppendLine();
        }

        sb.AppendLine("Imports:");
        if (Edges.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            for (int i = 0; i < Edges.Count; i++)
            {
                var edge = Edges[i];
                sb.Append("  - ")
                    .Append(FormatPath(edge.ImporterPath, displayRoot))
                    .Append(" -> ")
                    .Append(FormatPath(edge.DependencyPath, displayRoot))
                    .Append(" : ")
                    .Append(edge.BindingText)
                    .Append(" from \"")
                    .Append(edge.SourcePath)
                    .AppendLine("\"");
            }
        }

        return sb.ToString();
    }

    public string ToJsonString(string? displayRoot = null, bool indented = true)
    {
        var payload = new
        {
            entry = FormatPath(EntryPath, displayRoot),
            target = Target.ToCliValue(),
            requiredCapabilities = RequiredCapabilities,
            modules = Modules.Select(module => new
            {
                path = FormatPath(module.Path, displayRoot),
                package = module.PackageName,
                exports = module.Exports
            }),
            imports = Edges.Select(edge => new
            {
                from = FormatPath(edge.ImporterPath, displayRoot),
                to = FormatPath(edge.DependencyPath, displayRoot),
                source = edge.SourcePath,
                binding = edge.BindingText
            })
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = indented });
    }

    public string ToDotString(string? displayRoot = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph ModuleGraph {");
        sb.AppendLine("  rankdir=LR;");

        var nodeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int nextNodeId = 0;

        for (int i = 0; i < Modules.Count; i++)
        {
            var module = Modules[i];
            string nodeId = AddNode(nodeIds, module.Path, ref nextNodeId);
            string shape = string.Equals(module.Path, EntryPath, StringComparison.OrdinalIgnoreCase) ? "doubleoctagon" : "box";
            string label = BuildDotLabel(module, displayRoot);
            sb.Append("  ")
                .Append(nodeId)
                .Append(" [shape=")
                .Append(shape)
                .Append(", label=\"")
                .Append(label)
                .AppendLine("\"];");
        }

        for (int i = 0; i < Edges.Count; i++)
        {
            var edge = Edges[i];
            string fromId = AddNode(nodeIds, edge.ImporterPath, ref nextNodeId);
            string toId = AddNode(nodeIds, edge.DependencyPath, ref nextNodeId);
            string label = EscapeDotLabel($"{edge.BindingText} from \"{edge.SourcePath}\"");
            sb.Append("  ")
                .Append(fromId)
                .Append(" -> ")
                .Append(toId)
                .Append(" [label=\"")
                .Append(label)
                .AppendLine("\"];");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string AddNode(IDictionary<string, string> ids, string path, ref int nextNodeId)
    {
        if (ids.TryGetValue(path, out var existing))
            return existing;
        string id = $"n{nextNodeId++}";
        ids[path] = id;
        return id;
    }

    private static string BuildDotLabel(ModuleGraphModule module, string? displayRoot)
    {
        var lines = new List<string>
        {
            EscapeDotLabel(FormatPath(module.Path, displayRoot))
        };

        if (!string.IsNullOrWhiteSpace(module.PackageName))
            lines.Add(EscapeDotLabel($"package={module.PackageName}"));

        string exports = module.Exports.Count == 0 ? "(none)" : string.Join(", ", module.Exports);
        lines.Add(EscapeDotLabel($"exports={exports}"));

        return string.Join("\\n", lines);
    }

    private static string EscapeDotLabel(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string FormatPath(string path, string? displayRoot)
    {
        if (string.IsNullOrWhiteSpace(displayRoot))
            return Path.GetFileName(path);

        string fullRoot = Path.GetFullPath(displayRoot);
        string fullPath = Path.GetFullPath(path);
        string rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;

        if (fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');

        return Path.GetFileName(fullPath);
    }
}

sealed record ModuleGraphModule(string Path, string? PackageName, IReadOnlyList<string> Exports);

sealed record ModuleGraphEdge(string ImporterPath, string DependencyPath, string SourcePath, string BindingText);

static class ModuleCompiler
{
    public static byte[] CompileFromSource(string source, CompileTarget target = CompileTarget.VmNative)
    {
        _ = target; // reserved for source-level target checks when host ABI calls are introduced
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
        return CompileFromFileWithMetadata(entryPath).Bytecode;
    }

    public static byte[] CompileFromFile(string entryPath, ModuleCompileOptions options)
    {
        return CompileFromFileWithMetadata(entryPath, options).Bytecode;
    }

    public static ModuleCompileResult CompileFromFileWithMetadata(string entryPath, ModuleCompileOptions? options = null)
    {
        string fullEntryPath = Path.GetFullPath(entryPath);
        var baseOptions = options ?? new ModuleCompileOptions();
        var manifest = baseOptions.PackageManifest ?? PackageManifestLoader.TryLoadNearest(fullEntryPath, baseOptions.Target);
        string projectRoot = manifest?.PackageRoot ?? Directory.GetCurrentDirectory();
        var compileOptions = new ModuleCompileOptions
        {
            Target = baseOptions.Target,
            PackageManifest = manifest,
            TraceLinker = baseOptions.TraceLinker,
            TraceWriter = baseOptions.TraceWriter
        };
        var linker = new ModuleLinker(projectRoot, fullEntryPath, compileOptions);
        var linkResult = linker.Link(fullEntryPath);
        var typeChecker = new TypeChecker();
        typeChecker.Check(linkResult.Statements);
        var generator = new CodeGenerator();
        var bytecode = generator.Generate(linkResult.Statements);
        return new ModuleCompileResult(
            bytecode,
            linkResult.Graph,
            compileOptions.Target,
            linkResult.RequiredCapabilities);
    }

    private sealed class ModuleLinker
    {
        private readonly string _projectRoot;
        private readonly string _displayRoot;
        private readonly string _entryPath;
        private readonly ModuleCompileOptions _options;
        private readonly Dictionary<string, ModuleInfo> _modules = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _visiting = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _visitStack = new();
        private readonly List<string> _order = new();
        private readonly List<ModuleGraphEdge> _edges = new();
        private readonly Dictionary<string, CapabilityUse> _requiredCapabilities = new(StringComparer.Ordinal);

        public ModuleLinker(string projectRoot, string entryPath, ModuleCompileOptions options)
        {
            _projectRoot = Path.GetFullPath(projectRoot);
            _entryPath = Path.GetFullPath(entryPath);
            _displayRoot = Path.GetDirectoryName(_entryPath) ?? _projectRoot;
            _options = options;
        }

        public LinkResult Link(string entryPath)
        {
            _modules.Clear();
            _visiting.Clear();
            _visitStack.Clear();
            _order.Clear();
            _edges.Clear();
            _requiredCapabilities.Clear();
            RegisterManifestCapabilities();

            Trace($"Link entry module {FormatGraphPath(entryPath)}");
            Visit(entryPath);
            ValidateTargetCapabilities();

            var linked = new List<Stmt>();
            var moduleNodes = new List<ModuleGraphModule>();
            for (int i = 0; i < _order.Count; i++)
            {
                var module = _modules[_order[i]];
                linked.AddRange(module.LinkedStatements);
                moduleNodes.Add(new ModuleGraphModule(
                    module.Path,
                    module.PackageName,
                    module.ExportedDeclarations.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList()));
            }

            var requiredCapabilities = _requiredCapabilities.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
            var graph = new ModuleGraph(
                _entryPath,
                _options.Target,
                requiredCapabilities,
                moduleNodes,
                _edges.ToList());

            return new LinkResult(linked, graph, requiredCapabilities);
        }

        private ModuleInfo Visit(string modulePath)
        {
            modulePath = Path.GetFullPath(modulePath);
            if (_modules.TryGetValue(modulePath, out var cached))
            {
                Trace($"Reuse cached module {FormatGraphPath(modulePath)}");
                return cached;
            }
            if (_visiting.Contains(modulePath))
            {
                var cycle = BuildCycleChain(modulePath);
                Trace($"Detected circular import: {string.Join(" -> ", cycle.Select(FormatChainItem))}");
                throw BuildChainedError("Circular import detected.", 1, 1, cycle);
            }

            Trace($"Visit {FormatGraphPath(modulePath)}");
            _visiting.Add(modulePath);
            _visitStack.Add(modulePath);
            try
            {
                var module = ParseModule(modulePath);
                Trace($"Parsed {FormatGraphPath(modulePath)} (imports={module.Imports.Count}, exports={module.ExportedDeclarations.Count})");
                RegisterCapabilityFromPackage(module.PackageName, module.Path, module.PackageLine, module.PackageColumn);
                foreach (var import in module.Imports)
                {
                    RegisterCapabilityFromImport(import.SourcePath, module.Path, import.Source.Line, import.Source.Column);
                    string dependencyPath = ResolveImportPath(module.Path, import.SourcePath, import.Source);
                    _edges.Add(new ModuleGraphEdge(module.Path, dependencyPath, import.SourcePath, FormatBindingText(import.Bindings)));
                    Trace(
                        $"Resolve import {FormatBindingText(import.Bindings)} from \"{import.SourcePath}\" in {FormatGraphPath(module.Path)} -> {FormatGraphPath(dependencyPath)}");
                    var dependency = Visit(dependencyPath);
                    for (int i = 0; i < import.Bindings.Count; i++)
                    {
                        var binding = import.Bindings[i];
                        if (!dependency.ExportedDeclarations.TryGetValue(binding.Name.Lexeme, out var exported))
                        {
                            var chain = BuildImportChain(dependencyPath);
                            throw BuildChainedError(
                                $"Module '{Path.GetFileName(dependencyPath)}' does not export '{binding.Name.Lexeme}'",
                                binding.Name.Line,
                                binding.Name.Column,
                                chain);
                        }

                        if (binding.Alias is not null)
                        {
                            switch (exported)
                            {
                                case FunctionDecl:
                                    module.LinkedStatements.Add(BuildAliasWrapper(binding, exported));
                                    break;
                                case ObjectDecl:
                                case InterfaceDecl:
                                    module.TypeAliases[binding.Alias.Lexeme] = binding.Name.Lexeme;
                                    break;
                                default:
                                    throw new CompilerException(
                                        $"Alias import for '{binding.Name.Lexeme}' is not supported for this declaration kind",
                                        binding.Alias.Line,
                                        binding.Alias.Column);
                            }
                        }
                    }
                }

                module.LinkedStatements.AddRange(RewriteTypeAliases(module.LocalStatements, module.TypeAliases));
                _modules[modulePath] = module;
                _order.Add(modulePath);
                Trace($"Linked {FormatGraphPath(modulePath)}");
                return module;
            }
            catch (CompilerException ex)
            {
                if (ex.Message.Contains("Import chain:", StringComparison.Ordinal))
                {
                    throw;
                }
                throw BuildChainedError(ex.Message, ex.Line, ex.Column, _visitStack);
            }
            finally
            {
                _visitStack.RemoveAt(_visitStack.Count - 1);
                _visiting.Remove(modulePath);
            }
        }

        private sealed record CapabilityUse(
            string Capability,
            string ModulePath,
            int Line,
            int Column,
            string Context);

        private ModuleInfo ParseModule(string modulePath)
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
            var topLevelNames = new Dictionary<string, Token>(StringComparer.Ordinal);
            var importBindings = new Dictionary<string, Token>(StringComparer.Ordinal);
            PackageDecl? package = null;
            foreach (var stmt in statements)
            {
                switch (stmt)
                {
                    case PackageDecl pkg:
                        if (package is not null)
                        {
                            throw new CompilerException(
                                "Only one package declaration is allowed per module.",
                                pkg.NameToken.Line,
                                pkg.NameToken.Column);
                        }
                        if (imports.Count > 0 || locals.Count > 0)
                        {
                            throw new CompilerException(
                                "Package declaration must appear before imports and declarations.",
                                pkg.NameToken.Line,
                                pkg.NameToken.Column);
                        }
                        package = pkg;
                        break;
                    case ImportDecl imp:
                        if (locals.Count > 0)
                        {
                            throw new CompilerException(
                                "Import declarations must appear before module declarations.",
                                imp.Source.Line,
                                imp.Source.Column);
                        }
                        for (int i = 0; i < imp.Bindings.Count; i++)
                        {
                            string bindingName = imp.Bindings[i].Alias?.Lexeme ?? imp.Bindings[i].Name.Lexeme;
                            Token bindingToken = imp.Bindings[i].Alias ?? imp.Bindings[i].Name;
                            if (importBindings.ContainsKey(bindingName))
                            {
                                throw new CompilerException(
                                    $"Import binding '{bindingName}' is already declared in this module.",
                                    bindingToken.Line,
                                    bindingToken.Column);
                            }
                            if (topLevelNames.ContainsKey(bindingName))
                            {
                                throw new CompilerException(
                                    $"Import binding '{bindingName}' conflicts with a module declaration.",
                                    bindingToken.Line,
                                    bindingToken.Column);
                            }
                            importBindings[bindingName] = bindingToken;
                        }
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
                        RegisterTopLevelName(topLevelNames, importBindings, exportName, GetDeclToken(exp.Declaration));
                        locals.Add(exp.Declaration);
                        break;
                    }
                    default:
                        if (TryGetDeclarationName(stmt, out var declName, out var declToken))
                        {
                            RegisterTopLevelName(topLevelNames, importBindings, declName, declToken);
                        }
                        locals.Add(stmt);
                        break;
                }
            }

            return new ModuleInfo(
                modulePath,
                package?.Name,
                package?.NameToken.Line ?? 1,
                package?.NameToken.Column ?? 1,
                imports,
                locals,
                exports,
                new List<Stmt>(),
                new Dictionary<string, string>(StringComparer.Ordinal));
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

        private void RegisterManifestCapabilities()
        {
            if (_options.PackageManifest is null)
                return;

            var manifest = _options.PackageManifest;
            Trace($"Load manifest {PackageManifest.FileName} name={manifest.Name} target={_options.Target.ToCliValue()}");
            for (int i = 0; i < manifest.RequiredCapabilities.Count; i++)
            {
                RegisterCapability(
                    manifest.RequiredCapabilities[i],
                    manifest.Path,
                    1,
                    1,
                    "manifest hostAbi.requires");
            }
        }

        private void RegisterCapabilityFromPackage(string? packageName, string modulePath, int line, int column)
        {
            if (TryInferCapabilityFromPackage(packageName, out var capability))
                RegisterCapability(capability, modulePath, line, column, $"package '{packageName}'");
        }

        private void RegisterCapabilityFromImport(string sourcePath, string modulePath, int line, int column)
        {
            if (TryInferCapabilityFromImport(sourcePath, out var capability))
                RegisterCapability(capability, modulePath, line, column, $"import \"{sourcePath}\"");
        }

        private void RegisterCapability(string capability, string modulePath, int line, int column, string context)
        {
            if (_requiredCapabilities.ContainsKey(capability))
                return;

            _requiredCapabilities[capability] = new CapabilityUse(capability, modulePath, line, column, context);
            Trace($"Capability required: {capability} ({FormatGraphPath(modulePath)} {context})");
        }

        private void ValidateTargetCapabilities()
        {
            foreach (var capability in _requiredCapabilities.Values.OrderBy(value => value.Capability, StringComparer.Ordinal))
            {
                if (CapabilityCatalog.IsSupported(_options.Target, capability.Capability))
                    continue;

                throw new CompilerException(
                    $"Capability '{capability.Capability}' is not available for target '{_options.Target.ToCliValue()}'. Required by {FormatGraphPath(capability.ModulePath)} via {capability.Context}.",
                    capability.Line,
                    capability.Column);
            }
        }

        private static bool TryInferCapabilityFromPackage(string? packageName, out string capability)
        {
            capability = string.Empty;
            if (string.IsNullOrWhiteSpace(packageName))
                return false;

            var parts = packageName
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.ToLowerInvariant())
                .ToArray();
            if (parts.Length < 2)
                return false;

            string candidate = parts[0] + "." + parts[1];
            if (!CapabilityCatalog.IsKnown(candidate))
                return false;

            capability = candidate;
            return true;
        }

        private static bool TryInferCapabilityFromImport(string sourcePath, out string capability)
        {
            capability = string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath))
                return false;

            var parts = sourcePath
                .Split(new[] { '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.ToLowerInvariant())
                .Where(part => part is not "." and not "..")
                .ToArray();

            int partCount = parts.Length;
            if (partCount == 0)
                return false;

            // Ignore .code suffix after splitting.
            if (parts[^1] == "code")
                partCount--;

            for (int i = 0; i + 1 < partCount; i++)
            {
                string candidate = parts[i] + "." + parts[i + 1];
                if (CapabilityCatalog.IsKnown(candidate))
                {
                    capability = candidate;
                    return true;
                }
            }

            return false;
        }

        private static void RegisterTopLevelName(
            Dictionary<string, Token> topLevelNames,
            Dictionary<string, Token> importBindings,
            string name,
            Token token)
        {
            if (importBindings.ContainsKey(name))
            {
                throw new CompilerException(
                    $"Module declaration '{name}' conflicts with an import binding.",
                    token.Line,
                    token.Column);
            }
            if (topLevelNames.ContainsKey(name))
            {
                throw new CompilerException(
                    $"Module declaration '{name}' is already declared in this module.",
                    token.Line,
                    token.Column);
            }
            topLevelNames[name] = token;
        }

        private static Stmt BuildAliasWrapper(ImportBinding binding, Stmt exportedDecl)
        {
            if (binding.Alias is null)
                throw new InvalidOperationException("Alias token is required.");

            if (exportedDecl is not FunctionDecl fn)
            {
                throw new CompilerException(
                    $"Alias import for '{binding.Name.Lexeme}' is only supported for functions",
                    binding.Alias.Line,
                    binding.Alias.Column);
            }
            if (fn.ReturnType is null)
            {
                throw new CompilerException(
                    $"Cannot alias function '{fn.Name.Lexeme}' without an explicit return type",
                    binding.Alias.Line,
                    binding.Alias.Column);
            }

            var aliasName = new Token(TokenType.Identifier, binding.Alias.Lexeme, null, binding.Alias.Line, binding.Alias.Column);
            var callName = new Token(TokenType.Identifier, fn.Name.Lexeme, null, binding.Name.Line, binding.Name.Column);
            var parameters = new List<Parameter>(fn.Parameters.Count);
            var callArgs = new List<Expr>(fn.Parameters.Count);
            for (int i = 0; i < fn.Parameters.Count; i++)
            {
                var sourceParam = fn.Parameters[i];
                var paramToken = new Token(TokenType.Identifier, sourceParam.Name.Lexeme, null, binding.Alias.Line, binding.Alias.Column);
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

        private static bool TryGetDeclarationName(Stmt declaration, out string name, out Token token)
        {
            switch (declaration)
            {
                case FunctionDecl fn:
                    name = fn.Name.Lexeme;
                    token = fn.Name;
                    return true;
                case ObjectDecl obj:
                    name = obj.Name.Lexeme;
                    token = obj.Name;
                    return true;
                case InterfaceDecl iface:
                    name = iface.Name.Lexeme;
                    token = iface.Name;
                    return true;
                default:
                    name = string.Empty;
                    token = new Token(TokenType.Identifier, string.Empty, null, 1, 1);
                    return false;
            }
        }

        private static Token GetDeclToken(Stmt declaration) => declaration switch
        {
            FunctionDecl fn => fn.Name,
            ObjectDecl obj => obj.Name,
            InterfaceDecl iface => iface.Name,
            _ => new Token(TokenType.Identifier, string.Empty, null, 1, 1)
        };

        private void Trace(string message)
        {
            if (!_options.TraceLinker)
                return;
            _options.TraceWriter?.Invoke(message);
        }

        private string FormatGraphPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string rootWithSeparator = _displayRoot.EndsWith(Path.DirectorySeparatorChar)
                ? _displayRoot
                : _displayRoot + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(_displayRoot, fullPath).Replace('\\', '/');
            return Path.GetFileName(fullPath);
        }

        private static string FormatBindingText(IReadOnlyList<ImportBinding> bindings)
        {
            var names = new List<string>(bindings.Count);
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                names.Add(binding.Alias is null
                    ? binding.Name.Lexeme
                    : $"{binding.Name.Lexeme} as {binding.Alias.Lexeme}");
            }

            if (names.Count == 1)
                return names[0];
            return "{ " + string.Join(", ", names) + " }";
        }

        private List<string> BuildImportChain(string nextPath)
        {
            var chain = new List<string>(_visitStack);
            if (chain.Count == 0 || !string.Equals(chain[^1], nextPath, StringComparison.OrdinalIgnoreCase))
                chain.Add(nextPath);
            return chain;
        }

        private List<string> BuildCycleChain(string repeatedPath)
        {
            int index = _visitStack.FindIndex(p => string.Equals(p, repeatedPath, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                var fallback = new List<string>(_visitStack) { repeatedPath };
                return fallback;
            }

            var cycle = _visitStack.Skip(index).ToList();
            cycle.Add(repeatedPath);
            return cycle;
        }

        private static CompilerException BuildChainedError(string message, int line, int column, IEnumerable<string> chain)
        {
            string chainText = string.Join(" -> ", chain.Select(FormatChainItem));
            return new CompilerException($"{message}{Environment.NewLine}Import chain: {chainText}", line, column);
        }

        private static string FormatChainItem(string path)
        {
            string file = Path.GetFileName(path);
            return string.IsNullOrEmpty(file) ? path : file;
        }

        private static IList<Stmt> RewriteTypeAliases(IList<Stmt> statements, IReadOnlyDictionary<string, string> aliases)
        {
            if (aliases.Count == 0) return statements;
            var rewritten = new List<Stmt>(statements.Count);
            for (int i = 0; i < statements.Count; i++)
            {
                rewritten.Add(RewriteStmt(statements[i], aliases));
            }
            return rewritten;
        }

        private static Stmt RewriteStmt(Stmt stmt, IReadOnlyDictionary<string, string> aliases) => stmt switch
        {
            VarDecl v => new VarDecl(RewriteTypeRef(v.Type, aliases), v.Name, v.Initializer is null ? null : RewriteExpr(v.Initializer, aliases), v.IsConstant),
            ExprStmt e => new ExprStmt(RewriteExpr(e.Expression, aliases)),
            Block b => new Block(b.Statements.Select(s => RewriteStmt(s, aliases)).ToList()),
            IfStmt i => new IfStmt(RewriteExpr(i.Condition, aliases), RewriteStmt(i.ThenBranch, aliases), i.ElseBranch is null ? null : RewriteStmt(i.ElseBranch, aliases)),
            WhileStmt w => new WhileStmt(RewriteExpr(w.Condition, aliases), RewriteStmt(w.Body, aliases)),
            ReturnStmt r => new ReturnStmt(r.Value is null ? null : RewriteExpr(r.Value, aliases)),
            PrintStmt p => new PrintStmt(RewriteExpr(p.Value, aliases)),
            PanicStmt p => new PanicStmt(RewriteExpr(p.Value, aliases)),
            ForStmt f => new ForStmt(
                f.Initializer is null ? null : RewriteStmt(f.Initializer, aliases),
                RewriteExpr(f.Condition, aliases),
                f.Increment is null ? null : RewriteExpr(f.Increment, aliases),
                RewriteStmt(f.Body, aliases)),
            ForeachStmt fe => new ForeachStmt(fe.Iterator, RewriteExpr(fe.Iterable, aliases), RewriteStmt(fe.Body, aliases)) { IsArray = fe.IsArray },
            FunctionDecl fn => new FunctionDecl(
                fn.Name,
                fn.ReturnType is null ? null : RewriteTypeRef(fn.ReturnType, aliases),
                fn.Parameters.Select(p => new Parameter(p.Type is null ? null : RewriteTypeRef(p.Type, aliases), p.Name)).ToList(),
                (Block)RewriteStmt(fn.Body, aliases)),
            ObjectDecl obj => new ObjectDecl(
                obj.Name,
                obj.Fields.Select(f => new FieldDecl(RewriteTypeRef(f.Type, aliases), f.Name)).ToList(),
                obj.Constructors.Select(c => new ConstructorDecl(
                    c.Keyword,
                    c.Parameters.Select(p => new Parameter(p.Type is null ? null : RewriteTypeRef(p.Type, aliases), p.Name)).ToList(),
                    (Block)RewriteStmt(c.Body, aliases))).ToList(),
                obj.Methods.Select(m => new MethodDecl(
                    m.Name,
                    m.ReturnType is null ? null : RewriteTypeRef(m.ReturnType, aliases),
                    m.Parameters.Select(p => new Parameter(p.Type is null ? null : RewriteTypeRef(p.Type, aliases), p.Name)).ToList(),
                    (Block)RewriteStmt(m.Body, aliases))).ToList()),
            InterfaceDecl iface => new InterfaceDecl(
                iface.Name,
                iface.Methods.Select(m => new InterfaceMethodDecl(
                    m.Name,
                    RewriteTypeRef(m.ReturnType, aliases),
                    m.Parameters.Select(p => new Parameter(p.Type is null ? null : RewriteTypeRef(p.Type, aliases), p.Name)).ToList())).ToList()),
            ImplementDecl impl => new ImplementDecl(
                RewriteTypeToken(impl.InterfaceName, aliases),
                RewriteTypeToken(impl.ObjectName, aliases),
                impl.Methods.Select(m => new ImplementMethodMap(
                    m.InterfaceMethodName,
                    m.Parameters.Select(p => new Parameter(p.Type is null ? null : RewriteTypeRef(p.Type, aliases), p.Name)).ToList(),
                    RewriteTypeToken(m.ViaObjectName, aliases),
                    m.ViaMethodName)).ToList()),
            _ => stmt
        };

        private static Expr RewriteExpr(Expr expr, IReadOnlyDictionary<string, string> aliases) => expr switch
        {
            Binary b => new Binary(RewriteExpr(b.Left, aliases), b.Operator, RewriteExpr(b.Right, aliases)),
            Unary u => new Unary(u.Operator, RewriteExpr(u.Right, aliases)),
            Literal l => l,
            InterpString s => new InterpString(s.Parts.Select(p => p is Expr e ? (object)RewriteExpr(e, aliases) : p).ToList(), s.Line, s.Column),
            ArrayLiteral a => new ArrayLiteral(a.Elements.Select(e => RewriteExpr(e, aliases)).ToList(), a.Line, a.Column),
            NewArrayExpr na => new NewArrayExpr(RewriteTypeRef(na.ElementType, aliases), RewriteExpr(na.Size, aliases), na.Line, na.Column),
            ArrayLengthExpr al => new ArrayLengthExpr(RewriteExpr(al.Target, aliases), al.DotToken),
            ArrayIndexExpr ai => new ArrayIndexExpr(RewriteExpr(ai.Array, aliases), RewriteExpr(ai.Index, aliases)),
            OptionalOrExpr o => new OptionalOrExpr(RewriteExpr(o.Optional, aliases), RewriteExpr(o.Fallback, aliases)),
            OptionalHasValueExpr o => new OptionalHasValueExpr(RewriteExpr(o.Target, aliases)),
            OptionalValueExpr o => new OptionalValueExpr(RewriteExpr(o.Target, aliases)),
            FieldAccessExpr f => new FieldAccessExpr(RewriteExpr(f.Target, aliases), f.Name),
            FieldSetExpr f => new FieldSetExpr((FieldAccessExpr)RewriteExpr(f.Target, aliases), RewriteExpr(f.Value, aliases)),
            NewObjectExpr no => new NewObjectExpr(RewriteTypeToken(no.TypeName, aliases), no.Arguments.Select(a => RewriteExpr(a, aliases)).ToList()),
            ArraySetExpr a => new ArraySetExpr((ArrayIndexExpr)RewriteExpr(a.Target, aliases), RewriteExpr(a.Value, aliases)),
            Variable v => v,
            Assign a => new Assign(a.Name, RewriteExpr(a.Value, aliases)),
            Call c => new Call(c.Callee, c.Arguments.Select(a => RewriteExpr(a, aliases)).ToList()),
            MethodCallExpr m => new MethodCallExpr(RewriteExpr(m.Target, aliases), m.MethodName, m.Arguments.Select(a => RewriteExpr(a, aliases)).ToList())
            {
                ResolvedMethodKey = m.ResolvedMethodKey,
                ResolvedInterfaceName = m.ResolvedInterfaceName,
                ResolvedInterfaceMethodKey = m.ResolvedInterfaceMethodKey,
                ResolvedReturnTypeRef = m.ResolvedReturnTypeRef is null ? null : RewriteTypeRef(m.ResolvedReturnTypeRef, aliases)
            },
            _ => expr
        };

        private static TypeRef RewriteTypeRef(TypeRef type, IReadOnlyDictionary<string, string> aliases)
        {
            string name = aliases.TryGetValue(type.Name, out var mapped) ? mapped : type.Name;
            if (type.TypeArguments.Count == 0)
                return name == type.Name ? type : new TypeRef(name, null, type.Line, type.Column);

            var args = type.TypeArguments.Select(t => RewriteTypeRef(t, aliases)).ToList();
            return new TypeRef(name, args, type.Line, type.Column);
        }

        private static Token RewriteTypeToken(Token token, IReadOnlyDictionary<string, string> aliases)
        {
            if (!aliases.TryGetValue(token.Lexeme, out var mapped))
                return token;
            return new Token(token.Type, mapped, token.Literal, token.Line, token.Column);
        }
    }

    private sealed record LinkResult(IList<Stmt> Statements, ModuleGraph Graph, IReadOnlyList<string> RequiredCapabilities);

    private sealed record ModuleInfo(
        string Path,
        string? PackageName,
        int PackageLine,
        int PackageColumn,
        List<ImportDecl> Imports,
        List<Stmt> LocalStatements,
        Dictionary<string, Stmt> ExportedDeclarations,
        List<Stmt> LinkedStatements,
        Dictionary<string, string> TypeAliases);
}
