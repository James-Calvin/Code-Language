using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleApp1.Compiler;

sealed class TypeChecker
{
    private readonly Dictionary<string, FunctionSignature> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnumSymbol> _enums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ObjectSymbol> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InterfaceSymbol> _interfaces = new(StringComparer.Ordinal);
    private readonly HashSet<string> _interfaceObjectPairs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _interfaceMethodImplementers = new(StringComparer.Ordinal);
    private TypeRef? _currentReturnTypeRef;
    private TypeRef? _currentYieldTypeRef;
    private ObjectSymbol? _currentObjectSymbol;
    private TypeRef? _currentObjectTypeRef;
    private string? _currentAccessPackageName;
    private static readonly Dictionary<string, FunctionSignature> IntrinsicFunctions = BuildIntrinsicFunctions();

    public void Check(IList<Stmt> statements)
    {
        // Collect enum names first to allow forward references in type positions.
        foreach (var stmt in statements)
        {
            if (stmt is not EnumDecl enumDecl)
                continue;

            if (IsReservedBuiltInTypeName(enumDecl.Name.Lexeme))
                throw new CompilerException($"Type name '{enumDecl.Name.Lexeme}' is reserved for a built-in collection type", enumDecl.Name.Line, enumDecl.Name.Column);
            if (_enums.ContainsKey(enumDecl.Name.Lexeme))
                throw new CompilerException($"Enum '{enumDecl.Name.Lexeme}' already defined", enumDecl.Name.Line, enumDecl.Name.Column);
            if (_objects.ContainsKey(enumDecl.Name.Lexeme) || _interfaces.ContainsKey(enumDecl.Name.Lexeme))
                throw new CompilerException($"Type name '{enumDecl.Name.Lexeme}' is already used", enumDecl.Name.Line, enumDecl.Name.Column);
            _enums[enumDecl.Name.Lexeme] = new EnumSymbol(enumDecl.Name, new Dictionary<string, int>(StringComparer.Ordinal));
        }

        // Collect object names first to allow forward references in field types.
        foreach (var stmt in statements)
        {
            if (stmt is not ObjectDecl obj)
                continue;

            if (IsReservedBuiltInTypeName(obj.Name.Lexeme))
                throw new CompilerException($"Type name '{obj.Name.Lexeme}' is reserved for a built-in collection type", obj.Name.Line, obj.Name.Column);
            if (_objects.ContainsKey(obj.Name.Lexeme))
                throw new CompilerException($"Object '{obj.Name.Lexeme}' already defined", obj.Name.Line, obj.Name.Column);
            if (_interfaces.ContainsKey(obj.Name.Lexeme))
                throw new CompilerException($"Type name '{obj.Name.Lexeme}' is already used by an interface", obj.Name.Line, obj.Name.Column);
            if (_enums.ContainsKey(obj.Name.Lexeme))
                throw new CompilerException($"Type name '{obj.Name.Lexeme}' is already used by an enum", obj.Name.Line, obj.Name.Column);
            _objects[obj.Name.Lexeme] = new ObjectSymbol(
                obj.Name,
                obj.IsRecord,
                obj.OriginPackageName,
                obj.OriginModulePath,
                new Dictionary<string, FieldSignature>(StringComparer.Ordinal),
                new List<ConstructorSignature>(),
                new Dictionary<string, MethodSignature>(StringComparer.Ordinal));
        }

        // Collect interface names.
        foreach (var stmt in statements)
        {
            if (stmt is not InterfaceDecl iface)
                continue;

            if (IsReservedBuiltInTypeName(iface.Name.Lexeme))
                throw new CompilerException($"Type name '{iface.Name.Lexeme}' is reserved for a built-in collection type", iface.Name.Line, iface.Name.Column);
            if (_interfaces.ContainsKey(iface.Name.Lexeme))
                throw new CompilerException($"Interface '{iface.Name.Lexeme}' already defined", iface.Name.Line, iface.Name.Column);
            if (_objects.ContainsKey(iface.Name.Lexeme))
                throw new CompilerException($"Type name '{iface.Name.Lexeme}' is already used by an object", iface.Name.Line, iface.Name.Column);
            if (_enums.ContainsKey(iface.Name.Lexeme))
                throw new CompilerException($"Type name '{iface.Name.Lexeme}' is already used by an enum", iface.Name.Line, iface.Name.Column);
            _interfaces[iface.Name.Lexeme] = new InterfaceSymbol(
                iface.Name,
                new Dictionary<string, InterfaceMethodSignature>(StringComparer.Ordinal));
        }

        // Validate enum member declarations.
        foreach (var stmt in statements)
        {
            if (stmt is not EnumDecl enumDecl)
                continue;

            if (enumDecl.Members.Count == 0)
                throw new CompilerException($"Enum '{enumDecl.Name.Lexeme}' must declare at least one member", enumDecl.Name.Line, enumDecl.Name.Column);

            var symbol = _enums[enumDecl.Name.Lexeme];
            int nextValue = 0;
            foreach (var member in enumDecl.Members)
            {
                if (symbol.Members.ContainsKey(member.Name.Lexeme))
                    throw new CompilerException($"Enum member '{member.Name.Lexeme}' is already defined in enum '{enumDecl.Name.Lexeme}'", member.Name.Line, member.Name.Column);

                int assignedValue = member.ExplicitValue ?? nextValue;
                symbol.Members[member.Name.Lexeme] = assignedValue;
                try
                {
                    nextValue = checked(assignedValue + 1);
                }
                catch (OverflowException)
                {
                    throw new CompilerException($"Enum member '{member.Name.Lexeme}' value overflows the supported integer range", member.Name.Line, member.Name.Column);
                }
            }
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
            string typeKind = obj.IsRecord ? "record" : "object";
            foreach (var field in obj.Fields)
            {
                if (symbol.Fields.ContainsKey(field.Name.Lexeme))
                    throw new CompilerException($"Field '{field.Name.Lexeme}' is already defined in {typeKind} '{obj.Name.Lexeme}'", field.Name.Line, field.Name.Column);
                if (IsReservedPropertyName(field.Name.Lexeme))
                    throw new CompilerException($"Field name '{field.Name.Lexeme}' is reserved for built-in properties", field.Name.Line, field.Name.Column);
                if (field.Visibility == DeclarationVisibility.Package && string.IsNullOrWhiteSpace(symbol.PackageName))
                    throw new CompilerException("Package-visible members require a containing package declaration.", field.Name.Line, field.Name.Column);
                ValidateTypeRef(field.Type);
                EnsureNotVoidTypeRef(field.Type, $"{Capitalize(typeKind)} fields cannot be void", field.Name.Line, field.Name.Column);
                symbol.Fields[field.Name.Lexeme] = new FieldSignature(field.Name, field.Type, field.Visibility);
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
                    throw new CompilerException($"Constructor overload '{dispatchKey}' is already defined in {typeKind} '{obj.Name.Lexeme}'", ctor.Keyword.Line, ctor.Keyword.Column);
                }
                if (ctor.Visibility == DeclarationVisibility.Package && string.IsNullOrWhiteSpace(symbol.PackageName))
                    throw new CompilerException("Package-visible members require a containing package declaration.", ctor.Keyword.Line, ctor.Keyword.Column);
                symbol.Constructors.Add(new ConstructorSignature(ctor.Keyword, paramTypes, paramTypeRefs, dispatchKey, ctor.Body, ctor.Visibility));
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
                    throw new CompilerException($"Method overload '{method.Name.Lexeme}' with the same signature is already defined in {typeKind} '{obj.Name.Lexeme}'", method.Name.Line, method.Name.Column);

                if (method.Visibility == DeclarationVisibility.Package && string.IsNullOrWhiteSpace(symbol.PackageName))
                    throw new CompilerException("Package-visible members require a containing package declaration.", method.Name.Line, method.Name.Column);
                var paramTypes = method.Parameters.Select(p => MapType(p.Type!)).ToList();
                var returnType = MapType(returnTypeRef);
                symbol.Methods[methodKey] = new MethodSignature(method.Name, returnTypeRef, returnType, paramTypes, paramTypeRefs, methodKey, method.Body, method.Parameters, method.Visibility);
            }

            if (symbol.Fields.Count > 0 && symbol.Constructors.Count == 0)
            {
                throw new CompilerException($"{Capitalize(typeKind)} '{obj.Name.Lexeme}' declares fields but has no constructor to initialize them", obj.Name.Line, obj.Name.Column);
            }
        }

        ValidateRecordLayouts();

        // Validate interface implementation blocks, allowing multiple declarations to contribute to the same pair.
        var implementGroups = new Dictionary<string, List<ImplementDecl>>(StringComparer.Ordinal);
        foreach (var stmt in statements)
        {
            if (stmt is not ImplementDecl impl)
                continue;

            string pairKey = $"{impl.InterfaceName.Lexeme}->{impl.ObjectName.Lexeme}";
            if (!implementGroups.TryGetValue(pairKey, out var group))
            {
                group = [];
                implementGroups[pairKey] = group;
            }
            group.Add(impl);
        }

