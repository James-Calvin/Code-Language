namespace ConsoleApp1.Compiler;

/// <summary>Closes user-defined generic types before the existing concrete type pipeline runs.</summary>
static class GenericMonomorphizer
{
    public static IList<Stmt> Lower(IList<Stmt> statements)
    {
        var pass = new Pass(statements);
        return pass.Run();
    }

    private sealed class Pass
    {
        private readonly IList<Stmt> _source;
        private readonly Dictionary<string, ObjectDecl> _objects = new(StringComparer.Ordinal);
        private readonly Dictionary<string, InterfaceDecl> _interfaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _specializedNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reservedNames = new(StringComparer.Ordinal);
        private readonly List<Stmt> _generated = [];
        private readonly List<ImplementDecl> _openImplements = [];
        private int _specializationDepth;

        public Pass(IList<Stmt> source)
        {
            _source = source;
            foreach (var stmt in source)
            {
                if (stmt is ObjectDecl obj)
                {
                    _reservedNames.Add(obj.Name.Lexeme);
                    if (obj.TypeParameters.Count > 0) _objects[obj.Name.Lexeme] = obj;
                }
                else if (stmt is InterfaceDecl iface)
                {
                    _reservedNames.Add(iface.Name.Lexeme);
                    if (iface.TypeParameters.Count > 0) _interfaces[iface.Name.Lexeme] = iface;
                }
                else if (stmt is ImplementDecl impl && impl.ObjectType.TypeArguments.Count > 0)
                    _openImplements.Add(impl);
            }
        }

        public IList<Stmt> Run()
        {
            if (_objects.Count == 0 && _interfaces.Count == 0) return _source;
            var result = new List<Stmt>();
            foreach (var stmt in _source)
            {
                if (stmt is ObjectDecl { TypeParameters.Count: > 0 } or InterfaceDecl { TypeParameters.Count: > 0 })
                    continue;
                if (stmt is ImplementDecl impl && _openImplements.Contains(impl))
                    continue;
                result.Add(RewriteStmt(stmt, Empty));
            }
            result.AddRange(_generated);
            return result;
        }

        private static readonly IReadOnlyDictionary<string, TypeRef> Empty = new Dictionary<string, TypeRef>();

        private TypeRef RewriteType(TypeRef type, IReadOnlyDictionary<string, TypeRef> substitutions)
        {
            if (substitutions.TryGetValue(type.Name, out var replacement))
                return RewriteType(replacement, Empty);

            var arguments = type.TypeArguments.Select(arg => RewriteType(arg, substitutions)).ToList();
            _objects.TryGetValue(type.Name, out var obj);
            _interfaces.TryGetValue(type.Name, out var iface);
            if (obj is null && iface is null)
                return arguments.Count == 0 ? type : new TypeRef(type.Name, arguments, type.Line, type.Column);

            int arity = obj?.TypeParameters.Count ?? iface!.TypeParameters.Count;
            if (arguments.Count != arity)
                throw new CompilerException($"Generic type '{type.Name}' expects exactly {arity} type argument{(arity == 1 ? "" : "s")}, got {arguments.Count}", type.Line, type.Column);

            string name = EnsureSpecialization(type.Name, arguments, type.Line, type.Column);
            return new TypeRef(name, null, type.Line, type.Column);
        }

