namespace ConsoleApp1.Compiler;

sealed record DirectWasmCompilation(
    byte[] Module,
    int FunctionCount,
    int GlobalCount,
    int TypeCount,
    bool GarbageCollectionDisabled);

sealed class DirectWasmCompiler
{
    private readonly TypedProgram _program;
    private readonly bool _garbageCollectionDisabled;
    private readonly DirectWasmModuleBuilder _module = new();
    private readonly Dictionary<string, DirectFunction> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DirectGlobal> _globals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DirectObjectLayout> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<DirectInterfaceTarget>> _interfaceDispatch = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<DirectInterfaceFieldTarget>> _interfaceFieldDispatch = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DirectHostFunction> _hostFunctions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedIntrinsicNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DirectStringData> _stringData = new(StringComparer.Ordinal);
    private readonly List<Stmt> _topLevel = [];
    private int _printI32;
    private int _printI64;
    private int _printF64;
    private int _printString;
    private int _stringFromUtf8;
    private int _stringConcat;
    private int _stringEqual;
    private int _stringFromI32;
    private int _stringFromI64;
    private int _stringFromF64;
    private int _collectionNew;
    private int _collectionLength;
    private readonly Dictionary<string, int> _collectionFunctions = new(StringComparer.Ordinal);
    private bool _usesRuntimeCollections;
    private bool _usesPanic;
    private int _panicString;
    private int _allocator;
    private int _heapGlobal;
    private int _runFunction;
    private int _sceneFactory = -1;

    public DirectWasmCompiler(TypedProgram program, bool garbageCollectionDisabled = false)
    {
        _program = program;
        _garbageCollectionDisabled = garbageCollectionDisabled;
    }

    public DirectWasmCompilation Compile()
    {
        ClassifyProgram();
        _printI32 = _module.AddFunctionImport("code_host", "print_i32", [DirectWasmValueType.I32], []);
        _printI64 = _module.AddFunctionImport("code_host", "print_i64", [DirectWasmValueType.I64], []);
        _printF64 = _module.AddFunctionImport("code_host", "print_f64", [DirectWasmValueType.F64], []);
        _printString = _module.AddFunctionImport("code_host", "print_string", [DirectWasmValueType.I32], []);
        _stringFromUtf8 = _module.AddFunctionImport("code_runtime", "string_from_utf8", [DirectWasmValueType.I32, DirectWasmValueType.I32], [DirectWasmValueType.I32]);
        _stringConcat = _module.AddFunctionImport("code_runtime", "string_concat", [DirectWasmValueType.I32, DirectWasmValueType.I32], [DirectWasmValueType.I32]);
        _stringEqual = _module.AddFunctionImport("code_runtime", "string_equal", [DirectWasmValueType.I32, DirectWasmValueType.I32], [DirectWasmValueType.I32]);
        _stringFromI32 = _module.AddFunctionImport("code_runtime", "string_from_i32", [DirectWasmValueType.I32], [DirectWasmValueType.I32]);
        _stringFromI64 = _module.AddFunctionImport("code_runtime", "string_from_i64", [DirectWasmValueType.I64], [DirectWasmValueType.I32]);
        _stringFromF64 = _module.AddFunctionImport("code_runtime", "string_from_f64", [DirectWasmValueType.F64], [DirectWasmValueType.I32]);
        if (_usesRuntimeCollections) RegisterCollectionRuntime();
        if (_usesPanic) _panicString = _module.AddFunctionImport("code_host", "panic_string", [DirectWasmValueType.I32], []);
        RegisterHostFunctions();
        _heapGlobal = _module.AddGlobal(DirectWasmValueType.I32, mutable: true, initialValue: 1024);
        _allocator = _module.ReserveFunction("$code_alloc", [DirectWasmValueType.I32], [DirectWasmValueType.I32]);
        ReserveProgramFunctions();
        ExportLifecycleFunctions();
        ReserveSceneFactory();
        _runFunction = _module.ReserveFunction("code_run", [], [DirectWasmValueType.I32]);
        _module.ExportFunction("code_run", _runFunction);
        EmitAllocator();
        foreach (var function in _functions.Values) EmitFunction(function);
        EmitSceneFactory();
        EmitRun();
        var module = _module.Build();
        return new DirectWasmCompilation(module, _functions.Count + 2, _globals.Count, _objects.Count, _garbageCollectionDisabled);
    }

    private void ClassifyProgram()
    {
        var implementations = new List<ImplementDecl>();
        var interfaces = new Dictionary<string, InterfaceDecl>(StringComparer.Ordinal);
        foreach (var statement in _program.Statements)
        {
            CollectIntrinsicCalls(statement);
            switch (statement)
            {
                case ObjectDecl type:
                    RegisterObject(type);
                    break;
                case FunctionDecl function:
                    RegisterFunction(function);
                    break;
                case ImplementDecl implementation:
                    implementations.Add(implementation);
                    break;
                case InterfaceDecl iface:
                    interfaces[iface.Name.Lexeme] = iface;
                    break;
                case EnumDecl:
                    break;
                default:
                    _topLevel.Add(statement);
                    if (statement is VarDecl global)
                        RegisterGlobal(global);
                break;
            }
        }
        BuildInterfaceDispatch(implementations, interfaces);
    }

