using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleApp1.Compiler;

sealed class TypeChecker
{
    private readonly Dictionary<string, FunctionSignature> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ObjectSymbol> _objects = new(StringComparer.Ordinal);

    public void Check(IList<Stmt> statements)
    {
        // Collect object names first to allow forward references in field types.
        foreach (var stmt in statements)
        {
            if (stmt is not ObjectDecl obj)
                continue;

            if (_objects.ContainsKey(obj.Name.Lexeme))
                throw new CompilerException($"Object '{obj.Name.Lexeme}' already defined", obj.Name.Line, obj.Name.Column);
            _objects[obj.Name.Lexeme] = new ObjectSymbol(
                obj.Name,
                new Dictionary<string, TypeRef>(StringComparer.Ordinal),
                new List<ConstructorSignature>(),
                new Dictionary<string, MethodSignature>(StringComparer.Ordinal));
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
                symbol.Fields[field.Name.Lexeme] = field.Type;
            }

            var ctorArities = new HashSet<int>();
            foreach (var ctor in obj.Constructors)
            {
                if (!ctorArities.Add(ctor.Parameters.Count))
                    throw new CompilerException($"Constructor overload with {ctor.Parameters.Count} parameters is already defined in object '{obj.Name.Lexeme}'", ctor.Keyword.Line, ctor.Keyword.Column);

                var paramTypes = new List<TypeSymbol>(ctor.Parameters.Count);
                foreach (var param in ctor.Parameters)
                {
                    if (param.Type is null)
                        throw new CompilerException("Constructor parameters must be typed", param.Name.Line, param.Name.Column);
                    ValidateTypeRef(param.Type);
                    paramTypes.Add(MapType(param.Type));
                }
                symbol.Constructors.Add(new ConstructorSignature(ctor.Keyword, paramTypes, ctor.Body));
            }

            var methodKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in obj.Methods)
            {
                if (method.ReturnType is null)
                    throw new CompilerException($"Method '{method.Name.Lexeme}' is missing a return type", method.Name.Line, method.Name.Column);
                if (method.Parameters.Any(p => p.Type is null))
                    throw new CompilerException($"Method '{method.Name.Lexeme}' has untyped parameters", method.Name.Line, method.Name.Column);

                string methodKey = MethodKey(method.Name.Lexeme, method.Parameters.Count);
                if (!methodKeys.Add(methodKey))
                    throw new CompilerException($"Method overload '{method.Name.Lexeme}' with {method.Parameters.Count} parameters is already defined in object '{obj.Name.Lexeme}'", method.Name.Line, method.Name.Column);

                var paramTypes = method.Parameters.Select(p => MapType(p.Type!)).ToList();
                var returnType = MapType(method.ReturnType);
                symbol.Methods[methodKey] = new MethodSignature(method.Name, method.ReturnType, returnType, paramTypes, method.Body, method.Parameters);
            }