        private string EnsureSpecialization(string genericName, IReadOnlyList<TypeRef> arguments, int line, int column)
        {
            string key = $"{genericName}<{string.Join(",", arguments.Select(Canonical))}>";
            if (_specializedNames.TryGetValue(key, out var existing)) return existing;

            string specializedName = key;
            if (!_reservedNames.Add(specializedName))
                throw new CompilerException($"Generic specialization '{key}' conflicts with declared type '{specializedName}'", line, column);
            _specializedNames[key] = specializedName; // reserve before cloning recursive fields
            if (++_specializationDepth > 64)
                throw new CompilerException($"Generic specialization expansion exceeded 64 nested types while constructing '{key}'", line, column);

            if (_objects.TryGetValue(genericName, out var obj))
            {
                var env = Bind(obj.TypeParameters, arguments);
                var clone = RewriteObject(obj, env, specializedName);
                _generated.Add(clone);
                foreach (var impl in _openImplements.Where(candidate => candidate.ObjectType.Name == genericName))
                {
                    if (impl.ObjectType.TypeArguments.Count != obj.TypeParameters.Count)
                        throw new CompilerException($"Generic implementation for '{genericName}' must bind all {obj.TypeParameters.Count} type parameters", impl.ObjectType.Line, impl.ObjectType.Column);
                    var implEnv = new Dictionary<string, TypeRef>(env, StringComparer.Ordinal);
                    var rewritten = (ImplementDecl)RewriteStmt(impl, implEnv);
                    var objectType = new TypeRef(specializedName, null, impl.ObjectType.Line, impl.ObjectType.Column);
                    var maps = rewritten.Methods.Select(m => new ImplementMethodMap(
                        m.InterfaceMethodName, m.Parameters,
                        m.ViaObjectName.Lexeme == genericName ? Rename(m.ViaObjectName, specializedName) : m.ViaObjectName,
                        m.ViaMethodName)).ToList();
                    _generated.Add(Origin(impl, new ImplementDecl(rewritten.InterfaceType, objectType, maps)));
                }
            }
            else
            {
                var iface = _interfaces[genericName];
                var env = Bind(iface.TypeParameters, arguments);
                var clone = RewriteInterface(iface, env, specializedName);
                _generated.Add(clone);
            }
            _specializationDepth--;
            return specializedName;
        }

        private static Dictionary<string, TypeRef> Bind(IReadOnlyList<Token> parameters, IReadOnlyList<TypeRef> arguments)
        {
            var result = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
            for (int i = 0; i < parameters.Count; i++) result[parameters[i].Lexeme] = arguments[i];
            return result;
        }

        private ObjectDecl RewriteObject(ObjectDecl obj, IReadOnlyDictionary<string, TypeRef> env, string name)
        {
            var token = Rename(obj.Name, name);
            var clone = new ObjectDecl(token, obj.IsRecord,
                obj.Fields.Select(f => RewriteField(f, env)).ToList(),
                obj.Constructors.Select(c => new ConstructorDecl(c.Keyword, RewriteParameters(c.Parameters, env), (Block)RewriteStmt(c.Body, env), c.Visibility)).ToList(),
                obj.Methods.Select(m => new MethodDecl(m.Name, m.ReturnType is null ? null : RewriteType(m.ReturnType, env), RewriteParameters(m.Parameters, env), (Block)RewriteStmt(m.Body, env), m.Visibility, m.IsStatic)).ToList(),
                obj.InlineInterfaceMethods.Select(m => new InlineImplementMethodDecl(RewriteType(m.InterfaceType, env), m.MethodName, RewriteParameters(m.Parameters, env), (Block)RewriteStmt(m.Body, env), m.Visibility)).ToList(),
                typeParameters: null,
                inlineInterfaceGroups: obj.InlineInterfaceGroups.Select(g => RewriteGroup(g, env)).ToList());
            return Origin(obj, clone);
        }

        private InterfaceDecl RewriteInterface(InterfaceDecl iface, IReadOnlyDictionary<string, TypeRef> env, string name)
        {
            var clone = new InterfaceDecl(Rename(iface.Name, name),
                iface.Fields.Select(f => RewriteField(f, env)).ToList(),
                iface.Methods.Select(m => new InterfaceMethodDecl(m.Name, RewriteType(m.ReturnType, env), RewriteParameters(m.Parameters, env))).ToList());
            return Origin(iface, clone);
        }

        private FieldDecl RewriteField(FieldDecl field, IReadOnlyDictionary<string, TypeRef> env) =>
            new(RewriteType(field.Type, env), field.Name, field.Initializer is null ? null : RewriteExpr(field.Initializer, env), field.Visibility, field.IsConstant, field.IsStatic, field.HashRole);

        private List<Parameter> RewriteParameters(IReadOnlyList<Parameter> parameters, IReadOnlyDictionary<string, TypeRef> env) =>
            parameters.Select(p => new Parameter(p.Type is null ? null : RewriteType(p.Type, env), p.Name, p.DefaultValue is null ? null : RewriteExpr(p.DefaultValue, env))).ToList();