        foreach (var pair in implementGroups)
        {
            var first = pair.Value[0];
            if (!_interfaces.TryGetValue(first.InterfaceName.Lexeme, out var iface))
                throw new CompilerException($"Unknown interface '{first.InterfaceName.Lexeme}'", first.InterfaceName.Line, first.InterfaceName.Column);
            if (!_objects.TryGetValue(first.ObjectName.Lexeme, out var obj))
                throw new CompilerException($"Unknown object '{first.ObjectName.Lexeme}'", first.ObjectName.Line, first.ObjectName.Column);

            var mapped = new HashSet<string>(StringComparer.Ordinal);
            for (int declIndex = 0; declIndex < pair.Value.Count; declIndex++)
            {
                var impl = pair.Value[declIndex];
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
            }

            foreach (var ifaceMethod in iface.Methods.Values)
            {
                if (!mapped.Contains(ifaceMethod.SignatureKey))
                    throw new CompilerException($"Object '{first.ObjectName.Lexeme}' does not map interface method '{ifaceMethod.Name.Lexeme}'", first.ObjectName.Line, first.ObjectName.Column);
            }

            _interfaceObjectPairs.Add(pair.Key);
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
            else if (stmt is EnumDecl or ObjectDecl or InterfaceDecl or ImplementDecl or ImportDecl or ExportDecl or PackageDecl)
            {
                // Declarations are compile-time metadata.
            }
            else
            {
                var previousAccessPackageName = _currentAccessPackageName;
                _currentAccessPackageName = stmt.OriginPackageName;
                CheckStmt(stmt, global, currentReturn: null);
                _currentAccessPackageName = previousAccessPackageName;
            }
        }
    }

    private void CheckFunction(FunctionDecl fn)
    {
        var env = new TypeEnvironment();
        var returnTypeRef = fn.ReturnType ?? BuildImplicitVoidTypeRef(fn.Name);
        var retType = MapType(returnTypeRef);
        var previousReturnRef = _currentReturnTypeRef;
        var previousAccessPackageName = _currentAccessPackageName;
        _currentReturnTypeRef = returnTypeRef;
        _currentAccessPackageName = fn.OriginPackageName;
        // params occupy env
        for (int i = 0; i < fn.Parameters.Count; i++)
        {
            var param = fn.Parameters[i];
            var pType = MapType(param.Type!);
            env.Define(param.Name.Lexeme, pType, param.Type, param.Name.Line, param.Name.Column, assigned: true);
        }
        bool allPathsReturn = CheckStmt(fn.Body, env, retType);
        _currentReturnTypeRef = previousReturnRef;
        _currentAccessPackageName = previousAccessPackageName;
        if (retType != TypeSymbol.Void && !allPathsReturn)
            throw new CompilerException($"Function '{fn.Name.Lexeme}' may not return a value on all paths", fn.Name.Line, fn.Name.Column);
    }

    private void CheckObjectConstructors(ObjectDecl obj)
    {
        if (!_objects.TryGetValue(obj.Name.Lexeme, out var symbol))
            return;

        var previousObjectSymbol = _currentObjectSymbol;
        var previousObjectTypeRef = _currentObjectTypeRef;
        var previousAccessPackageName = _currentAccessPackageName;
        _currentObjectSymbol = symbol;
        _currentObjectTypeRef = new TypeRef(obj.Name.Lexeme, null, obj.Name.Line, obj.Name.Column);
        _currentAccessPackageName = obj.OriginPackageName;

        try
        {
            for (int i = 0; i < obj.Constructors.Count; i++)
            {
                var ctor = obj.Constructors[i];
                var ctorSig = symbol.Constructors[i];
                var env = new TypeEnvironment();
                var thisType = new TypeRef(obj.Name.Lexeme, null, obj.Name.Line, obj.Name.Column);
                env.Define("this", symbol.IsRecord ? TypeSymbol.Record : TypeSymbol.Object, thisType, ctor.Keyword.Line, ctor.Keyword.Column, assigned: true);
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
        finally
        {
            _currentObjectSymbol = previousObjectSymbol;
            _currentObjectTypeRef = previousObjectTypeRef;
            _currentAccessPackageName = previousAccessPackageName;
        }
    }

    private void CheckObjectMethods(ObjectDecl obj)
    {
        if (!_objects.TryGetValue(obj.Name.Lexeme, out var symbol))
            return;

        var previousObjectSymbol = _currentObjectSymbol;
        var previousObjectTypeRef = _currentObjectTypeRef;
        var previousAccessPackageName = _currentAccessPackageName;
        _currentObjectSymbol = symbol;
        _currentObjectTypeRef = new TypeRef(obj.Name.Lexeme, null, obj.Name.Line, obj.Name.Column);
        _currentAccessPackageName = obj.OriginPackageName;

        try
        {
            foreach (var method in symbol.Methods.Values)
            {
                var env = new TypeEnvironment();
                var thisType = new TypeRef(obj.Name.Lexeme, null, obj.Name.Line, obj.Name.Column);
                env.Define("this", symbol.IsRecord ? TypeSymbol.Record : TypeSymbol.Object, thisType, method.Name.Line, method.Name.Column, assigned: true);
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
        finally
        {
            _currentObjectSymbol = previousObjectSymbol;
            _currentObjectTypeRef = previousObjectTypeRef;
            _currentAccessPackageName = previousAccessPackageName;
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
            case SwitchStmt switchStmt:
            {
                if (switchStmt.DefaultBranch is null)
                    return assigned;

                HashSet<string>? branchAssigned = null;
                for (int i = 0; i < switchStmt.Cases.Count; i++)
                {
                    var caseAssigned = ComputeDefiniteFieldAssignments(switchStmt.Cases[i].Body, assigned);
                    if (branchAssigned is null)
                    {
                        branchAssigned = caseAssigned;
                    }
                    else
                    {
                        branchAssigned.IntersectWith(caseAssigned);
                    }
                }

                var defaultAssigned = ComputeDefiniteFieldAssignments(switchStmt.DefaultBranch, assigned);
                if (branchAssigned is null)
                    return defaultAssigned;

                branchAssigned.IntersectWith(defaultAssigned);
                return branchAssigned;
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
        if (expr is Assign implicitFieldAssign && implicitFieldAssign.ResolvesToImplicitField)
        {
            fieldName = implicitFieldAssign.Name.Lexeme;
            return true;
        }
        if (expr is not FieldSetExpr set)
            return false;
        if (set.Target.Target is not Variable thisVar || !string.Equals(thisVar.Name.Lexeme, "this", StringComparison.Ordinal))
            return false;
        fieldName = set.Target.Name.Lexeme;
        return true;
    }

    private void ValidateRecordLayouts()
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in _objects)
        {
            if (!pair.Value.IsRecord)
                continue;

            ValidateRecordLayout(pair.Value, visiting, visited);
        }
    }

    private void ValidateRecordLayout(ObjectSymbol record, HashSet<string> visiting, HashSet<string> visited)
    {
        if (visited.Contains(record.Name.Lexeme))
            return;

        if (!visiting.Add(record.Name.Lexeme))
            throw new CompilerException($"Record '{record.Name.Lexeme}' cannot contain itself by value", record.Name.Line, record.Name.Column);

        foreach (var field in record.Fields.Values)
        {
            foreach (var nestedRecord in EnumerateEmbeddedRecordTypes(field.TypeRef))
            {
                if (_objects.TryGetValue(nestedRecord, out var nestedSymbol) && nestedSymbol.IsRecord)
                    ValidateRecordLayout(nestedSymbol, visiting, visited);
            }
        }

        visiting.Remove(record.Name.Lexeme);
        visited.Add(record.Name.Lexeme);
    }

    private IEnumerable<string> EnumerateEmbeddedRecordTypes(TypeRef typeRef)
    {
        if (_objects.TryGetValue(typeRef.Name, out var symbol) && symbol.IsRecord)
        {
            yield return typeRef.Name;
            yield break;
        }

        if (string.Equals(typeRef.Name, "optional", StringComparison.Ordinal) && typeRef.TypeArguments.Count == 1)
        {
            foreach (var nested in EnumerateEmbeddedRecordTypes(typeRef.TypeArguments[0]))
                yield return nested;
        }
    }

    private bool TryResolveImplicitField(Token name, TypeEnvironment env, out TypeSymbol type, out TypeRef? typeRef)
    {
        type = TypeSymbol.Unknown;
        typeRef = null;

        if (_currentObjectSymbol is null || _currentObjectTypeRef is null)
            return false;

        if (env.Contains(name.Lexeme))
            return false;

        if (!_currentObjectSymbol.Fields.TryGetValue(name.Lexeme, out var field))
            return false;

        typeRef = field.TypeRef;
        type = MapType(typeRef);
        return true;
    }

    private bool CurrentObjectHasMethodNamed(string methodName)
    {
        return _currentObjectSymbol is not null &&
               _currentObjectSymbol.Methods.Values.Any(method => string.Equals(method.Name.Lexeme, methodName, StringComparison.Ordinal));
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
                if (CheckExpr(e.Expression, env, currentReturn) == TypeSymbol.Error)
                    throw new CompilerException("Use error.code or error.message inside an 'on error' handler", GetLine(e.Expression), GetCol(e.Expression));
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

            case SwitchStmt s:
            {
                var switchType = CheckExpr(s.Value, env, currentReturn);
                var switchTypeRef = ResolveExprTypeRef(s.Value, env);
                bool allCasesReturn = true;
                for (int i = 0; i < s.Cases.Count; i++)
                {
                    var caseClause = s.Cases[i];
                    var caseType = CheckExpr(caseClause.Value, env, currentReturn);
                    var caseTypeRef = ResolveExprTypeRef(caseClause.Value, env);
                    Require(
                        CanCompareForEquality(switchType, switchTypeRef, caseType, caseTypeRef),
                        caseClause.Value,
                        "Switch case value type must be comparable to switch value");
                    allCasesReturn &= CheckStmt(caseClause.Body, env.CreateChild(), currentReturn);
                }

                if (s.DefaultBranch is null)
                    return false;

                bool defaultReturns = CheckStmt(s.DefaultBranch, env.CreateChild(), currentReturn);
                return allCasesReturn && defaultReturns;
            }

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
                if (fe.IsArray)
                {
                    var iterableTypeRef = ResolveExprTypeRef(fe.Iterable, env);
                    if (iterableTypeRef is null || !iterableTypeRef.IsArray || iterableTypeRef.TypeArguments.Count != 1)
                        throw new CompilerException("Could not resolve array element type for foreach", fe.Iterator.Line, fe.Iterator.Column);
                    var iteratorTypeRef = iterableTypeRef.TypeArguments[0];
                    fe.IteratorTypeRef = iteratorTypeRef;
                    feEnv.Define(fe.Iterator.Lexeme, MapType(iteratorTypeRef), iteratorTypeRef, fe.Iterator.Line, fe.Iterator.Column, assigned: true);
                }
                else
                {
                    fe.IteratorTypeRef = null;
                    feEnv.Define(fe.Iterator.Lexeme, TypeSymbol.Integer, null, fe.Iterator.Line, fe.Iterator.Column, assigned: true);
                }
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
                if (_currentReturnTypeRef is not null && _currentReturnTypeRef.IsFallible && _currentReturnTypeRef.TypeArguments.Count == 2)
                {
                    if (rval == TypeSymbol.Fallible)
                    {
                        RequireAssignable(currentReturn.Value, _currentReturnTypeRef, rval, retRef, GetStmtLine(r), GetStmtCol(r), "Return type mismatch");
                    }
                    else
                    {
                        var successTypeRef = _currentReturnTypeRef.TypeArguments[0];
                        RequireAssignable(MapType(successTypeRef), successTypeRef, rval, retRef, GetStmtLine(r), GetStmtCol(r), "Return type mismatch");
                    }
                }
                else
                {
                    RequireAssignable(currentReturn.Value, _currentReturnTypeRef, rval, retRef, GetStmtLine(r), GetStmtCol(r), "Return type mismatch");
                }
                return true;

            case PrintStmt p:
                if (CheckExpr(p.Value, env, currentReturn) == TypeSymbol.Error)
                    throw new CompilerException("Use error.code or error.message inside an 'on error' handler", GetLine(p.Value), GetCol(p.Value));
                return false;

            case PanicStmt p:
                CheckExpr(p.Value, env, currentReturn);
                return true;

            case YieldStmt y:
            {
                if (_currentYieldTypeRef is null)
                    throw new CompilerException("'yield' is only valid inside an 'on error' handler", y.Keyword.Line, y.Keyword.Column);
                var valueType = CheckExpr(y.Value, env, currentReturn);
                var valueTypeRef = ResolveExprTypeRef(y.Value, env);
                RequireAssignable(
                    MapType(_currentYieldTypeRef),
                    _currentYieldTypeRef,
                    valueType,
                    valueTypeRef,
                    y.Keyword.Line,
                    y.Keyword.Column,
                    "Yield value type mismatch");
                return true;
            }

            case FunctionDecl:
                // handled earlier
                return false;
            case EnumDecl:
                // handled in symbol collection pass
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
                al.ResolvedTypeRef = InferArrayLiteralTypeRef(al, env, currentReturn);
                return TypeSymbol.Array;
            case NewArrayExpr na:
                var sizeType = CheckExpr(na.Size, env, currentReturn);
                Require(IsNumeric(sizeType), na.Size, "Array size must be numeric");
                return TypeSymbol.Array;
            case NewCollectionExpr nc:
                ValidateTypeRef(nc.CollectionType);
                return MapType(nc.CollectionType);
            case NewObjectExpr no:
            {
                if (!_objects.TryGetValue(no.TypeName.Lexeme, out var obj))
                    throw new CompilerException($"Unknown object or record type '{no.TypeName.Lexeme}'", no.TypeName.Line, no.TypeName.Column);

                var argTypes = new List<(TypeSymbol Symbol, TypeRef? Ref)>(no.Arguments.Count);
                for (int i = 0; i < no.Arguments.Count; i++)
                {
                    var argExpr = no.Arguments[i];
                    var argType = CheckExpr(argExpr, env, currentReturn);
                    var argTypeRef = ResolveExprTypeRef(argExpr, env);
                    argTypes.Add((argType, argTypeRef));
                }

                if (!TryResolveBestConstructor(obj, argTypes, requireAccessible: true, out var ctor, out bool ambiguous))
                {
                    if (obj.Constructors.Count == 0 && no.Arguments.Count == 0)
                        return obj.IsRecord ? TypeSymbol.Record : TypeSymbol.Object;
                    if (ambiguous)
                        throw new CompilerException($"Ambiguous constructor call for '{no.TypeName.Lexeme}'", no.TypeName.Line, no.TypeName.Column);
                    if (TryResolveBestConstructor(obj, argTypes, requireAccessible: false, out _, out _))
                        throw new CompilerException($"Constructor for '{no.TypeName.Lexeme}' is not accessible", no.TypeName.Line, no.TypeName.Column);
                    throw new CompilerException($"No matching constructor overload for '{no.TypeName.Lexeme}'", no.TypeName.Line, no.TypeName.Column);
                }

                no.ResolvedConstructorKey = ctor!.DispatchKey;
                return obj.IsRecord ? TypeSymbol.Record : TypeSymbol.Object;
            }
            case ArrayLengthExpr alen:
                var targType = CheckExpr(alen.Target, env, currentReturn);
                Require(IsBuiltInCollection(targType), alen.Target, "'.length' is only valid on arrays, maps, sets, queues, and stacks");
                return TypeSymbol.Integer;
            case ArrayIndexExpr aidx:
                var arrType = CheckExpr(aidx.Array, env, currentReturn);
                var idxType = CheckExpr(aidx.Index, env, currentReturn);
                var arrayTypeRef = ResolveExprTypeRef(aidx.Array, env);
                if (arrayTypeRef is null)
                    throw new CompilerException("Could not resolve indexed collection type", GetLine(aidx.Array), GetCol(aidx.Array));

                if (arrType == TypeSymbol.Array)
                {
                    Require(IsNumeric(idxType), aidx.Index, "Array index must be numeric");
                    if (!arrayTypeRef.IsArray || arrayTypeRef.TypeArguments.Count != 1)
                        throw new CompilerException("Could not resolve array element type", GetLine(aidx.Array), GetCol(aidx.Array));
                    aidx.ResolvedElementTypeRef = arrayTypeRef.TypeArguments[0];
                }
                else if (arrType == TypeSymbol.Map)
                {
                    if (!arrayTypeRef.IsMap || arrayTypeRef.TypeArguments.Count != 2)
                        throw new CompilerException("Could not resolve map key/value types", GetLine(aidx.Array), GetCol(aidx.Array));
                    RequireAssignable(
                        MapType(arrayTypeRef.TypeArguments[0]),
                        arrayTypeRef.TypeArguments[0],
                        idxType,
                        ResolveExprTypeRef(aidx.Index, env),
                        GetLine(aidx.Index),
                        GetCol(aidx.Index),
                        "Map key type mismatch");
                    aidx.ResolvedElementTypeRef = arrayTypeRef.TypeArguments[1];
                }
                else
                {
                    throw new CompilerException("Indexing requires an array or map", GetLine(aidx.Array), GetCol(aidx.Array));
                }
                return MapType(aidx.ResolvedElementTypeRef);
            case OptionalHasValueExpr ohv:
                CheckExpr(ohv.Target, env, currentReturn);
                return TypeSymbol.Boolean;
            case OptionalValueExpr oval:
            {
                var optionalType = CheckExpr(oval.Target, env, currentReturn);
                Require(optionalType == TypeSymbol.Optional, oval.Target, "'.value' requires optional target");
                var optionalTypeRef = ResolveExprTypeRef(oval.Target, env);
                if (optionalTypeRef is null || !optionalTypeRef.IsOptional || optionalTypeRef.TypeArguments.Count != 1)
                    throw new CompilerException("Could not resolve optional element type", GetLine(oval.Target), GetCol(oval.Target));
                return MapType(optionalTypeRef.TypeArguments[0]);
            }
            case OptionalOrExpr oor:
                var fbType = CheckExpr(oor.Fallback, env, currentReturn);
                var optionalValueType = CheckExpr(oor.Optional, env, currentReturn);
                Require(optionalValueType == TypeSymbol.Optional, oor.Optional, "'.or' requires optional target");
                return fbType;
            case FallibleErrorExpr ferr:
                return CheckFallibleErrorExpr(ferr, env, currentReturn);
            case OnErrorExpr onError:
                return CheckOnErrorExpr(onError, env, currentReturn);
            case FieldAccessExpr fa:
            {
                if (TryResolveEnumMember(fa, env, out var enumTypeRef, out var enumValue))
                {
                    fa.ResolvedEnumTypeRef = enumTypeRef;
                    fa.ResolvedEnumValue = enumValue;
                    return TypeSymbol.Enum;
                }

                fa.ResolvedEnumTypeRef = null;
                fa.ResolvedEnumValue = null;
                fa.ResolvedFallibleErrorFieldTypeRef = null;
                var targetType = CheckExpr(fa.Target, env, currentReturn);
                if (targetType == TypeSymbol.Error)
                {
                    var targetTypeRef = ResolveExprTypeRef(fa.Target, env);
                    if (targetTypeRef is null || !targetTypeRef.IsError || targetTypeRef.TypeArguments.Count != 1)
                        throw new CompilerException("Could not resolve error value type", fa.Name.Line, fa.Name.Column);

                    if (string.Equals(fa.Name.Lexeme, "code", StringComparison.Ordinal))
                    {
                        fa.ResolvedFallibleErrorFieldTypeRef = targetTypeRef.TypeArguments[0];
                        return MapType(targetTypeRef.TypeArguments[0]);
                    }

                    if (string.Equals(fa.Name.Lexeme, "message", StringComparison.Ordinal))
                    {
                        fa.ResolvedFallibleErrorFieldTypeRef = new TypeRef("string", null, fa.Name.Line, fa.Name.Column);
                        return TypeSymbol.String;
                    }

                    throw new CompilerException($"Recoverable error has no field '{fa.Name.Lexeme}'", fa.Name.Line, fa.Name.Column);
                }
                Require(targetType == TypeSymbol.Object || targetType == TypeSymbol.Record, fa.Target, "Field access requires object or record target");
                var resolved = ResolveFieldType(fa, env);
                return resolved ?? TypeSymbol.Unknown;
            }
            case ArraySetExpr aset:
                var arrT = CheckExpr(aset.Target.Array, env, currentReturn);
                var idxT = CheckExpr(aset.Target.Index, env, currentReturn);
                var valT = CheckExpr(aset.Value, env, currentReturn);
                var collectionTypeRef = ResolveExprTypeRef(aset.Target.Array, env);
                if (collectionTypeRef is null)
                    throw new CompilerException("Could not resolve indexed collection type", GetLine(aset.Target.Array), GetCol(aset.Target.Array));

                if (arrT == TypeSymbol.Array)
                {
                    Require(IsNumeric(idxT), aset.Target.Index, "Array index must be numeric");
                }
                else if (arrT == TypeSymbol.Map)
                {
                    if (!collectionTypeRef.IsMap || collectionTypeRef.TypeArguments.Count != 2)
                        throw new CompilerException("Could not resolve map key/value types", GetLine(aset.Target.Array), GetCol(aset.Target.Array));
                    RequireAssignable(
                        MapType(collectionTypeRef.TypeArguments[0]),
                        collectionTypeRef.TypeArguments[0],
                        idxT,
                        ResolveExprTypeRef(aset.Target.Index, env),
                        GetLine(aset.Target.Index),
                        GetCol(aset.Target.Index),
                        "Map key type mismatch");
                }
                else
                {
                    throw new CompilerException("Indexing requires an array or map", GetLine(aset.Target.Array), GetCol(aset.Target.Array));
                }

                var targetElementType = CheckExpr(aset.Target, env, currentReturn);
                var targetElementTypeRef = ResolveExprTypeRef(aset.Target, env);
                if (targetElementTypeRef is null)
                    throw new CompilerException("Could not resolve indexed value type", GetLine(aset.Target.Array), GetCol(aset.Target.Array));
                var valueTypeRef = ResolveExprTypeRef(aset.Value, env);
                RequireAssignable(targetElementType, targetElementTypeRef, valT, valueTypeRef, GetLine(aset.Target), GetCol(aset.Target), "Indexed assignment type mismatch");
                return targetElementType;
            case FieldSetExpr fset:
            {
                if (TryResolveEnumMember(fset.Target, env, out _, out _))
                    throw new CompilerException("Enum members are constants and cannot be assigned", fset.Target.Name.Line, fset.Target.Name.Column);

                var targetType = CheckExpr(fset.Target.Target, env, currentReturn);
                Require(targetType == TypeSymbol.Object || targetType == TypeSymbol.Record, fset.Target.Target, "Field assignment requires object or record target");
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

                if (IsBuiltInCollection(targetType))
                    return CheckBuiltInCollectionMethodCall(mc, targetType, targetTypeRef, argTypes);

                mc.ResolvedBuiltInCollectionMethodName = null;
                Require(targetType == TypeSymbol.Object || targetType == TypeSymbol.Record || targetType == TypeSymbol.Interface, mc.Target, "Method call target must be an object, record, interface, or built-in collection");

                if (_interfaces.TryGetValue(targetTypeRef.Name, out var iface))
                {
                    if (!TryResolveBestInterfaceMethod(iface, mc.MethodName.Lexeme, argTypes, out var ifaceMethod, out bool ambiguousIface))
                    {
                        if (ambiguousIface)
                            throw new CompilerException($"Ambiguous method call '{targetTypeRef.Name}.{mc.MethodName.Lexeme}'", mc.MethodName.Line, mc.MethodName.Column);
                        throw new CompilerException($"Interface '{targetTypeRef.Name}' has no matching method overload '{mc.MethodName.Lexeme}'", mc.MethodName.Line, mc.MethodName.Column);
                    }
                    var resolvedInterfaceMethod = ifaceMethod!;

                    mc.ResolvedMethodKey = null;
                    mc.ResolvedInterfaceName = targetTypeRef.Name;
                    mc.ResolvedInterfaceMethodKey = resolvedInterfaceMethod.SignatureKey;
                    mc.ResolvedReturnTypeRef = resolvedInterfaceMethod.ReturnTypeRef;
                    return resolvedInterfaceMethod.ReturnType;
                }

                if (!_objects.TryGetValue(targetTypeRef.Name, out var obj))
                    throw new CompilerException("Could not resolve method target type", mc.MethodName.Line, mc.MethodName.Column);

                if (!TryResolveBestMethod(obj, mc.MethodName.Lexeme, argTypes, requireAccessible: true, out var method, out bool ambiguous))
                {
                    if (ambiguous)
                        throw new CompilerException($"Ambiguous method call '{targetTypeRef.Name}.{mc.MethodName.Lexeme}'", mc.MethodName.Line, mc.MethodName.Column);
                    if (TryResolveBestMethod(obj, mc.MethodName.Lexeme, argTypes, requireAccessible: false, out _, out _))
                        throw new CompilerException($"Method '{targetTypeRef.Name}.{mc.MethodName.Lexeme}' is not accessible", mc.MethodName.Line, mc.MethodName.Column);
                    throw new CompilerException($"Object '{targetTypeRef.Name}' has no matching method overload '{mc.MethodName.Lexeme}'", mc.MethodName.Line, mc.MethodName.Column);
                }

                mc.ResolvedMethodKey = method!.DispatchKey;
                mc.ResolvedInterfaceName = null;
                mc.ResolvedInterfaceMethodKey = null;
                mc.ResolvedReturnTypeRef = method.ReturnTypeRef;
                return method.ReturnType;
            }
            case Variable v:
                if (env.TryLookupForRead(v.Name, out var localType))
                {
                    v.ResolvedImplicitFieldTypeRef = null;
                    return localType;
                }
                if (TryResolveImplicitField(v.Name, env, out var fieldType, out var fieldTypeRef))
                {
                    v.ResolvedImplicitFieldTypeRef = fieldTypeRef;
                    return fieldType;
                }
                throw new CompilerException($"Undefined variable '{v.Name.Lexeme}'", v.Name.Line, v.Name.Column);
            case Assign a:
            {
                var rhs = CheckExpr(a.Value, env, currentReturn);
                var rhsTypeRef = ResolveExprTypeRef(a.Value, env);

                if (env.TryLookupForReadOrWrite(a.Name, out var lhsType, requireAssigned: false))
                {
                    a.ResolvedImplicitFieldTypeRef = null;
                    env.EnsureCanAssign(a.Name);
                    var lhsTypeRef = env.TryGetDeclaredType(a.Name);
                    RequireAssignable(lhsType, lhsTypeRef, rhs, rhsTypeRef, a.Name.Line, a.Name.Column, "Assignment type mismatch");
                    env.MarkAssigned(a.Name);
                    return lhsType;
                }

                if (TryResolveImplicitField(a.Name, env, out var implicitFieldType, out var implicitFieldTypeRef))
                {
                    a.ResolvedImplicitFieldTypeRef = implicitFieldTypeRef;
                    RequireAssignable(implicitFieldType, implicitFieldTypeRef, rhs, rhsTypeRef, a.Name.Line, a.Name.Column, "Assignment type mismatch");
                    return implicitFieldType;
                }

                throw new CompilerException($"Undefined variable '{a.Name.Lexeme}'", a.Name.Line, a.Name.Column);
            }
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
                if (c.Target is Variable variableTarget && env.Contains(variableTarget.Name.Lexeme))
                    env.MarkAssigned(variableTarget.Name);
                return targetType;
            }
            case Call c:
            {
                var argTypes = new List<(TypeSymbol Symbol, TypeRef? Ref)>(c.Arguments.Count);
                for (int i = 0; i < c.Arguments.Count; i++)
                {
                    var argType = CheckExpr(c.Arguments[i], env, currentReturn);
                    var argTypeRef = ResolveExprTypeRef(c.Arguments[i], env);
                    argTypes.Add((argType, argTypeRef));
                }

                if (_currentObjectSymbol is not null && _currentObjectTypeRef is not null && CurrentObjectHasMethodNamed(c.Callee.Lexeme))
                {
                    if (!TryResolveBestMethod(_currentObjectSymbol, c.Callee.Lexeme, argTypes, requireAccessible: true, out var method, out bool ambiguousMethod))
                    {
                        if (ambiguousMethod)
                            throw new CompilerException($"Ambiguous method call '{_currentObjectTypeRef.Name}.{c.Callee.Lexeme}'", c.Callee.Line, c.Callee.Column);
                        throw new CompilerException($"Object '{_currentObjectTypeRef.Name}' has no matching method overload '{c.Callee.Lexeme}'", c.Callee.Line, c.Callee.Column);
                    }

                    c.ResolvedImplicitMethodOwnerTypeName = _currentObjectTypeRef.Name;
                    c.ResolvedImplicitMethodKey = method!.DispatchKey;
                    c.ResolvedImplicitMethodReturnTypeRef = method.ReturnTypeRef;
                    return method.ReturnType;
                }

                if (!TryGetFunctionSignature(c.Callee.Lexeme, out var sig))
                    throw new CompilerException($"Undefined function '{c.Callee.Lexeme}'", c.Callee.Line, c.Callee.Column);
                if (sig.Params.Count != c.Arguments.Count)
                    throw new CompilerException($"Function '{c.Callee.Lexeme}' expects {sig.Params.Count} args, got {c.Arguments.Count}", c.Callee.Line, c.Callee.Column);
                for (int i = 0; i < c.Arguments.Count; i++)
                {
                    RequireAssignable(sig.Params[i], sig.ParamTypeRefs[i], argTypes[i].Symbol, argTypes[i].Ref, c.Callee.Line, c.Callee.Column, $"Argument {i} type mismatch for '{c.Callee.Lexeme}'");
                }
                c.ResolvedImplicitMethodOwnerTypeName = null;
                c.ResolvedImplicitMethodKey = null;
                c.ResolvedImplicitMethodReturnTypeRef = null;
                return sig.Return;
            }
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
                var leftRef = ResolveExprTypeRef(b.Left, env);
                var rightRef = ResolveExprTypeRef(b.Right, env);
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
                        Require(CanCompareForEquality(lt, leftRef, rt, rightRef), b.Left, "Equality requires compatible types");
                        return TypeSymbol.Boolean;
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

    private TypeSymbol CheckFallibleErrorExpr(FallibleErrorExpr expr, TypeEnvironment env, TypeSymbol? currentReturn)
    {
        if (_currentReturnTypeRef is null ||
            !_currentReturnTypeRef.IsFallible ||
            _currentReturnTypeRef.TypeArguments.Count != 2 ||
            currentReturn != TypeSymbol.Fallible)
        {
            throw new CompilerException("'error(...)' is only valid inside a function or method returning fallible<Value, ErrorCode>", expr.ErrorToken.Line, expr.ErrorToken.Column);
        }

        if (expr.Arguments.Count is < 1 or > 2)
            throw new CompilerException("error(...) expects an error code and optional message", expr.ErrorToken.Line, expr.ErrorToken.Column);

        var errorCodeTypeRef = _currentReturnTypeRef.TypeArguments[1];
        var codeType = CheckExpr(expr.Arguments[0], env, currentReturn);
        var codeTypeRef = ResolveExprTypeRef(expr.Arguments[0], env);
        if (expr.Arguments.Count == 1 && codeType == TypeSymbol.String)
            throw new CompilerException("error(message) is not supported; use error(code) or error(code, message)", GetLine(expr.Arguments[0]), GetCol(expr.Arguments[0]));

        RequireAssignable(
            MapType(errorCodeTypeRef),
            errorCodeTypeRef,
            codeType,
            codeTypeRef,
            GetLine(expr.Arguments[0]),
            GetCol(expr.Arguments[0]),
            "Error code type mismatch");

        if (expr.Arguments.Count == 2)
        {
            var messageType = CheckExpr(expr.Arguments[1], env, currentReturn);
            Require(messageType == TypeSymbol.String, expr.Arguments[1], "Error message must be a string");
        }

        expr.ResolvedFallibleTypeRef = _currentReturnTypeRef;
        return TypeSymbol.Fallible;
    }

    private TypeSymbol CheckOnErrorExpr(OnErrorExpr expr, TypeEnvironment env, TypeSymbol? currentReturn)
    {
        var fallibleType = CheckExpr(expr.Fallible, env, currentReturn);
        Require(fallibleType == TypeSymbol.Fallible, expr.Fallible, "'on error' requires a fallible value");

        var fallibleTypeRef = ResolveExprTypeRef(expr.Fallible, env);
        if (fallibleTypeRef is null || !fallibleTypeRef.IsFallible || fallibleTypeRef.TypeArguments.Count != 2)
            throw new CompilerException("Could not resolve fallible value type", GetLine(expr.Fallible), GetCol(expr.Fallible));

        var successTypeRef = fallibleTypeRef.TypeArguments[0];
        var errorCodeTypeRef = fallibleTypeRef.TypeArguments[1];
        expr.ResolvedSuccessTypeRef = successTypeRef;
        expr.ResolvedErrorCodeTypeRef = errorCodeTypeRef;

        var handlerEnv = env.CreateChild();
        handlerEnv.Define(
            "error",
            TypeSymbol.Error,
            BuildErrorValueTypeRef(errorCodeTypeRef, expr.OnToken.Line, expr.OnToken.Column),
            expr.OnToken.Line,
            expr.OnToken.Column,
            assigned: true);

        var previousYieldTypeRef = _currentYieldTypeRef;
        _currentYieldTypeRef = successTypeRef;
        bool handlerTerminates;
        try
        {
            handlerTerminates = CheckStmt(expr.Handler, handlerEnv, currentReturn);
        }
        finally
        {
            _currentYieldTypeRef = previousYieldTypeRef;
        }

        if (!handlerTerminates)
            throw new CompilerException("'on error' handler must yield a fallback value, return, or panic", expr.OnToken.Line, expr.OnToken.Column);

        return MapType(successTypeRef);
    }

    private TypeSymbol CheckBuiltInCollectionMethodCall(
        MethodCallExpr methodCall,
        TypeSymbol targetType,
        TypeRef collectionTypeRef,
        IReadOnlyList<(TypeSymbol Symbol, TypeRef? Ref)> arguments)
    {
        methodCall.ResolvedBuiltInCollectionMethodName = null;
        methodCall.ResolvedMethodKey = null;
        methodCall.ResolvedInterfaceName = null;
        methodCall.ResolvedInterfaceMethodKey = null;

        if (targetType == TypeSymbol.Array)
        {
            if (!collectionTypeRef.IsArray || collectionTypeRef.TypeArguments.Count != 1)
                throw new CompilerException("Could not resolve array element type", methodCall.MethodName.Line, methodCall.MethodName.Column);

            var elementTypeRef = collectionTypeRef.TypeArguments[0];
            var elementType = MapType(elementTypeRef);

            if (string.Equals(methodCall.MethodName.Lexeme, "append", StringComparison.Ordinal))
            {
                if (arguments.Count != 1)
                    throw new CompilerException("Array method 'append' expects 1 argument", methodCall.MethodName.Line, methodCall.MethodName.Column);
                RequireAssignable(
                    elementType,
                    elementTypeRef,
                    arguments[0].Symbol,
                    arguments[0].Ref,
                    methodCall.MethodName.Line,
                    methodCall.MethodName.Column,
                    "Array append element type mismatch");
                methodCall.ResolvedBuiltInCollectionMethodName = "append";
                methodCall.ResolvedReturnTypeRef = BuildImplicitVoidTypeRef(methodCall.MethodName);
                return TypeSymbol.Void;
            }

            if (string.Equals(methodCall.MethodName.Lexeme, "remove_at", StringComparison.Ordinal))
            {
                if (arguments.Count != 1)
                    throw new CompilerException("Array method 'remove_at' expects 1 argument", methodCall.MethodName.Line, methodCall.MethodName.Column);
                Require(IsNumeric(arguments[0].Symbol), methodCall.Arguments[0], "Array method 'remove_at' index must be numeric");
                methodCall.ResolvedBuiltInCollectionMethodName = "remove_at";
                methodCall.ResolvedReturnTypeRef = BuildImplicitVoidTypeRef(methodCall.MethodName);
                return TypeSymbol.Void;
            }

            throw new CompilerException($"Array has no method '{methodCall.MethodName.Lexeme}'", methodCall.MethodName.Line, methodCall.MethodName.Column);
        }

        if (targetType == TypeSymbol.Map)
        {
            if (!collectionTypeRef.IsMap || collectionTypeRef.TypeArguments.Count != 2)
                throw new CompilerException("Could not resolve map key/value types", methodCall.MethodName.Line, methodCall.MethodName.Column);

            var keyTypeRef = collectionTypeRef.TypeArguments[0];
            var valueTypeRef = collectionTypeRef.TypeArguments[1];
            var keyType = MapType(keyTypeRef);

            if (string.Equals(methodCall.MethodName.Lexeme, "contains", StringComparison.Ordinal))
            {
                if (arguments.Count != 1)
                    throw new CompilerException("Map method 'contains' expects 1 argument", methodCall.MethodName.Line, methodCall.MethodName.Column);
                RequireAssignable(keyType, keyTypeRef, arguments[0].Symbol, arguments[0].Ref, methodCall.MethodName.Line, methodCall.MethodName.Column, "Map key type mismatch");
                methodCall.ResolvedBuiltInCollectionMethodName = "contains";
                methodCall.ResolvedReturnTypeRef = new TypeRef("boolean", null, methodCall.MethodName.Line, methodCall.MethodName.Column);
                return TypeSymbol.Boolean;
            }

            if (string.Equals(methodCall.MethodName.Lexeme, "remove", StringComparison.Ordinal))
            {
                if (arguments.Count != 1)
                    throw new CompilerException("Map method 'remove' expects 1 argument", methodCall.MethodName.Line, methodCall.MethodName.Column);
                RequireAssignable(keyType, keyTypeRef, arguments[0].Symbol, arguments[0].Ref, methodCall.MethodName.Line, methodCall.MethodName.Column, "Map key type mismatch");
                methodCall.ResolvedBuiltInCollectionMethodName = "remove";
                methodCall.ResolvedReturnTypeRef = BuildImplicitVoidTypeRef(methodCall.MethodName);
                return TypeSymbol.Void;
            }

            throw new CompilerException($"Map has no method '{methodCall.MethodName.Lexeme}'", methodCall.MethodName.Line, methodCall.MethodName.Column);
        }

        if (targetType == TypeSymbol.Set)
        {
            if (!collectionTypeRef.IsSet || collectionTypeRef.TypeArguments.Count != 1)
                throw new CompilerException("Could not resolve set element type", methodCall.MethodName.Line, methodCall.MethodName.Column);

            var elementTypeRef = collectionTypeRef.TypeArguments[0];
            var elementType = MapType(elementTypeRef);

            if (string.Equals(methodCall.MethodName.Lexeme, "add", StringComparison.Ordinal))
            {
                if (arguments.Count != 1)
                    throw new CompilerException("Set method 'add' expects 1 argument", methodCall.MethodName.Line, methodCall.MethodName.Column);
                RequireAssignable(elementType, elementTypeRef, arguments[0].Symbol, arguments[0].Ref, methodCall.MethodName.Line, methodCall.MethodName.Column, "Set element type mismatch");
                methodCall.ResolvedBuiltInCollectionMethodName = "add";
                methodCall.ResolvedReturnTypeRef = BuildImplicitVoidTypeRef(methodCall.MethodName);
                return TypeSymbol.Void;
            }

            if (string.Equals(methodCall.MethodName.Lexeme, "contains", StringComparison.Ordinal))
            {
                if (arguments.Count != 1)
                    throw new CompilerException("Set method 'contains' expects 1 argument", methodCall.MethodName.Line, methodCall.MethodName.Column);
                RequireAssignable(elementType, elementTypeRef, arguments[0].Symbol, arguments[0].Ref, methodCall.MethodName.Line, methodCall.MethodName.Column, "Set element type mismatch");
                methodCall.ResolvedBuiltInCollectionMethodName = "contains";
                methodCall.ResolvedReturnTypeRef = new TypeRef("boolean", null, methodCall.MethodName.Line, methodCall.MethodName.Column);
                return TypeSymbol.Boolean;
            }

            if (string.Equals(methodCall.MethodName.Lexeme, "remove", StringComparison.Ordinal))
            {
                if (arguments.Count != 1)
                    throw new CompilerException("Set method 'remove' expects 1 argument", methodCall.MethodName.Line, methodCall.MethodName.Column);
                RequireAssignable(elementType, elementTypeRef, arguments[0].Symbol, arguments[0].Ref, methodCall.MethodName.Line, methodCall.MethodName.Column, "Set element type mismatch");
                methodCall.ResolvedBuiltInCollectionMethodName = "remove";
                methodCall.ResolvedReturnTypeRef = BuildImplicitVoidTypeRef(methodCall.MethodName);
                return TypeSymbol.Void;
            }

            throw new CompilerException($"Set has no method '{methodCall.MethodName.Lexeme}'", methodCall.MethodName.Line, methodCall.MethodName.Column);
        }

        if (targetType == TypeSymbol.Queue)
        {
            if (!collectionTypeRef.IsQueue || collectionTypeRef.TypeArguments.Count != 1)
                throw new CompilerException("Could not resolve queue element type", methodCall.MethodName.Line, methodCall.MethodName.Column);

            var elementTypeRef = collectionTypeRef.TypeArguments[0];
            var elementType = MapType(elementTypeRef);

            if (string.Equals(methodCall.MethodName.Lexeme, "enqueue", StringComparison.Ordinal))
            {
                if (arguments.Count != 1)
                    throw new CompilerException("Queue method 'enqueue' expects 1 argument", methodCall.MethodName.Line, methodCall.MethodName.Column);
                RequireAssignable(elementType, elementTypeRef, arguments[0].Symbol, arguments[0].Ref, methodCall.MethodName.Line, methodCall.MethodName.Column, "Queue element type mismatch");
                methodCall.ResolvedBuiltInCollectionMethodName = "enqueue";
                methodCall.ResolvedReturnTypeRef = BuildImplicitVoidTypeRef(methodCall.MethodName);
                return TypeSymbol.Void;
            }

            if (string.Equals(methodCall.MethodName.Lexeme, "dequeue", StringComparison.Ordinal) ||
                string.Equals(methodCall.MethodName.Lexeme, "peek", StringComparison.Ordinal))
            {
                if (arguments.Count != 0)
                    throw new CompilerException($"Queue method '{methodCall.MethodName.Lexeme}' expects 0 arguments", methodCall.MethodName.Line, methodCall.MethodName.Column);
                methodCall.ResolvedBuiltInCollectionMethodName = methodCall.MethodName.Lexeme;
                methodCall.ResolvedReturnTypeRef = elementTypeRef;
                return elementType;
            }

            throw new CompilerException($"Queue has no method '{methodCall.MethodName.Lexeme}'", methodCall.MethodName.Line, methodCall.MethodName.Column);
        }

        if (targetType == TypeSymbol.Stack)
        {
            if (!collectionTypeRef.IsStack || collectionTypeRef.TypeArguments.Count != 1)
                throw new CompilerException("Could not resolve stack element type", methodCall.MethodName.Line, methodCall.MethodName.Column);

            var elementTypeRef = collectionTypeRef.TypeArguments[0];
            var elementType = MapType(elementTypeRef);

            if (string.Equals(methodCall.MethodName.Lexeme, "push", StringComparison.Ordinal))
            {
                if (arguments.Count != 1)
                    throw new CompilerException("Stack method 'push' expects 1 argument", methodCall.MethodName.Line, methodCall.MethodName.Column);
                RequireAssignable(elementType, elementTypeRef, arguments[0].Symbol, arguments[0].Ref, methodCall.MethodName.Line, methodCall.MethodName.Column, "Stack element type mismatch");
                methodCall.ResolvedBuiltInCollectionMethodName = "push";
                methodCall.ResolvedReturnTypeRef = BuildImplicitVoidTypeRef(methodCall.MethodName);
                return TypeSymbol.Void;
            }

            if (string.Equals(methodCall.MethodName.Lexeme, "pop", StringComparison.Ordinal) ||
                string.Equals(methodCall.MethodName.Lexeme, "peek", StringComparison.Ordinal))
            {
                if (arguments.Count != 0)
                    throw new CompilerException($"Stack method '{methodCall.MethodName.Lexeme}' expects 0 arguments", methodCall.MethodName.Line, methodCall.MethodName.Column);
                methodCall.ResolvedBuiltInCollectionMethodName = methodCall.MethodName.Lexeme;
                methodCall.ResolvedReturnTypeRef = elementTypeRef;
                return elementType;
            }

            throw new CompilerException($"Stack has no method '{methodCall.MethodName.Lexeme}'", methodCall.MethodName.Line, methodCall.MethodName.Column);
        }

        throw new CompilerException($"Unsupported built-in collection target '{collectionTypeRef.Name}'", methodCall.MethodName.Line, methodCall.MethodName.Column);
    }

    private TypeRef InferArrayLiteralTypeRef(ArrayLiteral arrayLiteral, TypeEnvironment env, TypeSymbol? currentReturn)
    {
        if (arrayLiteral.Elements.Count == 0)
            return new TypeRef("array", [new TypeRef("integer", null, arrayLiteral.Line, arrayLiteral.Column)], arrayLiteral.Line, arrayLiteral.Column);

        var firstExpr = arrayLiteral.Elements[0];
        var currentType = CheckExpr(firstExpr, env, currentReturn);
        var currentTypeRef = ResolveExprTypeRef(firstExpr, env) ?? BuildTypeRefForSymbol(currentType, GetLine(firstExpr), GetCol(firstExpr));

        for (int i = 1; i < arrayLiteral.Elements.Count; i++)
        {
            var elementExpr = arrayLiteral.Elements[i];
            var elementType = CheckExpr(elementExpr, env, currentReturn);
            var elementTypeRef = ResolveExprTypeRef(elementExpr, env) ?? BuildTypeRefForSymbol(elementType, GetLine(elementExpr), GetCol(elementExpr));

            if (TryConversionCost(currentType, currentTypeRef, elementType, elementTypeRef, out _))
                continue;

            if (TryConversionCost(elementType, elementTypeRef, currentType, currentTypeRef, out _))
            {
                currentType = elementType;
                currentTypeRef = elementTypeRef;
                continue;
            }

            throw new CompilerException("Array literal elements must share a compatible element type", GetLine(elementExpr), GetCol(elementExpr));
        }

        return new TypeRef("array", [currentTypeRef], arrayLiteral.Line, arrayLiteral.Column);
    }

    private TypeRef BuildTypeRefForSymbol(TypeSymbol type, int line, int column)
    {
        string name = type switch
        {
            TypeSymbol.Integer => "integer",
            TypeSymbol.Whole => "whole",
            TypeSymbol.Real => "real",
            TypeSymbol.Boolean => "boolean",
            TypeSymbol.String => "string",
            TypeSymbol.Map => "map",
            TypeSymbol.Set => "set",
            TypeSymbol.Queue => "queue",
            TypeSymbol.Stack => "stack",
            TypeSymbol.Void => "void",
            _ => throw new CompilerException("Could not infer concrete type", line, column)
        };
        return new TypeRef(name, [], line, column);
    }

    private static Dictionary<string, FunctionSignature> BuildIntrinsicFunctions()
    {
        var map = new Dictionary<string, FunctionSignature>(StringComparer.Ordinal);
        foreach (var intrinsic in HostAbiCatalog.IntrinsicSignatures)
        {
            var returnType = ParseIntrinsicTypeRef(intrinsic.ReturnTypeName);
            var paramTypes = new List<TypeSymbol>(intrinsic.ParameterTypes.Count);
            var paramTypeRefs = new List<TypeRef>(intrinsic.ParameterTypeNames.Count);
            for (int i = 0; i < intrinsic.ParameterTypes.Count; i++)
            {
                paramTypes.Add(intrinsic.ParameterTypes[i]);
                paramTypeRefs.Add(ParseIntrinsicTypeRef(intrinsic.ParameterTypeNames[i]));
            }

            map[intrinsic.Name] = new FunctionSignature(
                intrinsic.ReturnType,
                returnType,
                paramTypes,
                paramTypeRefs);
        }
        return map;
    }

    private static TypeRef ParseIntrinsicTypeRef(string text)
    {
        int genericStart = text.IndexOf('<');
        if (genericStart < 0)
            return new TypeRef(text, null, 0, 0);

        if (!text.EndsWith(">", StringComparison.Ordinal))
            throw new InvalidOperationException($"Invalid intrinsic type reference '{text}'.");

        string name = text[..genericStart];
        string argsText = text[(genericStart + 1)..^1];
        var typeArguments = new List<TypeRef>();
        int depth = 0;
        int segmentStart = 0;

        for (int i = 0; i <= argsText.Length; i++)
        {
            bool atEnd = i == argsText.Length;
            char ch = atEnd ? '\0' : argsText[i];
            if (!atEnd)
            {
                if (ch == '<')
                    depth++;
                else if (ch == '>')
                    depth--;
            }

            if (atEnd || (ch == ',' && depth == 0))
            {
                string part = argsText[segmentStart..i].Trim();
                if (part.Length == 0)
                    throw new InvalidOperationException($"Invalid intrinsic type reference '{text}'.");
                typeArguments.Add(ParseIntrinsicTypeRef(part));
                segmentStart = i + 1;
            }
        }

        return new TypeRef(name, typeArguments, 0, 0);
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
            "map" => TypeSymbol.Map,
            "set" => TypeSymbol.Set,
            "queue" => TypeSymbol.Queue,
            "stack" => TypeSymbol.Stack,
            "optional" => TypeSymbol.Optional,
            "fallible" => TypeSymbol.Fallible,
            "void" => TypeSymbol.Void,
            "__error" => TypeSymbol.Error,
            _ when _enums.ContainsKey(typeRef.Name) => TypeSymbol.Enum,
            _ when _interfaces.ContainsKey(typeRef.Name) => TypeSymbol.Interface,
            _ when _objects.TryGetValue(typeRef.Name, out var objectSymbol) && objectSymbol.IsRecord => TypeSymbol.Record,
            _ => TypeSymbol.Object
        };
    }

    private bool TryResolveBestConstructor(
        ObjectSymbol obj,
        IReadOnlyList<(TypeSymbol Symbol, TypeRef? Ref)> args,
        bool requireAccessible,
        out ConstructorSignature? best,
        out bool ambiguous)
    {
        best = null;
        ambiguous = false;
        int bestCost = int.MaxValue;

        foreach (var ctor in obj.Constructors)
        {
            if (requireAccessible && !IsMemberAccessible(obj, ctor.Visibility))
                continue;
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
        bool requireAccessible,
        out MethodSignature? best,
        out bool ambiguous)
    {
        best = null;
        ambiguous = false;
        int bestCost = int.MaxValue;

        foreach (var method in obj.Methods.Values.Where(m => string.Equals(m.Name.Lexeme, methodName, StringComparison.Ordinal)))
        {
            if (requireAccessible && !IsMemberAccessible(obj, method.Visibility))
                continue;
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

    private bool IsMemberAccessible(ObjectSymbol declaringType, DeclarationVisibility visibility)
    {
        if (visibility == DeclarationVisibility.Public)
            return true;

        if (_currentObjectSymbol is not null &&
            string.Equals(_currentObjectSymbol.Name.Lexeme, declaringType.Name.Lexeme, StringComparison.Ordinal))
        {
            return true;
        }

        if (visibility == DeclarationVisibility.Package)
            return ArePackagesEqual(_currentAccessPackageName, declaringType.PackageName);

        return false;
    }

    private static bool ArePackagesEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private void RequireMemberAccessible(
        ObjectSymbol declaringType,
        DeclarationVisibility visibility,
        Token token,
        string memberKind,
        string memberName)
    {
        if (IsMemberAccessible(declaringType, visibility))
            return;

        throw new CompilerException(
            $"{Capitalize(memberKind)} '{declaringType.Name.Lexeme}.{memberName}' is not accessible",
            token.Line,
            token.Column);
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
            if (IsBuiltInCollection(expected))
                return TryCollectionConversionCost(expectedRef, actualRef, out cost);
            if (expected == TypeSymbol.Fallible)
            {
                if (actualRef is not null && SameTypeRef(expectedRef, actualRef))
                {
                    cost = 0;
                    return true;
                }

                return false;
            }
            if (expected == TypeSymbol.Enum)
            {
                if (actualRef is not null && SameTypeRef(expectedRef, actualRef))
                {
                    cost = 0;
                    return true;
                }

                return false;
            }
            if (expected == TypeSymbol.Record)
            {
                if (actualRef is not null && SameTypeRef(expectedRef, actualRef))
                {
                    cost = 0;
                    return true;
                }

                return false;
            }
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

    private bool TryCollectionConversionCost(TypeRef expectedRef, TypeRef? actualRef, out int cost)
    {
        cost = int.MaxValue;
        if (actualRef is null ||
            !expectedRef.IsBuiltInCollection ||
            !actualRef.IsBuiltInCollection ||
            !string.Equals(expectedRef.Name, actualRef.Name, StringComparison.Ordinal) ||
            expectedRef.TypeArguments.Count != actualRef.TypeArguments.Count)
            return false;

        int totalCost = 0;
        for (int i = 0; i < expectedRef.TypeArguments.Count; i++)
        {
            var expectedArgRef = expectedRef.TypeArguments[i];
            var actualArgRef = actualRef.TypeArguments[i];
            if (!TryConversionCost(
                MapType(expectedArgRef),
                expectedArgRef,
                MapType(actualArgRef),
                actualArgRef,
                out int argCost))
            {
                return false;
            }
            totalCost += argCost;
        }

        cost = totalCost;
        return true;
    }

    private bool CanCompareForEquality(TypeSymbol left, TypeRef? leftRef, TypeSymbol right, TypeRef? rightRef)
    {
        if (left == right)
        {
            if (IsBuiltInCollection(left))
                return leftRef is not null && rightRef is not null && TryCollectionConversionCost(leftRef, rightRef, out _);
            if (left == TypeSymbol.Enum)
                return leftRef is not null && rightRef is not null && SameTypeRef(leftRef, rightRef);
            if (left == TypeSymbol.Record)
                return leftRef is not null &&
                       rightRef is not null &&
                       SameTypeRef(leftRef, rightRef) &&
                       IsHashableTypeRef(leftRef);
            if (left == TypeSymbol.Optional)
            {
                if (leftRef is null || rightRef is null || !SameTypeRef(leftRef, rightRef))
                    return false;
                if (RequiresHashableRecordSemantics(leftRef))
                    return IsHashableTypeRef(leftRef);
                return true;
            }
            if (left is TypeSymbol.Object or TypeSymbol.Interface)
                return leftRef is not null && rightRef is not null && TryReferenceConversionCost(leftRef, rightRef, out _);
            if (left is TypeSymbol.Fallible or TypeSymbol.Error)
                return false;
            return left != TypeSymbol.Void;
        }

        if (IsNumeric(left) && IsNumeric(right))
            return true;

        if (leftRef is null || rightRef is null)
            return false;

        return TryConversionCost(left, leftRef, right, rightRef, out _) ||
               TryConversionCost(right, rightRef, left, leftRef, out _);
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
        if (fieldAccess.ResolvedEnumTypeRef is not null)
            return TypeSymbol.Enum;

        if (fieldAccess.ResolvedFallibleErrorFieldTypeRef is not null)
            return MapType(fieldAccess.ResolvedFallibleErrorFieldTypeRef);

        var targetType = ResolveExprTypeRef(fieldAccess.Target, env);
        if (targetType is null)
            return null;

        if (!_objects.TryGetValue(targetType.Name, out var objSymbol))
            return null;

        if (!objSymbol.Fields.TryGetValue(fieldAccess.Name.Lexeme, out var field))
            throw new CompilerException($"Object '{targetType.Name}' has no field '{fieldAccess.Name.Lexeme}'", fieldAccess.Name.Line, fieldAccess.Name.Column);

        RequireMemberAccessible(objSymbol, field.Visibility, fieldAccess.Name, "field", field.Name.Lexeme);
        return MapType(field.TypeRef);
    }

    private TypeRef? ResolveFieldTypeRef(FieldAccessExpr fieldAccess, TypeEnvironment env)
    {
        if (fieldAccess.ResolvedEnumTypeRef is not null)
            return fieldAccess.ResolvedEnumTypeRef;

        if (fieldAccess.ResolvedFallibleErrorFieldTypeRef is not null)
            return fieldAccess.ResolvedFallibleErrorFieldTypeRef;

        var targetType = ResolveExprTypeRef(fieldAccess.Target, env);
        if (targetType is null)
            return null;
        if (!_objects.TryGetValue(targetType.Name, out var objSymbol))
            return null;
        if (!objSymbol.Fields.TryGetValue(fieldAccess.Name.Lexeme, out var field))
            throw new CompilerException($"Object '{targetType.Name}' has no field '{fieldAccess.Name.Lexeme}'", fieldAccess.Name.Line, fieldAccess.Name.Column);
        RequireMemberAccessible(objSymbol, field.Visibility, fieldAccess.Name, "field", field.Name.Lexeme);
        return field.TypeRef;
    }

    private TypeRef? ResolveExprTypeRef(Expr expr, TypeEnvironment env)
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
                if (env.TryGetDeclaredType(v.Name, out var declaredType))
                    return declaredType;
                return null;
            case NewObjectExpr no:
                return new TypeRef(no.TypeName.Lexeme, null, no.TypeName.Line, no.TypeName.Column);
            case Call c:
                if (c.ResolvedImplicitMethodReturnTypeRef is not null)
                    return c.ResolvedImplicitMethodReturnTypeRef;
                if (TryGetFunctionSignature(c.Callee.Lexeme, out var sig))
                    return sig.ReturnTypeRef;
                return null;
            case FieldAccessExpr fa:
                if (fa.ResolvedFallibleErrorFieldTypeRef is not null)
                    return fa.ResolvedFallibleErrorFieldTypeRef;
                if (fa.ResolvedEnumTypeRef is not null)
                    return fa.ResolvedEnumTypeRef;
            {
                var owner = ResolveExprTypeRef(fa.Target, env);
                if (owner is null) return null;
                if (!_objects.TryGetValue(owner.Name, out var objSymbol)) return null;
                if (!objSymbol.Fields.TryGetValue(fa.Name.Lexeme, out var field))
                    throw new CompilerException($"Object '{owner.Name}' has no field '{fa.Name.Lexeme}'", fa.Name.Line, fa.Name.Column);
                RequireMemberAccessible(objSymbol, field.Visibility, fa.Name, "field", field.Name.Lexeme);
                return field.TypeRef;
            }
            case MethodCallExpr mc:
                return mc.ResolvedReturnTypeRef;
            case ArrayIndexExpr ai:
                return ai.ResolvedElementTypeRef;
            case ArraySetExpr aset:
                return ResolveExprTypeRef(aset.Target, env);
            case OptionalValueExpr oval:
            {
                var optionalTypeRef = ResolveExprTypeRef(oval.Target, env);
                if (optionalTypeRef is null || !optionalTypeRef.IsOptional || optionalTypeRef.TypeArguments.Count != 1)
                    return null;
                return optionalTypeRef.TypeArguments[0];
            }
            case OptionalOrExpr oor:
                return ResolveExprTypeRef(oor.Fallback, env);
            case FallibleErrorExpr ferr:
                return ferr.ResolvedFallibleTypeRef;
            case OnErrorExpr onError:
                return onError.ResolvedSuccessTypeRef;
            default:
                return null;
        }
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (value.Length == 1)
            return value.ToUpperInvariant();
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private (TypeSymbol Type, TypeRef? TypeRef, Token AssignmentToken) CheckCompoundAssignmentTarget(
        Expr target,
        TypeEnvironment env,
        TypeSymbol? currentReturn)
    {
        switch (target)
        {
            case Variable variable:
                if (env.TryLookupForRead(variable.Name, out var variableType))
                {
                    variable.ResolvedImplicitFieldTypeRef = null;
                    env.EnsureCanAssign(variable.Name);
                    return (variableType, env.TryGetDeclaredType(variable.Name), variable.Name);
                }

                if (TryResolveImplicitField(variable.Name, env, out var implicitFieldType, out var implicitFieldTypeRef))
                {
                    variable.ResolvedImplicitFieldTypeRef = implicitFieldTypeRef;
                    return (implicitFieldType, implicitFieldTypeRef, variable.Name);
                }

                throw new CompilerException($"Undefined variable '{variable.Name.Lexeme}'", variable.Name.Line, variable.Name.Column);
            case FieldAccessExpr fieldAccess:
                if (TryResolveEnumMember(fieldAccess, env, out _, out _))
                    throw new CompilerException("Enum members are constants and cannot be assigned", fieldAccess.Name.Line, fieldAccess.Name.Column);

            {
                var targetType = CheckExpr(fieldAccess.Target, env, currentReturn);
                Require(targetType == TypeSymbol.Object || targetType == TypeSymbol.Record, fieldAccess.Target, "Field access requires object or record target");
                return (
                    ResolveFieldType(fieldAccess, env) ?? TypeSymbol.Unknown,
                    ResolveFieldTypeRef(fieldAccess, env),
                    fieldAccess.Name);
            }
            case ArrayIndexExpr arrayIndex:
            {
                var arrayType = CheckExpr(arrayIndex.Array, env, currentReturn);
                var indexType = CheckExpr(arrayIndex.Index, env, currentReturn);
                var elementType = CheckExpr(arrayIndex, env, currentReturn);
                var elementTypeRef = ResolveExprTypeRef(arrayIndex, env);
                if (elementTypeRef is null)
                    throw new CompilerException("Could not resolve indexed value type", GetLine(arrayIndex.Array), GetCol(arrayIndex.Array));
                Require(arrayType == TypeSymbol.Array || arrayType == TypeSymbol.Map, arrayIndex.Array, "Indexing requires an array or map");
                return (
                    elementType,
                    elementTypeRef,
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

            case "__error":
                if (typeRef.TypeArguments.Count != 1)
                    throw new CompilerException("Internal error value type expects exactly one type argument", typeRef.Line, typeRef.Column);
                ValidateTypeRef(typeRef.TypeArguments[0]);
                return;

            case "array":
            case "optional":
                if (typeRef.TypeArguments.Count != 1)
                    throw new CompilerException($"Type '{typeRef.Name}' expects exactly one type argument", typeRef.Line, typeRef.Column);
                ValidateTypeRef(typeRef.TypeArguments[0]);
                return;

            case "fallible":
                if (typeRef.TypeArguments.Count != 2)
                    throw new CompilerException("Type 'fallible' expects exactly two type arguments", typeRef.Line, typeRef.Column);
                ValidateTypeRef(typeRef.TypeArguments[0]);
                ValidateTypeRef(typeRef.TypeArguments[1]);
                EnsureNotVoidTypeRef(typeRef.TypeArguments[0], "fallible success type cannot be void", typeRef.TypeArguments[0].Line, typeRef.TypeArguments[0].Column);
                var errorType = MapType(typeRef.TypeArguments[1]);
                if (errorType is not TypeSymbol.Enum and not TypeSymbol.Integer)
                    throw new CompilerException("fallible error code type must be an enum or integer", typeRef.TypeArguments[1].Line, typeRef.TypeArguments[1].Column);
                return;

            case "set":
            case "queue":
            case "stack":
                if (typeRef.TypeArguments.Count != 1)
                    throw new CompilerException($"Type '{typeRef.Name}' expects exactly one type argument", typeRef.Line, typeRef.Column);
                ValidateTypeRef(typeRef.TypeArguments[0]);
                if (typeRef.Name == "set" &&
                    RequiresHashableRecordSemantics(typeRef.TypeArguments[0]) &&
                    !IsHashableTypeRef(typeRef.TypeArguments[0]))
                {
                    throw new CompilerException("Set elements that use record value types must be hashable", typeRef.Line, typeRef.Column);
                }
                return;

            case "map":
                if (typeRef.TypeArguments.Count != 2)
                    throw new CompilerException($"Type '{typeRef.Name}' expects exactly two type arguments", typeRef.Line, typeRef.Column);
                ValidateTypeRef(typeRef.TypeArguments[0]);
                ValidateTypeRef(typeRef.TypeArguments[1]);
                if (RequiresHashableRecordSemantics(typeRef.TypeArguments[0]) &&
                    !IsHashableTypeRef(typeRef.TypeArguments[0]))
                {
                    throw new CompilerException("Map keys that use record value types must be hashable", typeRef.Line, typeRef.Column);
                }
                return;

            default:
                if (typeRef.TypeArguments.Count > 0)
                    throw new CompilerException($"Type '{typeRef.Name}' does not support type arguments yet", typeRef.Line, typeRef.Column);
                if (!_objects.ContainsKey(typeRef.Name) && !_interfaces.ContainsKey(typeRef.Name) && !_enums.ContainsKey(typeRef.Name))
                    throw new CompilerException($"Unknown type '{typeRef.Name}'", typeRef.Line, typeRef.Column);
                return;
        }
    }

    private bool RequiresHashableRecordSemantics(TypeRef typeRef)
    {
        if (_objects.TryGetValue(typeRef.Name, out var symbol) && symbol.IsRecord)
            return true;

        return string.Equals(typeRef.Name, "optional", StringComparison.Ordinal) &&
               typeRef.TypeArguments.Count == 1 &&
               RequiresHashableRecordSemantics(typeRef.TypeArguments[0]);
    }

    private bool IsHashableTypeRef(TypeRef typeRef)
    {
        return IsHashableTypeRef(typeRef, new HashSet<string>(StringComparer.Ordinal));
    }

    private bool IsHashableTypeRef(TypeRef typeRef, HashSet<string> visitingRecords)
    {
        switch (typeRef.Name)
        {
            case "whole":
            case "integer":
            case "real":
            case "boolean":
            case "string":
                return true;
            case "optional":
                return typeRef.TypeArguments.Count == 1 && IsHashableTypeRef(typeRef.TypeArguments[0], visitingRecords);
        }

        if (_enums.ContainsKey(typeRef.Name))
            return true;

        if (!_objects.TryGetValue(typeRef.Name, out var objectSymbol) || !objectSymbol.IsRecord)
            return false;

        if (!visitingRecords.Add(typeRef.Name))
            return true;

        try
        {
            foreach (var field in objectSymbol.Fields.Values)
            {
                if (!IsHashableTypeRef(field.TypeRef, visitingRecords))
                    return false;
            }

            return true;
        }
        finally
        {
            visitingRecords.Remove(typeRef.Name);
        }
    }

    private static TypeRef BuildImplicitVoidTypeRef(Token origin)
    {
        return new TypeRef("void", null, origin.Line, origin.Column);
    }

    private static TypeRef BuildErrorValueTypeRef(TypeRef errorCodeTypeRef, int line, int column)
    {
        return new TypeRef("__error", [errorCodeTypeRef], line, column);
    }

    private static void EnsureNotVoidTypeRef(TypeRef typeRef, string message, int line, int col)
    {
        if (string.Equals(typeRef.Name, "void", StringComparison.Ordinal))
            throw new CompilerException(message, line, col);
    }

    private static bool IsNumeric(TypeSymbol t) => t is TypeSymbol.Integer or TypeSymbol.Whole or TypeSymbol.Real;
    private static bool IsBuiltInCollection(TypeSymbol t) => t is TypeSymbol.Array or TypeSymbol.Map or TypeSymbol.Set or TypeSymbol.Queue or TypeSymbol.Stack;
    private static bool IsReservedBuiltInTypeName(string name) => name is "map" or "set" or "queue" or "stack" or "fallible";

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

        if (target == TypeSymbol.Record)
        {
            if (targetRef is not null && valueRef is not null && SameTypeRef(targetRef, valueRef))
                return;
            throw new CompilerException(message, line, col);
        }

        if (target == TypeSymbol.Enum)
        {
            if (targetRef is not null && valueRef is not null && SameTypeRef(targetRef, valueRef))
                return;
            throw new CompilerException(message, line, col);
        }

        if (target == TypeSymbol.Fallible)
        {
            if (targetRef is not null && valueRef is not null && SameTypeRef(targetRef, valueRef))
                return;
            throw new CompilerException(message, line, col);
        }

        if (IsBuiltInCollection(target))
        {
            if (targetRef is not null && valueRef is not null && TryCollectionConversionCost(targetRef, valueRef, out _))
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

    private bool IsCompatibleInterfaceReturn(InterfaceMethodSignature ifaceMethod, MethodSignature objectMethod)
    {
        return TryConversionCost(
            ifaceMethod.ReturnType,
            ifaceMethod.ReturnTypeRef,
            objectMethod.ReturnType,
            objectMethod.ReturnTypeRef,
            out _);
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
        FallibleErrorExpr e => e.ErrorToken.Line,
        OnErrorExpr e => GetLine(e.Fallible),
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
        FallibleErrorExpr e => e.ErrorToken.Column,
        OnErrorExpr e => GetCol(e.Fallible),
        Unary u => GetCol(u.Right),
        Binary b => GetCol(b.Left),
        _ => 0
    };

    private static int GetStmtLine(Stmt stmt) => stmt switch
    {
        ReturnStmt r when r.Value is Expr e => GetLine(e),
        SwitchStmt s => s.Keyword.Line,
        YieldStmt y => y.Keyword.Line,
        ReturnStmt => 0,
        _ => 0
    };

    private static int GetStmtCol(Stmt stmt) => stmt switch
    {
        ReturnStmt r when r.Value is Expr e => GetCol(e),
        SwitchStmt s => s.Keyword.Column,
        YieldStmt y => y.Keyword.Column,
        ReturnStmt => 0,
        _ => 0
    };

    private sealed record FunctionSignature(
        TypeSymbol Return,
        TypeRef ReturnTypeRef,
        IList<TypeSymbol> Params,
        IReadOnlyList<TypeRef> ParamTypeRefs);
    private sealed record FieldSignature(
        Token Name,
        TypeRef TypeRef,
        DeclarationVisibility Visibility);
    private sealed record ConstructorSignature(
        Token Keyword,
        IList<TypeSymbol> Params,
        IReadOnlyList<TypeRef> ParamTypeRefs,
        string DispatchKey,
        Block Body,
        DeclarationVisibility Visibility);
    private sealed record MethodSignature(
        Token Name,
        TypeRef ReturnTypeRef,
        TypeSymbol ReturnType,
        IList<TypeSymbol> ParamTypes,
        IReadOnlyList<TypeRef> ParamTypeRefs,
        string DispatchKey,
        Block Body,
        IReadOnlyList<Parameter> Parameters,
        DeclarationVisibility Visibility);
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
    private sealed record EnumSymbol(
        Token Name,
        Dictionary<string, int> Members);
    private sealed record ObjectSymbol(
        Token Name,
        bool IsRecord,
        string? PackageName,
        string? ModulePath,
        Dictionary<string, FieldSignature> Fields,
        List<ConstructorSignature> Constructors,
        Dictionary<string, MethodSignature> Methods);

    private bool TryResolveEnumMember(FieldAccessExpr fieldAccess, TypeEnvironment env, out TypeRef? enumTypeRef, out int value)
    {
        enumTypeRef = null;
        value = 0;

        if (fieldAccess.Target is not Variable variableTarget)
            return false;

        if (env.Contains(variableTarget.Name.Lexeme))
            return false;

        if (!_enums.TryGetValue(variableTarget.Name.Lexeme, out var enumSymbol))
            return false;

        if (!enumSymbol.Members.TryGetValue(fieldAccess.Name.Lexeme, out value))
        {
            throw new CompilerException(
                $"Enum '{enumSymbol.Name.Lexeme}' has no member '{fieldAccess.Name.Lexeme}'",
                fieldAccess.Name.Line,
                fieldAccess.Name.Column);
        }

        enumTypeRef = new TypeRef(enumSymbol.Name.Lexeme, null, variableTarget.Name.Line, variableTarget.Name.Column);
        return true;
    }

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

        public bool TryLookupForRead(Token name, out TypeSymbol type)
        {
            if (!TryFind(name.Lexeme, out var info))
            {
                type = TypeSymbol.Unknown;
                return false;
            }

            if (!info.assigned)
                throw new CompilerException($"Variable '{name.Lexeme}' is used before being assigned", name.Line, name.Column);

            type = info.type;
            return true;
        }

        public TypeSymbol LookupForReadOrWrite(Token name, bool requireAssigned = true)
        {
            var info = Find(name);
            if (requireAssigned && !info.assigned)
                throw new CompilerException($"Variable '{name.Lexeme}' is used before being assigned", name.Line, name.Column);
            return info.type;
        }

        public bool TryLookupForReadOrWrite(Token name, out TypeSymbol type, bool requireAssigned = true)
        {
            if (!TryFind(name.Lexeme, out var info))
            {
                type = TypeSymbol.Unknown;
                return false;
            }

            if (requireAssigned && !info.assigned)
                throw new CompilerException($"Variable '{name.Lexeme}' is used before being assigned", name.Line, name.Column);

            type = info.type;
            return true;
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

        public bool Contains(string name)
        {
            return TryFind(name, out _);
        }

        public TypeRef? TryGetDeclaredType(Token name)
        {
            var info = Find(name);
            return info.declaredType;
        }

        public bool TryGetDeclaredType(Token name, out TypeRef? declaredType)
        {
            if (!TryFind(name.Lexeme, out var info))
            {
                declaredType = null;
                return false;
            }

            declaredType = info.declaredType;
            return true;
        }

        private bool TryFind(string name, out VarInfo info)
        {
            if (_vars.TryGetValue(name, out info))
                return true;
            if (_parent is not null)
                return _parent.TryFind(name, out info);
            info = default;
            return false;
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
