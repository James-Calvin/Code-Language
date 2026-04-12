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
    public WebSceneMetadata? WebScene { get; }

    public ModuleCompileResult(
        byte[] bytecode,
        ModuleGraph graph,
        CompileTarget target,
        IReadOnlyList<string> requiredCapabilities,
        WebSceneMetadata? webScene)
    {
        Bytecode = bytecode;
        Graph = graph;
        Target = target;
        RequiredCapabilities = requiredCapabilities;
        WebScene = webScene;
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
        AnnotateModuleStatements(ast, InferPackageName(ast), "<source>");
        ast = LowerModuleSurfaceDeclarations(ast);
        ast = LowerInlineInterfaceImplementations(ast);
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
        var loweredStatements = LowerInlineInterfaceImplementations(linkResult.Statements);
        var typeChecker = new TypeChecker();
        typeChecker.Check(loweredStatements);
        var generator = new CodeGenerator();
        var generated = generator.GenerateWithMetadata(loweredStatements);
        var bytecode = generated.Bytecode;

        if (manifest is not null && string.Equals(manifest.Kind, "library", StringComparison.Ordinal))
        {
            WriteLibraryArtifact(manifest, fullEntryPath, compileOptions.Target, linkResult.Graph, linkResult.RequiredCapabilities, bytecode);
            if (compileOptions.TraceLinker)
            {
                string artifactName = CodeLibraryArtifactFormat.GetFileName(manifest.Name, manifest.Version, compileOptions.Target);
                compileOptions.TraceWriter?.Invoke($"Built library artifact {artifactName}");
            }
        }

        if (manifest is not null)
        {
            Action<string>? traceWriter = compileOptions.TraceLinker ? compileOptions.TraceWriter : null;
            var lockfile = PackageDependencyResolver.Resolve(manifest, compileOptions.Target, traceWriter);
            PackageDependencyResolver.WriteLockfile(manifest, lockfile, traceWriter);
        }

        return new ModuleCompileResult(
            bytecode,
            linkResult.Graph,
            compileOptions.Target,
            linkResult.RequiredCapabilities,
            generated.WebScene);
    }

    private static void WriteLibraryArtifact(
        PackageManifest manifest,
        string entryPath,
        CompileTarget target,
        ModuleGraph graph,
        IReadOnlyList<string> requiredCapabilities,
        byte[] bytecode)
    {
        string artifactPath = Path.Combine(
            manifest.PackageRoot,
            CodeLibraryArtifactFormat.GetFileName(manifest.Name, manifest.Version, target));

        string entry = NormalizeRelativePath(manifest.PackageRoot, entryPath);
        var exports = BuildArtifactExports(manifest, graph);

        var artifact = new CodeLibraryArtifact(
            manifest.Name,
            manifest.Version,
            manifest.Kind,
            target,
            entry,
            exports,
            requiredCapabilities,
            bytecode);

        CodeLibraryArtifactFormat.Write(artifactPath, artifact);
    }

    private static IReadOnlyDictionary<string, string> BuildArtifactExports(PackageManifest manifest, ModuleGraph graph)
    {
        if (manifest.Exports.Count > 0)
            return new Dictionary<string, string>(manifest.Exports, StringComparer.Ordinal);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < graph.Modules.Count; i++)
        {
            var module = graph.Modules[i];
            string modulePath = NormalizeRelativePath(manifest.PackageRoot, module.Path);
            for (int e = 0; e < module.Exports.Count; e++)
            {
                string export = module.Exports[e];
                if (!map.ContainsKey(export))
                    map[export] = modulePath;
            }
        }

        return map;
    }

    private static string NormalizeRelativePath(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(fullRoot, fullPath);
        return relative.Replace('\\', '/');
    }

    private static IList<Stmt> LowerInlineInterfaceImplementations(IList<Stmt> statements)
    {
        bool hasInlineImplementations = statements.Any(stmt => stmt is ObjectDecl obj && obj.InlineInterfaceMethods.Count > 0);
        if (!hasInlineImplementations)
            return statements;

        var interfaces = new Dictionary<string, InterfaceDecl>(StringComparer.Ordinal);
        for (int i = 0; i < statements.Count; i++)
        {
            if (statements[i] is InterfaceDecl iface && !interfaces.ContainsKey(iface.Name.Lexeme))
                interfaces[iface.Name.Lexeme] = iface;
        }

        var lowered = new List<Stmt>(statements.Count);
        var generatedImplements = new Dictionary<string, GeneratedImplementGroup>(StringComparer.Ordinal);

        for (int i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not ObjectDecl obj || obj.InlineInterfaceMethods.Count == 0)
            {
                lowered.Add(statements[i]);
                continue;
            }

            var methods = new List<MethodDecl>(obj.Methods);
            for (int m = 0; m < obj.InlineInterfaceMethods.Count; m++)
            {
                var inline = obj.InlineInterfaceMethods[m];
                if (!interfaces.TryGetValue(inline.InterfaceName.Lexeme, out var iface))
                {
                    throw new CompilerException(
                        $"Unknown interface '{inline.InterfaceName.Lexeme}'",
                        inline.InterfaceName.Line,
                        inline.InterfaceName.Column);
                }

                var interfaceMethod = FindMatchingInterfaceMethod(iface, inline);
                if (interfaceMethod is null)
                {
                    throw new CompilerException(
                        $"Interface '{iface.Name.Lexeme}' has no method '{inline.MethodName.Lexeme}' with this signature",
                        inline.MethodName.Line,
                        inline.MethodName.Column);
                }

                methods.Add(new MethodDecl(
                    inline.MethodName,
                    interfaceMethod.ReturnType,
                    inline.Parameters,
                    inline.Body,
                    inline.Visibility));

                string pairKey = $"{iface.Name.Lexeme}->{obj.Name.Lexeme}";
                if (!generatedImplements.TryGetValue(pairKey, out var group))
                {
                    group = new GeneratedImplementGroup(inline.InterfaceName, obj.Name, new List<ImplementMethodMap>());
                    generatedImplements[pairKey] = group;
                }

                group.Methods.Add(new ImplementMethodMap(
                    inline.MethodName,
                    inline.Parameters,
                    obj.Name,
                    inline.MethodName));
            }

            lowered.Add(CopyOrigin(obj, new ObjectDecl(obj.Name, obj.IsRecord, obj.Fields, obj.Constructors, methods)));
        }

        foreach (var key in generatedImplements.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            var group = generatedImplements[key];
            lowered.Add(new ImplementDecl(group.InterfaceName, group.ObjectName, group.Methods));
        }

        return lowered;
    }

    private static IList<Stmt> LowerModuleSurfaceDeclarations(IList<Stmt> statements)
    {
        bool hasWrappers = statements.Any(stmt => stmt is ExportDecl or VisibilityDecl);
        if (!hasWrappers)
            return statements;

        var lowered = new List<Stmt>(statements.Count);
        for (int i = 0; i < statements.Count; i++)
        {
            switch (statements[i])
            {
                case ExportDecl exportDecl:
                    lowered.Add(exportDecl.Declaration);
                    break;
                case VisibilityDecl visibilityDecl:
                    lowered.Add(visibilityDecl.Declaration);
                    break;
                default:
                    lowered.Add(statements[i]);
                    break;
            }
        }

        return lowered;
    }

    private static InterfaceMethodDecl? FindMatchingInterfaceMethod(InterfaceDecl iface, InlineImplementMethodDecl inline)
    {
        for (int i = 0; i < iface.Methods.Count; i++)
        {
            var method = iface.Methods[i];
            if (!string.Equals(method.Name.Lexeme, inline.MethodName.Lexeme, StringComparison.Ordinal))
                continue;
            if (method.Parameters.Count != inline.Parameters.Count)
                continue;

            bool matches = true;
            for (int p = 0; p < method.Parameters.Count; p++)
            {
                var interfaceParamType = method.Parameters[p].Type ?? throw new InvalidOperationException("Interface parameters must be typed.");
                var inlineParamType = inline.Parameters[p].Type ?? throw new InvalidOperationException("Inline implement parameters must be typed.");
                if (!TypeRefEquals(interfaceParamType, inlineParamType))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return method;
        }

        return null;
    }

    private static bool TypeRefEquals(TypeRef left, TypeRef right)
    {
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal))
            return false;
        if (left.TypeArguments.Count != right.TypeArguments.Count)
            return false;
        for (int i = 0; i < left.TypeArguments.Count; i++)
        {
            if (!TypeRefEquals(left.TypeArguments[i], right.TypeArguments[i]))
                return false;
        }
        return true;
    }

    private static string? InferPackageName(IList<Stmt> statements)
    {
        for (int i = 0; i < statements.Count; i++)
        {
            if (statements[i] is PackageDecl packageDecl)
                return packageDecl.Name;
        }

        return null;
    }

    private static void AnnotateModuleStatements(IList<Stmt> statements, string? packageName, string modulePath)
    {
        for (int i = 0; i < statements.Count; i++)
            AnnotateStmt(statements[i], packageName, modulePath);
    }

    private static T CopyOrigin<T>(Stmt source, T target) where T : Stmt
    {
        target.OriginPackageName = source.OriginPackageName;
        target.OriginModulePath = source.OriginModulePath;
        return target;
    }

    private static void AnnotateStmt(Stmt stmt, string? packageName, string modulePath)
    {
        stmt.OriginPackageName = packageName;
        stmt.OriginModulePath = modulePath;

        switch (stmt)
        {
            case ExportDecl exportDecl:
                AnnotateStmt(exportDecl.Declaration, packageName, modulePath);
                break;
            case VisibilityDecl visibilityDecl:
                AnnotateStmt(visibilityDecl.Declaration, packageName, modulePath);
                break;
            default:
                break;
        }
    }

    private sealed record GeneratedImplementGroup(Token InterfaceName, Token ObjectName, List<ImplementMethodMap> Methods);

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
                RegisterCapabilitiesFromStatements(module.LocalStatements, module.Path);
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
                        if (binding.IsNamespace)
                        {
                            if (import.IsExported)
                            {
                                throw new CompilerException(
                                    "Namespace imports cannot be exported.",
                                    binding.Alias?.Line ?? binding.Name.Line,
                                    binding.Alias?.Column ?? binding.Name.Column);
                            }

                            string aliasName = binding.Alias?.Lexeme ?? throw new CompilerException(
                                "Namespace imports require an alias.",
                                binding.Name.Line,
                                binding.Name.Column);

                            var namespaceMembers = new Dictionary<string, string>(StringComparer.Ordinal);
                            foreach (var importablePair in GetAccessibleImportableDeclarations(module.PackageName, dependency).OrderBy(pair => pair.Key, StringComparer.Ordinal))
                            {
                                if (importablePair.Value.Declaration is not FunctionDecl exportedFunction)
                                    continue;

                                string wrapperName = BuildNamespaceWrapperName(aliasName, exportedFunction.Name.Lexeme, binding.Alias ?? binding.Name);
                                var wrapper = BuildNamespaceWrapper(wrapperName, exportedFunction, binding.Alias ?? binding.Name);
                                AnnotateStmt(wrapper, module.PackageName, module.Path);
                                module.LinkedStatements.Add(wrapper);
                                namespaceMembers[importablePair.Key] = wrapperName;
                            }

                            module.NamespaceAliases[aliasName] = namespaceMembers;
                            continue;
                        }

                        if (!TryResolveAccessibleImportableDeclaration(module.PackageName, dependency, binding.Name, out var imported))
                        {
                            if (dependency.ImportableDeclarations.TryGetValue(binding.Name.Lexeme, out var inaccessibleDecl) &&
                                inaccessibleDecl.Visibility == DeclarationVisibility.Package)
                            {
                                var packageChain = BuildImportChain(dependencyPath);
                                throw BuildChainedError(
                                    $"Declaration '{binding.Name.Lexeme}' is package-visible in module '{Path.GetFileName(dependencyPath)}' and cannot be imported from package '{module.PackageName ?? "(none)"}'",
                                    binding.Name.Line,
                                    binding.Name.Column,
                                    packageChain);
                            }

                            var importChain = BuildImportChain(dependencyPath);
                            throw BuildChainedError(
                                $"Module '{Path.GetFileName(dependencyPath)}' does not export '{binding.Name.Lexeme}'",
                                binding.Name.Line,
                                binding.Name.Column,
                                importChain);
                        }

                        var exported = imported.Declaration;

                        if (binding.Alias is not null)
                        {
                            switch (exported)
                            {
                                case FunctionDecl:
                                    var wrapper = BuildAliasWrapper(binding, exported);
                                    AnnotateStmt(wrapper, module.PackageName, module.Path);
                                    module.LinkedStatements.Add(wrapper);
                                    break;
                                case ObjectDecl:
                                case InterfaceDecl:
                                case EnumDecl:
                                    module.TypeAliases[binding.Alias.Lexeme] = binding.Name.Lexeme;
                                    break;
                                default:
                                    throw new CompilerException(
                                        $"Alias import for '{binding.Name.Lexeme}' is not supported for this declaration kind",
                                        binding.Alias.Line,
                                        binding.Alias.Column);
                            }
                        }

                        if (import.IsExported)
                        {
                            if (imported.Visibility != DeclarationVisibility.Public)
                            {
                                throw new CompilerException(
                                    $"Cannot publicly re-export non-public declaration '{binding.Name.Lexeme}'",
                                    binding.Alias?.Line ?? binding.Name.Line,
                                    binding.Alias?.Column ?? binding.Name.Column);
                            }

                            string exportName = binding.Alias?.Lexeme ?? binding.Name.Lexeme;
                            if (module.ExportedDeclarations.ContainsKey(exportName))
                            {
                                throw new CompilerException(
                                    $"Module export '{exportName}' is already declared",
                                    binding.Alias?.Line ?? binding.Name.Line,
                                    binding.Alias?.Column ?? binding.Name.Column);
                            }

                            module.ExportedDeclarations[exportName] = exported;
                            module.ImportableDeclarations[exportName] = new ImportableDeclaration(exported, DeclarationVisibility.Public);
                        }
                    }
                }

                var rewrittenLocals = RewriteModuleStatements(module.LocalStatements, module.TypeAliases, module.NamespaceAliases);
                AnnotateModuleStatements(rewrittenLocals, module.PackageName, module.Path);
                module.LinkedStatements.AddRange(rewrittenLocals);
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
            var importables = new Dictionary<string, ImportableDeclaration>(StringComparer.Ordinal);
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
                        importables[exportName] = new ImportableDeclaration(exp.Declaration, DeclarationVisibility.Public);
                        RegisterTopLevelName(topLevelNames, importBindings, exportName, GetDeclToken(exp.Declaration));
                        locals.Add(exp.Declaration);
                        break;
                    }
                    case VisibilityDecl visibilityDecl:
                    {
                        string name = GetExportName(visibilityDecl.Declaration);
                        Token visibilityDeclToken = GetDeclToken(visibilityDecl.Declaration);

                        if (visibilityDecl.Visibility == DeclarationVisibility.Package && package is null)
                        {
                            throw new CompilerException(
                                "Package-visible declarations require a preceding package declaration.",
                                visibilityDecl.VisibilityToken.Line,
                                visibilityDecl.VisibilityToken.Column);
                        }

                        if (visibilityDecl.Visibility == DeclarationVisibility.Public)
                        {
                            if (exports.ContainsKey(name))
                            {
                                throw new CompilerException(
                                    $"Module export '{name}' is already declared",
                                    GetDeclLine(visibilityDecl.Declaration),
                                    GetDeclColumn(visibilityDecl.Declaration));
                            }

                            exports[name] = visibilityDecl.Declaration;
                        }

                        if (visibilityDecl.Visibility is DeclarationVisibility.Public or DeclarationVisibility.Package)
                            importables[name] = new ImportableDeclaration(visibilityDecl.Declaration, visibilityDecl.Visibility);

                        RegisterTopLevelName(topLevelNames, importBindings, name, visibilityDeclToken);
                        locals.Add(visibilityDecl.Declaration);
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
                importables,
                new List<Stmt>(),
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal));
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

        private static IReadOnlyDictionary<string, ImportableDeclaration> GetAccessibleImportableDeclarations(
            string? importerPackageName,
            ModuleInfo dependency)
        {
            var accessible = new Dictionary<string, ImportableDeclaration>(StringComparer.Ordinal);
            foreach (var pair in dependency.ImportableDeclarations)
            {
                if (pair.Value.Visibility == DeclarationVisibility.Public ||
                    (pair.Value.Visibility == DeclarationVisibility.Package &&
                     ArePackagesEqual(importerPackageName, dependency.PackageName)))
                {
                    accessible[pair.Key] = pair.Value;
                }
            }

            return accessible;
        }

        private static bool TryResolveAccessibleImportableDeclaration(
            string? importerPackageName,
            ModuleInfo dependency,
            Token importName,
            out ImportableDeclaration declaration)
        {
            if (dependency.ImportableDeclarations.TryGetValue(importName.Lexeme, out var resolvedDeclaration))
            {
                if (resolvedDeclaration.Visibility == DeclarationVisibility.Public)
                {
                    declaration = resolvedDeclaration;
                    return true;
                }

                if (resolvedDeclaration.Visibility == DeclarationVisibility.Package &&
                    ArePackagesEqual(importerPackageName, dependency.PackageName))
                {
                    declaration = resolvedDeclaration;
                    return true;
                }
            }

            declaration = default!;
            return false;
        }

        private static bool ArePackagesEqual(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            return string.Equals(left, right, StringComparison.Ordinal);
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

        private void RegisterCapabilitiesFromStatements(IReadOnlyList<Stmt> statements, string modulePath)
        {
            for (int i = 0; i < statements.Count; i++)
                ScanStmtForCapabilities(statements[i], modulePath);
        }

        private void ScanStmtForCapabilities(Stmt stmt, string modulePath)
        {
            switch (stmt)
            {
                case VarDecl v:
                    if (v.Initializer is not null)
                        ScanExprForCapabilities(v.Initializer, modulePath);
                    break;

                case ExprStmt e:
                    ScanExprForCapabilities(e.Expression, modulePath);
                    break;

                case Block b:
                    for (int i = 0; i < b.Statements.Count; i++)
                        ScanStmtForCapabilities(b.Statements[i], modulePath);
                    break;

                case IfStmt i:
                    ScanExprForCapabilities(i.Condition, modulePath);
                    ScanStmtForCapabilities(i.ThenBranch, modulePath);
                    if (i.ElseBranch is not null)
                        ScanStmtForCapabilities(i.ElseBranch, modulePath);
                    break;

                case SwitchStmt s:
                    ScanExprForCapabilities(s.Value, modulePath);
                    for (int i = 0; i < s.Cases.Count; i++)
                    {
                        ScanExprForCapabilities(s.Cases[i].Value, modulePath);
                        ScanStmtForCapabilities(s.Cases[i].Body, modulePath);
                    }
                    if (s.DefaultBranch is not null)
                        ScanStmtForCapabilities(s.DefaultBranch, modulePath);
                    break;

                case WhileStmt w:
                    ScanExprForCapabilities(w.Condition, modulePath);
                    ScanStmtForCapabilities(w.Body, modulePath);
                    break;

                case ReturnStmt r:
                    if (r.Value is not null)
                        ScanExprForCapabilities(r.Value, modulePath);
                    break;

                case PrintStmt p:
                    RegisterCapability(
                        HostAbiCatalog.StandardInputOutputPrint.Capability,
                        modulePath,
                        GetExprLine(p.Value),
                        GetExprColumn(p.Value),
                        "print statement");
                    ScanExprForCapabilities(p.Value, modulePath);
                    break;

                case PanicStmt p:
                    ScanExprForCapabilities(p.Value, modulePath);
                    break;

                case YieldStmt y:
                    ScanExprForCapabilities(y.Value, modulePath);
                    break;

                case BreakStmt:
                case ContinueStmt:
                    break;

                case ForStmt f:
                    if (f.Initializer is not null)
                        ScanStmtForCapabilities(f.Initializer, modulePath);
                    ScanExprForCapabilities(f.Condition, modulePath);
                    if (f.Increment is not null)
                        ScanExprForCapabilities(f.Increment, modulePath);
                    ScanStmtForCapabilities(f.Body, modulePath);
                    break;

                case ForeachStmt fe:
                    ScanExprForCapabilities(fe.Iterable, modulePath);
                    ScanStmtForCapabilities(fe.Body, modulePath);
                    break;

                case FunctionDecl fn:
                    ScanStmtForCapabilities(fn.Body, modulePath);
                    break;

                case ObjectDecl obj:
                    for (int i = 0; i < obj.Constructors.Count; i++)
                        ScanStmtForCapabilities(obj.Constructors[i].Body, modulePath);
                    for (int i = 0; i < obj.Methods.Count; i++)
                        ScanStmtForCapabilities(obj.Methods[i].Body, modulePath);
                    for (int i = 0; i < obj.InlineInterfaceMethods.Count; i++)
                        ScanStmtForCapabilities(obj.InlineInterfaceMethods[i].Body, modulePath);
                    break;

                case ExportDecl ex:
                    ScanStmtForCapabilities(ex.Declaration, modulePath);
                    break;

                default:
                    break;
            }
        }

        private void ScanExprForCapabilities(Expr expr, string modulePath)
        {
            switch (expr)
            {
                case Binary b:
                    ScanExprForCapabilities(b.Left, modulePath);
                    ScanExprForCapabilities(b.Right, modulePath);
                    break;
                case Unary u:
                    ScanExprForCapabilities(u.Right, modulePath);
                    break;
                case InterpString s:
                    for (int i = 0; i < s.Parts.Count; i++)
                    {
                        if (s.Parts[i] is Expr partExpr)
                            ScanExprForCapabilities(partExpr, modulePath);
                    }
                    break;
                case ArrayLiteral a:
                    for (int i = 0; i < a.Elements.Count; i++)
                        ScanExprForCapabilities(a.Elements[i], modulePath);
                    break;
                case NewArrayExpr na:
                    ScanExprForCapabilities(na.Size, modulePath);
                    break;
                case ArrayLengthExpr al:
                    ScanExprForCapabilities(al.Target, modulePath);
                    break;
                case ArrayIndexExpr ai:
                    ScanExprForCapabilities(ai.Array, modulePath);
                    ScanExprForCapabilities(ai.Index, modulePath);
                    break;
                case OptionalOrExpr o:
                    ScanExprForCapabilities(o.Optional, modulePath);
                    ScanExprForCapabilities(o.Fallback, modulePath);
                    break;
                case OptionalHasValueExpr o:
                    ScanExprForCapabilities(o.Target, modulePath);
                    break;
                case OptionalValueExpr o:
                    ScanExprForCapabilities(o.Target, modulePath);
                    break;
                case FallibleErrorExpr f:
                    for (int i = 0; i < f.Arguments.Count; i++)
                        ScanExprForCapabilities(f.Arguments[i], modulePath);
                    break;
                case OnErrorExpr o:
                    ScanExprForCapabilities(o.Fallible, modulePath);
                    ScanStmtForCapabilities(o.Handler, modulePath);
                    break;
                case CastExpr c:
                    ScanExprForCapabilities(c.Value, modulePath);
                    break;
                case FieldAccessExpr f:
                    ScanExprForCapabilities(f.Target, modulePath);
                    break;
                case FieldSetExpr f:
                    ScanExprForCapabilities(f.Target.Target, modulePath);
                    ScanExprForCapabilities(f.Value, modulePath);
                    break;
                case NewObjectExpr n:
                    for (int i = 0; i < n.Arguments.Count; i++)
                        ScanExprForCapabilities(n.Arguments[i], modulePath);
                    break;
                case ArraySetExpr a:
                    ScanExprForCapabilities(a.Target.Array, modulePath);
                    ScanExprForCapabilities(a.Target.Index, modulePath);
                    ScanExprForCapabilities(a.Value, modulePath);
                    break;
                case Assign a:
                    ScanExprForCapabilities(a.Value, modulePath);
                    break;
                case CompoundAssignExpr c:
                    ScanExprForCapabilities(c.Target, modulePath);
                    ScanExprForCapabilities(c.Value, modulePath);
                    break;
                case Call c:
                    if (HostAbiCatalog.TryGetIntrinsic(c.Callee.Lexeme, out var intrinsic))
                    {
                        RegisterCapability(
                            intrinsic.Symbol.Capability,
                            modulePath,
                            c.Callee.Line,
                            c.Callee.Column,
                            $"call '{c.Callee.Lexeme}()'");
                    }
                    for (int i = 0; i < c.Arguments.Count; i++)
                        ScanExprForCapabilities(c.Arguments[i], modulePath);
                    break;
                case MethodCallExpr m:
                    ScanExprForCapabilities(m.Target, modulePath);
                    for (int i = 0; i < m.Arguments.Count; i++)
                        ScanExprForCapabilities(m.Arguments[i], modulePath);
                    break;
                default:
                    break;
            }
        }

        private static int GetExprLine(Expr expr) => expr switch
        {
            Literal l => l.Line,
            InterpString i => i.Line,
            ArrayLiteral a => a.Line,
            NewArrayExpr n => n.Line,
            NewCollectionExpr n => n.Line,
            Variable v => v.Name.Line,
            Assign a => a.Name.Line,
            CompoundAssignExpr c => GetExprLine(c.Target),
            Call c => c.Callee.Line,
            MethodCallExpr m => m.MethodName.Line,
            FieldAccessExpr f => f.Name.Line,
            FieldSetExpr f => f.Target.Name.Line,
            NewObjectExpr n => n.TypeName.Line,
            Binary b => GetExprLine(b.Left),
            Unary u => GetExprLine(u.Right),
            ArrayIndexExpr a => GetExprLine(a.Array),
            ArraySetExpr a => GetExprLine(a.Target.Array),
            OptionalOrExpr o => GetExprLine(o.Optional),
            OptionalHasValueExpr o => GetExprLine(o.Target),
            OptionalValueExpr o => GetExprLine(o.Target),
            FallibleErrorExpr e => e.ErrorToken.Line,
            OnErrorExpr o => GetExprLine(o.Fallible),
            CastExpr c => GetExprLine(c.Value),
            ArrayLengthExpr a => GetExprLine(a.Target),
            _ => 1
        };

        private static int GetExprColumn(Expr expr) => expr switch
        {
            Literal l => l.Column,
            InterpString i => i.Column,
            ArrayLiteral a => a.Column,
            NewArrayExpr n => n.Column,
            NewCollectionExpr n => n.Column,
            Variable v => v.Name.Column,
            Assign a => a.Name.Column,
            CompoundAssignExpr c => GetExprColumn(c.Target),
            Call c => c.Callee.Column,
            MethodCallExpr m => m.MethodName.Column,
            FieldAccessExpr f => f.Name.Column,
            FieldSetExpr f => f.Target.Name.Column,
            NewObjectExpr n => n.TypeName.Column,
            Binary b => GetExprColumn(b.Left),
            Unary u => GetExprColumn(u.Right),
            ArrayIndexExpr a => GetExprColumn(a.Array),
            ArraySetExpr a => GetExprColumn(a.Target.Array),
            OptionalOrExpr o => GetExprColumn(o.Optional),
            OptionalHasValueExpr o => GetExprColumn(o.Target),
            OptionalValueExpr o => GetExprColumn(o.Target),
            FallibleErrorExpr e => e.ErrorToken.Column,
            OnErrorExpr o => GetExprColumn(o.Fallible),
            CastExpr c => GetExprColumn(c.Value),
            ArrayLengthExpr a => GetExprColumn(a.Target),
            _ => 1
        };

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

        private static string BuildNamespaceWrapperName(string aliasName, string exportName, Token token)
        {
            static string Sanitize(string value)
            {
                var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
                return new string(chars);
            }

            return $"__namespace_{Sanitize(aliasName)}_{Sanitize(exportName)}_{token.Line}_{token.Column}";
        }

        private static Stmt BuildNamespaceWrapper(string wrapperName, FunctionDecl exportedFunction, Token token)
        {
            var aliasName = new Token(TokenType.Identifier, wrapperName, null, token.Line, token.Column);
            var callName = new Token(TokenType.Identifier, exportedFunction.Name.Lexeme, null, exportedFunction.Name.Line, exportedFunction.Name.Column);
            var parameters = new List<Parameter>(exportedFunction.Parameters.Count);
            var callArgs = new List<Expr>(exportedFunction.Parameters.Count);
            for (int i = 0; i < exportedFunction.Parameters.Count; i++)
            {
                var sourceParam = exportedFunction.Parameters[i];
                var parameterToken = new Token(TokenType.Identifier, sourceParam.Name.Lexeme, null, token.Line, token.Column);
                parameters.Add(new Parameter(sourceParam.Type, parameterToken));
                callArgs.Add(new Variable(parameterToken));
            }

            var call = new Call(callName, callArgs);
            var body = exportedFunction.ReturnType is null
                ? new Block([new ExprStmt(call)])
                : new Block([new ReturnStmt(call)]);
            return new FunctionDecl(aliasName, exportedFunction.ReturnType, parameters, body);
        }

        private static string GetExportName(Stmt declaration) => declaration switch
        {
            FunctionDecl fn => fn.Name.Lexeme,
            ObjectDecl obj => obj.Name.Lexeme,
            InterfaceDecl iface => iface.Name.Lexeme,
            EnumDecl enumDecl => enumDecl.Name.Lexeme,
            _ => throw new CompilerException(
                "Only function/object/interface/enum declarations can be exported",
                GetDeclLine(declaration),
                GetDeclColumn(declaration))
        };

        private static int GetDeclLine(Stmt declaration) => declaration switch
        {
            FunctionDecl fn => fn.Name.Line,
            ObjectDecl obj => obj.Name.Line,
            InterfaceDecl iface => iface.Name.Line,
            EnumDecl enumDecl => enumDecl.Name.Line,
            _ => 1
        };

        private static int GetDeclColumn(Stmt declaration) => declaration switch
        {
            FunctionDecl fn => fn.Name.Column,
            ObjectDecl obj => obj.Name.Column,
            InterfaceDecl iface => iface.Name.Column,
            EnumDecl enumDecl => enumDecl.Name.Column,
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
                case EnumDecl enumDecl:
                    name = enumDecl.Name.Lexeme;
                    token = enumDecl.Name;
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
            EnumDecl enumDecl => enumDecl.Name,
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
                if (binding.IsNamespace)
                {
                    names.Add($"everything as {binding.Alias?.Lexeme ?? binding.Name.Lexeme}");
                }
                else
                {
                    names.Add(binding.Alias is null
                        ? binding.Name.Lexeme
                        : $"{binding.Name.Lexeme} as {binding.Alias.Lexeme}");
                }
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

        private static IList<Stmt> RewriteModuleStatements(
            IList<Stmt> statements,
            IReadOnlyDictionary<string, string> typeAliases,
            IReadOnlyDictionary<string, Dictionary<string, string>> namespaceAliases)
        {
            if (typeAliases.Count == 0 && namespaceAliases.Count == 0)
                return statements;

            var rewritten = new List<Stmt>(statements.Count);
            for (int i = 0; i < statements.Count; i++)
            {
                rewritten.Add(RewriteStmt(statements[i], typeAliases, namespaceAliases));
            }
            return rewritten;
        }

        private static Stmt RewriteStmt(
            Stmt stmt,
            IReadOnlyDictionary<string, string> typeAliases,
            IReadOnlyDictionary<string, Dictionary<string, string>> namespaceAliases) => stmt switch
        {
            VarDecl v => new VarDecl(RewriteTypeRef(v.Type, typeAliases), v.Name, v.Initializer is null ? null : RewriteExpr(v.Initializer, typeAliases, namespaceAliases), v.IsConstant),
            ExprStmt e => new ExprStmt(RewriteExpr(e.Expression, typeAliases, namespaceAliases)),
            Block b => new Block(b.Statements.Select(s => RewriteStmt(s, typeAliases, namespaceAliases)).ToList()),
            IfStmt i => new IfStmt(
                RewriteExpr(i.Condition, typeAliases, namespaceAliases),
                RewriteStmt(i.ThenBranch, typeAliases, namespaceAliases),
                i.ElseBranch is null ? null : RewriteStmt(i.ElseBranch, typeAliases, namespaceAliases)),
            SwitchStmt s => new SwitchStmt(
                s.Keyword,
                RewriteExpr(s.Value, typeAliases, namespaceAliases),
                s.Cases.Select(c => new SwitchCase(
                    c.Keyword,
                    RewriteExpr(c.Value, typeAliases, namespaceAliases),
                    RewriteStmt(c.Body, typeAliases, namespaceAliases))).ToList(),
                s.DefaultBranch is null ? null : RewriteStmt(s.DefaultBranch, typeAliases, namespaceAliases)),
            WhileStmt w => new WhileStmt(RewriteExpr(w.Condition, typeAliases, namespaceAliases), RewriteStmt(w.Body, typeAliases, namespaceAliases)),
            ReturnStmt r => new ReturnStmt(r.Value is null ? null : RewriteExpr(r.Value, typeAliases, namespaceAliases)),
            PrintStmt p => new PrintStmt(RewriteExpr(p.Value, typeAliases, namespaceAliases)),
            PanicStmt p => new PanicStmt(RewriteExpr(p.Value, typeAliases, namespaceAliases)),
            YieldStmt y => new YieldStmt(y.Keyword, RewriteExpr(y.Value, typeAliases, namespaceAliases)),
            BreakStmt b => b,
            ContinueStmt c => c,
            ForStmt f => new ForStmt(
                f.Initializer is null ? null : RewriteStmt(f.Initializer, typeAliases, namespaceAliases),
                RewriteExpr(f.Condition, typeAliases, namespaceAliases),
                f.Increment is null ? null : RewriteExpr(f.Increment, typeAliases, namespaceAliases),
                RewriteStmt(f.Body, typeAliases, namespaceAliases)),
            ForeachStmt fe => new ForeachStmt(fe.Iterator, RewriteExpr(fe.Iterable, typeAliases, namespaceAliases), RewriteStmt(fe.Body, typeAliases, namespaceAliases)) { IsArray = fe.IsArray },
            FunctionDecl fn => new FunctionDecl(
                fn.Name,
                fn.ReturnType is null ? null : RewriteTypeRef(fn.ReturnType, typeAliases),
                fn.Parameters.Select(p => new Parameter(p.Type is null ? null : RewriteTypeRef(p.Type, typeAliases), p.Name)).ToList(),
                (Block)RewriteStmt(fn.Body, typeAliases, namespaceAliases)),
            EnumDecl enumDecl => enumDecl,
            ObjectDecl obj => new ObjectDecl(
                obj.Name,
                obj.IsRecord,
                obj.Fields.Select(f => new FieldDecl(RewriteTypeRef(f.Type, typeAliases), f.Name, f.Visibility)).ToList(),
                obj.Constructors.Select(c => new ConstructorDecl(
                    c.Keyword,
                    c.Parameters.Select(p => new Parameter(p.Type is null ? null : RewriteTypeRef(p.Type, typeAliases), p.Name)).ToList(),
                    (Block)RewriteStmt(c.Body, typeAliases, namespaceAliases),
                    c.Visibility)).ToList(),
                obj.Methods.Select(m => new MethodDecl(
                    m.Name,
                    m.ReturnType is null ? null : RewriteTypeRef(m.ReturnType, typeAliases),
                    m.Parameters.Select(p => new Parameter(p.Type is null ? null : RewriteTypeRef(p.Type, typeAliases), p.Name)).ToList(),
                    (Block)RewriteStmt(m.Body, typeAliases, namespaceAliases),
                    m.Visibility)).ToList(),
                obj.InlineInterfaceMethods.Select(m => new InlineImplementMethodDecl(
                    RewriteTypeToken(m.InterfaceName, typeAliases),
                    m.MethodName,
                    m.Parameters.Select(p => new Parameter(p.Type is null ? null : RewriteTypeRef(p.Type, typeAliases), p.Name)).ToList(),
                    (Block)RewriteStmt(m.Body, typeAliases, namespaceAliases),
                    m.Visibility)).ToList()),
            InterfaceDecl iface => new InterfaceDecl(
                iface.Name,
                iface.Methods.Select(m => new InterfaceMethodDecl(
                    m.Name,
                    RewriteTypeRef(m.ReturnType, typeAliases),
                    m.Parameters.Select(p => new Parameter(p.Type is null ? null : RewriteTypeRef(p.Type, typeAliases), p.Name)).ToList())).ToList()),
            ImplementDecl impl => new ImplementDecl(
                RewriteTypeToken(impl.InterfaceName, typeAliases),
                RewriteTypeToken(impl.ObjectName, typeAliases),
                impl.Methods.Select(m => new ImplementMethodMap(
                    m.InterfaceMethodName,
                    m.Parameters.Select(p => new Parameter(p.Type is null ? null : RewriteTypeRef(p.Type, typeAliases), p.Name)).ToList(),
                    RewriteTypeToken(m.ViaObjectName, typeAliases),
                    m.ViaMethodName)).ToList()),
            _ => stmt
        };

        private static Expr RewriteExpr(
            Expr expr,
            IReadOnlyDictionary<string, string> typeAliases,
            IReadOnlyDictionary<string, Dictionary<string, string>> namespaceAliases) => expr switch
        {
            Binary b => new Binary(RewriteExpr(b.Left, typeAliases, namespaceAliases), b.Operator, RewriteExpr(b.Right, typeAliases, namespaceAliases)),
            Unary u => new Unary(u.Operator, RewriteExpr(u.Right, typeAliases, namespaceAliases)),
            CastExpr c => new CastExpr(RewriteExpr(c.Value, typeAliases, namespaceAliases), c.AsToken, RewriteTypeRef(c.TargetType, typeAliases))
            {
                ResolvedIsEnumCast = c.ResolvedIsEnumCast,
                ResolvedRuntimeKind = c.ResolvedRuntimeKind
            },
            Literal l => l,
            InterpString s => new InterpString(s.Parts.Select(p => p is Expr e ? (object)RewriteExpr(e, typeAliases, namespaceAliases) : p).ToList(), s.Line, s.Column),
            ArrayLiteral a => new ArrayLiteral(a.Elements.Select(e => RewriteExpr(e, typeAliases, namespaceAliases)).ToList(), a.Line, a.Column)
            {
                ResolvedTypeRef = a.ResolvedTypeRef is null ? null : RewriteTypeRef(a.ResolvedTypeRef, typeAliases)
            },
            NewArrayExpr na => new NewArrayExpr(RewriteTypeRef(na.ElementType, typeAliases), RewriteExpr(na.Size, typeAliases, namespaceAliases), na.Line, na.Column),
            NewCollectionExpr nc => new NewCollectionExpr(RewriteTypeRef(nc.CollectionType, typeAliases), nc.Line, nc.Column),
            ArrayLengthExpr al => new ArrayLengthExpr(RewriteExpr(al.Target, typeAliases, namespaceAliases), al.DotToken),
            ArrayIndexExpr ai => new ArrayIndexExpr(RewriteExpr(ai.Array, typeAliases, namespaceAliases), RewriteExpr(ai.Index, typeAliases, namespaceAliases))
            {
                ResolvedElementTypeRef = ai.ResolvedElementTypeRef is null ? null : RewriteTypeRef(ai.ResolvedElementTypeRef, typeAliases)
            },
            OptionalOrExpr o => new OptionalOrExpr(RewriteExpr(o.Optional, typeAliases, namespaceAliases), RewriteExpr(o.Fallback, typeAliases, namespaceAliases)),
            OptionalHasValueExpr o => new OptionalHasValueExpr(RewriteExpr(o.Target, typeAliases, namespaceAliases)),
            OptionalValueExpr o => new OptionalValueExpr(RewriteExpr(o.Target, typeAliases, namespaceAliases)),
            FallibleErrorExpr e => new FallibleErrorExpr(e.ErrorToken, e.Arguments.Select(a => RewriteExpr(a, typeAliases, namespaceAliases)).ToList())
            {
                ResolvedFallibleTypeRef = e.ResolvedFallibleTypeRef is null ? null : RewriteTypeRef(e.ResolvedFallibleTypeRef, typeAliases),
                ResolvedUsesDefaultIntegerCode = e.ResolvedUsesDefaultIntegerCode
            },
            OnErrorExpr o => new OnErrorExpr(
                RewriteExpr(o.Fallible, typeAliases, namespaceAliases),
                o.OnToken,
                (Block)RewriteStmt(o.Handler, typeAliases, namespaceAliases))
            {
                ResolvedSuccessTypeRef = o.ResolvedSuccessTypeRef is null ? null : RewriteTypeRef(o.ResolvedSuccessTypeRef, typeAliases),
                ResolvedErrorCodeTypeRef = o.ResolvedErrorCodeTypeRef is null ? null : RewriteTypeRef(o.ResolvedErrorCodeTypeRef, typeAliases)
            },
            FieldAccessExpr f => RewriteFieldAccessExpr(f, typeAliases, namespaceAliases),
            FieldSetExpr f => new FieldSetExpr((FieldAccessExpr)RewriteExpr(f.Target, typeAliases, namespaceAliases), RewriteExpr(f.Value, typeAliases, namespaceAliases)),
            NewObjectExpr no => new NewObjectExpr(RewriteTypeToken(no.TypeName, typeAliases), no.Arguments.Select(a => RewriteExpr(a, typeAliases, namespaceAliases)).ToList()),
            ArraySetExpr a => new ArraySetExpr((ArrayIndexExpr)RewriteExpr(a.Target, typeAliases, namespaceAliases), RewriteExpr(a.Value, typeAliases, namespaceAliases)),
            Variable v => RewriteVariableExpr(v, namespaceAliases),
            Assign a => RewriteAssignExpr(a, typeAliases, namespaceAliases),
            CompoundAssignExpr c => new CompoundAssignExpr(RewriteExpr(c.Target, typeAliases, namespaceAliases), c.Operator, RewriteExpr(c.Value, typeAliases, namespaceAliases)),
            Call c => new Call(c.Callee, c.Arguments.Select(a => RewriteExpr(a, typeAliases, namespaceAliases)).ToList()),
            MethodCallExpr m => RewriteMethodCallExpr(m, typeAliases, namespaceAliases),
            _ => expr
        };

        private static Variable RewriteVariableExpr(Variable variable, IReadOnlyDictionary<string, Dictionary<string, string>> namespaceAliases)
        {
            if (namespaceAliases.ContainsKey(variable.Name.Lexeme))
            {
                throw new CompilerException(
                    $"Namespace '{variable.Name.Lexeme}' cannot be used as a runtime value.",
                    variable.Name.Line,
                    variable.Name.Column);
            }

            return variable;
        }

        private static Assign RewriteAssignExpr(
            Assign assign,
            IReadOnlyDictionary<string, string> typeAliases,
            IReadOnlyDictionary<string, Dictionary<string, string>> namespaceAliases)
        {
            if (namespaceAliases.ContainsKey(assign.Name.Lexeme))
            {
                throw new CompilerException(
                    $"Namespace '{assign.Name.Lexeme}' cannot be assigned to.",
                    assign.Name.Line,
                    assign.Name.Column);
            }

            return new Assign(assign.Name, RewriteExpr(assign.Value, typeAliases, namespaceAliases));
        }

        private static Expr RewriteFieldAccessExpr(
            FieldAccessExpr fieldAccess,
            IReadOnlyDictionary<string, string> typeAliases,
            IReadOnlyDictionary<string, Dictionary<string, string>> namespaceAliases)
        {
            if (fieldAccess.Target is Variable variable &&
                namespaceAliases.TryGetValue(variable.Name.Lexeme, out _))
            {
                throw new CompilerException(
                    $"Namespace member '{variable.Name.Lexeme}.{fieldAccess.Name.Lexeme}' cannot be used as a runtime value.",
                    fieldAccess.Name.Line,
                    fieldAccess.Name.Column);
            }

            Expr rewrittenTarget = RewriteExpr(fieldAccess.Target, typeAliases, namespaceAliases);
            if (fieldAccess.Target is Variable typeAliasVariable &&
                typeAliases.TryGetValue(typeAliasVariable.Name.Lexeme, out var mappedTypeName))
            {
                var mappedToken = new Token(TokenType.Identifier, mappedTypeName, null, typeAliasVariable.Name.Line, typeAliasVariable.Name.Column);
                rewrittenTarget = new Variable(mappedToken);
            }

            return new FieldAccessExpr(rewrittenTarget, fieldAccess.Name)
            {
                ResolvedEnumTypeRef = fieldAccess.ResolvedEnumTypeRef is null ? null : RewriteTypeRef(fieldAccess.ResolvedEnumTypeRef, typeAliases),
                ResolvedEnumValue = fieldAccess.ResolvedEnumValue,
                ResolvedFallibleErrorFieldTypeRef = fieldAccess.ResolvedFallibleErrorFieldTypeRef is null ? null : RewriteTypeRef(fieldAccess.ResolvedFallibleErrorFieldTypeRef, typeAliases)
            };
        }

        private static Expr RewriteMethodCallExpr(
            MethodCallExpr methodCall,
            IReadOnlyDictionary<string, string> typeAliases,
            IReadOnlyDictionary<string, Dictionary<string, string>> namespaceAliases)
        {
            if (methodCall.Target is Variable variable &&
                namespaceAliases.TryGetValue(variable.Name.Lexeme, out var namespaceMembers))
            {
                if (!namespaceMembers.TryGetValue(methodCall.MethodName.Lexeme, out var wrapperName))
                {
                    throw new CompilerException(
                        $"Namespace '{variable.Name.Lexeme}' does not export function '{methodCall.MethodName.Lexeme}'.",
                        methodCall.MethodName.Line,
                        methodCall.MethodName.Column);
                }

                var callee = new Token(TokenType.Identifier, wrapperName, null, methodCall.MethodName.Line, methodCall.MethodName.Column);
                return new Call(callee, methodCall.Arguments.Select(a => RewriteExpr(a, typeAliases, namespaceAliases)).ToList());
            }

            return new MethodCallExpr(
                RewriteExpr(methodCall.Target, typeAliases, namespaceAliases),
                methodCall.MethodName,
                methodCall.Arguments.Select(a => RewriteExpr(a, typeAliases, namespaceAliases)).ToList())
            {
                ResolvedBuiltInCollectionMethodName = methodCall.ResolvedBuiltInCollectionMethodName,
                ResolvedMethodKey = methodCall.ResolvedMethodKey,
                ResolvedInterfaceName = methodCall.ResolvedInterfaceName,
                ResolvedInterfaceMethodKey = methodCall.ResolvedInterfaceMethodKey,
                ResolvedReturnTypeRef = methodCall.ResolvedReturnTypeRef is null ? null : RewriteTypeRef(methodCall.ResolvedReturnTypeRef, typeAliases)
            };
        }

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

    private sealed record ImportableDeclaration(Stmt Declaration, DeclarationVisibility Visibility);

    private sealed record ModuleInfo(
        string Path,
        string? PackageName,
        int PackageLine,
        int PackageColumn,
        List<ImportDecl> Imports,
        List<Stmt> LocalStatements,
        Dictionary<string, Stmt> ExportedDeclarations,
        Dictionary<string, ImportableDeclaration> ImportableDeclarations,
        List<Stmt> LinkedStatements,
        Dictionary<string, string> TypeAliases,
        Dictionary<string, Dictionary<string, string>> NamespaceAliases);
}
