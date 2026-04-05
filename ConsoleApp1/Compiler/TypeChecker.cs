using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleApp1.Compiler;

sealed class TypeChecker
{
    private readonly Dictionary<string, FunctionSignature> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ObjectSymbol> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InterfaceSymbol> _interfaces = new(StringComparer.Ordinal);
    private readonly HashSet<string> _interfaceObjectPairs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _interfaceMethodImplementers = new(StringComparer.Ordinal);
    private TypeRef? _currentReturnTypeRef;
    private static readonly Dictionary<string, FunctionSignature> IntrinsicFunctions = BuildIntrinsicFunctions();

    public void Check(IList<Stmt> statements)
    {
        // Collect object names first to allow forward references in field types.
        foreach (var stmt in statements)
        {
            if (stmt is not ObjectDecl obj)
                continue;

            if (_objects.ContainsKey(obj.Name.Lexeme))
                throw new CompilerException($"Object '{obj.Name.Lexeme}' already defined", obj.Name.Line, obj.Name.Column);
            if (_interfaces.ContainsKey(obj.Name.Lexeme))
                throw new CompilerException($"Type name '{obj.Name.Lexeme}' is already used by an interface", obj.Name.Line, obj.Name.Column);
            _objects[obj.Name.Lexeme] = new ObjectSymbol(
                obj.Name,
                new Dictionary<string, TypeRef>(StringComparer.Ordinal),
                new List<ConstructorSignature>(),
                new Dictionary<string, MethodSignature>(StringComparer.Ordinal));
        }

        // Collect interface names.
        foreach (var stmt in statements)
        {
            if (stmt is not InterfaceDecl iface)
                continue;

            if (_interfaces.ContainsKey(iface.Name.Lexeme))
                throw new CompilerException($"Interface '{iface.Name.Lexeme}' already defined", iface.Name.Line, iface.Name.Column);
            if (_objects.ContainsKey(iface.Name.Lexeme))
                throw new CompilerException($"Type name '{iface.Name.Lexeme}' is already used by an object", iface.Name.Line, iface.Name.Column);
            _interfaces[iface.Name.Lexeme] = new InterfaceSymbol(
                iface.Name,
                new Dictionary<string, InterfaceMethodSignature>(StringComparer.Ordinal));
        }

        // Validate interface method declarations.
        foreach (var stmt in statements)
        {
            if (stmt is not InterfaceDecl iface)
                continue;

            var ifaceSym = _interfaces[iface.Name.Lexeme];
            foreach (var method in iface.Methods)
            {
                ValidateTypeRef(method.ReturnType);
                EnsureNotVoidTypeRef(method.ReturnType, "Interface method return type cannot be void", method.Name.Line, method.Name.Column);
                var paramTypeRefs = new List<TypeRef>(method.Parameters.Count);
                var paramTypes = new List<TypeSymbol>(method.Parameters.Count);
                for (int i = 0; i < method.Parameters.Count; i++)
                {
                    var p = method.Parameters[i];
                    if (p.Type is null)
                        throw new CompilerException($"Interface method '{method.Name.Lexeme}' has untyped parameters", method.Name.Line, method.Name.Column);
                    ValidateTypeRef(p.Type);
                    EnsureNotVoidTypeRef(p.Type, "Interface method parameters cannot be void", p.Name.Line, p.Name.Column);
                    paramTypeRefs.Add(p.Type);
                    paramTypes.Add(MapType(p.Type));
                }
                string key = InterfaceMethodKey(method.Name.Lexeme, paramTypeRefs);
                if (ifaceSym.Methods.ContainsKey(key))
                    throw new CompilerException($"Interface method overload '{method.Name.Lexeme}' with the same signature is already defined in '{iface.Name.Lexeme}'", method.Name.Line, method.Name.Column);
                ifaceSym.Methods[key] = new InterfaceMethodSignature(method.Name, method.ReturnType, MapType(method.ReturnType), paramTypeRefs, paramTypes, key);
            }
        }

        // Validate object field declarations.
        foreach (var stmt in statements)
        {
            if (stmt is not ObjectDecl obj)
                continue;

            var symbol = _objects[obj.Name.Lexeme];
            foreach (var field in obj.Fields)
            {
                if (symbol.Fields.ContainsKey(field.Name.Lexeme))
                    throw new CompilerException($"Field '{field.Name.Lexeme}' is already defined in object '{obj.Name.Lexeme}'", field.Name.Line, field.Name.Column);
                if (IsReservedPropertyName(field.Name.Lexeme))
                    throw new CompilerException($"Field name '{field.Name.Lexeme}' is reserved for built-in properties", field.Name.Line, field.Name.Column);
                ValidateTypeRef(field.Type);
                EnsureNotVoidTypeRef(field.Type, "Object fields cannot be void", field.Name.Line, field.Name.Column);
                symbol.Fields[field.Name.Lexeme] = field.Type;
            }

            var ctorSignatures = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ctor in obj.Constructors)
            {
                var paramTypes = new List<TypeSymbol>(ctor.Parameters.Count);
                var paramTypeRefs = new List<TypeRef>(ctor.Parameters.Count);
                foreach (var param in ctor.Parameters)
                {
                    if (param.Type is null)
                        throw new CompilerException("Constructor parameters must be typed", param.Name.Line, param.Name.Column);
                    ValidateTypeRef(param.Type);
                    EnsureNotVoidTypeRef(param.Type, "Constructor parameters cannot be void", param.Name.Line, param.Name.Column);
                    paramTypeRefs.Add(param.Type);
                    paramTypes.Add(MapType(param.Type));
                }
                string dispatchKey = ConstructorDispatchKey(obj.Name.Lexeme, paramTypeRefs);
                if (!ctorSignatures.Add(dispatchKey))
                {
                    throw new CompilerException($"Constructor overload '{dispatchKey}' is already defined in object '{obj.Name.Lexeme}'", ctor.Keyword.Line, ctor.Keyword.Column);
                }
                symbol.Constructors.Add(new ConstructorSignature(ctor.Keyword, paramTypes, paramTypeRefs, dispatchKey, ctor.Body));
            }

            var methodKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in obj.Methods)
            {
                if (method.Parameters.Any(p => p.Type is null))
                    throw new CompilerException($"Method '{method.Name.Lexeme}' has untyped parameters", method.Name.Line, method.Name.Column);

                var returnTypeRef = method.ReturnType ?? BuildImplicitVoidTypeRef(method.Name);
                ValidateTypeRef(returnTypeRef);

                var paramTypeRefs = method.Parameters.Select(p => p.Type!).ToList();
                for (int i = 0; i < paramTypeRefs.Count; i++)
                {
                    EnsureNotVoidTypeRef(paramTypeRefs[i], "Method parameters cannot be void", method.Parameters[i].Name.Line, method.Parameters[i].Name.Column);
                }
                string methodKey = MethodDispatchKey(obj.Name.Lexeme, method.Name.Lexeme, paramTypeRefs);
                if (!methodKeys.Add(methodKey))
                    throw new CompilerException($"Method overload '{method.Name.Lexeme}' with the same signature is already defined in object '{obj.Name.Lexeme}'", method.Name.Line, method.Name.Column);

                var paramTypes = method.Parameters.Select(p => MapType(p.Type!)).ToList();
                var returnType = MapType(returnTypeRef);
                symbol.Methods[methodKey] = new MethodSignature(method.Name, returnTypeRef, returnType, paramTypes, paramTypeRefs, methodKey, method.Body, method.Parameters);
            }

