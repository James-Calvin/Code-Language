using System;
using System.Collections.Generic;

namespace ConsoleApp1.Compiler;

sealed class GeneratedCallableMetadata
{
    public int TargetIp { get; }
    public int FrameSize { get; }

    public GeneratedCallableMetadata(int targetIp, int frameSize)
    {
        TargetIp = targetIp;
        FrameSize = frameSize;
    }
}

sealed class WebSceneMetadata
{
    public string SceneTypeName { get; }
    public GeneratedCallableMetadata Constructor { get; }
    public GeneratedCallableMetadata Start { get; }
    public GeneratedCallableMetadata Update { get; }
    public GeneratedCallableMetadata Draw { get; }
    public GeneratedCallableMetadata? DrawHud { get; }

    public WebSceneMetadata(
        string sceneTypeName,
        GeneratedCallableMetadata constructor,
        GeneratedCallableMetadata start,
        GeneratedCallableMetadata update,
        GeneratedCallableMetadata draw,
        GeneratedCallableMetadata? drawHud)
    {
        SceneTypeName = sceneTypeName;
        Constructor = constructor;
        Start = start;
        Update = update;
        Draw = draw;
        DrawHud = drawHud;
    }
}

sealed class CodeGenerationResult
{
    public byte[] Bytecode { get; }
    public WebSceneMetadata? WebScene { get; }

    public CodeGenerationResult(byte[] bytecode, WebSceneMetadata? webScene)
    {
        Bytecode = bytecode;
        WebScene = webScene;
    }
}