        private Stmt RewriteStmt(Stmt stmt, IReadOnlyDictionary<string, TypeRef> env)
        {
            Stmt result = stmt switch
            {
                VarDecl v => new VarDecl(RewriteType(v.Type, env), v.Name, v.Initializer is null ? null : RewriteExpr(v.Initializer, env), v.IsConstant),
                ExprStmt e => new ExprStmt(RewriteExpr(e.Expression, env)),
                Block b => new Block(b.Statements.Select(s => RewriteStmt(s, env)).ToList()),
                IfStmt i => new IfStmt(RewriteExpr(i.Condition, env), RewriteStmt(i.ThenBranch, env), i.ElseBranch is null ? null : RewriteStmt(i.ElseBranch, env)),
                SwitchStmt s => new SwitchStmt(s.Keyword, RewriteExpr(s.Value, env), s.Cases.Select(c => new SwitchCase(c.Keyword, RewriteExpr(c.Value, env), RewriteStmt(c.Body, env))).ToList(), s.DefaultBranch is null ? null : RewriteStmt(s.DefaultBranch, env)),
                WhileStmt w => new WhileStmt(RewriteExpr(w.Condition, env), RewriteStmt(w.Body, env)),
                ReturnStmt r => new ReturnStmt(r.Value is null ? null : RewriteExpr(r.Value, env)),
                PrintStmt p => new PrintStmt(RewriteExpr(p.Value, env)),
                PanicStmt p => new PanicStmt(RewriteExpr(p.Value, env)),
                YieldStmt y => new YieldStmt(y.Keyword, RewriteExpr(y.Value, env)),
                BreakStmt b => new BreakStmt(b.Keyword),
                ContinueStmt c => new ContinueStmt(c.Keyword),
                ForStmt f => new ForStmt(f.Initializer is null ? null : RewriteStmt(f.Initializer, env), RewriteExpr(f.Condition, env), f.Increment is null ? null : RewriteExpr(f.Increment, env), RewriteStmt(f.Body, env)),
                ForeachStmt f => new ForeachStmt(f.Iterator, RewriteExpr(f.Iterable, env), RewriteStmt(f.Body, env)),
                FunctionDecl f => new FunctionDecl(f.Name, f.ReturnType is null ? null : RewriteType(f.ReturnType, env), RewriteParameters(f.Parameters, env), (Block)RewriteStmt(f.Body, env)),
                ObjectDecl o => RewriteConcreteObject(o, env),
                InterfaceDecl i => RewriteConcreteInterface(i, env),
                ImplementDecl i => new ImplementDecl(RewriteType(i.InterfaceType, env), RewriteType(i.ObjectType, env), i.Methods.Select(m => new ImplementMethodMap(m.InterfaceMethodName, RewriteParameters(m.Parameters, env), m.ViaObjectName, m.ViaMethodName)).ToList()),
                _ => stmt
            };
            return Origin(stmt, result);
        }

        private ObjectDecl RewriteConcreteObject(ObjectDecl obj, IReadOnlyDictionary<string, TypeRef> env) =>
            new(obj.Name, obj.IsRecord, obj.Fields.Select(f => RewriteField(f, env)).ToList(),
                obj.Constructors.Select(c => new ConstructorDecl(c.Keyword, RewriteParameters(c.Parameters, env), (Block)RewriteStmt(c.Body, env), c.Visibility)).ToList(),
                obj.Methods.Select(m => new MethodDecl(m.Name, m.ReturnType is null ? null : RewriteType(m.ReturnType, env), RewriteParameters(m.Parameters, env), (Block)RewriteStmt(m.Body, env), m.Visibility, m.IsStatic)).ToList(),
                obj.InlineInterfaceMethods.Select(m => new InlineImplementMethodDecl(RewriteType(m.InterfaceType, env), m.MethodName, RewriteParameters(m.Parameters, env), (Block)RewriteStmt(m.Body, env), m.Visibility)).ToList(),
                obj.TypeParameters,
                obj.InlineInterfaceGroups.Select(g => RewriteGroup(g, env)).ToList());

        private InlineImplementGroupDecl RewriteGroup(InlineImplementGroupDecl group, IReadOnlyDictionary<string, TypeRef> env) =>
            new(RewriteType(group.InterfaceType, env),
                group.Methods.Select(m => new InlineImplementGroupMethodDecl(
                    m.Name,
                    RewriteType(m.ReturnType, env),
                    RewriteParameters(m.Parameters, env),
                    (Block)RewriteStmt(m.Body, env))).ToList(),
                group.Visibility);

