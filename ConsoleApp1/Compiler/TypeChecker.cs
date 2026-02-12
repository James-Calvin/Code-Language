using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleApp1.Compiler;

sealed class TypeChecker
{
    private readonly Dictionary<string, FunctionSignature> _functions = new(StringComparer.Ordinal);

    public void Check(IList<Stmt> statements)
    {
        // Collect function signatures
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

        var global = new TypeEnvironment();

        // Type-check top-level statements
        foreach (var stmt in statements)
        {
            if (stmt is FunctionDecl fn)
            {
                CheckFunction(fn);
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
            env.Define(param.Name.Lexeme, pType, param.Name.Line, param.Name.Column, assigned: true);
        }
        bool allPathsReturn = CheckStmt(fn.Body, env, retType);
        if (!allPathsReturn)
            throw new CompilerException($"Function '{fn.Name.Lexeme}' may not return a value on all paths", fn.Name.Line, fn.Name.Column);
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
                env.Define(v.Name.Lexeme, t, v.Name.Line, v.Name.Column, assignedFlag);
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
                feEnv.Define(fe.Iterator.Lexeme, TypeSymbol.Integer, fe.Iterator.Line, fe.Iterator.Column, assigned: true);
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
            case ArraySetExpr aset:
                var arrT = CheckExpr(aset.Target.Array, env, currentReturn);
                Require(arrT == TypeSymbol.Array, aset.Target.Array, "Indexing requires an array");
                var idxT = CheckExpr(aset.Target.Index, env, currentReturn);
                Require(IsNumeric(idxT), aset.Target.Index, "Array index must be numeric");
                var valT = CheckExpr(aset.Value, env, currentReturn);
                return valT;
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

    private static TypeSymbol MapType(TypeRef typeRef)
    {
        if (typeRef.Name == "array")
        {
            if (typeRef.TypeArguments.Count != 1)
                throw new CompilerException("array<T> expects exactly one type argument", typeRef.Line, typeRef.Column);
            return TypeSymbol.Array;
        }
        if (typeRef.Name == "optional")
        {
            if (typeRef.TypeArguments.Count != 1)
                throw new CompilerException("optional<T> expects exactly one type argument", typeRef.Line, typeRef.Column);
            return TypeSymbol.Optional;
        }
        if (typeRef.TypeArguments.Count > 0)
            throw new CompilerException($"Type '{typeRef.Name}' does not support type arguments yet", typeRef.Line, typeRef.Column);

        return typeRef.Name switch
        {
            "integer" => TypeSymbol.Integer,
            "whole" => TypeSymbol.Whole,
            "real" => TypeSymbol.Real,
            "boolean" => TypeSymbol.Boolean,
            "string" => TypeSymbol.String,
            _ => throw new CompilerException($"Unknown type '{typeRef.Name}'", typeRef.Line, typeRef.Column)
        };
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

    private static int GetLine(Expr expr) => expr switch
    {
        Literal => 0,
        Variable v => v.Name.Line,
        Assign a => a.Name.Line,
        Call c => c.Callee.Line,
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

    private sealed class TypeEnvironment
    {
        private readonly Dictionary<string, VarInfo> _vars = new(StringComparer.Ordinal);
        private readonly TypeEnvironment? _parent;
        public TypeEnvironment(TypeEnvironment? parent = null) => _parent = parent;
        public TypeEnvironment CreateChild() => new(this);

        public void Define(string name, TypeSymbol type, int line, int col, bool assigned)
        {
            if (_vars.ContainsKey(name))
                throw new CompilerException($"'{name}' already defined in scope", line, col);
            _vars[name] = new VarInfo(type, assigned, line, col);
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

        private record struct VarInfo(TypeSymbol type, bool assigned, int line, int col);
    }
}