sealed class CodeGenerator
{
    private readonly BytecodeBuilder _builder = BytecodeBuilder.New();
    private Dictionary<string, int> _locals = new(StringComparer.Ordinal);
    private Dictionary<string, TypeRef> _localDeclaredTypes = new(StringComparer.Ordinal);
    private int _nextLocalIndex;
    private int _functionLocalHighWater;
    private readonly Stack<Dictionary<string, int>> _scopeStack = new();
    private readonly Stack<Dictionary<string, TypeRef>> _scopeDeclaredTypesStack = new();
    private readonly Stack<int> _nextLocalStack = new();
    private readonly List<int> _freeTemps = new();
    private readonly Dictionary<string, (string Label, int ParamCount, int LocalCount)> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Label, int ParamCount, int LocalCount)> _constructors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Label, int ParamCount, int LocalCount)> _methods = new(StringComparer.Ordinal);
    private readonly HashSet<string> _objectNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _interfaceNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, TypeRef>> _objectFieldTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<InterfaceDispatchTarget>> _interfaceDispatch = new(StringComparer.Ordinal);
    private int _labelCounter;

    public byte[] Generate(IList<Stmt> statements) => GenerateWithMetadata(statements).Bytecode;

    public CodeGenerationResult GenerateWithMetadata(IList<Stmt> statements)
    {
        var functionDecls = new List<FunctionDecl>();
        var objectDecls = new List<ObjectDecl>();
        var interfaceDecls = new List<InterfaceDecl>();
        var implementDecls = new List<ImplementDecl>();
        var topLevel = new List<Stmt>();
        foreach (var stmt in statements)
        {
            if (stmt is FunctionDecl fd) functionDecls.Add(fd);
            else if (stmt is ObjectDecl od) objectDecls.Add(od);
            else if (stmt is InterfaceDecl iface) interfaceDecls.Add(iface);
            else if (stmt is ImplementDecl impl) implementDecls.Add(impl);
            else topLevel.Add(stmt);
        }

        string mainLabel = NewLabel("main");
        _builder.Jump(mainLabel); // skip over function bodies

        // Pre-register function labels so calls can resolve forward references
        foreach (var fn in functionDecls)
        {
            string label = $"fn_{fn.Name.Lexeme}";
            _functions[fn.Name.Lexeme] = (label, fn.Parameters.Count, 0);
        }

        // Pre-register object names and constructor labels for forward constructor calls.
        foreach (var obj in objectDecls)
        {
            _objectNames.Add(obj.Name.Lexeme);
            var fieldTypes = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
            foreach (var field in obj.Fields)
            {
                fieldTypes[field.Name.Lexeme] = field.Type;
            }
            _objectFieldTypes[obj.Name.Lexeme] = fieldTypes;
            foreach (var ctor in obj.Constructors)
            {
                string key = ConstructorKey(obj.Name.Lexeme, ctor.Parameters);
                string label = $"ctor_{obj.Name.Lexeme}_{_constructors.Count}";
                _constructors[key] = (label, ctor.Parameters.Count + 1, 0); // +1 for implicit this
            }
            foreach (var method in obj.Methods)
            {
                string key = MethodKey(obj.Name.Lexeme, method.Name.Lexeme, method.Parameters);
                string label = $"m_{obj.Name.Lexeme}_{method.Name.Lexeme}_{_methods.Count}";
                _methods[key] = (label, method.Parameters.Count + 1, 0); // +1 for implicit this
            }
        }
        foreach (var iface in interfaceDecls)
        {
            _interfaceNames.Add(iface.Name.Lexeme);
        }
        BuildInterfaceDispatch(implementDecls);

        // Emit object methods/constructors before normal functions.
        foreach (var obj in objectDecls)
        {
            EmitObjectMethods(obj);
            EmitObjectConstructors(obj);
        }

        // Emit functions to compute locals and bodies
        foreach (var fn in functionDecls)
        {
            EmitFunction(fn);
        }

        // Top-level script body
        _builder.Label(mainLabel);
        PushScope();
        foreach (var stmt in topLevel)
        {
            Emit(stmt);
        }
        _builder.Halt();
        PopScope();

        byte[] bytecode = _builder.ToArray();
        return new CodeGenerationResult(bytecode, TryBuildWebSceneMetadata(objectDecls));
    }

    private void SetLoc(Token token) => _builder.SetDebugLocation(token.Line, token.Column);
    private void SetLoc(int line, int column) => _builder.SetDebugLocation(line, column);

    private void EmitFunction(FunctionDecl fn)
    {
        string label = _functions[fn.Name.Lexeme].Label;
        _builder.Label(label);

        // reset per-function allocators
        _nextLocalIndex = 0;
        _functionLocalHighWater = 0;
        _freeTemps.Clear();
        PushScope();
        // Parameters occupy leading slots
        for (int i = 0; i < fn.Parameters.Count; i++)
        {
            var param = fn.Parameters[i];
            _locals[param.Name.Lexeme] = _nextLocalIndex++;
            if (param.Type is not null)
                _localDeclaredTypes[param.Name.Lexeme] = param.Type;
            _functionLocalHighWater = Math.Max(_functionLocalHighWater, _nextLocalIndex);
        }

        Emit(fn.Body);

        // If function body didn't end with a return, emit implicit return 0
        _builder.PushInt(0);
        _builder.Ret();

        _functions[fn.Name.Lexeme] = (label, fn.Parameters.Count, _functionLocalHighWater);
        PopScope();
    }

    private void EmitObjectConstructors(ObjectDecl obj)
    {
        foreach (var ctor in obj.Constructors)
        {
            EmitConstructor(obj, ctor);
        }
    }

    private void EmitObjectMethods(ObjectDecl obj)
    {
        foreach (var method in obj.Methods)
        {
            EmitMethod(obj, method);
        }
    }

    private void EmitConstructor(ObjectDecl obj, ConstructorDecl ctor)
    {
        string key = ConstructorKey(obj.Name.Lexeme, ctor.Parameters);
        var info = _constructors[key];
        _builder.Label(info.Label);

        _nextLocalIndex = 0;
        _functionLocalHighWater = 0;
        _freeTemps.Clear();
        PushScope();

        // implicit this
        _locals["this"] = _nextLocalIndex++;
        _localDeclaredTypes["this"] = new TypeRef(obj.Name.Lexeme, null, obj.Name.Line, obj.Name.Column);
        _functionLocalHighWater = Math.Max(_functionLocalHighWater, _nextLocalIndex);

        for (int i = 0; i < ctor.Parameters.Count; i++)
        {
            var param = ctor.Parameters[i];
            _locals[param.Name.Lexeme] = _nextLocalIndex++;
            if (param.Type is not null)
                _localDeclaredTypes[param.Name.Lexeme] = param.Type;
            _functionLocalHighWater = Math.Max(_functionLocalHighWater, _nextLocalIndex);
        }

        Emit(ctor.Body);

        _builder.PushInt(0);
        _builder.Ret();

        _constructors[key] = (info.Label, info.ParamCount, _functionLocalHighWater);
        PopScope();
    }

    private void EmitMethod(ObjectDecl obj, MethodDecl method)
    {
        string key = MethodKey(obj.Name.Lexeme, method.Name.Lexeme, method.Parameters);
        var info = _methods[key];
        _builder.Label(info.Label);

        _nextLocalIndex = 0;
        _functionLocalHighWater = 0;
        _freeTemps.Clear();
        PushScope();

        _locals["this"] = _nextLocalIndex++;
        _localDeclaredTypes["this"] = new TypeRef(obj.Name.Lexeme, null, obj.Name.Line, obj.Name.Column);
        _functionLocalHighWater = Math.Max(_functionLocalHighWater, _nextLocalIndex);

        for (int i = 0; i < method.Parameters.Count; i++)
        {
            var param = method.Parameters[i];
            _locals[param.Name.Lexeme] = _nextLocalIndex++;
            if (param.Type is not null)
                _localDeclaredTypes[param.Name.Lexeme] = param.Type;
            _functionLocalHighWater = Math.Max(_functionLocalHighWater, _nextLocalIndex);
        }

        Emit(method.Body);

        _builder.PushInt(0);
        _builder.Ret();

        _methods[key] = (info.Label, info.ParamCount, _functionLocalHighWater);
        PopScope();
    }

    private void Emit(Stmt stmt)
    {
        switch (stmt)
        {
            case VarDecl v:
                SetLoc(v.Name);
                int slot = GetOrAllocate(v.Name.Lexeme);
                _localDeclaredTypes[v.Name.Lexeme] = v.Type;
                if (v.Initializer is not null)
                {
                    Emit(v.Initializer);
                }
                else
                {
                    if (v.Type.IsOptional)
                        _builder.PushNone();
                    else
                        _builder.PushInt(0); // default
                }
                SetLoc(v.Name);
                _builder.Store(slot);
                break;

            case ExprStmt e:
                Emit(e.Expression);
                if (ExpressionLeavesValue(e.Expression))
                {
                    _builder.Pop();
                }
                break;

            case Block b:
                foreach (var inner in b.Statements) Emit(inner);
                break;

            case IfStmt i:
                Emit(i.Condition);
                string elseLabel = NewLabel("else");
                string endLabel = NewLabel("endif");
                _builder.JumpIfZero(elseLabel);
                Emit(i.ThenBranch);
                _builder.Jump(endLabel);
                _builder.Label(elseLabel);
                if (i.ElseBranch is not null) Emit(i.ElseBranch);
                _builder.Label(endLabel);
                break;

            case WhileStmt w:
                string loopStart = NewLabel("loop_start");
                string loopEnd = NewLabel("loop_end");
                _builder.Label(loopStart);
                Emit(w.Condition);
                _builder.JumpIfZero(loopEnd);
                Emit(w.Body);
                _builder.Jump(loopStart);
                _builder.Label(loopEnd);
                break;

            case ReturnStmt r:
                if (r.Value is not null) Emit(r.Value);
                else _builder.PushInt(0);
                _builder.Ret();
                break;

            case PrintStmt p:
                Emit(p.Value);
                _builder.HostCall(HostAbiCatalog.StandardInputOutputPrint.Symbol, HostAbiCatalog.StandardInputOutputPrint.Arity);
                _builder.Pop(); // host calls always return a value; discard print's void-like return
                break;

            case PanicStmt p:
                Emit(p.Value);
                _builder.ThrowError();
                break;

            case ForStmt f:
                if (f.Initializer is not null) Emit(f.Initializer);
                string forStart = NewLabel("for_start");
                string forEnd = NewLabel("for_end");
                _builder.Label(forStart);
                Emit(f.Condition);
                _builder.JumpIfZero(forEnd);
                Emit(f.Body);
                if (f.Increment is not null)
                {
                    Emit(f.Increment);
                    if (ExpressionLeavesValue(f.Increment))
                        _builder.Pop();
                }
                _builder.Jump(forStart);
                _builder.Label(forEnd);
                break;

            case ForeachStmt fe:
                {
                    SetLoc(fe.Iterator);
                    if (fe.IsArray || fe.Iterable is ArrayLiteral)
                    {
                        EmitArrayForeach(fe);
                    }
                    else
                    {
                        EmitNumericForeach(fe);
                    }
                }
                break;

            case FunctionDecl:
                // already handled in outer pass
                break;
            case ObjectDecl:
                // object declarations are type metadata only for now
                break;
            case EnumDecl:
            case InterfaceDecl:
            case ImplementDecl:
            case ImportDecl:
            case ExportDecl:
            case PackageDecl:
                // interface metadata does not emit bytecode in current MVP
                break;

            default:
                throw new NotSupportedException($"Unhandled statement type {stmt.GetType().Name}");
        }
    }

    private void Emit(Expr expr)
    {
        switch (expr)
        {
            case Literal lit:
                SetLoc(lit.Line, lit.Column);
                if (lit.Value is string s)
                {
                    _builder.PushString(s);
                }
                else if (lit.Value is bool b)
                {
                    _builder.PushInt(b ? 1 : 0);
                }
                else if (lit.Value == OptionalNone.Value)
                {
                    _builder.PushNone();
                }
                else
                {
                    _builder.PushInt(Convert.ToInt32(lit.Value ?? 0));
                }
                break;

            case InterpString istr:
                SetLoc(istr.Line, istr.Column);
                EmitInterpolatedString(istr);
                break;
            case ArrayLiteral arr:
                foreach (var el in arr.Elements)
                {
                    Emit(el);
                }
                _builder.NewArray(arr.Elements.Count);
                break;
            case NewArrayExpr na:
                Emit(na.Size);
                _builder.NewArrayN();
                break;
            case NewCollectionExpr nc:
                EmitNewCollection(nc);
                break;
            case NewObjectExpr no:
            {
                if (!_objectNames.Contains(no.TypeName.Lexeme))
                    throw new InvalidOperationException($"Unknown object type '{no.TypeName.Lexeme}' at line {no.TypeName.Line}, col {no.TypeName.Column}");
                _builder.NewObject(no.TypeName.Lexeme);
                string ctorKey = no.ResolvedConstructorKey ?? ConstructorKey(no.TypeName.Lexeme, no.Arguments.Count);
                if (_constructors.TryGetValue(ctorKey, out var ctor))
                {
                    _builder.Dup(); // keep object on stack after constructor call
                    foreach (var arg in no.Arguments)
                    {
                        Emit(arg);
                    }
                    int frameSize = Math.Max(ctor.LocalCount, ctor.ParamCount);
                    _builder.Call(ctor.Label, ctor.ParamCount, frameSize);
                    _builder.Pop(); // discard constructor return value
                }
                else if (no.Arguments.Count > 0 || HasConstructors(no.TypeName.Lexeme))
                {
                    throw new InvalidOperationException($"No constructor found for '{no.TypeName.Lexeme}' with {no.Arguments.Count} arguments at line {no.TypeName.Line}, col {no.TypeName.Column}");
                }
                break;
            }
            case ArrayLengthExpr alen:
                Emit(alen.Target);
                _builder.ArrayLength();
                break;
            case ArrayIndexExpr aidx:
                Emit(aidx.Array);
                Emit(aidx.Index);
                EmitIndexGet(aidx.Array);
                break;
            case OptionalHasValueExpr ohv:
                Emit(ohv.Target);
                _builder.OptionalHas();
                break;
            case OptionalValueExpr oval:
                Emit(oval.Target);
                _builder.OptionalValue();
                break;
            case OptionalOrExpr oor:
                Emit(oor.Optional);
                Emit(oor.Fallback);
                _builder.OptionalOr();
                break;
            case FieldAccessExpr fa:
                if (fa.ResolvesToEnumMember)
                {
                    SetLoc(fa.Name);
                    _builder.PushInt(fa.ResolvedEnumValue ?? 0);
                    break;
                }
                Emit(fa.Target);
                _builder.GetField(fa.Name.Lexeme);
                break;
            case Variable v:
                SetLoc(v.Name);
                if (v.ResolvesToImplicitField)
                {
                    EmitCurrentObject();
                    _builder.GetField(v.Name.Lexeme);
                }
                else
                {
                    _builder.Load(GetSlot(v.Name));
                }
                break;

            case Assign a:
                SetLoc(a.Name);
                if (a.ResolvesToImplicitField)
                {
                    EmitCurrentObject();
                    Emit(a.Value);
                    _builder.SetField(a.Name.Lexeme);
                    _builder.Pop();
                }
                else
                {
                    Emit(a.Value);
                    _builder.Store(GetSlot(a.Name));
                }
                break;
            case CompoundAssignExpr c:
                EmitCompoundAssignment(c);
                break;
            case ArraySetExpr aset:
                Emit(aset.Target.Array);
                Emit(aset.Target.Index);
                Emit(aset.Value);
                EmitIndexSet(aset.Target.Array);
                break;
            case FieldSetExpr fset:
                Emit(fset.Target.Target);
                Emit(fset.Value);
                _builder.SetField(fset.Target.Name.Lexeme);
                break;
            case MethodCallExpr mc:
            {
                SetLoc(mc.MethodName);
                var targetType = TryResolveTypeRef(mc.Target);
                if (targetType is null)
                    throw new InvalidOperationException($"Unable to resolve method target type for '{mc.MethodName.Lexeme}'");

                if (mc.ResolvesToBuiltInCollectionMethod || targetType.IsBuiltInCollection)
                {
                    EmitBuiltInCollectionMethodCall(mc, targetType);
                }
                else if (mc.ResolvedInterfaceName is not null || _interfaceNames.Contains(targetType.Name))
                {
                    EmitInterfaceMethodCall(mc, targetType);
                }
                else
                {
                    EmitObjectMethodCall(mc, targetType);
                }
                break;
            }

            case Call call:
                SetLoc(call.Callee);
                EmitCall(call);
                break;

            case Unary u:
                SetLoc(u.Operator);
                Emit(u.Right);
                if (u.Operator.Type == TokenType.Minus)
                {
                    _builder.PushInt(0);
                    _builder.Swap();
                    _builder.Sub();
                }
                else if (u.Operator.Type == TokenType.Plus)
                {
                    // no-op
                }
                else if (u.Operator.Type == TokenType.Not)
                {
                    string trueLabel = NewLabel("not_true");
                    string endLabel = NewLabel("not_end");
                    _builder.JumpIfZero(trueLabel);
                    _builder.PushInt(0);
                    _builder.Jump(endLabel);
                    _builder.Label(trueLabel);
                    _builder.PushInt(1);
                    _builder.Label(endLabel);
                }
                break;

            case Binary b:
                if (TryFoldBinary(b, out var folded))
                {
                    SetLoc(b.Operator);
                    if (folded is string sFold)
                    {
                        _builder.PushString(sFold);
                    }
                    else
                    {
                        _builder.PushInt(Convert.ToInt32(folded));
                    }
                    break;
                }
                if (b.Operator.Type == TokenType.And)
                {
                    SetLoc(b.Operator);
                    EmitLogicalAnd(b);
                    break;
                }
                if (b.Operator.Type == TokenType.Or)
                {
                    SetLoc(b.Operator);
                    EmitLogicalOr(b);
                    break;
                }
                SetLoc(b.Operator);
                Emit(b.Left);
                Emit(b.Right);
                switch (b.Operator.Type)
                {
                    case TokenType.Plus: _builder.Add(); break;
                    case TokenType.Minus: _builder.Sub(); break;
                    case TokenType.Star: _builder.Mul(); break;
                    case TokenType.Slash: _builder.Div(); break;
                    case TokenType.Percent: _builder.Mod(); break;
                    case TokenType.EqualEqual: _builder.Eq(); break;
                    case TokenType.BangEqual:
                        _builder.Eq();
                        _builder.PushInt(0);
                        _builder.Swap();
                        _builder.Eq();
                        break;
                    case TokenType.Less: _builder.Lt(); break;
                    case TokenType.Greater: _builder.Gt(); break;
                    case TokenType.LessEqual:
                        // a <= b  => !(a > b)
                        _builder.Gt();
                        _builder.PushInt(0);
                        _builder.Swap();
                        _builder.Eq();
                        break;
                    case TokenType.GreaterEqual:
                        // a >= b => !(a < b)
                        _builder.Lt();
                        _builder.PushInt(0);
                        _builder.Swap();
                        _builder.Eq();
                        break;
                    default:
                        throw new NotSupportedException($"Operator {b.Operator.Type} not supported yet.");
                }
                break;

            default:
                throw new NotSupportedException($"Unhandled expression type {expr.GetType().Name}");
        }
    }

    private int GetOrAllocate(string name)
    {
        if (_locals.TryGetValue(name, out var slot)) return slot;
        slot = _nextLocalIndex++;
        _locals[name] = slot;
        _functionLocalHighWater = Math.Max(_functionLocalHighWater, _nextLocalIndex);
        return slot;
    }

    private int AllocateTemp()
    {
        if (_freeTemps.Count > 0)
        {
            int idx = _freeTemps[^1];
            _freeTemps.RemoveAt(_freeTemps.Count - 1);
            return idx;
        }
        return GetOrAllocate($"__temp{_labelCounter++}");
    }

    private void ReleaseTemp(int slot)
    {
        _freeTemps.Add(slot);
    }

    private int GetSlot(Token name)
    {
        if (!_locals.TryGetValue(name.Lexeme, out var slot))
            throw new InvalidOperationException($"Undefined variable '{name.Lexeme}' at line {name.Line}, col {name.Column}");
        return slot;
    }

    private static string TypeRefKey(TypeRef t) =>
        t.TypeArguments.Count == 0
            ? t.Name
            : $"{t.Name}<{string.Join(",", GetTypeArgKeys(t.TypeArguments))}>";

    private static IEnumerable<string> GetTypeArgKeys(IReadOnlyList<TypeRef> args)
    {
        for (int i = 0; i < args.Count; i++)
            yield return TypeRefKey(args[i]);
    }

    private static string ConstructorKey(string typeName, IReadOnlyList<Parameter> parameters)
    {
        var parts = new List<string>(parameters.Count);
        foreach (var p in parameters)
        {
            if (p.Type is null)
                throw new InvalidOperationException($"Constructor parameter '{p.Name.Lexeme}' is missing a type.");
            parts.Add(TypeRefKey(p.Type));
        }
        return $"{typeName}({string.Join(",", parts)})";
    }

    private static string ConstructorKey(string typeName, int arity) => $"{typeName}#arity:{arity}";

    private static string MethodKey(string typeName, string methodName, IReadOnlyList<Parameter> parameters)
    {
        var parts = new List<string>(parameters.Count);
        foreach (var p in parameters)
        {
            if (p.Type is null)
                throw new InvalidOperationException($"Method parameter '{p.Name.Lexeme}' is missing a type.");
            parts.Add(TypeRefKey(p.Type));
        }
        return $"{typeName}.{methodName}({string.Join(",", parts)})";
    }

    private static string MethodKey(string typeName, string methodName, int arity) => $"{typeName}.{methodName}#arity:{arity}";
    private static string InterfaceMethodKey(string methodName, IReadOnlyList<Parameter> parameters)
    {
        var parts = new List<string>(parameters.Count);
        foreach (var p in parameters)
        {
            if (p.Type is null)
                throw new InvalidOperationException($"Interface mapping parameter '{p.Name.Lexeme}' is missing a type.");
            parts.Add(TypeRefKey(p.Type));
        }
        return $"{methodName}({string.Join(",", parts)})";
    }

    private static string InterfaceDispatchKey(string interfaceName, string interfaceMethodKey) =>
        $"{interfaceName}.{interfaceMethodKey}";

    private void BuildInterfaceDispatch(IReadOnlyList<ImplementDecl> implementDecls)
    {
        foreach (var impl in implementDecls)
        {
            foreach (var map in impl.Methods)
            {
                string ifaceMethodKey = InterfaceMethodKey(map.InterfaceMethodName.Lexeme, map.Parameters);
                string dispatchKey = InterfaceDispatchKey(impl.InterfaceName.Lexeme, ifaceMethodKey);
                string objectMethodKey = MethodKey(impl.ObjectName.Lexeme, map.ViaMethodName.Lexeme, map.Parameters);
                if (!_interfaceDispatch.TryGetValue(dispatchKey, out var targets))
                {
                    targets = new List<InterfaceDispatchTarget>();
                    _interfaceDispatch[dispatchKey] = targets;
                }
                targets.Add(new InterfaceDispatchTarget(impl.ObjectName.Lexeme, objectMethodKey));
            }
        }
    }

    private TypeRef? TryResolveTypeRef(Expr expr)
    {
        switch (expr)
        {
            case ArrayLiteral al:
                return al.ResolvedTypeRef;
            case NewArrayExpr na:
                return new TypeRef("array", [na.ElementType], na.Line, na.Column);
            case NewCollectionExpr nc:
                return nc.CollectionType;
            case Variable v:
                if (v.ResolvedImplicitFieldTypeRef is not null)
                    return v.ResolvedImplicitFieldTypeRef;
                return _localDeclaredTypes.TryGetValue(v.Name.Lexeme, out var t) ? t : null;
            case NewObjectExpr no:
                return new TypeRef(no.TypeName.Lexeme, null, no.TypeName.Line, no.TypeName.Column);
            case Call c:
                return c.ResolvedImplicitMethodReturnTypeRef;
            case ArrayIndexExpr ai:
                return ai.ResolvedElementTypeRef;
            case FieldAccessExpr fa:
                if (fa.ResolvedEnumTypeRef is not null)
                    return fa.ResolvedEnumTypeRef;
            {
                var ownerType = TryResolveTypeRef(fa.Target);
                if (ownerType is null) return null;
                if (_objectFieldTypes.TryGetValue(ownerType.Name, out var fields) &&
                    fields.TryGetValue(fa.Name.Lexeme, out var fieldType))
                {
                    return fieldType;
                }
                return null;
            }
            case MethodCallExpr mc:
                return mc.ResolvedReturnTypeRef;
            case ArraySetExpr aset:
                return TryResolveTypeRef(aset.Target);
            default:
                return null;
        }
    }

    private static bool ExpressionLeavesValue(Expr expr)
        => expr is not Assign;

    private void EmitCompoundAssignment(CompoundAssignExpr expr)
    {
        switch (expr.Target)
        {
            case Variable variable:
            {
                SetLoc(variable.Name);
                if (variable.ResolvesToImplicitField)
                {
                    int objectSlot = AllocateTemp();
                    int valueSlot = AllocateTemp();
                    EmitCurrentObject();
                    _builder.Store(objectSlot);
                    _builder.Load(objectSlot);
                    _builder.GetField(variable.Name.Lexeme);
                    Emit(expr.Value);
                    EmitBinaryOperator(expr.Operator);
                    _builder.Store(valueSlot);
                    _builder.Load(objectSlot);
                    _builder.Load(valueSlot);
                    _builder.SetField(variable.Name.Lexeme);
                    ReleaseTemp(valueSlot);
                    ReleaseTemp(objectSlot);
                }
                else
                {
                    _builder.Load(GetSlot(variable.Name));
                    Emit(expr.Value);
                    EmitBinaryOperator(expr.Operator);
                    _builder.Dup();
                    _builder.Store(GetSlot(variable.Name));
                }
                break;
            }
            case FieldAccessExpr fieldAccess:
            {
                int objectSlot = AllocateTemp();
                int valueSlot = AllocateTemp();
                Emit(fieldAccess.Target);
                _builder.Store(objectSlot);
                _builder.Load(objectSlot);
                _builder.GetField(fieldAccess.Name.Lexeme);
                Emit(expr.Value);
                EmitBinaryOperator(expr.Operator);
                _builder.Store(valueSlot);
                _builder.Load(objectSlot);
                _builder.Load(valueSlot);
                _builder.SetField(fieldAccess.Name.Lexeme);
                ReleaseTemp(valueSlot);
                ReleaseTemp(objectSlot);
                break;
            }
            case ArrayIndexExpr arrayIndex:
            {
                int arraySlot = AllocateTemp();
                int indexSlot = AllocateTemp();
                int valueSlot = AllocateTemp();
                Emit(arrayIndex.Array);
                _builder.Store(arraySlot);
                Emit(arrayIndex.Index);
                _builder.Store(indexSlot);
                _builder.Load(arraySlot);
                _builder.Load(indexSlot);
                EmitIndexGet(arrayIndex.Array);
                Emit(expr.Value);
                EmitBinaryOperator(expr.Operator);
                _builder.Store(valueSlot);
                _builder.Load(arraySlot);
                _builder.Load(indexSlot);
                _builder.Load(valueSlot);
                EmitIndexSet(arrayIndex.Array);
                ReleaseTemp(valueSlot);
                ReleaseTemp(indexSlot);
                ReleaseTemp(arraySlot);
                break;
            }
            default:
                throw new InvalidOperationException("Invalid assignment target for compound assignment");
        }
    }

    private void EmitBinaryOperator(Token op)
    {
        switch (op.Type)
        {
            case TokenType.Plus:
                _builder.Add();
                break;
            case TokenType.Minus:
                _builder.Sub();
                break;
            case TokenType.Star:
                _builder.Mul();
                break;
            case TokenType.Slash:
                _builder.Div();
                break;
            case TokenType.Percent:
                _builder.Mod();
                break;
            default:
                throw new InvalidOperationException($"Unsupported compound assignment operator '{op.Lexeme}'");
        }
    }

    private void EmitBuiltInCollectionMethodCall(MethodCallExpr mc, TypeRef targetType)
    {
        if (mc.ResolvedBuiltInCollectionMethodName is null)
            throw new InvalidOperationException($"Missing resolved built-in collection method for '{mc.MethodName.Lexeme}'");

        Emit(mc.Target);
        foreach (var arg in mc.Arguments)
        {
            Emit(arg);
        }

        switch (targetType.Name, mc.ResolvedBuiltInCollectionMethodName)
        {
            case ("array", "append"):
                _builder.ArrayAppend();
                break;
            case ("array", "remove_at"):
                _builder.ArrayRemoveAt();
                break;
            case ("map", "contains"):
                _builder.MapContains();
                break;
            case ("map", "remove"):
                _builder.MapRemove();
                break;
            case ("set", "add"):
                _builder.SetAdd();
                break;
            case ("set", "contains"):
                _builder.SetContains();
                break;
            case ("set", "remove"):
                _builder.SetRemove();
                break;
            case ("queue", "enqueue"):
                _builder.QueueEnqueue();
                break;
            case ("queue", "dequeue"):
                _builder.QueueDequeue();
                break;
            case ("queue", "peek"):
                _builder.QueuePeek();
                break;
            case ("stack", "push"):
                _builder.StackPush();
                break;
            case ("stack", "pop"):
                _builder.StackPop();
                break;
            case ("stack", "peek"):
                _builder.StackPeek();
                break;
            default:
                throw new InvalidOperationException($"Unsupported built-in collection method '{targetType.Name}.{mc.ResolvedBuiltInCollectionMethodName}'");
        }
    }

    private void EmitNewCollection(NewCollectionExpr collection)
    {
        switch (collection.CollectionType.Name)
        {
            case "map":
                _builder.NewMap();
                break;
            case "set":
                _builder.NewSet();
                break;
            case "queue":
                _builder.NewQueue();
                break;
            case "stack":
                _builder.NewStack();
                break;
            default:
                throw new InvalidOperationException($"Unsupported built-in collection type '{collection.CollectionType.Name}'");
        }
    }

    private void EmitIndexGet(Expr collectionExpr)
    {
        var collectionType = TryResolveTypeRef(collectionExpr)
            ?? throw new InvalidOperationException("Unable to resolve indexed collection type.");

        switch (collectionType.Name)
        {
            case "array":
                _builder.ArrayGet();
                break;
            case "map":
                _builder.MapGet();
                break;
            default:
                throw new InvalidOperationException($"Unsupported indexed collection type '{collectionType.Name}'");
        }
    }

    private void EmitIndexSet(Expr collectionExpr)
    {
        var collectionType = TryResolveTypeRef(collectionExpr)
            ?? throw new InvalidOperationException("Unable to resolve indexed collection type.");

        switch (collectionType.Name)
        {
            case "array":
                _builder.ArraySet();
                break;
            case "map":
                _builder.MapSet();
                break;
            default:
                throw new InvalidOperationException($"Unsupported indexed collection type '{collectionType.Name}'");
        }
    }

    private void EmitObjectMethodCall(MethodCallExpr mc, TypeRef targetType)
    {
        Emit(mc.Target);
        foreach (var arg in mc.Arguments)
        {
            Emit(arg);
        }

        string key = mc.ResolvedMethodKey ?? MethodKey(targetType.Name, mc.MethodName.Lexeme, mc.Arguments.Count);
        if (!_methods.TryGetValue(key, out var info))
            throw new InvalidOperationException($"Undefined method '{targetType.Name}.{mc.MethodName.Lexeme}' with {mc.Arguments.Count} args");

        int frameSize = Math.Max(info.LocalCount, info.ParamCount);
        _builder.Call(info.Label, info.ParamCount, frameSize);
    }

    private void EmitImplicitMethodCall(Call call)
    {
        if (call.ResolvedImplicitMethodOwnerTypeName is null || call.ResolvedImplicitMethodKey is null)
            throw new InvalidOperationException($"Missing implicit method resolution for '{call.Callee.Lexeme}'");

        EmitCurrentObject();
        foreach (var arg in call.Arguments)
        {
            Emit(arg);
        }

        if (!_methods.TryGetValue(call.ResolvedImplicitMethodKey, out var info))
            throw new InvalidOperationException($"Undefined method '{call.ResolvedImplicitMethodOwnerTypeName}.{call.Callee.Lexeme}' with {call.Arguments.Count} args");

        int frameSize = Math.Max(info.LocalCount, info.ParamCount);
        _builder.Call(info.Label, info.ParamCount, frameSize);
    }

    private void EmitInterfaceMethodCall(MethodCallExpr mc, TypeRef interfaceType)
    {
        if (mc.ResolvedInterfaceMethodKey is null)
            throw new InvalidOperationException($"Missing resolved interface method key for '{interfaceType.Name}.{mc.MethodName.Lexeme}'");
        string ifaceMethodKey = mc.ResolvedInterfaceMethodKey;
        string dispatchKey = InterfaceDispatchKey(interfaceType.Name, ifaceMethodKey);
        if (!_interfaceDispatch.TryGetValue(dispatchKey, out var targets))
            targets = [];

        Emit(mc.Target);
        foreach (var arg in mc.Arguments)
        {
            Emit(arg);
        }

        var entries = new List<BytecodeBuilder.InterfaceDispatchEntry>(targets.Count);
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!_methods.TryGetValue(target.ObjectMethodDispatchKey, out var info))
                throw new InvalidOperationException($"Undefined mapped method '{target.ObjectMethodDispatchKey}'");
            int frameSize = Math.Max(info.LocalCount, info.ParamCount);
            entries.Add(new BytecodeBuilder.InterfaceDispatchEntry(target.ObjectTypeName, info.Label, frameSize));
        }

        _builder.InterfaceCall(mc.Arguments.Count, entries);
    }

    private bool HasConstructors(string typeName)
    {
        foreach (var key in _constructors.Keys)
        {
            if (key.StartsWith($"{typeName}(", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private string NewLabel(string prefix) => $"{prefix}_{_labelCounter++}";

    private void EmitLogicalOr(Binary b)
    {
        Emit(b.Left);
        string trueLabel = NewLabel("or_true");
        string endLabel = NewLabel("or_end");

        _builder.JumpIfNotZero(trueLabel); // pops left
        Emit(b.Right);
        _builder.JumpIfNotZero(trueLabel);
        _builder.PushInt(0);
        _builder.Jump(endLabel);

        _builder.Label(trueLabel);
        _builder.PushInt(1);
        _builder.Label(endLabel);
    }

    private void EmitLogicalAnd(Binary b)
    {
        Emit(b.Left);
        string falseLabel = NewLabel("and_false");
        string endLabel = NewLabel("and_end");

        _builder.JumpIfZero(falseLabel); // pops left
        Emit(b.Right);
        _builder.JumpIfZero(falseLabel);
        _builder.PushInt(1);
        _builder.Jump(endLabel);

        _builder.Label(falseLabel);
        _builder.PushInt(0);
        _builder.Label(endLabel);
    }

    private void EmitArrayForeach(ForeachStmt fe)
    {
        int lenSlot = AllocateTemp();
        int idxSlot = AllocateTemp();
        int arrSlot = AllocateTemp();
        int iterSlot = GetOrAllocate(fe.Iterator.Lexeme);
        if (fe.IteratorTypeRef is not null)
            _localDeclaredTypes[fe.Iterator.Lexeme] = fe.IteratorTypeRef;

        Emit(fe.Iterable);
        _builder.Store(arrSlot);

        _builder.Load(arrSlot);
        _builder.ArrayLength();
        _builder.Store(lenSlot);

        _builder.PushInt(0);
        _builder.Store(idxSlot);

        string feStart = NewLabel("fe_start");
        string feEnd = NewLabel("fe_end");
        _builder.Label(feStart);
        _builder.Load(idxSlot);
        _builder.Load(lenSlot);
        _builder.Lt();
        _builder.JumpIfZero(feEnd);

        _builder.Load(arrSlot);
        _builder.Load(idxSlot);
        _builder.ArrayGet();
        _builder.Store(iterSlot);

        Emit(fe.Body);

        _builder.Load(idxSlot);
        _builder.PushInt(1);
        _builder.Add();
        _builder.Store(idxSlot);
        _builder.Jump(feStart);
        _builder.Label(feEnd);

        ReleaseTemp(arrSlot);
        ReleaseTemp(lenSlot);
        ReleaseTemp(idxSlot);
    }

    private void EmitNumericForeach(ForeachStmt fe)
    {
        int endSlot = AllocateTemp();
        int idxSlot = AllocateTemp();
        int iterSlot = GetOrAllocate(fe.Iterator.Lexeme);
        _localDeclaredTypes[fe.Iterator.Lexeme] = new TypeRef("integer", null, fe.Iterator.Line, fe.Iterator.Column);

        Emit(fe.Iterable);
        _builder.Store(endSlot);

        _builder.PushInt(0);
        _builder.Store(idxSlot);

        string feStart = NewLabel("fe_start");
        string feEnd = NewLabel("fe_end");
        _builder.Label(feStart);
        _builder.Load(idxSlot);
        _builder.Load(endSlot);
        _builder.Lt();
        _builder.JumpIfZero(feEnd);

        _builder.Load(idxSlot);
        _builder.Store(iterSlot);

        Emit(fe.Body);

        _builder.Load(idxSlot);
        _builder.PushInt(1);
        _builder.Add();
        _builder.Store(idxSlot);
        _builder.Jump(feStart);
        _builder.Label(feEnd);

        ReleaseTemp(endSlot);
        ReleaseTemp(idxSlot);
    }

    private bool TryFoldBinary(Binary b, out object result)
    {
        result = default!;
        if (b.Left is Literal lLit && b.Right is Literal rLit)
        {
            if (lLit.Value is string ls && rLit.Value is string rs && b.Operator.Type == TokenType.Plus)
            {
                result = ls + rs;
                return true;
            }

            bool IsInt(object? v, out int iv)
            {
                switch (v)
                {
                    case int i: iv = i; return true;
                    case double d when d == Math.Truncate(d): iv = (int)d; return true;
                    case null: iv = 0; return true;
                    default: iv = 0; return false;
                }
            }

            if (IsInt(lLit.Value, out var li) && IsInt(rLit.Value, out var ri))
            {
                switch (b.Operator.Type)
                {
                    case TokenType.Plus: result = li + ri; return true;
                    case TokenType.Minus: result = li - ri; return true;
                    case TokenType.Star: result = li * ri; return true;
                    case TokenType.Percent:
                        if (ri == 0) return false;
                        result = li % ri;
                        return true;
                }
            }
        }
        return false;
    }

    private void EmitCall(Call call)
    {
        if (call.ResolvesToImplicitMethod)
        {
            EmitImplicitMethodCall(call);
            return;
        }

        if (TryEmitIntrinsicCall(call))
            return;

        if (!_functions.TryGetValue(call.Callee.Lexeme, out var info))
            throw new InvalidOperationException($"Call to undefined function '{call.Callee.Lexeme}' at line {call.Callee.Line}, col {call.Callee.Column}");
        foreach (var arg in call.Arguments)
        {
            Emit(arg);
        }
        int frameSize = Math.Max(info.LocalCount, info.ParamCount); // include params in frame size
        _builder.Call(info.Label, call.Arguments.Count, frameSize);
    }

    private bool TryEmitIntrinsicCall(Call call)
    {
        if (!HostAbiCatalog.TryGetIntrinsic(call.Callee.Lexeme, out var intrinsic))
            return false;
        if (call.Arguments.Count != intrinsic.Arity)
            throw new InvalidOperationException($"Intrinsic '{call.Callee.Lexeme}' expects {intrinsic.Arity} args.");

        for (int i = 0; i < call.Arguments.Count; i++)
        {
            Emit(call.Arguments[i]);
        }

        _builder.HostCall(intrinsic.Symbol.Symbol, intrinsic.Arity);
        return true;
    }

    private int GetThisSlot()
    {
        if (!_locals.TryGetValue("this", out var slot))
            throw new InvalidOperationException("Implicit 'this' is not available in the current scope.");
        return slot;
    }

    private void EmitCurrentObject()
    {
        _builder.Load(GetThisSlot());
    }

    private void EmitInterpolatedString(InterpString istr)
    {
        bool hasAny = false;
        foreach (var part in istr.Parts)
        {
            if (part is string s && s.Length == 0) continue;
            if (part is string sPart)
            {
                _builder.PushString(sPart);
            }
            else if (part is Expr ePart)
            {
                Emit(ePart);
            }
            else
            {
                throw new InvalidOperationException("Unknown interpolation part");
            }

            if (!hasAny)
            {
                hasAny = true;
            }
            else
            {
                _builder.Add(); // string concat via Add when string present
            }
        }

        if (!hasAny)
        {
            _builder.PushString(string.Empty);
        }
    }

    private void PushScope()
    {
        _scopeStack.Push(_locals);
        _scopeDeclaredTypesStack.Push(_localDeclaredTypes);
        _nextLocalStack.Push(_nextLocalIndex);
        _locals = new Dictionary<string, int>(StringComparer.Ordinal);
        _localDeclaredTypes = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
    }

    private void PopScope()
    {
        _locals = _scopeStack.Pop();
        _localDeclaredTypes = _scopeDeclaredTypesStack.Pop();
        _nextLocalIndex = _nextLocalStack.Pop();
    }

    private WebSceneMetadata? TryBuildWebSceneMetadata(IReadOnlyList<ObjectDecl> objectDecls)
    {
        ObjectDecl? sceneObject = null;
        for (int i = 0; i < objectDecls.Count; i++)
        {
            if (string.Equals(objectDecls[i].Name.Lexeme, "MainScene", StringComparison.Ordinal))
            {
                sceneObject = objectDecls[i];
                break;
            }
        }

        if (sceneObject is null)
            return null;

        ConstructorDecl? ctor = null;
        for (int i = 0; i < sceneObject.Constructors.Count; i++)
        {
            if (sceneObject.Constructors[i].Parameters.Count == 0)
            {
                ctor = sceneObject.Constructors[i];
                break;
            }
        }

        MethodDecl? start = FindZeroArgMethod(sceneObject.Methods, "start");
        MethodDecl? update = FindZeroArgMethod(sceneObject.Methods, "update");
        MethodDecl? draw = FindZeroArgMethod(sceneObject.Methods, "draw");
        MethodDecl? drawHud = FindZeroArgMethod(sceneObject.Methods, "draw_hud");

        if (ctor is null || start is null || update is null || draw is null)
            return null;

        if (!TryResolveCallableMetadata(_constructors, ConstructorKey(sceneObject.Name.Lexeme, ctor.Parameters), out var ctorMeta))
            return null;
        if (!TryResolveCallableMetadata(_methods, MethodKey(sceneObject.Name.Lexeme, start.Name.Lexeme, start.Parameters), out var startMeta))
            return null;
        if (!TryResolveCallableMetadata(_methods, MethodKey(sceneObject.Name.Lexeme, update.Name.Lexeme, update.Parameters), out var updateMeta))
            return null;
        if (!TryResolveCallableMetadata(_methods, MethodKey(sceneObject.Name.Lexeme, draw.Name.Lexeme, draw.Parameters), out var drawMeta))
            return null;
        GeneratedCallableMetadata? drawHudMeta = null;
        if (drawHud is not null &&
            !TryResolveCallableMetadata(_methods, MethodKey(sceneObject.Name.Lexeme, drawHud.Name.Lexeme, drawHud.Parameters), out drawHudMeta))
        {
            return null;
        }

        return new WebSceneMetadata(sceneObject.Name.Lexeme, ctorMeta, startMeta, updateMeta, drawMeta, drawHudMeta);
    }

    private static MethodDecl? FindZeroArgMethod(IReadOnlyList<MethodDecl> methods, string name)
    {
        for (int i = 0; i < methods.Count; i++)
        {
            var method = methods[i];
            if (string.Equals(method.Name.Lexeme, name, StringComparison.Ordinal) && method.Parameters.Count == 0)
                return method;
        }

        return null;
    }

    private bool TryResolveCallableMetadata(
        IReadOnlyDictionary<string, (string Label, int ParamCount, int LocalCount)> map,
        string key,
        out GeneratedCallableMetadata metadata)
    {
        metadata = null!;
        if (!map.TryGetValue(key, out var info))
            return false;
        if (!_builder.TryGetLabelAddress(info.Label, out int targetIp))
            return false;

        metadata = new GeneratedCallableMetadata(targetIp, Math.Max(info.LocalCount, info.ParamCount));
        return true;
    }

    private sealed record InterfaceDispatchTarget(string ObjectTypeName, string ObjectMethodDispatchKey);

}