            if (symbol.Fields.Count > 0 && symbol.Constructors.Count == 0)
            {
                throw new CompilerException($"Object '{obj.Name.Lexeme}' declares fields but has no constructor to initialize them", obj.Name.Line, obj.Name.Column);
            }
        }

        // Validate explicit interface implementation blocks.
        foreach (var stmt in statements)
        {
            if (stmt is not ImplementDecl impl)
                continue;

            if (!_interfaces.TryGetValue(impl.InterfaceName.Lexeme, out var iface))
                throw new CompilerException($"Unknown interface '{impl.InterfaceName.Lexeme}'", impl.InterfaceName.Line, impl.InterfaceName.Column);
            if (!_objects.TryGetValue(impl.ObjectName.Lexeme, out var obj))
                throw new CompilerException($"Unknown object '{impl.ObjectName.Lexeme}'", impl.ObjectName.Line, impl.ObjectName.Column);
            string pairKey = $"{impl.InterfaceName.Lexeme}->{impl.ObjectName.Lexeme}";
            if (!_interfaceObjectPairs.Add(pairKey))
                throw new CompilerException($"Interface '{impl.InterfaceName.Lexeme}' is already implemented for object '{impl.ObjectName.Lexeme}'", impl.ObjectName.Line, impl.ObjectName.Column);

            var mapped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var map in impl.Methods)
            {
                if (!string.Equals(map.ViaObjectName.Lexeme, impl.ObjectName.Lexeme, StringComparison.Ordinal))
                {
                    throw new CompilerException(
                        $"Implement block for '{impl.ObjectName.Lexeme}' cannot map via '{map.ViaObjectName.Lexeme}'",
                        map.ViaObjectName.Line,
                        map.ViaObjectName.Column);
                }

                var mapParamTypeRefs = new List<TypeRef>(map.Parameters.Count);
                for (int i = 0; i < map.Parameters.Count; i++)
                {
                    var p = map.Parameters[i];
                    if (p.Type is null)
                        throw new CompilerException("Implementation mapping parameters must be typed", p.Name.Line, p.Name.Column);
                    ValidateTypeRef(p.Type);
                    EnsureNotVoidTypeRef(p.Type, "Implementation mapping parameters cannot be void", p.Name.Line, p.Name.Column);
                    mapParamTypeRefs.Add(p.Type);
                }

                string ifaceKey = InterfaceMethodKey(map.InterfaceMethodName.Lexeme, mapParamTypeRefs);
                if (!iface.Methods.TryGetValue(ifaceKey, out var ifaceMethod))
                    throw new CompilerException($"Interface '{iface.Name.Lexeme}' has no method '{map.InterfaceMethodName.Lexeme}' with this signature", map.InterfaceMethodName.Line, map.InterfaceMethodName.Column);
                if (!mapped.Add(ifaceKey))
                    throw new CompilerException($"Interface method '{map.InterfaceMethodName.Lexeme}' is mapped more than once", map.InterfaceMethodName.Line, map.InterfaceMethodName.Column);

                string objectMethodKey = MethodDispatchKey(impl.ObjectName.Lexeme, map.ViaMethodName.Lexeme, mapParamTypeRefs);
                if (!obj.Methods.TryGetValue(objectMethodKey, out var objectMethod))
                    throw new CompilerException($"Object '{impl.ObjectName.Lexeme}' has no method '{map.ViaMethodName.Lexeme}' with this signature", map.ViaMethodName.Line, map.ViaMethodName.Column);

                if (!IsCompatibleInterfaceReturn(ifaceMethod, objectMethod))
                {
                    throw new CompilerException(
                        $"Method '{impl.ObjectName.Lexeme}.{map.ViaMethodName.Lexeme}' return type does not satisfy interface '{impl.InterfaceName.Lexeme}'",
                        map.ViaMethodName.Line,
                        map.ViaMethodName.Column);
                }

                string ifaceDispatchKey = InterfaceDispatchKey(impl.InterfaceName.Lexeme, ifaceKey);
                if (!_interfaceMethodImplementers.TryGetValue(ifaceDispatchKey, out var implementers))
                {
                    implementers = new HashSet<string>(StringComparer.Ordinal);
                    _interfaceMethodImplementers[ifaceDispatchKey] = implementers;
                }
                implementers.Add(impl.ObjectName.Lexeme);
            }

            foreach (var ifaceMethod in iface.Methods.Values)
            {
                if (!mapped.Contains(ifaceMethod.SignatureKey))
                    throw new CompilerException($"Object '{impl.ObjectName.Lexeme}' does not map interface method '{ifaceMethod.Name.Lexeme}'", impl.ObjectName.Line, impl.ObjectName.Column);
            }
        }

        // Collect function signatures.
        foreach (var stmt in statements)
        {
            if (stmt is FunctionDecl fn)
            {
                if (fn.Parameters.Any(p => p.Type is null))
                    throw new CompilerException($"Function '{fn.Name.Lexeme}' has untyped parameters", fn.Name.Line, fn.Name.Column);
                if (_functions.ContainsKey(fn.Name.Lexeme))
                    throw new CompilerException($"Function '{fn.Name.Lexeme}' already defined", fn.Name.Line, fn.Name.Column);
                var returnTypeRef = fn.ReturnType ?? BuildImplicitVoidTypeRef(fn.Name);
                ValidateTypeRef(returnTypeRef);
                var sig = new FunctionSignature(
                    Return: MapType(returnTypeRef),
                    ReturnTypeRef: returnTypeRef,
                    Params: fn.Parameters.Select(p => MapType(p.Type!)).ToList(),
                    ParamTypeRefs: fn.Parameters.Select(p => p.Type!).ToList()
                );
                for (int i = 0; i < fn.Parameters.Count; i++)
                {
                    EnsureNotVoidTypeRef(fn.Parameters[i].Type!, "Function parameters cannot be void", fn.Parameters[i].Name.Line, fn.Parameters[i].Name.Column);
                }
                _functions[fn.Name.Lexeme] = sig;
            }
        }

        // Validate constructor bodies and field definite-initialization.
        foreach (var stmt in statements)
        {
            if (stmt is not ObjectDecl obj)
                continue;
            CheckObjectConstructors(obj);
            CheckObjectMethods(obj);
        }

        var global = new TypeEnvironment();

        // Type-check top-level statements
        foreach (var stmt in statements)
        {
            if (stmt is FunctionDecl fn)
            {
                CheckFunction(fn);
            }
            else if (stmt is ObjectDecl or InterfaceDecl or ImplementDecl or ImportDecl or ExportDecl or PackageDecl)
            {
                // Declarations are compile-time metadata.
            }
            else
            {
                CheckStmt(stmt, global, currentReturn: null);
            }
        }
    }

    private void CheckFunction(FunctionDecl fn)
    {
        var env = new TypeEnvironment();
        var returnTypeRef = fn.ReturnType ?? BuildImplicitVoidTypeRef(fn.Name);
        var retType = MapType(returnTypeRef);
        var previousReturnRef = _currentReturnTypeRef;
        _currentReturnTypeRef = returnTypeRef;
        // params occupy env
        for (int i = 0; i < fn.Parameters.Count; i++)
        {
            var param = fn.Parameters[i];
            var pType = MapType(param.Type!);
            env.Define(param.Name.Lexeme, pType, param.Type, param.Name.Line, param.Name.Column, assigned: true);
        }
        bool allPathsReturn = CheckStmt(fn.Body, env, retType);
        _currentReturnTypeRef = previousReturnRef;
        if (retType != TypeSymbol.Void && !allPathsReturn)
            throw new CompilerException($"Function '{fn.Name.Lexeme}' may not return a value on all paths", fn.Name.Line, fn.Name.Column);
    }

    private void CheckObjectConstructors(ObjectDecl obj)
    {
        if (!_objects.TryGetValue(obj.Name.Lexeme, out var symbol))
            return;

        for (int i = 0; i < obj.Constructors.Count; i++)
        {
            var ctor = obj.Constructors[i];
            var ctorSig = symbol.Constructors[i];
            var env = new TypeEnvironment();
            var thisType = new TypeRef(obj.Name.Lexeme, null, obj.Name.Line, obj.Name.Column);
            env.Define("this", TypeSymbol.Object, thisType, ctor.Keyword.Line, ctor.Keyword.Column, assigned: true);
            foreach (var param in ctor.Parameters)
            {
                var pType = MapType(param.Type!);
                env.Define(param.Name.Lexeme, pType, param.Type, param.Name.Line, param.Name.Column, assigned: true);
            }

            // Constructor bodies are type-checked like regular blocks. Explicit return is currently not supported.
            var previousReturnRef = _currentReturnTypeRef;
            _currentReturnTypeRef = null;
            CheckStmt(ctorSig.Body, env, currentReturn: null);
            _currentReturnTypeRef = previousReturnRef;
            EnsureAllFieldsInitialized(obj, ctorSig.Body);
        }
    }

    private void CheckObjectMethods(ObjectDecl obj)
    {
        if (!_objects.TryGetValue(obj.Name.Lexeme, out var symbol))
            return;

        foreach (var method in symbol.Methods.Values)
        {
            var env = new TypeEnvironment();
            var thisType = new TypeRef(obj.Name.Lexeme, null, obj.Name.Line, obj.Name.Column);
            env.Define("this", TypeSymbol.Object, thisType, method.Name.Line, method.Name.Column, assigned: true);
            var previousReturnRef = _currentReturnTypeRef;
            _currentReturnTypeRef = method.ReturnTypeRef;

            for (int i = 0; i < method.Parameters.Count; i++)
            {
                var paramDecl = method.Parameters[i];
                var pType = method.ParamTypes[i];
                env.Define(paramDecl.Name.Lexeme, pType, paramDecl.Type, paramDecl.Name.Line, paramDecl.Name.Column, assigned: true);
            }

            bool allPathsReturn = CheckStmt(method.Body, env, method.ReturnType);
            _currentReturnTypeRef = previousReturnRef;
            if (method.ReturnType != TypeSymbol.Void && !allPathsReturn)
                throw new CompilerException($"Method '{obj.Name.Lexeme}.{method.Name.Lexeme}' may not return a value on all paths", method.Name.Line, method.Name.Column);
        }
    }

    private void EnsureAllFieldsInitialized(ObjectDecl obj, Block body)
    {
        var assigned = ComputeDefiniteFieldAssignments(body, new HashSet<string>(StringComparer.Ordinal));
        var missing = new List<string>();
        foreach (var field in obj.Fields)
        {
            if (!assigned.Contains(field.Name.Lexeme))
                missing.Add(field.Name.Lexeme);
        }

        if (missing.Count > 0)
        {
            throw new CompilerException(
                $"Constructor for '{obj.Name.Lexeme}' does not definitely assign fields: {string.Join(", ", missing)}",
                obj.Name.Line,
                obj.Name.Column);
        }
    }

    private HashSet<string> ComputeDefiniteFieldAssignments(Stmt stmt, HashSet<string> incoming)
    {
        var assigned = new HashSet<string>(incoming, StringComparer.Ordinal);
        switch (stmt)
        {
            case ExprStmt es:
                if (TryGetThisFieldName(es.Expression, out var fieldName))
                    assigned.Add(fieldName);
                return assigned;
            case Block block:
                foreach (var s in block.Statements)
                {
                    assigned = ComputeDefiniteFieldAssignments(s, assigned);
                }
                return assigned;
            case IfStmt ifs:
            {
                var thenAssigned = ComputeDefiniteFieldAssignments(ifs.ThenBranch, assigned);
                if (ifs.ElseBranch is null)
                    return assigned;
                var elseAssigned = ComputeDefiniteFieldAssignments(ifs.ElseBranch, assigned);
                thenAssigned.IntersectWith(elseAssigned);
                return thenAssigned;
            }
            case WhileStmt:
            case ForStmt:
            case ForeachStmt:
                return assigned; // loops may not execute
            case ReturnStmt r:
                throw new CompilerException("Return is not supported inside constructors yet", GetStmtLine(r), GetStmtCol(r));
            default:
                return assigned;
        }
    }

    private static bool TryGetThisFieldName(Expr expr, out string fieldName)
    {
        fieldName = string.Empty;
        if (expr is not FieldSetExpr set)
            return false;
        if (set.Target.Target is not Variable thisVar || !string.Equals(thisVar.Name.Lexeme, "this", StringComparison.Ordinal))
            return false;
        fieldName = set.Target.Name.Lexeme;
        return true;
    }

    private bool CheckStmt(Stmt stmt, TypeEnvironment env, TypeSymbol? currentReturn)
    {
        switch (stmt)
        {
            case VarDecl v:
                var t = MapType(v.Type);
                if (t == TypeSymbol.Void)
                    throw new CompilerException("Variables cannot be declared with type 'void'", v.Name.Line, v.Name.Column);
                bool hasInit = v.Initializer is not null;
                if (v.IsConstant && !hasInit)
                    throw new CompilerException($"Constant '{v.Name.Lexeme}' must be initialized", v.Name.Line, v.Name.Column);
                if (v.Initializer is not null)
                {
                    var init = CheckExpr(v.Initializer, env, currentReturn);
                    var initRef = ResolveExprTypeRef(v.Initializer, env);
                    RequireAssignable(t, v.Type, init, initRef, v.Type.Line, v.Type.Column, "Initializer type mismatch");
                }
                bool assignedFlag = hasInit || t == TypeSymbol.Optional;
                env.Define(v.Name.Lexeme, t, v.Type, v.Name.Line, v.Name.Column, assignedFlag, isConstant: v.IsConstant);
                return false;

            case ExprStmt e:
                CheckExpr(e.Expression, env, currentReturn);
                return false;

            case Block b:
                env = env.CreateChild();
                bool returned = false;
                foreach (var s in b.Statements)
                {
                    returned |= CheckStmt(s, env, currentReturn);
                    if (returned) break;
                }
                return returned;

            case IfStmt i:
                var cond = CheckExpr(i.Condition, env, currentReturn);
                Require(cond == TypeSymbol.Boolean, i.Condition, "Condition must be boolean");
                bool thenRet = CheckStmt(i.ThenBranch, env.CreateChild(), currentReturn);
                bool elseRet = i.ElseBranch is not null && CheckStmt(i.ElseBranch, env.CreateChild(), currentReturn);
                return thenRet && (i.ElseBranch is null ? false : elseRet);

            case WhileStmt w:
                var cType = CheckExpr(w.Condition, env, currentReturn);
                Require(cType == TypeSymbol.Boolean, w.Condition, "Condition must be boolean");
                CheckStmt(w.Body, env.CreateChild(), currentReturn);
                return false; // conservatively assume loop may not run

            case ForStmt f:
                var forEnv = env.CreateChild();
                if (f.Initializer is not null) CheckStmt(f.Initializer, forEnv, currentReturn);
                var condType = CheckExpr(f.Condition, forEnv, currentReturn);
                Require(condType == TypeSymbol.Boolean || IsNumeric(condType), f.Condition, "For condition must be boolean or numeric comparison");
                if (f.Increment is not null) CheckExpr(f.Increment, forEnv, currentReturn);
                CheckStmt(f.Body, forEnv.CreateChild(), currentReturn);
                return false;

            case ForeachStmt fe:
                var iterType = CheckExpr(fe.Iterable, env, currentReturn);
                Require(IsNumeric(iterType) || iterType == TypeSymbol.Array, fe.Iterable, "foreach iterable must be numeric (count) or array");
                fe.IsArray = iterType == TypeSymbol.Array;
                var feEnv = env.CreateChild();
                feEnv.Define(fe.Iterator.Lexeme, TypeSymbol.Integer, null, fe.Iterator.Line, fe.Iterator.Column, assigned: true);
                CheckStmt(fe.Body, feEnv, currentReturn);
                return false;

            case ReturnStmt r:
                if (currentReturn is null)
                    throw new CompilerException("Return outside function", GetStmtLine(r), GetStmtCol(r));
                if (currentReturn == TypeSymbol.Void)
                {
                    if (r.Value is not null)
                        throw new CompilerException("Void function cannot return a value", GetStmtLine(r), GetStmtCol(r));
                    return true;
                }
                var rval = r.Value is null ? TypeSymbol.Integer : CheckExpr(r.Value, env, currentReturn);
                var retRef = r.Value is null ? null : ResolveExprTypeRef(r.Value, env);
                RequireAssignable(currentReturn.Value, _currentReturnTypeRef, rval, retRef, GetStmtLine(r), GetStmtCol(r), "Return type mismatch");
                return true;

            case PrintStmt p:
                CheckExpr(p.Value, env, currentReturn);
                return false;

            case PanicStmt p:
                CheckExpr(p.Value, env, currentReturn);
                return false;

            case FunctionDecl:
                // handled earlier
                return false;
            case ObjectDecl:
                // handled in symbol collection pass
                return false;
            case InterfaceDecl:
                // handled in symbol collection pass
                return false;
            case ImplementDecl:
                // handled in symbol validation pass
                return false;
            case ImportDecl:
            case ExportDecl:
            case PackageDecl:
                // module linker consumes import/export before type checking
                return false;

            default:
                throw new CompilerException($"Unhandled statement in type checker: {stmt.GetType().Name}", 0, 0);
        }
    }

    private TypeSymbol CheckExpr(Expr expr, TypeEnvironment env, TypeSymbol? currentReturn)
    {
        switch (expr)
        {
            case Literal lit:
                return lit.Value switch
                {
                    bool => TypeSymbol.Boolean,
                    string => TypeSymbol.String,
                    IList<Expr> => TypeSymbol.Array,
                    _ => TypeSymbol.Integer // numeric literals as integer for now
                };
            case InterpString istr:
                foreach (var part in istr.Parts)
                {
                    if (part is Expr e) CheckExpr(e, env, currentReturn);
                }
                return TypeSymbol.String;
            case ArrayLiteral al:
                foreach (var e in al.Elements) CheckExpr(e, env, currentReturn);
                return TypeSymbol.Array;
            case NewArrayExpr na:
                var sizeType = CheckExpr(na.Size, env, currentReturn);
                Require(IsNumeric(sizeType), na.Size, "Array size must be numeric");
                return TypeSymbol.Array;
            case NewObjectExpr no:
            {
                if (!_objects.TryGetValue(no.TypeName.Lexeme, out var obj))
                    throw new CompilerException($"Unknown object type '{no.TypeName.Lexeme}'", no.TypeName.Line, no.TypeName.Column);

                var argTypes = new List<(TypeSymbol Symbol, TypeRef? Ref)>(no.Arguments.Count);
                for (int i = 0; i < no.Arguments.Count; i++)
                {
                    var argExpr = no.Arguments[i];
                    var argType = CheckExpr(argExpr, env, currentReturn);
                    var argTypeRef = ResolveExprTypeRef(argExpr, env);
                    argTypes.Add((argType, argTypeRef));
                }

                if (!TryResolveBestConstructor(obj, argTypes, out var ctor, out bool ambiguous))
                {
                    if (obj.Constructors.Count == 0 && no.Arguments.Count == 0)
                        return TypeSymbol.Object;
                    if (ambiguous)
                        throw new CompilerException($"Ambiguous constructor call for '{no.TypeName.Lexeme}'", no.TypeName.Line, no.TypeName.Column);
                    throw new CompilerException($"No matching constructor overload for '{no.TypeName.Lexeme}'", no.TypeName.Line, no.TypeName.Column);
                }

                no.ResolvedConstructorKey = ctor!.DispatchKey;
                return TypeSymbol.Object;
            }
            case ArrayLengthExpr alen:
                var targType = CheckExpr(alen.Target, env, currentReturn);
                Require(targType == TypeSymbol.Array, alen.Target, "'.length' is only valid on arrays");
                return TypeSymbol.Integer;
            case ArrayIndexExpr aidx:
                var arrType = CheckExpr(aidx.Array, env, currentReturn);
                Require(arrType == TypeSymbol.Array, aidx.Array, "Indexing requires an array");
                var idxType = CheckExpr(aidx.Index, env, currentReturn);
                Require(IsNumeric(idxType), aidx.Index, "Array index must be numeric");
                // Element typing not tracked; default to integer
                return TypeSymbol.Integer;
            case OptionalHasValueExpr ohv:
                CheckExpr(ohv.Target, env, currentReturn);
                return TypeSymbol.Boolean;
            case OptionalValueExpr oval:
                CheckExpr(oval.Target, env, currentReturn);
                return TypeSymbol.Unknown;
            case OptionalOrExpr oor:
                var fbType = CheckExpr(oor.Fallback, env, currentReturn);
                CheckExpr(oor.Optional, env, currentReturn);
                return fbType;
            case FieldAccessExpr fa:
            {
                var targetType = CheckExpr(fa.Target, env, currentReturn);
                Require(targetType == TypeSymbol.Object, fa.Target, "Field access requires object target");
                var resolved = ResolveFieldType(fa, env);
                return resolved ?? TypeSymbol.Unknown;
            }
            case ArraySetExpr aset:
                var arrT = CheckExpr(aset.Target.Array, env, currentReturn);
                Require(arrT == TypeSymbol.Array, aset.Target.Array, "Indexing requires an array");
                var idxT = CheckExpr(aset.Target.Index, env, currentReturn);
                Require(IsNumeric(idxT), aset.Target.Index, "Array index must be numeric");
                var valT = CheckExpr(aset.Value, env, currentReturn);
                return valT;
            case FieldSetExpr fset:
            {
                var targetType = CheckExpr(fset.Target.Target, env, currentReturn);
                Require(targetType == TypeSymbol.Object, fset.Target.Target, "Field assignment requires object target");
                var rhsType = CheckExpr(fset.Value, env, currentReturn);
                var expectedType = ResolveFieldType(fset.Target, env);
                if (expectedType is TypeSymbol expected)
                {
                    var expectedTypeRef = ResolveFieldTypeRef(fset.Target, env);
                    var fieldValueTypeRef = ResolveExprTypeRef(fset.Value, env);
                    RequireAssignable(expected, expectedTypeRef, rhsType, fieldValueTypeRef, fset.Target.Name.Line, fset.Target.Name.Column, "Field assignment type mismatch");
                }
                return rhsType;
            }
            case MethodCallExpr mc:
            {
                var targetType = CheckExpr(mc.Target, env, currentReturn);
                Require(targetType == TypeSymbol.Object || targetType == TypeSymbol.Interface, mc.Target, "Method call target must be an object or interface");
                var targetTypeRef = ResolveExprTypeRef(mc.Target, env);
                if (targetTypeRef is null)
                    throw new CompilerException("Could not resolve method target type", mc.MethodName.Line, mc.MethodName.Column);

                var argTypes = new List<(TypeSymbol Symbol, TypeRef? Ref)>(mc.Arguments.Count);
                for (int i = 0; i < mc.Arguments.Count; i++)
                {
                    var argExpr = mc.Arguments[i];
                    var argType = CheckExpr(argExpr, env, currentReturn);
                    var argTypeRef = ResolveExprTypeRef(argExpr, env);
                    argTypes.Add((argType, argTypeRef));
                }

                if (_interfaces.TryGetValue(targetTypeRef.Name, out var iface))
                {
                    if (!TryResolveBestInterfaceMethod(iface, mc.MethodName.Lexeme, argTypes, out var ifaceMethod, out bool ambiguousIface))
                    {
                        if (ambiguousIface)
                            throw new CompilerException($"Ambiguous method call '{targetTypeRef.Name}.{mc.MethodName.Lexeme}'", mc.MethodName.Line, mc.MethodName.Column);
                        throw new CompilerException($"Interface '{targetTypeRef.Name}' has no matching method overload '{mc.MethodName.Lexeme}'", mc.MethodName.Line, mc.MethodName.Column);
                    }

                    if (!HasAnyImplementationForMethod(targetTypeRef.Name, ifaceMethod!.SignatureKey))
                    {
                        throw new CompilerException(
                            $"No object implements interface method '{targetTypeRef.Name}.{mc.MethodName.Lexeme}' with this signature",
                            mc.MethodName.Line,
                            mc.MethodName.Column);
                    }

                    mc.ResolvedMethodKey = null;
                    mc.ResolvedInterfaceName = targetTypeRef.Name;
                    mc.ResolvedInterfaceMethodKey = ifaceMethod.SignatureKey;
                    mc.ResolvedReturnTypeRef = ifaceMethod.ReturnTypeRef;
                    return ifaceMethod.ReturnType;
                }

                if (!_objects.TryGetValue(targetTypeRef.Name, out var obj))
                    throw new CompilerException("Could not resolve method target type", mc.MethodName.Line, mc.MethodName.Column);

                if (!TryResolveBestMethod(obj, mc.MethodName.Lexeme, argTypes, out var method, out bool ambiguous))
                {
                    if (ambiguous)
                        throw new CompilerException($"Ambiguous method call '{targetTypeRef.Name}.{mc.MethodName.Lexeme}'", mc.MethodName.Line, mc.MethodName.Column);
                    throw new CompilerException($"Object '{targetTypeRef.Name}' has no matching method overload '{mc.MethodName.Lexeme}'", mc.MethodName.Line, mc.MethodName.Column);
                }

                mc.ResolvedMethodKey = method!.DispatchKey;
                mc.ResolvedInterfaceName = null;
                mc.ResolvedInterfaceMethodKey = null;
                mc.ResolvedReturnTypeRef = method.ReturnTypeRef;
                return method.ReturnType;
            }
            case Variable v:
                return env.LookupForRead(v.Name);
            case Assign a:
                env.EnsureCanAssign(a.Name);
                var rhs = CheckExpr(a.Value, env, currentReturn);
                var lhsType = env.LookupForReadOrWrite(a.Name, requireAssigned: false);
                var lhsTypeRef = env.TryGetDeclaredType(a.Name);
                var rhsTypeRef = ResolveExprTypeRef(a.Value, env);
                RequireAssignable(lhsType, lhsTypeRef, rhs, rhsTypeRef, a.Name.Line, a.Name.Column, "Assignment type mismatch");
                env.MarkAssigned(a.Name);
                return lhsType;
            case CompoundAssignExpr c:
            {
                var (targetType, targetTypeRef, assignToken) = CheckCompoundAssignmentTarget(c.Target, env, currentReturn);
                var valueType = CheckExpr(c.Value, env, currentReturn);
                var resultType = CheckCompoundAssignmentOperator(
                    c.Operator,
                    targetType,
                    valueType,
                    GetLine(c.Target),
                    GetCol(c.Target));
                RequireAssignable(
                    targetType,
                    targetTypeRef,
                    resultType,
                    null,
                    assignToken.Line,
                    assignToken.Column,
                    "Assignment type mismatch");
                if (c.Target is Variable variableTarget)
                    env.MarkAssigned(variableTarget.Name);
                return targetType;
            }
            case Call c:
                if (!TryGetFunctionSignature(c.Callee.Lexeme, out var sig))
                    throw new CompilerException($"Undefined function '{c.Callee.Lexeme}'", c.Callee.Line, c.Callee.Column);
                if (sig.Params.Count != c.Arguments.Count)
                    throw new CompilerException($"Function '{c.Callee.Lexeme}' expects {sig.Params.Count} args, got {c.Arguments.Count}", c.Callee.Line, c.Callee.Column);
                for (int i = 0; i < c.Arguments.Count; i++)
                {
                    var argType = CheckExpr(c.Arguments[i], env, currentReturn);
                    var argTypeRef = ResolveExprTypeRef(c.Arguments[i], env);
                    RequireAssignable(sig.Params[i], sig.ParamTypeRefs[i], argType, argTypeRef, c.Callee.Line, c.Callee.Column, $"Argument {i} type mismatch for '{c.Callee.Lexeme}'");
                }
                return sig.Return;
            case Unary u:
                var ut = CheckExpr(u.Right, env, currentReturn);
                if (u.Operator.Type == TokenType.Not)
                    Require(ut == TypeSymbol.Boolean, u.Right, "'not' requires boolean");
                else
                    Require(IsNumeric(ut), u.Right, "Unary +/− require numeric");
                return u.Operator.Type == TokenType.Not ? TypeSymbol.Boolean : ut;
            case Binary b:
                var lt = CheckExpr(b.Left, env, currentReturn);
                var rt = CheckExpr(b.Right, env, currentReturn);
                switch (b.Operator.Type)
                {
                    case TokenType.And:
                    case TokenType.Or:
                        Require(lt == TypeSymbol.Boolean && rt == TypeSymbol.Boolean, b.Left, "Logical ops require boolean");
                        return TypeSymbol.Boolean;
                    case TokenType.Plus:
                        if (lt == TypeSymbol.String || rt == TypeSymbol.String)
                            return TypeSymbol.String; // string concatenation
                        Require(IsNumeric(lt) && IsNumeric(rt), b.Left, "Arithmetic requires numeric");
                        return Promote(lt, rt);
                    case TokenType.Minus:
                    case TokenType.Star:
                    case TokenType.Slash:
                    case TokenType.Percent:
                        Require(IsNumeric(lt) && IsNumeric(rt), b.Left, "Arithmetic requires numeric");
                        return Promote(lt, rt);
                    case TokenType.EqualEqual:
                    case TokenType.BangEqual:
                    case TokenType.Less:
                    case TokenType.Greater:
                    case TokenType.LessEqual:
                    case TokenType.GreaterEqual:
                        Require(IsNumeric(lt) && IsNumeric(rt), b.Left, "Comparison requires numeric");
                        return TypeSymbol.Boolean;
                    default:
                        throw new CompilerException($"Unsupported operator {b.Operator.Type}", 0, 0);
                }
            default:
                throw new CompilerException($"Unhandled expression {expr.GetType().Name}", 0, 0);
        }
    }

    private bool TryGetFunctionSignature(string name, out FunctionSignature signature)
    {
        if (_functions.TryGetValue(name, out signature!))
            return true;
        return IntrinsicFunctions.TryGetValue(name, out signature!);
    }

    private static Dictionary<string, FunctionSignature> BuildIntrinsicFunctions()
    {
        var map = new Dictionary<string, FunctionSignature>(StringComparer.Ordinal);
        foreach (var intrinsic in HostAbiCatalog.IntrinsicSignatures)
        {
            var returnType = new TypeRef(intrinsic.ReturnTypeName, null, 0, 0);
            var paramTypes = new List<TypeSymbol>(intrinsic.ParameterTypes.Count);
            var paramTypeRefs = new List<TypeRef>(intrinsic.ParameterTypeNames.Count);
            for (int i = 0; i < intrinsic.ParameterTypes.Count; i++)
            {
                paramTypes.Add(intrinsic.ParameterTypes[i]);
                paramTypeRefs.Add(new TypeRef(intrinsic.ParameterTypeNames[i], null, 0, 0));
            }

            map[intrinsic.Name] = new FunctionSignature(
                intrinsic.ReturnType,
                returnType,
                paramTypes,
                paramTypeRefs);
        }
        return map;
    }

    private TypeSymbol MapType(TypeRef typeRef)
    {
        ValidateTypeRef(typeRef);

        return typeRef.Name switch
        {
            "integer" => TypeSymbol.Integer,
            "whole" => TypeSymbol.Whole,
            "real" => TypeSymbol.Real,
            "boolean" => TypeSymbol.Boolean,
            "string" => TypeSymbol.String,
            "array" => TypeSymbol.Array,
            "optional" => TypeSymbol.Optional,
            "void" => TypeSymbol.Void,
            _ => _interfaces.ContainsKey(typeRef.Name) ? TypeSymbol.Interface : TypeSymbol.Object
        };
    }

    private bool TryResolveBestConstructor(
        ObjectSymbol obj,
        IReadOnlyList<(TypeSymbol Symbol, TypeRef? Ref)> args,
        out ConstructorSignature? best,
        out bool ambiguous)
    {
        best = null;
        ambiguous = false;
        int bestCost = int.MaxValue;

        foreach (var ctor in obj.Constructors)
        {
            if (!TryCandidateCost(ctor.Params, ctor.ParamTypeRefs, args, out int cost))
                continue;

            if (cost < bestCost)
            {
                best = ctor;
                bestCost = cost;
                ambiguous = false;
            }
            else if (cost == bestCost)
            {
                ambiguous = true;
            }
        }

        return best is not null;
    }

    private bool TryResolveBestMethod(
        ObjectSymbol obj,
        string methodName,
        IReadOnlyList<(TypeSymbol Symbol, TypeRef? Ref)> args,
        out MethodSignature? best,
        out bool ambiguous)
    {
        best = null;
        ambiguous = false;
        int bestCost = int.MaxValue;

        foreach (var method in obj.Methods.Values.Where(m => string.Equals(m.Name.Lexeme, methodName, StringComparison.Ordinal)))
        {
            if (!TryCandidateCost(method.ParamTypes, method.ParamTypeRefs, args, out int cost))
                continue;

            if (cost < bestCost)
            {
                best = method;
                bestCost = cost;
                ambiguous = false;
            }
            else if (cost == bestCost)
            {
                ambiguous = true;
            }
        }

        return best is not null;
    }

    private bool TryResolveBestInterfaceMethod(
        InterfaceSymbol iface,
        string methodName,
        IReadOnlyList<(TypeSymbol Symbol, TypeRef? Ref)> args,
        out InterfaceMethodSignature? best,
        out bool ambiguous)
    {
        best = null;
        ambiguous = false;
        int bestCost = int.MaxValue;

        foreach (var method in iface.Methods.Values.Where(m => string.Equals(m.Name.Lexeme, methodName, StringComparison.Ordinal)))
        {
            if (!TryCandidateCost(method.ParamTypes.ToList(), method.ParamTypeRefs, args, out int cost))
                continue;

            if (cost < bestCost)
            {
                best = method;
                bestCost = cost;
                ambiguous = false;
            }
            else if (cost == bestCost)
            {
                ambiguous = true;
            }
        }

        return best is not null;
    }

    private bool ImplementsInterface(string objectTypeName, string interfaceName) =>
        _interfaceObjectPairs.Contains($"{interfaceName}->{objectTypeName}");

    private bool HasAnyImplementationForMethod(string interfaceName, string interfaceMethodKey)
    {
        string dispatchKey = InterfaceDispatchKey(interfaceName, interfaceMethodKey);
        return _interfaceMethodImplementers.TryGetValue(dispatchKey, out var implementers) && implementers.Count > 0;
    }

    private bool TryCandidateCost(
        IList<TypeSymbol> expectedSymbols,
        IReadOnlyList<TypeRef> expectedTypeRefs,
        IReadOnlyList<(TypeSymbol Symbol, TypeRef? Ref)> actuals,
        out int totalCost)
    {
        totalCost = 0;
        if (expectedSymbols.Count != actuals.Count) return false;

        for (int i = 0; i < expectedSymbols.Count; i++)
        {
            if (!TryConversionCost(expectedSymbols[i], expectedTypeRefs[i], actuals[i].Symbol, actuals[i].Ref, out int cost))
                return false;
            totalCost += cost;
        }
        return true;
    }

    private bool TryConversionCost(
        TypeSymbol expected,
        TypeRef expectedRef,
        TypeSymbol actual,
        TypeRef? actualRef,
        out int cost)
    {
        cost = int.MaxValue;
        if (expected == actual)
        {
            if (expected is TypeSymbol.Object or TypeSymbol.Interface)
            {
                return TryReferenceConversionCost(expectedRef, actualRef, out cost);
            }
            cost = 0;
            return true;
        }

        if (expected is TypeSymbol.Object or TypeSymbol.Interface)
        {
            return TryReferenceConversionCost(expectedRef, actualRef, out cost);
        }

        if (expected == TypeSymbol.Optional)
        {
            cost = 3;
            return true;
        }

        if (IsNumeric(expected) && IsNumeric(actual))
        {
            int eRank = NumericRank(expected);
            int aRank = NumericRank(actual);
            if (aRank <= eRank)
            {
                cost = eRank - aRank + 1; // exact handled above
                return true;
            }
            return false;
        }

        return false;
    }

    private bool TryReferenceConversionCost(TypeRef expectedRef, TypeRef? actualRef, out int cost)
    {
        cost = int.MaxValue;
        if (actualRef is null)
            return false;
        if (SameTypeRef(expectedRef, actualRef))
        {
            cost = 0;
            return true;
        }

        bool expectedIsInterface = _interfaces.ContainsKey(expectedRef.Name);
        bool actualIsObject = _objects.ContainsKey(actualRef.Name);

        if (expectedIsInterface && actualIsObject && ImplementsInterface(actualRef.Name, expectedRef.Name))
        {
            cost = 1;
            return true;
        }

        return false;
    }

    private static int NumericRank(TypeSymbol t) => t switch
    {
        TypeSymbol.Whole => 1,
        TypeSymbol.Integer => 2,
        TypeSymbol.Real => 3,
        _ => 0
    };

    private TypeSymbol? ResolveFieldType(FieldAccessExpr fieldAccess, TypeEnvironment env)
    {
        var targetType = ResolveExprTypeRef(fieldAccess.Target, env);
        if (targetType is null)
            return null;

        if (!_objects.TryGetValue(targetType.Name, out var objSymbol))
            return null;

        if (!objSymbol.Fields.TryGetValue(fieldAccess.Name.Lexeme, out var fieldType))
            throw new CompilerException($"Object '{targetType.Name}' has no field '{fieldAccess.Name.Lexeme}'", fieldAccess.Name.Line, fieldAccess.Name.Column);

        return MapType(fieldType);
    }

    private TypeRef? ResolveFieldTypeRef(FieldAccessExpr fieldAccess, TypeEnvironment env)
    {
        var targetType = ResolveExprTypeRef(fieldAccess.Target, env);
        if (targetType is null)
            return null;
        if (!_objects.TryGetValue(targetType.Name, out var objSymbol))
            return null;
        if (!objSymbol.Fields.TryGetValue(fieldAccess.Name.Lexeme, out var fieldType))
            throw new CompilerException($"Object '{targetType.Name}' has no field '{fieldAccess.Name.Lexeme}'", fieldAccess.Name.Line, fieldAccess.Name.Column);
        return fieldType;
    }

    private TypeRef? ResolveExprTypeRef(Expr expr, TypeEnvironment env)
    {
        switch (expr)
        {
            case Variable v:
                return env.TryGetDeclaredType(v.Name);
            case NewObjectExpr no:
                return new TypeRef(no.TypeName.Lexeme, null, no.TypeName.Line, no.TypeName.Column);
            case Call c:
                if (TryGetFunctionSignature(c.Callee.Lexeme, out var sig))
                    return sig.ReturnTypeRef;
                return null;
            case FieldAccessExpr fa:
            {
                var owner = ResolveExprTypeRef(fa.Target, env);
                if (owner is null) return null;
                if (!_objects.TryGetValue(owner.Name, out var objSymbol)) return null;
                if (!objSymbol.Fields.TryGetValue(fa.Name.Lexeme, out var fieldType))
                    throw new CompilerException($"Object '{owner.Name}' has no field '{fa.Name.Lexeme}'", fa.Name.Line, fa.Name.Column);
                return fieldType;
            }
            case MethodCallExpr mc:
                return mc.ResolvedReturnTypeRef;
            default:
                return null;
        }
    }

    private (TypeSymbol Type, TypeRef? TypeRef, Token AssignmentToken) CheckCompoundAssignmentTarget(
        Expr target,
        TypeEnvironment env,
        TypeSymbol? currentReturn)
    {
        switch (target)
        {
            case Variable variable:
                env.EnsureCanAssign(variable.Name);
                return (env.LookupForRead(variable.Name), env.TryGetDeclaredType(variable.Name), variable.Name);
            case FieldAccessExpr fieldAccess:
            {
                var targetType = CheckExpr(fieldAccess.Target, env, currentReturn);
                Require(targetType == TypeSymbol.Object, fieldAccess.Target, "Field access requires object target");
                return (
                    ResolveFieldType(fieldAccess, env) ?? TypeSymbol.Unknown,
                    ResolveFieldTypeRef(fieldAccess, env),
                    fieldAccess.Name);
            }
            case ArrayIndexExpr arrayIndex:
            {
                var arrayType = CheckExpr(arrayIndex.Array, env, currentReturn);
                Require(arrayType == TypeSymbol.Array, arrayIndex.Array, "Indexing requires an array");
                var indexType = CheckExpr(arrayIndex.Index, env, currentReturn);
                Require(IsNumeric(indexType), arrayIndex.Index, "Array index must be numeric");
                return (
                    TypeSymbol.Integer,
                    null,
                    arrayIndex.Array is Variable variableArray ? variableArray.Name : BuildSyntheticToken(GetLine(arrayIndex), GetCol(arrayIndex)));
            }
            default:
                throw new CompilerException("Invalid assignment target.", GetLine(target), GetCol(target));
        }
    }

    private TypeSymbol CheckCompoundAssignmentOperator(
        Token op,
        TypeSymbol leftType,
        TypeSymbol rightType,
        int line,
        int column)
    {
        switch (op.Type)
        {
            case TokenType.Plus:
                if (leftType == TypeSymbol.String || rightType == TypeSymbol.String)
                    return TypeSymbol.String;
                RequireAt(line, column, IsNumeric(leftType) && IsNumeric(rightType), "Arithmetic requires numeric");
                return Promote(leftType, rightType);
            case TokenType.Minus:
            case TokenType.Star:
            case TokenType.Slash:
            case TokenType.Percent:
                RequireAt(line, column, IsNumeric(leftType) && IsNumeric(rightType), "Arithmetic requires numeric");
                return Promote(leftType, rightType);
            default:
                throw new CompilerException($"Unsupported compound assignment operator '{op.Lexeme}'", op.Line, op.Column);
        }
    }

    private static Token BuildSyntheticToken(int line, int column)
        => new(TokenType.Identifier, "<synthetic>", null, line, column);

    private static void RequireAt(int line, int column, bool condition, string message)
    {
        if (!condition)
            throw new CompilerException(message, line, column);
    }

    private void ValidateTypeRef(TypeRef typeRef)
    {
        switch (typeRef.Name)
        {
            case "integer":
            case "whole":
            case "real":
            case "boolean":
            case "string":
            case "void":
                if (typeRef.TypeArguments.Count > 0)
                    throw new CompilerException($"Type '{typeRef.Name}' does not accept type arguments", typeRef.Line, typeRef.Column);
                return;

            case "array":
            case "optional":
                if (typeRef.TypeArguments.Count != 1)
                    throw new CompilerException($"Type '{typeRef.Name}' expects exactly one type argument", typeRef.Line, typeRef.Column);
                ValidateTypeRef(typeRef.TypeArguments[0]);
                return;

            default:
                if (typeRef.TypeArguments.Count > 0)
                    throw new CompilerException($"Type '{typeRef.Name}' does not support type arguments yet", typeRef.Line, typeRef.Column);
                if (!_objects.ContainsKey(typeRef.Name) && !_interfaces.ContainsKey(typeRef.Name))
                    throw new CompilerException($"Unknown type '{typeRef.Name}'", typeRef.Line, typeRef.Column);
                return;
        }
    }

    private static TypeRef BuildImplicitVoidTypeRef(Token origin)
    {
        return new TypeRef("void", null, origin.Line, origin.Column);
    }

    private static void EnsureNotVoidTypeRef(TypeRef typeRef, string message, int line, int col)
    {
        if (string.Equals(typeRef.Name, "void", StringComparison.Ordinal))
            throw new CompilerException(message, line, col);
    }

    private static bool IsNumeric(TypeSymbol t) => t is TypeSymbol.Integer or TypeSymbol.Whole or TypeSymbol.Real;

    private static TypeSymbol Promote(TypeSymbol a, TypeSymbol b)
    {
        if (!IsNumeric(a) || !IsNumeric(b)) return TypeSymbol.Unknown;
        int Rank(TypeSymbol t) => t switch
        {
            TypeSymbol.Whole => 1,
            TypeSymbol.Integer => 2,
            TypeSymbol.Real => 3,
            _ => 0
        };
        return Rank(a) >= Rank(b) ? a : b;
    }

    private static void Require(bool condition, Expr expr, string message)
    {
        if (!condition) throw new CompilerException(message, GetLine(expr), GetCol(expr));
    }

    private void RequireAssignable(
        TypeSymbol target,
        TypeRef? targetRef,
        TypeSymbol value,
        TypeRef? valueRef,
        int line,
        int col,
        string message)
    {
        if (target is TypeSymbol.Object or TypeSymbol.Interface)
        {
            if (targetRef is null)
                throw new CompilerException(message, line, col);

            if (TryReferenceConversionCost(targetRef, valueRef, out _))
                return;

            throw new CompilerException(message, line, col);
        }

        if (target == value) return;
        if (target == TypeSymbol.Optional) return; // allow any value into optional
        if (IsNumeric(target) && IsNumeric(value) && CanWiden(value, target)) return;
        throw new CompilerException(message, line, col);
    }

    private static bool CanWiden(TypeSymbol from, TypeSymbol to)
    {
        int Rank(TypeSymbol t) => t switch
        {
            TypeSymbol.Whole => 1,
            TypeSymbol.Integer => 2,
            TypeSymbol.Real => 3,
            _ => 0
        };
        return Rank(from) <= Rank(to) && Rank(to) > 0;
    }

    private static bool IsReservedPropertyName(string name) =>
        name is "length" or "hasValue" or "value" or "or";

    private static string TypeRefKey(TypeRef t) =>
        t.TypeArguments.Count == 0
            ? t.Name
            : $"{t.Name}<{string.Join(",", t.TypeArguments.Select(TypeRefKey))}>";
    private static string InterfaceMethodKey(string methodName, IReadOnlyList<TypeRef> paramTypes) =>
        $"{methodName}({string.Join(",", paramTypes.Select(TypeRefKey))})";
    private static string InterfaceDispatchKey(string interfaceName, string interfaceMethodKey) =>
        $"{interfaceName}.{interfaceMethodKey}";
    private static string ConstructorDispatchKey(string typeName, IReadOnlyList<TypeRef> paramTypes) =>
        $"{typeName}({string.Join(",", paramTypes.Select(TypeRefKey))})";
    private static string MethodDispatchKey(string typeName, string methodName, IReadOnlyList<TypeRef> paramTypes) =>
        $"{typeName}.{methodName}({string.Join(",", paramTypes.Select(TypeRefKey))})";

    private static bool IsCompatibleInterfaceReturn(InterfaceMethodSignature ifaceMethod, MethodSignature objectMethod)
    {
        if (ifaceMethod.ReturnType != objectMethod.ReturnType)
            return false;
        if (ifaceMethod.ReturnType != TypeSymbol.Object)
            return true;
        return SameTypeRef(ifaceMethod.ReturnTypeRef, objectMethod.ReturnTypeRef);
    }

    private static bool SameTypeRef(TypeRef a, TypeRef b)
    {
        if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal))
            return false;
        if (a.TypeArguments.Count != b.TypeArguments.Count)
            return false;
        for (int i = 0; i < a.TypeArguments.Count; i++)
        {
            if (!SameTypeRef(a.TypeArguments[i], b.TypeArguments[i]))
                return false;
        }
        return true;
    }

    private static int GetLine(Expr expr) => expr switch
    {
        Literal => 0,
        Variable v => v.Name.Line,
        Assign a => a.Name.Line,
        CompoundAssignExpr c => GetLine(c.Target),
        Call c => c.Callee.Line,
        MethodCallExpr mc => mc.MethodName.Line,
        Unary u => GetLine(u.Right),
        Binary b => GetLine(b.Left),
        _ => 0
    };

    private static int GetCol(Expr expr) => expr switch
    {
        Literal => 0,
        Variable v => v.Name.Column,
        Assign a => a.Name.Column,
        CompoundAssignExpr c => GetCol(c.Target),
        Call c => c.Callee.Column,
        MethodCallExpr mc => mc.MethodName.Column,
        Unary u => GetCol(u.Right),
        Binary b => GetCol(b.Left),
        _ => 0
    };

    private static int GetStmtLine(Stmt stmt) => stmt switch
    {
        ReturnStmt r when r.Value is Expr e => GetLine(e),
        ReturnStmt => 0,
        _ => 0
    };

    private static int GetStmtCol(Stmt stmt) => stmt switch
    {
        ReturnStmt r when r.Value is Expr e => GetCol(e),
        ReturnStmt => 0,
        _ => 0
    };

    private sealed record FunctionSignature(
        TypeSymbol Return,
        TypeRef ReturnTypeRef,
        IList<TypeSymbol> Params,
        IReadOnlyList<TypeRef> ParamTypeRefs);
    private sealed record ConstructorSignature(
        Token Keyword,
        IList<TypeSymbol> Params,
        IReadOnlyList<TypeRef> ParamTypeRefs,
        string DispatchKey,
        Block Body);
    private sealed record MethodSignature(
        Token Name,
        TypeRef ReturnTypeRef,
        TypeSymbol ReturnType,
        IList<TypeSymbol> ParamTypes,
        IReadOnlyList<TypeRef> ParamTypeRefs,
        string DispatchKey,
        Block Body,
        IReadOnlyList<Parameter> Parameters);
    private sealed record InterfaceMethodSignature(
        Token Name,
        TypeRef ReturnTypeRef,
        TypeSymbol ReturnType,
        IReadOnlyList<TypeRef> ParamTypeRefs,
        IReadOnlyList<TypeSymbol> ParamTypes,
        string SignatureKey);
    private sealed record InterfaceSymbol(
        Token Name,
        Dictionary<string, InterfaceMethodSignature> Methods);
    private sealed record ObjectSymbol(
        Token Name,
        Dictionary<string, TypeRef> Fields,
        List<ConstructorSignature> Constructors,
        Dictionary<string, MethodSignature> Methods);

    private sealed class TypeEnvironment
    {
        private readonly Dictionary<string, VarInfo> _vars = new(StringComparer.Ordinal);
        private readonly TypeEnvironment? _parent;
        public TypeEnvironment(TypeEnvironment? parent = null) => _parent = parent;
        public TypeEnvironment CreateChild() => new(this);

        public void Define(string name, TypeSymbol type, TypeRef? declaredType, int line, int col, bool assigned, bool isConstant = false)
        {
            if (_vars.ContainsKey(name))
                throw new CompilerException($"'{name}' already defined in scope", line, col);
            _vars[name] = new VarInfo(type, declaredType, assigned, isConstant, line, col);
        }

        public TypeSymbol LookupForRead(Token name)
        {
            var info = Find(name);
            if (!info.assigned)
                throw new CompilerException($"Variable '{name.Lexeme}' is used before being assigned", name.Line, name.Column);
            return info.type;
        }

        public TypeSymbol LookupForReadOrWrite(Token name, bool requireAssigned = true)
        {
            var info = Find(name);
            if (requireAssigned && !info.assigned)
                throw new CompilerException($"Variable '{name.Lexeme}' is used before being assigned", name.Line, name.Column);
            return info.type;
        }

        public void MarkAssigned(Token name)
        {
            var (env, info) = FindWithEnv(name);
            env._vars[name.Lexeme] = info with { assigned = true };
        }

        public void EnsureCanAssign(Token name)
        {
            var info = Find(name);
            if (info.isConstant && info.assigned)
                throw new CompilerException($"Cannot assign to constant '{name.Lexeme}'", name.Line, name.Column);
        }

        public string? TryGetObjectTypeName(Token name)
        {
            var info = Find(name);
            if (info.type != TypeSymbol.Object || info.declaredType is null)
                return null;
            return info.declaredType.Name;
        }

        public TypeRef? TryGetDeclaredType(Token name)
        {
            var info = Find(name);
            return info.declaredType;
        }

        private VarInfo Find(Token name)
        {
            var info = FindWithEnv(name).info;
            return info;
        }

        private (TypeEnvironment env, VarInfo info) FindWithEnv(Token name)
        {
            if (_vars.TryGetValue(name.Lexeme, out var t)) return (this, t);
            if (_parent is not null) return _parent.FindWithEnv(name);
            throw new CompilerException($"Undefined variable '{name.Lexeme}'", name.Line, name.Column);
        }

        private record struct VarInfo(TypeSymbol type, TypeRef? declaredType, bool assigned, bool isConstant, int line, int col);
    }
}
