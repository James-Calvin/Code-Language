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
                if (fn.Parameters.Any(p => p.TypeToken is null))
                    throw new CompilerException($"Function '{fn.Name.Lexeme}' has untyped parameters", fn.Name.Line, fn.Name.Column);
                if (_functions.ContainsKey(fn.Name.Lexeme))
                    throw new CompilerException($"Function '{fn.Name.Lexeme}' already defined", fn.Name.Line, fn.Name.Column);
                var sig = new FunctionSignature(
                    Return: MapType(fn.ReturnType),
                    Params: fn.Parameters.Select(p => MapType(p.TypeToken!)).ToList()
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
            var pType = MapType(param.TypeToken!);
            env.Define(param.Name.Lexeme, pType, param.Name.Line, param.Name.Column);
        }
        CheckStmt(fn.Body, env, retType);
    }

    private void CheckStmt(Stmt stmt, TypeEnvironment env, TypeSymbol? currentReturn)
    {
        switch (stmt)
        {
            case VarDecl v:
                var t = MapType(v.TypeToken);
                if (v.Initializer is not null)
                {
                    var init = CheckExpr(v.Initializer, env, currentReturn);
                    RequireAssignable(t, init, v.TypeToken.Line, v.TypeToken.Column, "Initializer type mismatch");
                }
                env.Define(v.Name.Lexeme, t, v.Name.Line, v.Name.Column);
                break;

            case ExprStmt e:
                CheckExpr(e.Expression, env, currentReturn);
                break;

            case Block b:
                env = env.CreateChild();
                foreach (var s in b.Statements) CheckStmt(s, env, currentReturn);
                break;

            case IfStmt i:
                var cond = CheckExpr(i.Condition, env, currentReturn);
                Require(cond == TypeSymbol.Boolean, i.Condition, "Condition must be boolean");
                CheckStmt(i.ThenBranch, env.CreateChild(), currentReturn);
                if (i.ElseBranch is not null) CheckStmt(i.ElseBranch, env.CreateChild(), currentReturn);
                break;

            case WhileStmt w:
                var cType = CheckExpr(w.Condition, env, currentReturn);
                Require(cType == TypeSymbol.Boolean, w.Condition, "Condition must be boolean");
                CheckStmt(w.Body, env.CreateChild(), currentReturn);
                break;

            case ForStmt f:
                var forEnv = env.CreateChild();
                if (f.Initializer is not null) CheckStmt(f.Initializer, forEnv, currentReturn);
                var condType = CheckExpr(f.Condition, forEnv, currentReturn);
                Require(condType == TypeSymbol.Boolean || IsNumeric(condType), f.Condition, "For condition must be boolean or numeric comparison");
                if (f.Increment is not null) CheckExpr(f.Increment, forEnv, currentReturn);
                CheckStmt(f.Body, forEnv.CreateChild(), currentReturn);
                break;

            case ForeachStmt fe:
                var iterType = CheckExpr(fe.Iterable, env, currentReturn);
                Require(IsNumeric(iterType), fe.Iterable, "foreach iterable must be numeric (count)");
                var feEnv = env.CreateChild();
                feEnv.Define(fe.Iterator.Lexeme, TypeSymbol.Integer, fe.Iterator.Line, fe.Iterator.Column);
                CheckStmt(fe.Body, feEnv, currentReturn);
                break;

            case ReturnStmt r:
                if (currentReturn is null)
                    throw new CompilerException("Return outside function", r is { Value: { } val } ? val is Literal lit ? 0 : 0 : 0, 0);
                var rval = r.Value is null ? TypeSymbol.Integer : CheckExpr(r.Value, env, currentReturn);
                RequireAssignable(currentReturn.Value, rval, r is { Value: { } vexpr } ? vexpr is Literal lit2 ? 0 : 0 : 0, r is { Value: { } vexpr2 } ? 0 : 0, "Return type mismatch");
                break;

            case PrintStmt p:
                CheckExpr(p.Value, env, currentReturn);
                break;

            case FunctionDecl:
                // handled earlier
                break;

            default:
                throw new CompilerException($"Unhandled statement in type checker: {stmt.GetType().Name}", 0, 0);
        }
    }

    private TypeSymbol CheckExpr(Expr expr, TypeEnvironment env, TypeSymbol? currentReturn)
    {
        switch (expr)
        {
            case Literal:
                return TypeSymbol.Integer; // all numeric literals treated as integer for now
            case Variable v:
                return env.Lookup(v.Name);
            case Assign a:
                var rhs = CheckExpr(a.Value, env, currentReturn);
                var lhsType = env.Lookup(a.Name);
                RequireAssignable(lhsType, rhs, a.Name.Line, a.Name.Column, "Assignment type mismatch");
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
                    case TokenType.Minus:
                    case TokenType.Star:
                    case TokenType.Slash:
                        Require(IsNumeric(lt) && IsNumeric(rt), b.Left, "Arithmetic requires numeric");
                        return lt; // simplistic
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

    private static TypeSymbol MapType(Token typeToken) => typeToken.Type switch
    {
        TokenType.Integer => TypeSymbol.Integer,
        TokenType.Whole => TypeSymbol.Whole,
        TokenType.Real => TypeSymbol.Real,
        TokenType.Boolean => TypeSymbol.Boolean,
        _ => TypeSymbol.Unknown
    };

    private static bool IsNumeric(TypeSymbol t) => t is TypeSymbol.Integer or TypeSymbol.Whole or TypeSymbol.Real;

    private static void Require(bool condition, Expr expr, string message)
    {
        if (!condition) throw new CompilerException(message, GetLine(expr), GetCol(expr));
    }

    private static void RequireAssignable(TypeSymbol target, TypeSymbol value, int line, int col, string message)
    {
        if (target == value) return;
        throw new CompilerException(message, line, col);
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

    private sealed record FunctionSignature(TypeSymbol Return, IList<TypeSymbol> Params);

    private sealed class TypeEnvironment
    {
        private readonly Dictionary<string, TypeSymbol> _vars = new(StringComparer.Ordinal);
        private readonly TypeEnvironment? _parent;
        public TypeEnvironment(TypeEnvironment? parent = null) => _parent = parent;
        public TypeEnvironment CreateChild() => new(this);
        public void Define(string name, TypeSymbol type, int line, int col)
        {
            if (_vars.ContainsKey(name))
                throw new CompilerException($"'{name}' already defined in scope", line, col);
            _vars[name] = type;
        }
        public TypeSymbol Lookup(Token name)
        {
            if (_vars.TryGetValue(name.Lexeme, out var t)) return t;
            return _parent?.Lookup(name) ?? throw new CompilerException($"Undefined variable '{name.Lexeme}'", name.Line, name.Column);
        }
    }
}