            if (symbol.Fields.Count > 0 && symbol.Constructors.Count == 0)
            {
                throw new CompilerException($"Object '{obj.Name.Lexeme}' declares fields but has no constructor to initialize them", obj.Name.Line, obj.Name.Column);
            }
        }

        // Collect function signatures.
        foreach (var stmt in statements)
        {
            if (stmt is FunctionDecl fn)
            {
                if (fn.ReturnType is null)
                    throw new CompilerException($"Function '{fn.Name.Lexeme}' is missing a return type", fn.Name.Line, fn.Name.Column);
                if (fn.Parameters.Any(p => p.Type is null))
                    throw new CompilerException($"Function '{fn.Name.Lexeme}' has untyped parameters", fn.Name.Line, fn.Name.Column);
                if (_functions.ContainsKey(fn.Name.Lexeme))
                    throw new CompilerException($"Function '{fn.Name.Lexeme}' already defined", fn.Name.Line, fn.Name.Column);
                var sig = new FunctionSignature(
                    Return: MapType(fn.ReturnType),
                    Params: fn.Parameters.Select(p => MapType(p.Type!)).ToList()
                );
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
            else if (stmt is ObjectDecl)
            {
                // Object declarations are compile-time metadata for now.
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
        var retType = MapType(fn.ReturnType!);
        // params occupy env
        for (int i = 0; i < fn.Parameters.Count; i++)
        {
            var param = fn.Parameters[i];
            var pType = MapType(param.Type!);
            env.Define(param.Name.Lexeme, pType, param.Type, param.Name.Line, param.Name.Column, assigned: true);
        }
        bool allPathsReturn = CheckStmt(fn.Body, env, retType);
        if (!allPathsReturn)
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
            CheckStmt(ctorSig.Body, env, currentReturn: null);
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

            for (int i = 0; i < method.Parameters.Count; i++)
            {
                var paramDecl = method.Parameters[i];
                var pType = method.ParamTypes[i];
                env.Define(paramDecl.Name.Lexeme, pType, paramDecl.Type, paramDecl.Name.Line, paramDecl.Name.Column, assigned: true);
            }

            bool allPathsReturn = CheckStmt(method.Body, env, method.ReturnType);
            if (!allPathsReturn)
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
                bool hasInit = v.Initializer is not null;
                if (v.Initializer is not null)
                {
                    var init = CheckExpr(v.Initializer, env, currentReturn);
                    RequireAssignable(t, init, v.Type.Line, v.Type.Column, "Initializer type mismatch");
                }
                bool assignedFlag = hasInit || t == TypeSymbol.Optional;
                env.Define(v.Name.Lexeme, t, v.Type, v.Name.Line, v.Name.Column, assignedFlag);
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
                var rval = r.Value is null ? TypeSymbol.Integer : CheckExpr(r.Value, env, currentReturn);
                RequireAssignable(currentReturn.Value, rval, GetStmtLine(r), GetStmtCol(r), "Return type mismatch");
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

                var ctor = obj.Constructors.FirstOrDefault(c => c.Params.Count == no.Arguments.Count);
                if (ctor is null)
                {
                    if (obj.Constructors.Count == 0 && no.Arguments.Count == 0)
                        return TypeSymbol.Object;
                    throw new CompilerException(
                        $"No constructor for '{no.TypeName.Lexeme}' takes {no.Arguments.Count} argument(s)",
                        no.TypeName.Line,
                        no.TypeName.Column);
                }

                for (int i = 0; i < no.Arguments.Count; i++)
                {
                    var argType = CheckExpr(no.Arguments[i], env, currentReturn);
                    RequireAssignable(ctor.Params[i], argType, no.TypeName.Line, no.TypeName.Column, $"Constructor argument {i} type mismatch");
                }

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
                    RequireAssignable(expected, rhsType, fset.Target.Name.Line, fset.Target.Name.Column, "Field assignment type mismatch");
                }
                return rhsType;
            }
            case MethodCallExpr mc:
            {
                var targetType = CheckExpr(mc.Target, env, currentReturn);
                Require(targetType == TypeSymbol.Object, mc.Target, "Method call target must be an object");
                var targetTypeRef = ResolveExprTypeRef(mc.Target, env);
                if (targetTypeRef is null || !_objects.TryGetValue(targetTypeRef.Name, out var obj))
                    throw new CompilerException("Could not resolve method target type", mc.MethodName.Line, mc.MethodName.Column);

                string key = MethodKey(mc.MethodName.Lexeme, mc.Arguments.Count);
                if (!obj.Methods.TryGetValue(key, out var method))
                    throw new CompilerException($"Object '{targetTypeRef.Name}' has no method '{mc.MethodName.Lexeme}' with {mc.Arguments.Count} arguments", mc.MethodName.Line, mc.MethodName.Column);

                for (int i = 0; i < mc.Arguments.Count; i++)
                {
                    var argType = CheckExpr(mc.Arguments[i], env, currentReturn);
                    RequireAssignable(method.ParamTypes[i], argType, mc.MethodName.Line, mc.MethodName.Column, $"Method argument {i} type mismatch");
                }
                return method.ReturnType;
            }
            case Variable v:
                return env.LookupForRead(v.Name);
            case Assign a:
                var rhs = CheckExpr(a.Value, env, currentReturn);
                var lhsType = env.LookupForReadOrWrite(a.Name, requireAssigned: false);
                RequireAssignable(lhsType, rhs, a.Name.Line, a.Name.Column, "Assignment type mismatch");
                env.MarkAssigned(a.Name);
                return lhsType;
            case Call c:
                if (!_functions.TryGetValue(c.Callee.Lexeme, out var sig))
                    throw new CompilerException($"Undefined function '{c.Callee.Lexeme}'", c.Callee.Line, c.Callee.Column);
                if (sig.Params.Count != c.Arguments.Count)
                    throw new CompilerException($"Function '{c.Callee.Lexeme}' expects {sig.Params.Count} args, got {c.Arguments.Count}", c.Callee.Line, c.Callee.Column);
                for (int i = 0; i < c.Arguments.Count; i++)
                {
                    var argType = CheckExpr(c.Arguments[i], env, currentReturn);
                    RequireAssignable(sig.Params[i], argType, c.Callee.Line, c.Callee.Column, $"Argument {i} type mismatch for '{c.Callee.Lexeme}'");
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
            _ => TypeSymbol.Object
        };
    }

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

    private TypeRef? ResolveExprTypeRef(Expr expr, TypeEnvironment env)
    {
        switch (expr)
        {
            case Variable v:
                return env.TryGetDeclaredType(v.Name);
            case NewObjectExpr no:
                return new TypeRef(no.TypeName.Lexeme, null, no.TypeName.Line, no.TypeName.Column);
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
            {
                var owner = ResolveExprTypeRef(mc.Target, env);
                if (owner is null) return null;
                if (!_objects.TryGetValue(owner.Name, out var objSymbol)) return null;
                string key = MethodKey(mc.MethodName.Lexeme, mc.Arguments.Count);
                if (!objSymbol.Methods.TryGetValue(key, out var method)) return null;
                return method.ReturnType == TypeSymbol.Object ? method.ReturnTypeRef : null;
            }
            default:
                return null;
        }
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
                if (!_objects.ContainsKey(typeRef.Name))
                    throw new CompilerException($"Unknown type '{typeRef.Name}'", typeRef.Line, typeRef.Column);
                return;
        }
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

    private static void RequireAssignable(TypeSymbol target, TypeSymbol value, int line, int col, string message)
    {
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

    private static string MethodKey(string name, int arity) => $"{name}#{arity}";

    private static int GetLine(Expr expr) => expr switch
    {
        Literal => 0,
        Variable v => v.Name.Line,
        Assign a => a.Name.Line,
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

    private sealed record FunctionSignature(TypeSymbol Return, IList<TypeSymbol> Params);
    private sealed record ConstructorSignature(Token Keyword, IList<TypeSymbol> Params, Block Body);
    private sealed record MethodSignature(
        Token Name,
        TypeRef ReturnTypeRef,
        TypeSymbol ReturnType,
        IList<TypeSymbol> ParamTypes,
        Block Body,
        IReadOnlyList<Parameter> Parameters);
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

        public void Define(string name, TypeSymbol type, TypeRef? declaredType, int line, int col, bool assigned)
        {
            if (_vars.ContainsKey(name))
                throw new CompilerException($"'{name}' already defined in scope", line, col);
            _vars[name] = new VarInfo(type, declaredType, assigned, line, col);
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

        private record struct VarInfo(TypeSymbol type, TypeRef? declaredType, bool assigned, int line, int col);
    }
}