        private InterfaceDecl RewriteConcreteInterface(InterfaceDecl iface, IReadOnlyDictionary<string, TypeRef> env) =>
            new(iface.Name, iface.Fields.Select(f => RewriteField(f, env)).ToList(), iface.Methods.Select(m => new InterfaceMethodDecl(m.Name, RewriteType(m.ReturnType, env), RewriteParameters(m.Parameters, env))).ToList());

        private Expr RewriteExpr(Expr expr, IReadOnlyDictionary<string, TypeRef> env) => expr switch
        {
            Binary b => new Binary(RewriteExpr(b.Left, env), b.Operator, RewriteExpr(b.Right, env)),
            Unary u => new Unary(u.Operator, RewriteExpr(u.Right, env)),
            CastExpr c => new CastExpr(RewriteExpr(c.Value, env), c.AsToken, RewriteType(c.TargetType, env)),
            Literal l => l,
            DefaultValueExpr d => new DefaultValueExpr(RewriteType(d.Type, env), d.Line, d.Column),
            InterpString s => new InterpString(s.Parts.Select(p => p is Expr e ? (object)RewriteExpr(e, env) : p).ToList(), s.Line, s.Column),
            ArrayLiteral a => new ArrayLiteral(a.Elements.Select(e => RewriteExpr(e, env)).ToList(), a.Line, a.Column),
            NewArrayExpr a => new NewArrayExpr(RewriteType(a.ElementType, env), RewriteExpr(a.Size, env), a.Line, a.Column),
            NewCollectionExpr c => new NewCollectionExpr(RewriteType(c.CollectionType, env), c.Line, c.Column),
            ArrayLengthExpr a => new ArrayLengthExpr(RewriteExpr(a.Target, env), a.DotToken),
            ArrayIndexExpr a => new ArrayIndexExpr(RewriteExpr(a.Array, env), RewriteExpr(a.Index, env)),
            OptionalOrExpr o => new OptionalOrExpr(RewriteExpr(o.Optional, env), RewriteExpr(o.Fallback, env)),
            OptionalHasValueExpr o => new OptionalHasValueExpr(RewriteExpr(o.Target, env)),
            OptionalValueExpr o => new OptionalValueExpr(RewriteExpr(o.Target, env)),
            FallibleErrorExpr e => new FallibleErrorExpr(e.ErrorToken, e.Arguments.Select(a => RewriteExpr(a, env)).ToList()),
            OnErrorExpr o => new OnErrorExpr(RewriteExpr(o.Fallible, env), o.OnToken, (Block)RewriteStmt(o.Handler, env)),
            FieldAccessExpr f => new FieldAccessExpr(RewriteExpr(f.Target, env), f.Name),
            FieldSetExpr f => new FieldSetExpr((FieldAccessExpr)RewriteExpr(f.Target, env), RewriteExpr(f.Value, env)),
            NewObjectExpr n => new NewObjectExpr(RewriteType(n.Type, env), n.Arguments.Select(a => RewriteExpr(a, env)).ToList()),
            ArraySetExpr a => new ArraySetExpr((ArrayIndexExpr)RewriteExpr(a.Target, env), RewriteExpr(a.Value, env)),
            Variable v => new Variable(v.Name),
            Assign a => new Assign(a.Name, RewriteExpr(a.Value, env)),
            CompoundAssignExpr c => new CompoundAssignExpr(RewriteExpr(c.Target, env), c.Operator, RewriteExpr(c.Value, env)),
            Call c => new Call(c.Callee, c.Arguments.Select(a => RewriteExpr(a, env)).ToList()),
            MethodCallExpr m => new MethodCallExpr(RewriteExpr(m.Target, env), m.MethodName, m.Arguments.Select(a => RewriteExpr(a, env)).ToList()),
            _ => expr
        };

        private static string Canonical(TypeRef type) => type.TypeArguments.Count == 0 ? type.Name : $"{type.Name}<{string.Join(",", type.TypeArguments.Select(Canonical))}>";
        private static Token Rename(Token token, string name) => new(TokenType.Identifier, name, null, token.Line, token.Column);
        private static T Origin<T>(Stmt source, T target) where T : Stmt { target.OriginPackageName = source.OriginPackageName; target.OriginModulePath = source.OriginModulePath; return target; }
    }
}