    private void BuildInterfaceDispatch(IEnumerable<ImplementDecl> implementations, IReadOnlyDictionary<string, InterfaceDecl> interfaces)
    {
        var fieldPairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var implementation in implementations)
        {
            if (interfaces.TryGetValue(implementation.InterfaceName.Lexeme, out var iface) &&
                _objects.TryGetValue(implementation.ObjectName.Lexeme, out var owner))
            {
                string pairKey = $"{implementation.InterfaceName.Lexeme}->{implementation.ObjectName.Lexeme}";
                if (fieldPairs.Add(pairKey))
                {
                    foreach (var field in iface.Fields)
                    {
                        if (!owner.Fields.TryGetValue(field.Name.Lexeme, out var concreteField))
                            throw new InvalidOperationException($"Direct-Wasm interface field target '{pairKey}.{field.Name.Lexeme}' is unavailable.");
                        string fieldKey = $"{implementation.InterfaceName.Lexeme}.{field.Name.Lexeme}";
                        if (!_interfaceFieldDispatch.TryGetValue(fieldKey, out var fieldTargets))
                            _interfaceFieldDispatch[fieldKey] = fieldTargets = [];
                        fieldTargets.Add(new DirectInterfaceFieldTarget(owner, concreteField));
                    }
                }
            }

            foreach (var map in implementation.Methods)
            {
                string interfaceKey = $"{implementation.InterfaceName.Lexeme}.{InterfaceMethodKey(map.InterfaceMethodName.Lexeme, map.Parameters)}";
                string methodKey = MethodKey(implementation.ObjectName.Lexeme, map.ViaMethodName.Lexeme, map.Parameters);
                if (!_objects.TryGetValue(implementation.ObjectName.Lexeme, out var methodOwner) || !_functions.TryGetValue(methodKey, out var function))
                    throw new InvalidOperationException($"Direct-Wasm interface target '{methodKey}' is unavailable.");
                if (!_interfaceDispatch.TryGetValue(interfaceKey, out var targets))
                    _interfaceDispatch[interfaceKey] = targets = [];
                targets.Add(new DirectInterfaceTarget(methodOwner, function));
            }
        }
    }

    private void RegisterHostFunctions()
    {
        foreach (string name in _usedIntrinsicNames.OrderBy(value => value, StringComparer.Ordinal))
        {
            if (IsNativeMathIntrinsic(name)) continue;
            if (!HostAbiCatalog.TryGetIntrinsic(name, out var intrinsic)) continue;
            var parameters = intrinsic.ParameterTypeNames.Select(ParseTypeName).Select(MapType).ToArray();
            var returnType = ParseTypeName(intrinsic.ReturnTypeName);
            var results = IsVoid(returnType) ? Array.Empty<DirectWasmValueType>() : [MapType(returnType)];
            int index = _module.AddFunctionImport("code_host", intrinsic.Symbol.Symbol, parameters, results);
            _hostFunctions[name] = new DirectHostFunction(intrinsic, index, returnType);
        }
    }

    private void RegisterCollectionRuntime()
    {
        _collectionNew = _module.AddFunctionImport("code_runtime", "collection_new", [DirectWasmValueType.I32], [DirectWasmValueType.I32]);
        _collectionLength = _module.AddFunctionImport("code_runtime", "collection_length", [DirectWasmValueType.I32], [DirectWasmValueType.I64]);
        foreach (var type in new[] { DirectWasmValueType.I32, DirectWasmValueType.I64, DirectWasmValueType.F64 })
        {
            string suffix = RuntimeTypeSuffix(type);
            AddCollectionImport($"add:{type}", $"collection_add_{suffix}", [DirectWasmValueType.I32, type], []);
            AddCollectionImport($"contains:{type}", $"collection_contains_{suffix}", [DirectWasmValueType.I32, type], [DirectWasmValueType.I32]);
            AddCollectionImport($"remove:{type}", $"collection_remove_{suffix}", [DirectWasmValueType.I32, type], []);
            AddCollectionImport($"peek:{type}", $"collection_peek_{suffix}", [DirectWasmValueType.I32], [type]);
            AddCollectionImport($"pop:{type}", $"collection_pop_{suffix}", [DirectWasmValueType.I32], [type]);
            foreach (var valueType in new[] { DirectWasmValueType.I32, DirectWasmValueType.I64, DirectWasmValueType.F64 })
            {
                string valueSuffix = RuntimeTypeSuffix(valueType);
                AddCollectionImport($"map-set:{type}:{valueType}", $"map_set_{suffix}_{valueSuffix}", [DirectWasmValueType.I32, type, valueType], []);
                AddCollectionImport($"map-get:{type}:{valueType}", $"map_get_{suffix}_{valueSuffix}", [DirectWasmValueType.I32, type], [valueType]);
            }
        }
    }

    private void AddCollectionImport(string key, string name, DirectWasmValueType[] parameters, DirectWasmValueType[] results)
        => _collectionFunctions[key] = _module.AddFunctionImport("code_runtime", name, parameters, results);

    private static string RuntimeTypeSuffix(DirectWasmValueType type) => type switch
    {
        DirectWasmValueType.I32 => "i32",
        DirectWasmValueType.I64 => "i64",
        DirectWasmValueType.F64 => "f64",
        _ => throw new InvalidOperationException($"Unsupported collection runtime type {type}.")
    };

    private int CollectionFunction(string operation, params DirectWasmValueType[] types)
    {
        string key = $"{operation}:{string.Join(":", types)}";
        return _collectionFunctions.TryGetValue(key, out int function)
            ? function
            : throw new InvalidOperationException($"Direct-Wasm collection helper '{key}' was not registered.");
    }

    private static int CollectionKind(TypeRef type) => type.NormalizeBuiltInShorthands().Name switch
    {
        "map" => 1,
        "set" => 2,
        "queue" => 3,
        "stack" => 4,
        _ => throw new InvalidOperationException($"Unsupported direct-Wasm collection '{type.Name}'.")
    };

    private void RegisterObject(ObjectDecl declaration)
    {
        int offset = 8;
        var fields = new Dictionary<string, DirectField>(StringComparer.Ordinal);
        foreach (var field in declaration.Fields)
        {
            var type = MapType(field.Type);
            int alignment = StorageSize(type);
            offset = Align(offset, alignment);
            fields[field.Name.Lexeme] = new DirectField(field.Name.Lexeme, field.Type, type, offset);
            offset += StorageSize(type);
        }
        var layout = new DirectObjectLayout(declaration.Name.Lexeme, declaration, fields, Align(offset, 8));
        _objects.Add(layout.Name, layout);
        foreach (var constructor in declaration.Constructors)
        {
            string key = ConstructorKey(declaration.Name.Lexeme, constructor.Parameters);
            _functions.Add(key, new DirectFunction(key, $"{declaration.Name.Lexeme}.constructor", constructor.Parameters, null, constructor.Body, layout, true));
        }
        foreach (var method in declaration.Methods)
        {
            string key = MethodKey(declaration.Name.Lexeme, method.Name.Lexeme, method.Parameters);
            var returnType = method.ReturnType ?? VoidType(method.Name);
            _functions.Add(key, new DirectFunction(key, $"{declaration.Name.Lexeme}.{method.Name.Lexeme}", method.Parameters, returnType, method.Body, layout, false));
        }
    }

    private void RegisterFunction(FunctionDecl declaration)
    {
        var returnType = declaration.ReturnType ?? VoidType(declaration.Name);
        _functions.Add(declaration.Name.Lexeme, new DirectFunction(
            declaration.Name.Lexeme,
            declaration.Name.Lexeme,
            declaration.Parameters,
            returnType,
            declaration.Body,
            null,
            false));
    }

    private void RegisterGlobal(VarDecl declaration)
    {
        var wasmType = MapType(declaration.Type);
        int index = _module.AddGlobal(wasmType);
        var global = new DirectGlobal(declaration.Name.Lexeme, declaration.Type, wasmType, index, declaration);
        _globals.Add(declaration.Name.Lexeme, global);
    }

    private void ReserveProgramFunctions()
    {
        foreach (var function in _functions.Values)
        {
            var parameters = new List<DirectWasmValueType>();
            if (function.Owner is not null) parameters.Add(DirectWasmValueType.I32);
            parameters.AddRange(function.Parameters.Select(parameter => MapType(parameter.Type!)));
            var results = IsVoid(function.ReturnType) ? Array.Empty<DirectWasmValueType>() : [MapType(function.ReturnType!)];
            function.Index = _module.ReserveFunction(function.DisplayName, parameters, results);
        }
    }

    private void ExportLifecycleFunctions()
    {
        foreach (var (sourceName, exportName) in new[]
        {
            ("start", "code_start"),
            ("update", "code_update"),
            ("draw", "code_draw"),
            ("drawHud", "code_draw_hud")
        })
        {
            var function = _functions.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, sourceName, StringComparison.Ordinal) ||
                string.Equals(candidate.DisplayName, $"MainScene.{sourceName}", StringComparison.Ordinal));
            if (function is not null)
                _module.ExportFunction(exportName, function.Index);
        }
    }

    private void ReserveSceneFactory()
    {
        if (!_objects.ContainsKey("MainScene")) return;
        _sceneFactory = _module.ReserveFunction("code_scene_new", [], [DirectWasmValueType.I32]);
        _module.ExportFunction("code_scene_new", _sceneFactory);
    }

    private void EmitSceneFactory()
    {
        if (_sceneFactory < 0) return;
        var layout = GetObject("MainScene");
        var body = _module.GetFunctionBody(_sceneFactory);
        int pointer = body.AddLocal(DirectWasmValueType.I32, 0);
        body.I32Const(layout.Size);
        body.Call(_allocator);
        body.LocalTee(pointer);
        body.I32Const(ObjectTypeId(layout.Name));
        body.Store(0x36, 2);
        var constructor = _functions.Values.FirstOrDefault(candidate =>
            candidate.Owner == layout && candidate.IsConstructor && candidate.Parameters.Count == 0);
        if (constructor is not null)
        {
            body.LocalGet(pointer);
            body.Call(constructor.Index);
        }
        body.LocalGet(pointer);
    }

    private void EmitAllocator()
    {
        var body = _module.GetFunctionBody(_allocator);
        int previous = body.AddLocal(DirectWasmValueType.I32, 1);
        body.GlobalGet(_heapGlobal);
        body.LocalTee(previous);
        body.LocalGet(0);
        body.Op(0x6a); // i32.add
        body.I32Const(7);
        body.Op(0x6a);
        body.I32Const(-8);
        body.Op(0x71); // i32.and
        body.GlobalSet(_heapGlobal);
        body.GlobalGet(_heapGlobal);
        body.Op(0x3f); body.U32(0); // memory.size
        body.I32Const(16);
        body.Op(0x74); // i32.shl (pages to bytes)
        body.Op(0x4b); // i32.gt_u
        body.Op(0x04); body.Op(0x40);
        body.GlobalGet(_heapGlobal);
        body.I32Const(65535);
        body.Op(0x6a);
        body.I32Const(16);
        body.Op(0x76); // i32.shr_u (bytes to pages, rounded up)
        body.Op(0x3f); body.U32(0);
        body.Op(0x6b);
        body.Op(0x40); body.U32(0); // memory.grow
        body.Op(0x1a);
        body.Op(0x0b);
        body.LocalGet(previous);
    }

    private void EmitFunction(DirectFunction function)
    {
        var body = _module.GetFunctionBody(function.Index);
        var context = new FunctionContext(this, function, body);
        EmitStatement(function.Body, context);
        if (IsVoid(function.ReturnType))
            return;
        // Type checking guarantees a source return. This keeps validation deterministic if a future
        // frontend regression permits fallthrough.
        EmitDefault(function.ReturnType!, body);
    }

    private void EmitRun()
    {
        var body = _module.GetFunctionBody(_runFunction);
        var function = new DirectFunction("$run", "code_run", [], IntegerType(), new Block(_topLevel), null, false)
        {
            Index = _runFunction
        };
        var context = new FunctionContext(this, function, body);
        body.I32Const(_module.StaticDataEnd);
        body.GlobalSet(_heapGlobal);
        foreach (var global in _globals.Values)
            EmitDefaultToGlobal(global, body);
        EmitStatement(function.Body, context);
        body.I32Const(0);
    }

    private void EmitStatement(Stmt statement, FunctionContext context)
    {
        switch (statement)
        {
            case Block block:
                context.PushScope();
                foreach (var child in block.Statements)
                {
                    if (context.TryGetLoop(out var loop) && child is not BreakStmt && child is not ContinueStmt)
                    {
                        context.Body.LocalGet(loop.BreakLocal);
                        context.Body.LocalGet(loop.ContinueLocal);
                        context.Body.Op(0x72);
                        context.Body.Op(0x45);
                        context.Body.Op(0x04); context.Body.Op(0x40);
                        EmitStatement(child, context);
                        context.Body.Op(0x0b);
                    }
                    else EmitStatement(child, context);
                }
                context.PopScope();
                break;
            case VarDecl variable:
            {
                if (context.IsRun && _globals.TryGetValue(variable.Name.Lexeme, out var global))
                {
                    if (variable.Initializer is not null)
                    {
                        EmitExpressionAs(variable.Initializer, global.Type, context);
                        context.Body.GlobalSet(global.Index);
                    }
                    break;
                }
                int local = context.Declare(variable.Name.Lexeme, variable.Type);
                if (variable.Initializer is null) EmitDefault(variable.Type, context.Body);
                else EmitExpressionAs(variable.Initializer, variable.Type, context);
                context.Body.LocalSet(local);
                break;
            }
            case ExprStmt expressionStatement:
            {
                bool leavesValue = EmitExpression(expressionStatement.Expression, context);
                if (leavesValue) context.Body.Op(0x1a);
                break;
            }
            case IfStmt conditional:
                EmitCondition(conditional.Condition, context);
                context.Body.Op(0x04); context.Body.Op(0x40); // if void
                EmitStatement(conditional.ThenBranch, context);
                if (conditional.ElseBranch is not null)
                {
                    context.Body.Op(0x05);
                    EmitStatement(conditional.ElseBranch, context);
                }
                context.Body.Op(0x0b);
                break;
            case SwitchStmt selection:
                EmitSwitch(selection, context);
                break;
            case WhileStmt loop:
                EmitWhile(loop.Condition, loop.Body, context);
                break;
            case ForStmt loop:
                context.PushScope();
                if (loop.Initializer is not null) EmitStatement(loop.Initializer, context);
                EmitWhile(loop.Condition, loop.Body, context, loop.Increment);
                context.PopScope();
                break;
            case ForeachStmt loop:
                EmitForeach(loop, context);
                break;
            case ReturnStmt result:
                if (result.Value is not null)
                    EmitExpressionAs(result.Value, context.Function.ReturnType!, context);
                context.Body.Op(0x0f);
                break;
            case PrintStmt print:
            {
                var type = GetType(print.Value);
                EmitExpressionAs(print.Value, type, context);
                context.Body.Call(MapType(type) switch
                {
                    DirectWasmValueType.I32 when type.NormalizeBuiltInShorthands().Name == "string" => _printString,
                    DirectWasmValueType.I32 => _printI32,
                    DirectWasmValueType.I64 => _printI64,
                    DirectWasmValueType.F64 => _printF64,
                    _ => throw Unsupported(print.Value, "print type")
                });
                break;
            }
            case YieldStmt yield:
                if (context.YieldResultLocal < 0 || context.YieldResultType is null)
                    throw Unsupported(yield, "yield outside recoverable-error handler");
                EmitExpressionAs(yield.Value, context.YieldResultType, context);
                context.Body.LocalSet(context.YieldResultLocal);
                break;
            case PanicStmt panic:
                EmitStringValue(panic.Value, context);
                context.Body.Call(_panicString);
                context.Body.Op(0x00);
                break;
            case BreakStmt:
                if (!context.TryGetLoop(out var breakLoop)) throw Unsupported(statement, "break outside loop");
                context.Body.I32Const(1);
                context.Body.LocalSet(breakLoop.BreakLocal);
                break;
            case ContinueStmt:
                if (!context.TryGetLoop(out var continueLoop)) throw Unsupported(statement, "continue outside loop");
                context.Body.I32Const(1);
                context.Body.LocalSet(continueLoop.ContinueLocal);
                break;
            case ObjectDecl or FunctionDecl or InterfaceDecl or ImplementDecl or EnumDecl:
                break;
            default:
                throw Unsupported(statement, "statement");
        }
    }

    private void EmitWhile(Expr condition, Stmt bodyStatement, FunctionContext context, Expr? increment = null)
    {
        int breakLocal = context.AddTemporary(DirectWasmValueType.I32);
        int continueLocal = context.AddTemporary(DirectWasmValueType.I32);
        context.Body.I32Const(0); context.Body.LocalSet(breakLocal);
        context.Body.Op(0x02); context.Body.Op(0x40); // outer block
        context.Body.Op(0x03); context.Body.Op(0x40); // loop
        context.Body.I32Const(0); context.Body.LocalSet(continueLocal);
        EmitCondition(condition, context);
        context.Body.Op(0x45); // i32.eqz
        context.Body.BranchIf(1);
        context.PushLoop(breakLocal, continueLocal);
        EmitStatement(bodyStatement, context);
        context.PopLoop();
        context.Body.LocalGet(breakLocal);
        context.Body.BranchIf(1);
        if (increment is not null)
        {
            bool leaves = EmitExpression(increment, context);
            if (leaves) context.Body.Op(0x1a);
        }
        context.Body.Branch(0);
        context.Body.Op(0x0b);
        context.Body.Op(0x0b);
    }

    private void EmitSwitch(SwitchStmt selection, FunctionContext context)
    {
        var type = GetType(selection.Value);
        var wasmType = MapType(type);
        int value = context.AddTemporary(wasmType);
        int matched = context.AddTemporary(DirectWasmValueType.I32);
        EmitExpressionAs(selection.Value, type, context);
        context.Body.LocalSet(value);
        context.Body.I32Const(0);
        context.Body.LocalSet(matched);
        foreach (var item in selection.Cases)
        {
            context.Body.LocalGet(matched);
            context.Body.Op(0x45);
            context.Body.Op(0x04); context.Body.Op(0x40);
            context.Body.LocalGet(value);
            EmitExpressionAs(item.Value, type, context);
            context.Body.Op(BinaryOpcode(TokenType.EqualEqual, wasmType));
            context.Body.Op(0x04); context.Body.Op(0x40);
            EmitStatement(item.Body, context);
            context.Body.I32Const(1);
            context.Body.LocalSet(matched);
            context.Body.Op(0x0b);
            context.Body.Op(0x0b);
        }
        if (selection.DefaultBranch is not null)
        {
            context.Body.LocalGet(matched);
            context.Body.Op(0x45);
            context.Body.Op(0x04); context.Body.Op(0x40);
            EmitStatement(selection.DefaultBranch, context);
            context.Body.Op(0x0b);
        }
    }

    private void EmitForeach(ForeachStmt loop, FunctionContext context)
    {
        if (loop.IsArray)
        {
            context.PushScope();
            var arrayType = GetType(loop.Iterable).NormalizeBuiltInShorthands();
            var elementType = arrayType.TypeArguments[0];
            int array = context.AddTemporary(DirectWasmValueType.I32);
            int index = context.AddTemporary(DirectWasmValueType.I32);
            int arrayIterator = context.Declare(loop.Iterator.Lexeme, loop.IteratorTypeRef ?? elementType);
            int breakLocal = context.AddTemporary(DirectWasmValueType.I32);
            int continueLocal = context.AddTemporary(DirectWasmValueType.I32);
            EmitExpressionAs(loop.Iterable, arrayType, context);
            context.Body.LocalSet(array);
            context.Body.I32Const(0);
            context.Body.LocalSet(index);
            context.Body.I32Const(0);
            context.Body.LocalSet(breakLocal);
            context.Body.Op(0x02); context.Body.Op(0x40);
            context.Body.Op(0x03); context.Body.Op(0x40);
            context.Body.I32Const(0);
            context.Body.LocalSet(continueLocal);
            context.Body.LocalGet(index);
            context.Body.LocalGet(array);
            context.Body.Load(0x28, 2);
            context.Body.Op(0x49); // i32.lt_u
            context.Body.Op(0x45);
            context.Body.BranchIf(1);
            context.Body.LocalGet(array);
            context.Body.Load(0x28, 2, 8);
            context.Body.LocalGet(index);
            context.Body.I32Const(StorageSize(MapType(elementType)));
            context.Body.Op(0x6c);
            context.Body.Op(0x6a);
            EmitMemoryLoad(elementType, context.Body);
            context.Body.LocalSet(arrayIterator);
            context.PushLoop(breakLocal, continueLocal);
            EmitStatement(loop.Body, context);
            context.PopLoop();
            context.Body.LocalGet(breakLocal);
            context.Body.BranchIf(1);
            context.Body.LocalGet(index);
            context.Body.I32Const(1);
            context.Body.Op(0x6a);
            context.Body.LocalSet(index);
            context.Body.Branch(0);
            context.Body.Op(0x0b);
            context.Body.Op(0x0b);
            context.PopScope();
            return;
        }
        context.PushScope();
        int iterator = context.Declare(loop.Iterator.Lexeme, loop.IteratorTypeRef ?? IntegerType());
        int limit = context.AddTemporary(DirectWasmValueType.I64);
        int numericBreak = context.AddTemporary(DirectWasmValueType.I32);
        int numericContinue = context.AddTemporary(DirectWasmValueType.I32);
        context.Body.I64Const(0);
        context.Body.LocalSet(iterator);
        EmitExpressionAs(loop.Iterable, IntegerType(), context);
        context.Body.LocalSet(limit);
        context.Body.I32Const(0); context.Body.LocalSet(numericBreak);
        context.Body.Op(0x02); context.Body.Op(0x40);
        context.Body.Op(0x03); context.Body.Op(0x40);
        context.Body.I32Const(0); context.Body.LocalSet(numericContinue);
        context.Body.LocalGet(iterator);
        context.Body.LocalGet(limit);
        context.Body.Op(0x53); // i64.lt_s
        context.Body.Op(0x45);
        context.Body.BranchIf(1);
        context.PushLoop(numericBreak, numericContinue);
        EmitStatement(loop.Body, context);
        context.PopLoop();
        context.Body.LocalGet(numericBreak);
        context.Body.BranchIf(1);
        context.Body.LocalGet(iterator);
        context.Body.I64Const(1);
        context.Body.Op(0x7c);
        context.Body.LocalSet(iterator);
        context.Body.Branch(0);
        context.Body.Op(0x0b);
        context.Body.Op(0x0b);
        context.PopScope();
    }

    private bool EmitExpression(Expr expression, FunctionContext context)
    {
        switch (expression)
        {
            case Literal literal:
                EmitLiteral(literal, context);
                return true;
            case InterpString text:
                EmitInterpolatedString(text, context);
                return true;
            case Variable variable:
                EmitVariable(variable, context);
                return true;
            case Assign assignment:
                EmitAssignment(assignment, context);
                return false;
            case Binary binary:
                EmitBinary(binary, context);
                return true;
            case Unary unary:
                EmitUnary(unary, context);
                return true;
            case CompoundAssignExpr compound:
                EmitCompoundAssignment(compound, context);
                return true;
            case NewArrayExpr array:
                EmitNewArray(array, context);
                return true;
            case NewCollectionExpr collection:
                context.Body.I32Const(CollectionKind(collection.CollectionType));
                context.Body.Call(_collectionNew);
                return true;
            case ArrayLiteral array:
                EmitArrayLiteral(array, context);
                return true;
            case ArrayLengthExpr length:
                EmitCollectionLength(length, context);
                return true;
            case ArrayIndexExpr index:
                EmitIndexGet(index, context);
                return true;
            case ArraySetExpr set:
                EmitIndexSet(set.Target, set.Value, context);
                return true;
            case OptionalHasValueExpr optional:
                EmitExpressionAs(optional.Target, GetType(optional.Target), context);
                context.Body.Op(0x45);
                context.Body.Op(0x45);
                return true;
            case OptionalValueExpr optional:
                EmitOptionalValue(optional, context);
                return true;
            case OptionalOrExpr optional:
                EmitOptionalOr(optional, context);
                return true;
            case FallibleErrorExpr error:
                EmitFallibleError(error, context);
                return true;
            case OnErrorExpr error:
                EmitOnError(error, context);
                return true;
            case NewObjectExpr instance:
                EmitNewObject(instance, context);
                return true;
            case FieldAccessExpr field:
                if (field.ResolvesToFallibleErrorField)
                {
                    context.Body.LocalGet(field.Name.Lexeme == "code" ? context.ErrorCodeLocal : context.ErrorMessageLocal);
                    return true;
                }
                if (field.ResolvesToEnumMember)
                {
                    context.Body.I32Const(field.ResolvedEnumValue!.Value);
                    return true;
                }
                if (field.ResolvesToInterfaceField)
                {
                    EmitInterfaceFieldGet(field, context);
                    return true;
                }
                EmitFieldAddress(field, context);
                EmitMemoryLoad(GetType(field), context.Body);
                return true;
            case FieldSetExpr set:
                if (set.Target.ResolvesToInterfaceField)
                    EmitInterfaceFieldSet(set.Target, set.Value, context);
                else
                    EmitFieldSet(set.Target, set.Value, context);
                return true;
            case Call call:
                return EmitCall(call, context);
            case MethodCallExpr call:
                return EmitMethodCall(call, context);
            case CastExpr cast:
                EmitExpressionAs(cast.Value, cast.TargetType, context);
                return true;
            default:
                throw Unsupported(expression, "expression");
        }
    }

    private void EmitLiteral(Literal literal, FunctionContext context)
    {
        var body = context.Body;
        if (ReferenceEquals(literal.Value, ConsoleApp1.OptionalNone.Value))
        {
            body.I32Const(0);
            return;
        }
        switch (literal.Value)
        {
            case null: body.I32Const(0); break;
            case bool value: body.I32Const(value ? 1 : 0); break;
            case string value:
                EmitStringLiteral(value, body);
                break;
            case double value: body.F64Const(value); break;
            case long value: body.I64Const(value); break;
            case int value: body.I64Const(value); break;
            default: throw Unsupported(literal, "literal");
        }
    }

    private void EmitVariable(Variable variable, FunctionContext context)
    {
        if (variable.ResolvesToImplicitField)
        {
            var owner = context.Function.Owner ?? throw Unsupported(variable, "implicit field without object context");
            context.Body.LocalGet(0);
            EmitFieldLoadAddress(owner, variable.Name.Lexeme, context.Body);
            EmitMemoryLoad(variable.ResolvedImplicitFieldTypeRef!, context.Body);
            return;
        }
        if (context.TryLookup(variable.Name.Lexeme, out var local))
        {
            context.Body.LocalGet(local.Index);
            return;
        }
        if (_globals.TryGetValue(variable.Name.Lexeme, out var global))
        {
            context.Body.GlobalGet(global.Index);
            return;
        }
        if (variable.ResolvedBuiltInConstant)
        {
            context.Body.F64Const(variable.Name.Lexeme == "pi" ? Math.PI : Math.Tau);
            return;
        }
        throw Unsupported(variable, $"variable '{variable.Name.Lexeme}'");
    }

    private void EmitAssignment(Assign assignment, FunctionContext context)
    {
        if (assignment.ResolvesToImplicitField)
        {
            var owner = context.Function.Owner!;
            context.Body.LocalGet(0);
            EmitFieldLoadAddress(owner, assignment.Name.Lexeme, context.Body);
            EmitExpressionAs(assignment.Value, assignment.ResolvedImplicitFieldTypeRef!, context);
            EmitMemoryStore(assignment.ResolvedImplicitFieldTypeRef!, context.Body);
            return;
        }
        if (context.TryLookup(assignment.Name.Lexeme, out var local))
        {
            EmitExpressionAs(assignment.Value, local.Type, context);
            context.Body.LocalSet(local.Index);
            return;
        }
        if (_globals.TryGetValue(assignment.Name.Lexeme, out var global))
        {
            EmitExpressionAs(assignment.Value, global.Type, context);
            context.Body.GlobalSet(global.Index);
            return;
        }
        throw Unsupported(assignment, $"assignment '{assignment.Name.Lexeme}'");
    }

    private void EmitBinary(Binary binary, FunctionContext context)
    {
        var result = GetType(binary);
        bool hasStringOperand = GetType(binary.Left).NormalizeBuiltInShorthands().Name == "string" ||
            GetType(binary.Right).NormalizeBuiltInShorthands().Name == "string";
        if (hasStringOperand)
        {
            if (binary.Operator.Type == TokenType.Plus)
            {
                EmitStringValue(binary.Left, context);
                EmitStringValue(binary.Right, context);
                context.Body.Call(_stringConcat);
                return;
            }
            if (binary.Operator.Type is TokenType.EqualEqual or TokenType.BangEqual)
            {
                EmitStringValue(binary.Left, context);
                EmitStringValue(binary.Right, context);
                context.Body.Call(_stringEqual);
                if (binary.Operator.Type == TokenType.BangEqual) context.Body.Op(0x45);
                return;
            }
        }
        if (binary.Operator.Type is TokenType.And or TokenType.Or)
        {
            EmitCondition(binary.Left, context);
            context.Body.Op(0x04); context.Body.Op((byte)DirectWasmValueType.I32);
            if (binary.Operator.Type == TokenType.And)
            {
                EmitCondition(binary.Right, context);
                context.Body.Op(0x05);
                context.Body.I32Const(0);
            }
            else
            {
                context.Body.I32Const(1);
                context.Body.Op(0x05);
                EmitCondition(binary.Right, context);
            }
            context.Body.Op(0x0b);
            return;
        }

        bool comparison = binary.Operator.Type is TokenType.EqualEqual or TokenType.BangEqual
            or TokenType.Less or TokenType.Greater or TokenType.LessEqual or TokenType.GreaterEqual;
        if (binary.Operator.Type is TokenType.EqualEqual or TokenType.BangEqual &&
            (RequiresStructuralValueSemantics(GetType(binary.Left)) || RequiresStructuralValueSemantics(GetType(binary.Right))))
        {
            throw Unsupported(binary, "record and collection structural equality in direct-Wasm");
        }
        var operationType = comparison ? Promote(GetType(binary.Left), GetType(binary.Right)) : result;
        EmitExpressionAs(binary.Left, operationType, context);
        EmitExpressionAs(binary.Right, operationType, context);
        var wasmType = MapType(operationType);
        context.Body.Op(BinaryOpcode(binary.Operator.Type, wasmType));
    }

    private void EmitStringLiteral(string value, DirectWasmFunctionBody body)
    {
        var data = RegisterStringData(value);
        body.I32Const(data.Pointer);
        body.I32Const(data.Length);
        body.Call(_stringFromUtf8);
    }

    private void EmitInterpolatedString(InterpString text, FunctionContext context)
    {
        bool emitted = false;
        foreach (var part in text.Parts)
        {
            if (part is string segment) EmitStringLiteral(segment, context.Body);
            else if (part is Expr expression) EmitStringValue(expression, context);
            else continue;
            if (emitted) context.Body.Call(_stringConcat);
            emitted = true;
        }
        if (!emitted) EmitStringLiteral(string.Empty, context.Body);
    }

    private void EmitStringValue(Expr expression, FunctionContext context)
    {
        var type = GetType(expression);
        if (type.NormalizeBuiltInShorthands().Name == "string")
        {
            EmitExpressionAs(expression, type, context);
            return;
        }
        EmitExpressionAs(expression, type, context);
        context.Body.Call(MapType(type) switch
        {
            DirectWasmValueType.I32 => _stringFromI32,
            DirectWasmValueType.I64 => _stringFromI64,
            DirectWasmValueType.F64 => _stringFromF64,
            _ => throw Unsupported(expression, "string interpolation type")
        });
    }

    private DirectStringData RegisterStringData(string value)
    {
        if (_stringData.TryGetValue(value, out var existing)) return existing;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var data = new DirectStringData(_module.AddData(bytes), bytes.Length);
        _stringData[value] = data;
        return data;
    }

    private void EmitUnary(Unary unary, FunctionContext context)
    {
        var type = GetType(unary);
        if (unary.Operator.Type == TokenType.Not)
        {
            EmitCondition(unary.Right, context);
            context.Body.Op(0x45);
            return;
        }
        if (unary.Operator.Type == TokenType.Plus)
        {
            EmitExpressionAs(unary.Right, type, context);
            return;
        }
        if (MapType(type) == DirectWasmValueType.F64)
        {
            context.Body.F64Const(0);
            EmitExpressionAs(unary.Right, type, context);
            context.Body.Op(0xa1);
        }
        else
        {
            context.Body.I64Const(0);
            EmitExpressionAs(unary.Right, type, context);
            context.Body.Op(0x7d);
        }
    }

    private void EmitCompoundAssignment(CompoundAssignExpr expression, FunctionContext context)
    {
        var type = GetType(expression);
        switch (expression.Target)
        {
            case Variable variable when variable.ResolvesToImplicitField:
            {
                var owner = context.Function.Owner!;
                int address = context.AddTemporary(DirectWasmValueType.I32);
                context.Body.LocalGet(0);
                EmitFieldLoadAddress(owner, variable.Name.Lexeme, context.Body);
                context.Body.LocalTee(address);
                EmitMemoryLoad(type, context.Body);
                EmitExpressionAs(expression.Value, type, context);
                context.Body.Op(BinaryOpcode(expression.Operator.Type, MapType(type)));
                int result = context.AddTemporary(MapType(type));
                context.Body.LocalSet(result);
                context.Body.LocalGet(address);
                context.Body.LocalGet(result);
                EmitMemoryStore(type, context.Body);
                context.Body.LocalGet(result);
                break;
            }
            case Variable variable when context.TryLookup(variable.Name.Lexeme, out var local):
                context.Body.LocalGet(local.Index);
                EmitExpressionAs(expression.Value, local.Type, context);
                context.Body.Op(BinaryOpcode(expression.Operator.Type, local.WasmType));
                context.Body.LocalTee(local.Index);
                break;
            case Variable variable when _globals.TryGetValue(variable.Name.Lexeme, out var global):
            {
                context.Body.GlobalGet(global.Index);
                EmitExpressionAs(expression.Value, global.Type, context);
                context.Body.Op(BinaryOpcode(expression.Operator.Type, global.WasmType));
                int value = context.AddTemporary(global.WasmType);
                context.Body.LocalTee(value);
                context.Body.GlobalSet(global.Index);
                context.Body.LocalGet(value);
                break;
            }
            case FieldAccessExpr field:
                if (field.ResolvesToInterfaceField)
                    EmitCompoundInterfaceField(field, expression.Value, expression.Operator.Type, context);
                else
                    EmitCompoundField(field, expression.Value, expression.Operator.Type, context);
                break;
            case ArrayIndexExpr index when GetType(index.Array).NormalizeBuiltInShorthands().Name == "map":
                EmitCompoundMap(index, expression.Value, expression.Operator.Type, context);
                break;
            default:
                throw Unsupported(expression, "compound assignment target");
        }
    }

    private void EmitCompoundField(FieldAccessExpr field, Expr value, TokenType operation, FunctionContext context)
    {
        var type = GetType(field);
        int address = context.AddTemporary(DirectWasmValueType.I32);
        EmitFieldAddress(field, context);
        context.Body.LocalTee(address);
        EmitMemoryLoad(type, context.Body);
        EmitExpressionAs(value, type, context);
        context.Body.Op(BinaryOpcode(operation, MapType(type)));
        int result = context.AddTemporary(MapType(type));
        context.Body.LocalSet(result);
        context.Body.LocalGet(address);
        context.Body.LocalGet(result);
        EmitMemoryStore(type, context.Body);
        context.Body.LocalGet(result);
    }

    private void EmitNewArray(NewArrayExpr array, FunctionContext context)
    {
        EmitArrayAllocation(array.ElementType, () =>
        {
            EmitExpressionAs(array.Size, IntegerType(), context);
            context.Body.Op(0xa7);
        }, context);
    }

    private void EmitCollectionLength(ArrayLengthExpr length, FunctionContext context)
    {
        var targetType = GetType(length.Target).NormalizeBuiltInShorthands();
        EmitExpressionAs(length.Target, targetType, context);
        if (targetType.Name == "array")
        {
            context.Body.Load(0x28, 2);
            context.Body.Op(0xac);
        }
        else
        {
            context.Body.Call(_collectionLength);
        }
    }

    private void EmitIndexGet(ArrayIndexExpr index, FunctionContext context)
    {
        var targetType = GetType(index.Array).NormalizeBuiltInShorthands();
        if (targetType.Name == "array")
        {
            EmitArrayAddress(index.Array, index.Index, index.ResolvedElementTypeRef!, context);
            EmitMemoryLoad(index.ResolvedElementTypeRef!, context.Body);
            return;
        }
        var keyType = targetType.TypeArguments[0];
        var valueType = targetType.TypeArguments[1];
        EnsureDirectWasmCollectionKeySupported(keyType, index);
        EmitExpressionAs(index.Array, targetType, context);
        EmitExpressionAs(index.Index, keyType, context);
        context.Body.Call(CollectionFunction("map-get", MapType(keyType), MapType(valueType)));
    }

    private void EmitIndexSet(ArrayIndexExpr index, Expr value, FunctionContext context)
    {
        var targetType = GetType(index.Array).NormalizeBuiltInShorthands();
        if (targetType.Name == "array")
        {
            EmitArraySet(index.Array, index.Index, value, index.ResolvedElementTypeRef!, context);
            return;
        }
        var keyType = targetType.TypeArguments[0];
        var valueType = targetType.TypeArguments[1];
        EnsureDirectWasmCollectionKeySupported(keyType, index);
        int result = context.AddTemporary(MapType(valueType));
        EmitExpressionAs(value, valueType, context);
        context.Body.LocalSet(result);
        EmitExpressionAs(index.Array, targetType, context);
        EmitExpressionAs(index.Index, keyType, context);
        context.Body.LocalGet(result);
        context.Body.Call(CollectionFunction("map-set", MapType(keyType), MapType(valueType)));
        context.Body.LocalGet(result);
    }

    private void EmitCompoundMap(ArrayIndexExpr index, Expr value, TokenType operation, FunctionContext context)
    {
        var mapType = GetType(index.Array).NormalizeBuiltInShorthands();
        var keyType = mapType.TypeArguments[0];
        var valueType = mapType.TypeArguments[1];
        EnsureDirectWasmCollectionKeySupported(keyType, index);
        int map = context.AddTemporary(DirectWasmValueType.I32);
        int key = context.AddTemporary(MapType(keyType));
        int result = context.AddTemporary(MapType(valueType));
        EmitExpressionAs(index.Array, mapType, context);
        context.Body.LocalSet(map);
        EmitExpressionAs(index.Index, keyType, context);
        context.Body.LocalSet(key);
        context.Body.LocalGet(map);
        context.Body.LocalGet(key);
        context.Body.Call(CollectionFunction("contains", MapType(keyType)));
        context.Body.Op(0x04);
        context.Body.Op((byte)MapType(valueType));
        context.Body.LocalGet(map);
        context.Body.LocalGet(key);
        context.Body.Call(CollectionFunction("map-get", MapType(keyType), MapType(valueType)));
        context.Body.Op(0x05);
        EmitDefault(valueType, context.Body);
        context.Body.Op(0x0b);
        EmitExpressionAs(value, valueType, context);
        context.Body.Op(BinaryOpcode(operation, MapType(valueType)));
        context.Body.LocalSet(result);
        context.Body.LocalGet(map);
        context.Body.LocalGet(key);
        context.Body.LocalGet(result);
        context.Body.Call(CollectionFunction("map-set", MapType(keyType), MapType(valueType)));
        context.Body.LocalGet(result);
    }

    private void EmitArrayLiteral(ArrayLiteral array, FunctionContext context)
    {
        var arrayType = GetType(array).NormalizeBuiltInShorthands();
        var elementType = arrayType.TypeArguments[0];
        EmitArrayAllocation(elementType, () => context.Body.I32Const(array.Elements.Count), context);
        int pointer = context.AddTemporary(DirectWasmValueType.I32);
        context.Body.LocalSet(pointer);
        for (int index = 0; index < array.Elements.Count; index++)
        {
            context.Body.LocalGet(pointer);
            context.Body.Load(0x28, 2, 8);
            context.Body.I32Const(index * StorageSize(MapType(elementType)));
            context.Body.Op(0x6a);
            EmitExpressionAs(array.Elements[index], elementType, context);
            EmitMemoryStore(elementType, context.Body);
        }
        context.Body.LocalGet(pointer);
    }

    private void EmitArrayAllocation(TypeRef elementType, Action emitLength, FunctionContext context)
    {
        int length = context.AddTemporary(DirectWasmValueType.I32);
        int capacity = context.AddTemporary(DirectWasmValueType.I32);
        int pointer = context.AddTemporary(DirectWasmValueType.I32);
        int data = context.AddTemporary(DirectWasmValueType.I32);
        emitLength();
        context.Body.LocalSet(length);
        context.Body.LocalGet(length);
        context.Body.I32Const(4);
        context.Body.LocalGet(length);
        context.Body.I32Const(4);
        context.Body.Op(0x4b); // i32.gt_u
        context.Body.Op(0x1b); // select max(length, 4)
        context.Body.LocalSet(capacity);
        context.Body.I32Const(16);
        context.Body.Call(_allocator);
        context.Body.LocalSet(pointer);
        context.Body.LocalGet(capacity);
        context.Body.I32Const(StorageSize(MapType(elementType)));
        context.Body.Op(0x6c);
        context.Body.Call(_allocator);
        context.Body.LocalSet(data);
        context.Body.LocalGet(pointer);
        context.Body.LocalGet(length);
        context.Body.Store(0x36, 2);
        context.Body.LocalGet(pointer);
        context.Body.LocalGet(capacity);
        context.Body.Store(0x36, 2, 4);
        context.Body.LocalGet(pointer);
        context.Body.LocalGet(data);
        context.Body.Store(0x36, 2, 8);
        context.Body.LocalGet(pointer);
        context.Body.I32Const(StorageSize(MapType(elementType)));
        context.Body.Store(0x36, 2, 12);
        context.Body.LocalGet(data);
        context.Body.I32Const(0);
        context.Body.LocalGet(capacity);
        context.Body.I32Const(StorageSize(MapType(elementType)));
        context.Body.Op(0x6c);
        context.Body.Op(0xfc); context.Body.U32(11); context.Body.U32(0); // memory.fill
        context.Body.LocalGet(pointer);
    }

    private void EmitArrayAddress(Expr array, Expr index, TypeRef elementType, FunctionContext context)
    {
        EmitExpressionAs(array, ReferenceType(), context);
        context.Body.Load(0x28, 2, 8);
        EmitExpressionAs(index, IntegerType(), context);
        context.Body.Op(0xa7);
        context.Body.I32Const(StorageSize(MapType(elementType)));
        context.Body.Op(0x6c);
        context.Body.Op(0x6a);
    }

    private void EmitArraySet(Expr array, Expr index, Expr value, TypeRef elementType, FunctionContext context)
    {
        int address = context.AddTemporary(DirectWasmValueType.I32);
        int result = context.AddTemporary(MapType(elementType));
        EmitArrayAddress(array, index, elementType, context);
        context.Body.LocalSet(address);
        EmitExpressionAs(value, elementType, context);
        context.Body.LocalSet(result);
        context.Body.LocalGet(address);
        context.Body.LocalGet(result);
        EmitMemoryStore(elementType, context.Body);
        context.Body.LocalGet(result);
    }

    private void EmitNewObject(NewObjectExpr instance, FunctionContext context)
    {
        EmitNewObject(instance.TypeName, instance.Arguments, instance.ResolvedDefaultArguments, instance.ResolvedConstructorKey, context);
    }

    private void EmitNewObject(
        Token typeName,
        IReadOnlyList<Expr> arguments,
        IReadOnlyList<Expr> defaultArguments,
        string? resolvedConstructorKey,
        FunctionContext context)
    {
        var layout = GetObject(typeName.Lexeme);
        int pointer = context.AddTemporary(DirectWasmValueType.I32);
        context.Body.I32Const(layout.Size);
        context.Body.Call(_allocator);
        context.Body.LocalTee(pointer);
        context.Body.I32Const(ObjectTypeId(layout.Name));
        context.Body.Store(0x36, 2);
        foreach (var field in layout.Declaration.Fields)
        {
            if (field.Initializer is null) continue;
            context.Body.LocalGet(pointer);
            EmitFieldLoadAddress(layout, field.Name.Lexeme, context.Body);
            EmitExpressionAs(field.Initializer, field.Type, context);
            EmitMemoryStore(field.Type, context.Body);
        }
        var allArguments = CombineArguments(arguments, defaultArguments);
        string key = resolvedConstructorKey ?? ConstructorKey(layout.Name, allArguments.Count);
        if (_functions.TryGetValue(key, out var constructor))
        {
            context.Body.LocalGet(pointer);
            for (int index = 0; index < allArguments.Count; index++)
                EmitExpressionAs(allArguments[index], constructor.Parameters[index].Type!, context);
            context.Body.Call(constructor.Index);
        }
        else if (allArguments.Count != 0)
            throw new CompilerException($"Direct-Wasm does not support constructor '{key}'.", typeName.Line, typeName.Column);
        context.Body.LocalGet(pointer);
    }

    private void EmitFieldAddress(FieldAccessExpr field, FunctionContext context)
    {
        var targetType = GetType(field.Target);
        var layout = GetObject(targetType.Name);
        EmitExpressionAs(field.Target, ReferenceType(), context);
        EmitFieldLoadAddress(layout, field.Name.Lexeme, context.Body);
    }

    private IReadOnlyList<DirectInterfaceFieldTarget> InterfaceFieldTargets(FieldAccessExpr field)
    {
        string interfaceName = GetType(field.Target).Name;
        string fieldName = field.ResolvedInterfaceFieldName ?? field.Name.Lexeme;
        string key = $"{interfaceName}.{fieldName}";
        if (!_interfaceFieldDispatch.TryGetValue(key, out var targets) || targets.Count == 0)
            throw Unsupported(field, $"interface field dispatch '{key}'");
        return targets;
    }

    private void EmitInterfaceFieldGet(FieldAccessExpr field, FunctionContext context)
    {
        var type = GetType(field);
        var targets = InterfaceFieldTargets(field);
        int receiver = context.AddTemporary(DirectWasmValueType.I32);
        EmitExpressionAs(field.Target, GetType(field.Target), context);
        context.Body.LocalSet(receiver);
        EmitInterfaceFieldGetTarget(targets, 0, receiver, type, context);
    }

    private void EmitInterfaceFieldGetTarget(
        IReadOnlyList<DirectInterfaceFieldTarget> targets,
        int index,
        int receiver,
        TypeRef type,
        FunctionContext context)
    {
        var target = targets[index];
        if (index + 1 < targets.Count)
        {
            context.Body.LocalGet(receiver);
            context.Body.Load(0x28, 2);
            context.Body.I32Const(ObjectTypeId(target.Owner.Name));
            context.Body.Op(0x46);
            context.Body.Op(0x04);
            context.Body.Op((byte)MapType(type));
            EmitDirectInterfaceFieldGet(target, receiver, type, context);
            context.Body.Op(0x05);
            EmitInterfaceFieldGetTarget(targets, index + 1, receiver, type, context);
            context.Body.Op(0x0b);
            return;
        }

        EmitDirectInterfaceFieldGet(target, receiver, type, context);
    }

    private void EmitDirectInterfaceFieldGet(DirectInterfaceFieldTarget target, int receiver, TypeRef type, FunctionContext context)
    {
        context.Body.LocalGet(receiver);
        EmitFieldLoadAddress(target.Owner, target.Field.Name, context.Body);
        EmitMemoryLoad(type, context.Body);
    }

    private void EmitInterfaceFieldSet(FieldAccessExpr field, Expr value, FunctionContext context)
    {
        var type = GetType(field);
        var targets = InterfaceFieldTargets(field);
        int receiver = context.AddTemporary(DirectWasmValueType.I32);
        int result = context.AddTemporary(MapType(type));
        EmitExpressionAs(field.Target, GetType(field.Target), context);
        context.Body.LocalSet(receiver);
        EmitExpressionAs(value, type, context);
        context.Body.LocalSet(result);
        EmitInterfaceFieldSetTarget(targets, 0, receiver, result, type, context);
    }

    private void EmitInterfaceFieldSetTarget(
        IReadOnlyList<DirectInterfaceFieldTarget> targets,
        int index,
        int receiver,
        int result,
        TypeRef type,
        FunctionContext context)
    {
        var target = targets[index];
        if (index + 1 < targets.Count)
        {
            context.Body.LocalGet(receiver);
            context.Body.Load(0x28, 2);
            context.Body.I32Const(ObjectTypeId(target.Owner.Name));
            context.Body.Op(0x46);
            context.Body.Op(0x04);
            context.Body.Op((byte)MapType(type));
            EmitDirectInterfaceFieldSet(target, receiver, result, type, context);
            context.Body.Op(0x05);
            EmitInterfaceFieldSetTarget(targets, index + 1, receiver, result, type, context);
            context.Body.Op(0x0b);
            return;
        }

        EmitDirectInterfaceFieldSet(target, receiver, result, type, context);
    }

    private void EmitDirectInterfaceFieldSet(DirectInterfaceFieldTarget target, int receiver, int result, TypeRef type, FunctionContext context)
    {
        context.Body.LocalGet(receiver);
        EmitFieldLoadAddress(target.Owner, target.Field.Name, context.Body);
        context.Body.LocalGet(result);
        EmitMemoryStore(type, context.Body);
        context.Body.LocalGet(result);
    }

    private void EmitCompoundInterfaceField(FieldAccessExpr field, Expr value, TokenType operation, FunctionContext context)
    {
        var type = GetType(field);
        var targets = InterfaceFieldTargets(field);
        int receiver = context.AddTemporary(DirectWasmValueType.I32);
        int current = context.AddTemporary(MapType(type));
        int rhs = context.AddTemporary(MapType(type));
        int result = context.AddTemporary(MapType(type));
        EmitExpressionAs(field.Target, GetType(field.Target), context);
        context.Body.LocalSet(receiver);
        EmitInterfaceFieldGetTarget(targets, 0, receiver, type, context);
        context.Body.LocalSet(current);
        EmitExpressionAs(value, type, context);
        context.Body.LocalSet(rhs);
        context.Body.LocalGet(current);
        context.Body.LocalGet(rhs);
        context.Body.Op(BinaryOpcode(operation, MapType(type)));
        context.Body.LocalSet(result);
        EmitInterfaceFieldSetTarget(targets, 0, receiver, result, type, context);
    }

    private static void EmitFieldLoadAddress(DirectObjectLayout layout, string fieldName, DirectWasmFunctionBody body)
    {
        if (!layout.Fields.TryGetValue(fieldName, out var field))
            throw new InvalidOperationException($"Unknown direct-Wasm field '{layout.Name}.{fieldName}'.");
        body.I32Const(field.Offset);
        body.Op(0x6a);
    }

    private void EmitFieldSet(FieldAccessExpr field, Expr value, FunctionContext context)
    {
        var type = GetType(field);
        int address = context.AddTemporary(DirectWasmValueType.I32);
        int result = context.AddTemporary(MapType(type));
        EmitFieldAddress(field, context);
        context.Body.LocalSet(address);
        EmitExpressionAs(value, type, context);
        context.Body.LocalSet(result);
        context.Body.LocalGet(address);
        context.Body.LocalGet(result);
        EmitMemoryStore(type, context.Body);
        context.Body.LocalGet(result);
    }

    private bool EmitCall(Call call, FunctionContext context)
    {
        if (EmitNativeMathCall(call, context, out bool nativeLeavesValue))
            return nativeLeavesValue;
        if (call.ResolvesToConstructor)
        {
            var typeToken = new Token(TokenType.Identifier, call.ResolvedConstructorTypeName!, null, call.Callee.Line, call.Callee.Column);
            EmitNewObject(typeToken, call.Arguments, call.ResolvedDefaultArguments, call.ResolvedConstructorKey, context);
            return true;
        }
        DirectFunction function;
        if (call.ResolvesToImplicitMethod)
        {
            function = _functions[call.ResolvedImplicitMethodKey!];
            context.Body.LocalGet(0);
        }
        else if (_functions.TryGetValue(call.Callee.Lexeme, out function!))
        {
        }
        else if (_hostFunctions.TryGetValue(call.Callee.Lexeme, out var host))
        {
            for (int index = 0; index < call.Arguments.Count; index++)
                EmitExpressionAs(call.Arguments[index], ParseTypeName(host.Intrinsic.ParameterTypeNames[index]), context);
            context.Body.Call(host.Index);
            return !IsVoid(host.ReturnType);
        }
        else throw Unsupported(call, $"call '{call.Callee.Lexeme}'");
        var allArguments = CombineArguments(call.Arguments, call.ResolvedDefaultArguments);
        for (int index = 0; index < allArguments.Count; index++)
            EmitExpressionAs(allArguments[index], function.Parameters[index].Type!, context);
        context.Body.Call(function.Index);
        return !IsVoid(function.ReturnType);
    }

    private bool EmitNativeMathCall(Call call, FunctionContext context, out bool leavesValue)
    {
        leavesValue = true;
        switch (call.Callee.Lexeme)
        {
            case "squareRoot":
                EmitExpressionAs(call.Arguments[0], RealType(), context);
                context.Body.Op(0x9f);
                return true;
            case "minimum":
            case "maximum":
                EmitExpressionAs(call.Arguments[0], RealType(), context);
                EmitExpressionAs(call.Arguments[1], RealType(), context);
                context.Body.Op(call.Callee.Lexeme == "minimum" ? (byte)0xa4 : (byte)0xa5);
                return true;
            case "absolute":
                EmitExpressionAs(call.Arguments[0], RealType(), context);
                context.Body.Op(0x99);
                return true;
            case "lerp":
                EmitExpressionAs(call.Arguments[0], RealType(), context);
                EmitExpressionAs(call.Arguments[1], RealType(), context);
                EmitExpressionAs(call.Arguments[0], RealType(), context);
                context.Body.Op(0xa1);
                EmitExpressionAs(call.Arguments[2], RealType(), context);
                context.Body.Op(0xa2);
                context.Body.Op(0xa0);
                return true;
            default:
                leavesValue = false;
                return false;
        }
    }

    private bool EmitMethodCall(MethodCallExpr call, FunctionContext context)
    {
        if (call.ResolvesToBuiltInCollectionMethod)
            return EmitBuiltInCollectionCall(call, context);
        if (call.ResolvedInterfaceName is not null && call.ResolvedInterfaceMethodKey is not null)
            return EmitInterfaceCall(call, context);
        if (call.ResolvedMethodKey is null || !_functions.TryGetValue(call.ResolvedMethodKey, out var function))
            throw Unsupported(call, $"method '{call.MethodName.Lexeme}'");
        if (IsRecord(GetType(call.Target))) EmitRecordClone(call.Target, GetType(call.Target), context);
        else EmitExpressionAs(call.Target, ReferenceType(), context);
        var allArguments = CombineArguments(call.Arguments, call.ResolvedDefaultArguments);
        for (int index = 0; index < allArguments.Count; index++)
            EmitExpressionAs(allArguments[index], function.Parameters[index].Type!, context);
        context.Body.Call(function.Index);
        return !IsVoid(function.ReturnType);
    }

    private bool EmitInterfaceCall(MethodCallExpr call, FunctionContext context)
    {
        string key = $"{call.ResolvedInterfaceName}.{call.ResolvedInterfaceMethodKey}";
        if (!_interfaceDispatch.TryGetValue(key, out var targets) || targets.Count == 0)
        {
            targets = _functions.Values
                .Where(function => function.Owner is not null &&
                    function.DisplayName.EndsWith($".{call.MethodName.Lexeme}", StringComparison.Ordinal) &&
                    function.Parameters.Count == call.Arguments.Count)
                .Select(function => new DirectInterfaceTarget(function.Owner!, function))
                .ToList();
            if (targets.Count == 0) throw Unsupported(call, $"interface dispatch '{key}'");
        }
        int receiver = context.AddTemporary(DirectWasmValueType.I32);
        EmitExpressionAs(call.Target, GetType(call.Target), context);
        context.Body.LocalSet(receiver);
        var returnType = call.ResolvedReturnTypeRef ?? VoidType(call.MethodName);
        EmitInterfaceTarget(targets, 0, receiver, call.Arguments, returnType, context);
        return !IsVoid(returnType);
    }

    private void EmitInterfaceTarget(
        IReadOnlyList<DirectInterfaceTarget> targets,
        int index,
        int receiver,
        IReadOnlyList<Expr> arguments,
        TypeRef returnType,
        FunctionContext context)
    {
        var target = targets[index];
        if (index + 1 < targets.Count)
        {
            context.Body.LocalGet(receiver);
            context.Body.Load(0x28, 2);
            context.Body.I32Const(ObjectTypeId(target.Owner.Name));
            context.Body.Op(0x46);
            context.Body.Op(0x04);
            context.Body.Op(IsVoid(returnType) ? (byte)0x40 : (byte)MapType(returnType));
            EmitDirectInterfaceTarget(target, receiver, arguments, context);
            context.Body.Op(0x05);
            EmitInterfaceTarget(targets, index + 1, receiver, arguments, returnType, context);
            context.Body.Op(0x0b);
            return;
        }
        EmitDirectInterfaceTarget(target, receiver, arguments, context);
    }

    private void EmitDirectInterfaceTarget(DirectInterfaceTarget target, int receiver, IReadOnlyList<Expr> arguments, FunctionContext context)
    {
        context.Body.LocalGet(receiver);
        for (int argumentIndex = 0; argumentIndex < arguments.Count; argumentIndex++)
            EmitExpressionAs(arguments[argumentIndex], target.Function.Parameters[argumentIndex].Type!, context);
        context.Body.Call(target.Function.Index);
    }

    private bool EmitBuiltInCollectionCall(MethodCallExpr call, FunctionContext context)
    {
        var targetType = GetType(call.Target).NormalizeBuiltInShorthands();
        if (targetType.Name == "array" && call.ResolvedBuiltInCollectionMethodName == "append")
        {
            EmitArrayAppend(call.Target, call.Arguments[0], targetType.TypeArguments[0], context);
            return false;
        }
        if (targetType.Name == "array" && call.ResolvedBuiltInCollectionMethodName == "removeAt")
        {
            EmitArrayRemoveAt(call.Target, call.Arguments[0], targetType.TypeArguments[0], context);
            return false;
        }
        if (targetType.Name == "map")
        {
            var keyType = targetType.TypeArguments[0];
            EnsureDirectWasmCollectionKeySupported(keyType, call);
            EmitExpressionAs(call.Target, targetType, context);
            EmitExpressionAs(call.Arguments[0], keyType, context);
            string operation = call.ResolvedBuiltInCollectionMethodName == "contains" ? "contains" : "remove";
            context.Body.Call(CollectionFunction(operation, MapType(keyType)));
            return operation == "contains";
        }
        if (targetType.Name == "set")
        {
            var valueType = targetType.TypeArguments[0];
            EnsureDirectWasmCollectionKeySupported(valueType, call);
            EmitExpressionAs(call.Target, targetType, context);
            EmitExpressionAs(call.Arguments[0], valueType, context);
            string operation = call.ResolvedBuiltInCollectionMethodName switch
            {
                "add" => "add",
                "contains" => "contains",
                _ => "remove"
            };
            context.Body.Call(CollectionFunction(operation, MapType(valueType)));
            return operation == "contains";
        }
        if (targetType.Name is "queue" or "stack")
        {
            var valueType = targetType.TypeArguments[0];
            string method = call.ResolvedBuiltInCollectionMethodName!;
            string operation = method switch
            {
                "enqueue" or "push" => "add",
                "peek" => "peek",
                "dequeue" or "pop" => "pop",
                _ => throw Unsupported(call, $"collection method '{targetType.Name}.{method}'")
            };
            EmitExpressionAs(call.Target, targetType, context);
            if (operation == "add") EmitExpressionAs(call.Arguments[0], valueType, context);
            context.Body.Call(CollectionFunction(operation, MapType(valueType)));
            return operation is "peek" or "pop";
        }
        throw Unsupported(call, $"collection method '{targetType.Name}.{call.ResolvedBuiltInCollectionMethodName}'");
    }

    private void EmitArrayAppend(Expr target, Expr value, TypeRef elementType, FunctionContext context)
    {
        int pointer = context.AddTemporary(DirectWasmValueType.I32);
        int length = context.AddTemporary(DirectWasmValueType.I32);
        int capacity = context.AddTemporary(DirectWasmValueType.I32);
        int data = context.AddTemporary(DirectWasmValueType.I32);
        int newData = context.AddTemporary(DirectWasmValueType.I32);
        int elementSize = StorageSize(MapType(elementType));
        EmitExpressionAs(target, GetType(target), context);
        context.Body.LocalSet(pointer);
        context.Body.LocalGet(pointer);
        context.Body.Load(0x28, 2);
        context.Body.LocalSet(length);
        context.Body.LocalGet(pointer);
        context.Body.Load(0x28, 2, 4);
        context.Body.LocalTee(capacity);
        context.Body.LocalGet(length);
        context.Body.Op(0x4d); // i32.le_u
        context.Body.Op(0x04); context.Body.Op(0x40);
        context.Body.LocalGet(capacity);
        context.Body.I32Const(2);
        context.Body.Op(0x6c);
        context.Body.LocalTee(capacity);
        context.Body.I32Const(elementSize);
        context.Body.Op(0x6c);
        context.Body.Call(_allocator);
        context.Body.LocalSet(newData);
        context.Body.LocalGet(newData);
        context.Body.LocalGet(pointer);
        context.Body.Load(0x28, 2, 8);
        context.Body.LocalGet(length);
        context.Body.I32Const(elementSize);
        context.Body.Op(0x6c);
        context.Body.Op(0xfc); context.Body.U32(10); context.Body.U32(0); context.Body.U32(0);
        context.Body.LocalGet(pointer);
        context.Body.LocalGet(capacity);
        context.Body.Store(0x36, 2, 4);
        context.Body.LocalGet(pointer);
        context.Body.LocalGet(newData);
        context.Body.Store(0x36, 2, 8);
        context.Body.Op(0x0b);
        context.Body.LocalGet(pointer);
        context.Body.Load(0x28, 2, 8);
        context.Body.LocalSet(data);
        context.Body.LocalGet(data);
        context.Body.LocalGet(length);
        context.Body.I32Const(elementSize);
        context.Body.Op(0x6c);
        context.Body.Op(0x6a);
        EmitExpressionAs(value, elementType, context);
        EmitMemoryStore(elementType, context.Body);
        context.Body.LocalGet(pointer);
        context.Body.LocalGet(length);
        context.Body.I32Const(1);
        context.Body.Op(0x6a);
        context.Body.Store(0x36, 2);
    }

    private void EmitArrayRemoveAt(Expr target, Expr indexExpression, TypeRef elementType, FunctionContext context)
    {
        int pointer = context.AddTemporary(DirectWasmValueType.I32);
        int index = context.AddTemporary(DirectWasmValueType.I32);
        int length = context.AddTemporary(DirectWasmValueType.I32);
        int destination = context.AddTemporary(DirectWasmValueType.I32);
        int elementSize = StorageSize(MapType(elementType));
        EmitExpressionAs(target, GetType(target), context);
        context.Body.LocalSet(pointer);
        EmitExpressionAs(indexExpression, IntegerType(), context);
        context.Body.Op(0xa7);
        context.Body.LocalSet(index);
        context.Body.LocalGet(pointer);
        context.Body.Load(0x28, 2);
        context.Body.LocalSet(length);
        context.Body.LocalGet(pointer);
        context.Body.Load(0x28, 2, 8);
        context.Body.LocalGet(index);
        context.Body.I32Const(elementSize);
        context.Body.Op(0x6c);
        context.Body.Op(0x6a);
        context.Body.LocalTee(destination);
        context.Body.LocalGet(destination);
        context.Body.I32Const(elementSize);
        context.Body.Op(0x6a);
        context.Body.LocalGet(length);
        context.Body.LocalGet(index);
        context.Body.Op(0x6b);
        context.Body.I32Const(1);
        context.Body.Op(0x6b);
        context.Body.I32Const(elementSize);
        context.Body.Op(0x6c);
        context.Body.Op(0xfc); context.Body.U32(10); context.Body.U32(0); context.Body.U32(0);
        context.Body.LocalGet(pointer);
        context.Body.LocalGet(length);
        context.Body.I32Const(1);
        context.Body.Op(0x6b);
        context.Body.Store(0x36, 2);
    }

    private void EmitExpressionAs(Expr expression, TypeRef expected, FunctionContext context)
    {
        if (expression is Literal literal &&
            (literal.Value is null || ReferenceEquals(literal.Value, ConsoleApp1.OptionalNone.Value)) &&
            IsOptional(expected))
        {
            context.Body.I32Const(0);
            return;
        }
        var actual = GetType(expression);
        if (IsFallible(expected) && !IsFallible(actual))
        {
            EmitFallibleSuccess(expression, expected, context);
            return;
        }
        if (IsRecord(expected) && IsRecord(actual))
        {
            EmitRecordClone(expression, actual, context);
            return;
        }
        if (IsOptional(expected) && !IsOptional(actual))
        {
            EmitOptionalBox(expression, expected, context);
            return;
        }
        bool leaves = EmitExpression(expression, context);
        if (!leaves) throw Unsupported(expression, "value-producing expression");
        EmitConversion(MapType(actual), MapType(expected), context.Body);
    }

    private void EmitRecordClone(Expr expression, TypeRef recordType, FunctionContext context)
    {
        var layout = GetObject(recordType.NormalizeBuiltInShorthands().Name);
        int source = context.AddTemporary(DirectWasmValueType.I32);
        int destination = context.AddTemporary(DirectWasmValueType.I32);
        bool leaves = EmitExpression(expression, context);
        if (!leaves) throw Unsupported(expression, "record value expression");
        context.Body.LocalSet(source);
        context.Body.I32Const(layout.Size);
        context.Body.Call(_allocator);
        context.Body.LocalTee(destination);
        context.Body.LocalGet(source);
        context.Body.I32Const(layout.Size);
        context.Body.Op(0xfc); context.Body.U32(10); context.Body.U32(0); context.Body.U32(0);
        context.Body.LocalGet(destination);
    }

    private void EmitOptionalBox(Expr expression, TypeRef optionalType, FunctionContext context)
    {
        var valueType = optionalType.NormalizeBuiltInShorthands().TypeArguments[0];
        int pointer = context.AddTemporary(DirectWasmValueType.I32);
        context.Body.I32Const(Align(8 + StorageSize(MapType(valueType)), 8));
        context.Body.Call(_allocator);
        context.Body.LocalTee(pointer);
        context.Body.I32Const(1);
        context.Body.Store(0x36, 2);
        context.Body.LocalGet(pointer);
        context.Body.I32Const(8);
        context.Body.Op(0x6a);
        EmitExpressionAs(expression, valueType, context);
        EmitMemoryStore(valueType, context.Body);
        context.Body.LocalGet(pointer);
    }

    private void EmitFallibleSuccess(Expr expression, TypeRef fallibleType, FunctionContext context)
    {
        fallibleType.TryGetFallibleTypeArguments(out var successType, out _);
        int pointer = context.AddTemporary(DirectWasmValueType.I32);
        context.Body.I32Const(24);
        context.Body.Call(_allocator);
        context.Body.LocalSet(pointer);
        context.Body.LocalGet(pointer);
        context.Body.I32Const(0);
        context.Body.Store(0x36, 2);
        context.Body.LocalGet(pointer);
        context.Body.I32Const(8);
        context.Body.Op(0x6a);
        EmitExpressionAs(expression, successType, context);
        EmitMemoryStore(successType, context.Body);
        context.Body.LocalGet(pointer);
    }

    private void EmitFallibleError(FallibleErrorExpr error, FunctionContext context)
    {
        var fallibleType = error.ResolvedFallibleTypeRef ?? GetType(error);
        fallibleType.TryGetFallibleTypeArguments(out _, out var codeType);
        int pointer = context.AddTemporary(DirectWasmValueType.I32);
        context.Body.I32Const(24);
        context.Body.Call(_allocator);
        context.Body.LocalSet(pointer);
        context.Body.LocalGet(pointer);
        context.Body.I32Const(1);
        context.Body.Store(0x36, 2);
        context.Body.LocalGet(pointer);
        context.Body.I32Const(8);
        context.Body.Op(0x6a);
        if (error.ResolvedUsesDefaultIntegerCode) context.Body.I64Const(0);
        else EmitExpressionAs(error.Arguments[0], codeType, context);
        EmitMemoryStore(codeType, context.Body);
        context.Body.LocalGet(pointer);
        context.Body.I32Const(16);
        context.Body.Op(0x6a);
        if (error.ResolvedUsesDefaultIntegerCode) EmitExpressionAs(error.Arguments[0], StringType(), context);
        else if (error.Arguments.Count > 1) EmitExpressionAs(error.Arguments[1], StringType(), context);
        else EmitStringLiteral(string.Empty, context.Body);
        context.Body.Store(0x36, 2);
        context.Body.LocalGet(pointer);
    }

    private void EmitOnError(OnErrorExpr error, FunctionContext context)
    {
        var fallibleType = GetType(error.Fallible);
        fallibleType.TryGetFallibleTypeArguments(out var successType, out var codeType);
        int pointer = context.AddTemporary(DirectWasmValueType.I32);
        int result = context.AddTemporary(MapType(successType));
        int code = context.AddTemporary(MapType(codeType));
        int message = context.AddTemporary(DirectWasmValueType.I32);
        EmitExpressionAs(error.Fallible, fallibleType, context);
        context.Body.LocalSet(pointer);
        context.Body.LocalGet(pointer);
        context.Body.Load(0x28, 2);
        context.Body.Op(0x04); context.Body.Op(0x40);
        context.Body.LocalGet(pointer);
        context.Body.I32Const(8);
        context.Body.Op(0x6a);
        EmitMemoryLoad(codeType, context.Body);
        context.Body.LocalSet(code);
        context.Body.LocalGet(pointer);
        context.Body.Load(0x28, 2, 16);
        context.Body.LocalSet(message);
        var previous = context.SetErrorHandler(code, message, result, successType);
        EmitStatement(error.Handler, context);
        context.RestoreErrorHandler(previous);
        context.Body.Op(0x05);
        context.Body.LocalGet(pointer);
        context.Body.I32Const(8);
        context.Body.Op(0x6a);
        EmitMemoryLoad(successType, context.Body);
        context.Body.LocalSet(result);
        context.Body.Op(0x0b);
        context.Body.LocalGet(result);
    }

    private void EmitOptionalValue(OptionalValueExpr optional, FunctionContext context)
    {
        var optionalType = GetType(optional.Target).NormalizeBuiltInShorthands();
        var valueType = optionalType.TypeArguments[0];
        EmitExpressionAs(optional.Target, optionalType, context);
        context.Body.I32Const(8);
        context.Body.Op(0x6a);
        EmitMemoryLoad(valueType, context.Body);
    }

    private void EmitOptionalOr(OptionalOrExpr optional, FunctionContext context)
    {
        var optionalType = GetType(optional.Optional).NormalizeBuiltInShorthands();
        var valueType = optionalType.TypeArguments[0];
        int pointer = context.AddTemporary(DirectWasmValueType.I32);
        EmitExpressionAs(optional.Optional, optionalType, context);
        context.Body.LocalTee(pointer);
        context.Body.Op(0x04);
        context.Body.Op((byte)MapType(valueType));
        context.Body.LocalGet(pointer);
        context.Body.I32Const(8);
        context.Body.Op(0x6a);
        EmitMemoryLoad(valueType, context.Body);
        context.Body.Op(0x05);
        EmitExpressionAs(optional.Fallback, valueType, context);
        context.Body.Op(0x0b);
    }

    private static void EmitConversion(DirectWasmValueType actual, DirectWasmValueType expected, DirectWasmFunctionBody body)
    {
        if (actual == expected) return;
        if (actual == DirectWasmValueType.I64 && expected == DirectWasmValueType.F64) body.Op(0xb9);
        else if (actual == DirectWasmValueType.F64 && expected == DirectWasmValueType.I64) body.Op(0xb0);
        else if (actual == DirectWasmValueType.F64 && expected == DirectWasmValueType.I32) body.Op(0xaa);
        else if (actual == DirectWasmValueType.I32 && expected == DirectWasmValueType.F64) body.Op(0xb7);
        else if (actual == DirectWasmValueType.I32 && expected == DirectWasmValueType.I64) body.Op(0xac);
        else if (actual == DirectWasmValueType.I64 && expected == DirectWasmValueType.I32) body.Op(0xa7);
        else throw new InvalidOperationException($"Unsupported direct-Wasm conversion {actual} -> {expected}.");
    }

    private void EmitCondition(Expr expression, FunctionContext context)
    {
        EmitExpressionAs(expression, BooleanType(), context);
    }

    private TypeRef GetType(Expr expression) => _program.Semantics.Get(expression).TypeRef.NormalizeBuiltInShorthands();

    private static TypeRef Promote(TypeRef left, TypeRef right)
    {
        if (MapType(left) == DirectWasmValueType.F64 || MapType(right) == DirectWasmValueType.F64) return RealType();
        return IntegerType();
    }

    private static DirectWasmValueType MapType(TypeRef type)
    {
        type = type.NormalizeBuiltInShorthands();
        return type.Name switch
        {
            "boolean" => DirectWasmValueType.I32,
            "real" or "real32" => DirectWasmValueType.F64,
            "integer" or "whole" or "integer8" or "integer16" or "integer32" or "whole8" or "whole16" or "whole32" => DirectWasmValueType.I64,
            "array" or "map" or "set" or "queue" or "stack" or "string" or "optional" or "fallible" => DirectWasmValueType.I32,
            "void" => throw new InvalidOperationException("Void has no Wasm value type."),
            _ => DirectWasmValueType.I32
        };
    }

    private static int StorageSize(DirectWasmValueType type) => type is DirectWasmValueType.I64 or DirectWasmValueType.F64 ? 8 : 4;

    private static void EmitMemoryLoad(TypeRef type, DirectWasmFunctionBody body)
    {
        switch (MapType(type))
        {
            case DirectWasmValueType.I32: body.Load(0x28, 2); break;
            case DirectWasmValueType.I64: body.Load(0x29, 3); break;
            case DirectWasmValueType.F64: body.Load(0x2b, 3); break;
            default: throw new InvalidOperationException();
        }
    }

    private static void EmitMemoryStore(TypeRef type, DirectWasmFunctionBody body)
    {
        switch (MapType(type))
        {
            case DirectWasmValueType.I32: body.Store(0x36, 2); break;
            case DirectWasmValueType.I64: body.Store(0x37, 3); break;
            case DirectWasmValueType.F64: body.Store(0x39, 3); break;
            default: throw new InvalidOperationException();
        }
    }

    private static byte BinaryOpcode(TokenType operation, DirectWasmValueType type)
    {
        return (operation, type) switch
        {
            (TokenType.Plus, DirectWasmValueType.I64) => 0x7c,
            (TokenType.Minus, DirectWasmValueType.I64) => 0x7d,
            (TokenType.Star, DirectWasmValueType.I64) => 0x7e,
            (TokenType.Slash, DirectWasmValueType.I64) => 0x7f,
            (TokenType.Percent, DirectWasmValueType.I64) => 0x81,
            (TokenType.Plus, DirectWasmValueType.F64) => 0xa0,
            (TokenType.Minus, DirectWasmValueType.F64) => 0xa1,
            (TokenType.Star, DirectWasmValueType.F64) => 0xa2,
            (TokenType.Slash, DirectWasmValueType.F64) => 0xa3,
            (TokenType.EqualEqual, DirectWasmValueType.I64) => 0x51,
            (TokenType.BangEqual, DirectWasmValueType.I64) => 0x52,
            (TokenType.Less, DirectWasmValueType.I64) => 0x53,
            (TokenType.Greater, DirectWasmValueType.I64) => 0x55,
            (TokenType.LessEqual, DirectWasmValueType.I64) => 0x57,
            (TokenType.GreaterEqual, DirectWasmValueType.I64) => 0x59,
            (TokenType.EqualEqual, DirectWasmValueType.F64) => 0x61,
            (TokenType.BangEqual, DirectWasmValueType.F64) => 0x62,
            (TokenType.Less, DirectWasmValueType.F64) => 0x63,
            (TokenType.Greater, DirectWasmValueType.F64) => 0x64,
            (TokenType.LessEqual, DirectWasmValueType.F64) => 0x65,
            (TokenType.GreaterEqual, DirectWasmValueType.F64) => 0x66,
            (TokenType.EqualEqual, DirectWasmValueType.I32) => 0x46,
            (TokenType.BangEqual, DirectWasmValueType.I32) => 0x47,
            _ => throw new InvalidOperationException($"Unsupported direct-Wasm operation {operation} for {type}.")
        };
    }

    private static void EmitDefault(TypeRef type, DirectWasmFunctionBody body)
    {
        switch (MapType(type))
        {
            case DirectWasmValueType.I32: body.I32Const(0); break;
            case DirectWasmValueType.I64: body.I64Const(0); break;
            case DirectWasmValueType.F64: body.F64Const(0); break;
            default: throw new InvalidOperationException();
        }
    }

    private static void EmitDefaultToGlobal(DirectGlobal global, DirectWasmFunctionBody body)
    {
        EmitDefault(global.Type, body);
        body.GlobalSet(global.Index);
    }

    private DirectObjectLayout GetObject(string name)
        => _objects.TryGetValue(name, out var layout) ? layout : throw new InvalidOperationException($"Unknown direct-Wasm object '{name}'.");

    private int ObjectTypeId(string name)
        => _objects.Keys.OrderBy(value => value, StringComparer.Ordinal).ToList().IndexOf(name) + 1;

    private void CollectIntrinsicCalls(Stmt statement)
    {
        switch (statement)
        {
            case Block block:
                foreach (var child in block.Statements) CollectIntrinsicCalls(child);
                break;
            case VarDecl variable when variable.Initializer is not null: CollectIntrinsicCalls(variable.Initializer); break;
            case ExprStmt expression: CollectIntrinsicCalls(expression.Expression); break;
            case IfStmt conditional:
                CollectIntrinsicCalls(conditional.Condition); CollectIntrinsicCalls(conditional.ThenBranch);
                if (conditional.ElseBranch is not null) CollectIntrinsicCalls(conditional.ElseBranch);
                break;
            case SwitchStmt selection:
                CollectIntrinsicCalls(selection.Value);
                foreach (var item in selection.Cases) { CollectIntrinsicCalls(item.Value); CollectIntrinsicCalls(item.Body); }
                if (selection.DefaultBranch is not null) CollectIntrinsicCalls(selection.DefaultBranch);
                break;
            case WhileStmt loop: CollectIntrinsicCalls(loop.Condition); CollectIntrinsicCalls(loop.Body); break;
            case ForStmt loop:
                if (loop.Initializer is not null) CollectIntrinsicCalls(loop.Initializer);
                CollectIntrinsicCalls(loop.Condition);
                if (loop.Increment is not null) CollectIntrinsicCalls(loop.Increment);
                CollectIntrinsicCalls(loop.Body);
                break;
            case ForeachStmt loop: CollectIntrinsicCalls(loop.Iterable); CollectIntrinsicCalls(loop.Body); break;
            case ReturnStmt result when result.Value is not null: CollectIntrinsicCalls(result.Value); break;
            case PrintStmt print: CollectIntrinsicCalls(print.Value); break;
            case PanicStmt panic: _usesPanic = true; CollectIntrinsicCalls(panic.Value); break;
            case YieldStmt yield: CollectIntrinsicCalls(yield.Value); break;
            case FunctionDecl function: CollectIntrinsicCalls(function.Body); break;
            case ObjectDecl type:
                foreach (var field in type.Fields) if (field.Initializer is not null) CollectIntrinsicCalls(field.Initializer);
                foreach (var constructor in type.Constructors) CollectIntrinsicCalls(constructor.Body);
                foreach (var method in type.Methods) CollectIntrinsicCalls(method.Body);
                break;
        }
    }

    private void CollectIntrinsicCalls(Expr expression)
    {
        switch (expression)
        {
            case Literal { Value: string value }:
                RegisterStringData(value);
                break;
            case Call call:
                if (HostAbiCatalog.TryGetIntrinsic(call.Callee.Lexeme, out _)) _usedIntrinsicNames.Add(call.Callee.Lexeme);
                foreach (var argument in call.Arguments) CollectIntrinsicCalls(argument);
                break;
            case MethodCallExpr call:
                CollectIntrinsicCalls(call.Target);
                foreach (var argument in call.Arguments) CollectIntrinsicCalls(argument);
                break;
            case Binary binary: CollectIntrinsicCalls(binary.Left); CollectIntrinsicCalls(binary.Right); break;
            case Unary unary: CollectIntrinsicCalls(unary.Right); break;
            case CastExpr cast: CollectIntrinsicCalls(cast.Value); break;
            case InterpString text:
                foreach (var part in text.Parts)
                {
                    if (part is string segment) RegisterStringData(segment);
                    else if (part is Expr partExpression) CollectIntrinsicCalls(partExpression);
                }
                break;
            case ArrayLiteral array:
                foreach (var element in array.Elements) CollectIntrinsicCalls(element);
                break;
            case NewArrayExpr array: CollectIntrinsicCalls(array.Size); break;
            case NewCollectionExpr:
                _usesRuntimeCollections = true;
                break;
            case ArrayLengthExpr length: CollectIntrinsicCalls(length.Target); break;
            case ArrayIndexExpr index: CollectIntrinsicCalls(index.Array); CollectIntrinsicCalls(index.Index); break;
            case ArraySetExpr set: CollectIntrinsicCalls(set.Target); CollectIntrinsicCalls(set.Value); break;
            case OptionalOrExpr optional: CollectIntrinsicCalls(optional.Optional); CollectIntrinsicCalls(optional.Fallback); break;
            case OptionalHasValueExpr optional: CollectIntrinsicCalls(optional.Target); break;
            case OptionalValueExpr optional: CollectIntrinsicCalls(optional.Target); break;
            case FallibleErrorExpr error:
                foreach (var argument in error.Arguments) CollectIntrinsicCalls(argument);
                break;
            case OnErrorExpr error: CollectIntrinsicCalls(error.Fallible); CollectIntrinsicCalls(error.Handler); break;
            case FieldAccessExpr field: CollectIntrinsicCalls(field.Target); break;
            case FieldSetExpr set: CollectIntrinsicCalls(set.Target); CollectIntrinsicCalls(set.Value); break;
            case NewObjectExpr instance:
                foreach (var argument in instance.Arguments) CollectIntrinsicCalls(argument);
                break;
            case Assign assignment: CollectIntrinsicCalls(assignment.Value); break;
            case CompoundAssignExpr assignment: CollectIntrinsicCalls(assignment.Target); CollectIntrinsicCalls(assignment.Value); break;
        }
    }

    private static bool IsNativeMathIntrinsic(string name)
        => name is "squareRoot" or "minimum" or "maximum" or "absolute" or "lerp";

    private static TypeRef ParseTypeName(string text)
    {
        int genericStart = text.IndexOf('<');
        if (genericStart < 0) return new TypeRef(text, null, 0, 0);
        string name = text[..genericStart];
        string argumentsText = text[(genericStart + 1)..^1];
        var arguments = new List<TypeRef>();
        int depth = 0;
        int start = 0;
        for (int index = 0; index <= argumentsText.Length; index++)
        {
            bool atEnd = index == argumentsText.Length;
            if (!atEnd)
            {
                if (argumentsText[index] == '<') depth++;
                else if (argumentsText[index] == '>') depth--;
            }
            if (!atEnd && (argumentsText[index] != ',' || depth != 0)) continue;
            arguments.Add(ParseTypeName(argumentsText[start..index].Trim()));
            start = index + 1;
        }
        return new TypeRef(name, arguments, 0, 0);
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) & -alignment;
    private static bool IsVoid(TypeRef? type) => type is null || type.NormalizeBuiltInShorthands().Name == "void";
    private static bool IsOptional(TypeRef type) => type.NormalizeBuiltInShorthands().Name == "optional";
    private static bool IsFallible(TypeRef type) => type.NormalizeBuiltInShorthands().Name == "fallible";
    private static TypeRef VoidType(Token token) => new("void", null, token.Line, token.Column);
    private static TypeRef IntegerType() => new("integer", null, 0, 0);
    private static TypeRef RealType() => new("real", null, 0, 0);
    private static TypeRef BooleanType() => new("boolean", null, 0, 0);
    private static TypeRef StringType() => new("string", null, 0, 0);
    private static TypeRef ReferenceType() => new("array", [IntegerType()], 0, 0);

    private static IReadOnlyList<Expr> CombineArguments(IReadOnlyList<Expr> arguments, IReadOnlyList<Expr> defaultArguments)
    {
        if (defaultArguments.Count == 0)
            return arguments;

        var combined = new List<Expr>(arguments.Count + defaultArguments.Count);
        combined.AddRange(arguments);
        combined.AddRange(defaultArguments);
        return combined;
    }

    private static string TypeKey(TypeRef type)
    {
        type = type.NormalizeBuiltInShorthands();
        return type.TypeArguments.Count == 0 ? type.Name : $"{type.Name}<{string.Join(",", type.TypeArguments.Select(TypeKey))}>";
    }
    private static string ConstructorKey(string type, IReadOnlyList<Parameter> parameters)
        => $"{type}({string.Join(",", parameters.Select(parameter => TypeKey(parameter.Type!)))})";
    private static string ConstructorKey(string type, int arity) => $"{type}#arity:{arity}";
    private static string MethodKey(string type, string method, IReadOnlyList<Parameter> parameters)
        => $"{type}.{method}({string.Join(",", parameters.Select(parameter => TypeKey(parameter.Type!)))})";
    private static string InterfaceMethodKey(string method, IReadOnlyList<Parameter> parameters)
        => $"{method}({string.Join(",", parameters.Select(parameter => TypeKey(parameter.Type!)))})";

    private bool IsRecord(TypeRef type)
        => _objects.TryGetValue(type.NormalizeBuiltInShorthands().Name, out var layout) && layout.Declaration.IsRecord;

    private void EnsureDirectWasmCollectionKeySupported(TypeRef type, Expr expression)
    {
        if (RequiresStructuralValueSemantics(type))
            throw Unsupported(expression, "record and collection structural map/set keys in direct-Wasm");
    }

    private bool RequiresStructuralValueSemantics(TypeRef type)
    {
        type = type.NormalizeBuiltInShorthands();
        if (IsRecord(type))
            return true;
        if (type.Name is "array" or "map" or "set" or "queue" or "stack")
            return true;
        return type.Name is "optional" or "fallible" &&
               type.TypeArguments.Any(RequiresStructuralValueSemantics);
    }

    private static CompilerException Unsupported(object node, string feature)
    {
        int line = node switch
        {
            Expr expression => ExpressionLocation(expression).Line,
            _ => 0
        };
        int column = node is Expr expressionNode ? ExpressionLocation(expressionNode).Column : 0;
        return new CompilerException($"Direct-Wasm spike does not support {feature} ({node.GetType().Name}).", line, column);
    }

    private static (int Line, int Column) ExpressionLocation(Expr expression) => expression switch
    {
        Literal literal => (literal.Line, literal.Column),
        Variable variable => (variable.Name.Line, variable.Name.Column),
        Call call => (call.Callee.Line, call.Callee.Column),
        MethodCallExpr call => (call.MethodName.Line, call.MethodName.Column),
        NewObjectExpr instance => (instance.TypeName.Line, instance.TypeName.Column),
        FieldAccessExpr field => (field.Name.Line, field.Name.Column),
        Binary binary => (binary.Operator.Line, binary.Operator.Column),
        Unary unary => (unary.Operator.Line, unary.Operator.Column),
        _ => (0, 0)
    };

    private sealed record DirectField(string Name, TypeRef Type, DirectWasmValueType WasmType, int Offset);
    private sealed record DirectObjectLayout(string Name, ObjectDecl Declaration, IReadOnlyDictionary<string, DirectField> Fields, int Size);
    private sealed record DirectGlobal(string Name, TypeRef Type, DirectWasmValueType WasmType, int Index, VarDecl Declaration);
    private sealed record DirectHostFunction(HostAbiIntrinsic Intrinsic, int Index, TypeRef ReturnType);
    private sealed record DirectStringData(int Pointer, int Length);
    private sealed record DirectInterfaceTarget(DirectObjectLayout Owner, DirectFunction Function);
    private sealed record DirectInterfaceFieldTarget(DirectObjectLayout Owner, DirectField Field);
    private sealed record DirectLoop(int BreakLocal, int ContinueLocal);

    private sealed class DirectFunction
    {
        public string Key { get; }
        public string DisplayName { get; }
        public IReadOnlyList<Parameter> Parameters { get; }
        public TypeRef? ReturnType { get; }
        public Block Body { get; }
        public DirectObjectLayout? Owner { get; }
        public bool IsConstructor { get; }
        public int Index { get; set; }
        public DirectFunction(string key, string displayName, IReadOnlyList<Parameter> parameters, TypeRef? returnType, Block body, DirectObjectLayout? owner, bool isConstructor)
        {
            Key = key; DisplayName = displayName; Parameters = parameters; ReturnType = returnType; Body = body; Owner = owner; IsConstructor = isConstructor;
        }
    }

    private sealed record DirectLocal(int Index, TypeRef Type, DirectWasmValueType WasmType);

    private sealed class FunctionContext
    {
        private readonly DirectWasmCompiler _compiler;
        private readonly Stack<Dictionary<string, DirectLocal?>> _scopes = new();
        private readonly Stack<DirectLoop> _loops = new();
        private readonly Dictionary<string, DirectLocal> _locals = new(StringComparer.Ordinal);
        public DirectFunction Function { get; }
        public DirectWasmFunctionBody Body { get; }
        public bool IsRun => Function.Key == "$run";
        public int ParameterCount { get; }
        public int ErrorCodeLocal { get; private set; } = -1;
        public int ErrorMessageLocal { get; private set; } = -1;
        public int YieldResultLocal { get; private set; } = -1;
        public TypeRef? YieldResultType { get; private set; }

        public FunctionContext(DirectWasmCompiler compiler, DirectFunction function, DirectWasmFunctionBody body)
        {
            _compiler = compiler; Function = function; Body = body;
            int index = 0;
            if (function.Owner is not null)
            {
                _locals["this"] = new DirectLocal(index++, new TypeRef(function.Owner.Name, null, 0, 0), DirectWasmValueType.I32);
            }
            foreach (var parameter in function.Parameters)
            {
                var type = parameter.Type!;
                _locals[parameter.Name.Lexeme] = new DirectLocal(index++, type, MapType(type));
            }
            ParameterCount = index;
            PushScope();
        }

        public void PushScope() => _scopes.Push(new Dictionary<string, DirectLocal?>(StringComparer.Ordinal));
        public void PopScope()
        {
            foreach (var pair in _scopes.Pop())
            {
                if (pair.Value is null) _locals.Remove(pair.Key);
                else _locals[pair.Key] = pair.Value;
            }
        }
        public int Declare(string name, TypeRef type)
        {
            var previous = _locals.TryGetValue(name, out var existing) ? existing : null;
            _scopes.Peek()[name] = previous;
            int index = Body.AddLocal(MapType(type), ParameterCount);
            _locals[name] = new DirectLocal(index, type, MapType(type));
            return index;
        }
        public int AddTemporary(DirectWasmValueType type) => Body.AddLocal(type, ParameterCount);
        public bool TryLookup(string name, out DirectLocal local) => _locals.TryGetValue(name, out local!);
        public void PushLoop(int breakLocal, int continueLocal) => _loops.Push(new DirectLoop(breakLocal, continueLocal));
        public void PopLoop() => _loops.Pop();
        public bool TryGetLoop(out DirectLoop loop)
        {
            if (_loops.Count > 0) { loop = _loops.Peek(); return true; }
            loop = null!;
            return false;
        }

        public (int Code, int Message, int Result, TypeRef? Type) SetErrorHandler(int code, int message, int result, TypeRef type)
        {
            var previous = (ErrorCodeLocal, ErrorMessageLocal, YieldResultLocal, YieldResultType);
            ErrorCodeLocal = code;
            ErrorMessageLocal = message;
            YieldResultLocal = result;
            YieldResultType = type;
            return previous;
        }

        public void RestoreErrorHandler((int Code, int Message, int Result, TypeRef? Type) previous)
        {
            ErrorCodeLocal = previous.Code;
            ErrorMessageLocal = previous.Message;
            YieldResultLocal = previous.Result;
            YieldResultType = previous.Type;
        }
    }
}
