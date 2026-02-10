using System;
using System.Collections.Generic;

namespace ConsoleApp1.Compiler;

sealed class CodeGenerator
{
    private readonly BytecodeBuilder _builder = BytecodeBuilder.New();
    private readonly Dictionary<string, int> _locals = new(StringComparer.Ordinal);

    public byte[] Generate(IList<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            Emit(stmt);
        }
        _builder.Halt();
        return _builder.ToArray();
    }

    private void Emit(Stmt stmt)
    {
        switch (stmt)
        {
            case VarDecl v:
                int slot = GetOrAllocate(v.Name.Lexeme);
                if (v.Initializer is not null)
                {
                    Emit(v.Initializer);
                }
                else
                {
                    _builder.PushInt(0); // default
                }
                _builder.Store(slot);
                break;

            case ExprStmt e:
                Emit(e.Expression);
                _builder.Pop();
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
                _builder.Print();
                break;

            case ForStmt f:
                if (f.Initializer is not null) Emit(f.Initializer);
                string forStart = NewLabel("for_start");
                string forEnd = NewLabel("for_end");
                _builder.Label(forStart);
                Emit(f.Condition);
                _builder.JumpIfZero(forEnd);
                Emit(f.Body);
                if (f.Increment is not null) Emit(f.Increment);
                _builder.Jump(forStart);
                _builder.Label(forEnd);
                break;

            case ForeachStmt fe:
                throw new NotSupportedException("foreach not yet implemented in codegen");

            default:
                throw new NotSupportedException($"Unhandled statement type {stmt.GetType().Name}");
        }
    }

    private void Emit(Expr expr)
    {
        switch (expr)
        {
            case Literal lit:
                _builder.PushInt(Convert.ToInt32(lit.Value ?? 0));
                break;

            case Variable v:
                _builder.Load(GetSlot(v.Name));
                break;

            case Assign a:
                Emit(a.Value);
                _builder.Store(GetSlot(a.Name));
                break;

            case Unary u:
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
                if (b.Operator.Type == TokenType.And)
                {
                    EmitLogicalAnd(b);
                    break;
                }
                if (b.Operator.Type == TokenType.Or)
                {
                    EmitLogicalOr(b);
                    break;
                }
                Emit(b.Left);
                Emit(b.Right);
                switch (b.Operator.Type)
                {
                    case TokenType.Plus: _builder.Add(); break;
                    case TokenType.Minus: _builder.Sub(); break;
                    case TokenType.Star: _builder.Mul(); break;
                    case TokenType.Slash: _builder.Div(); break;
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
        slot = _locals.Count;
        _locals[name] = slot;
        return slot;
    }

    private int GetSlot(Token name)
    {
        if (!_locals.TryGetValue(name.Lexeme, out var slot))
            throw new InvalidOperationException($"Undefined variable '{name.Lexeme}' at line {name.Line}, col {name.Column}");
        return slot;
    }

    private int _labelCounter;
    private string NewLabel(string prefix) => $"{prefix}_{_labelCounter++}";

    private void EmitLogicalOr(Binary b)
    {
        Emit(b.Left);
        string trueLabel = NewLabel("or_true");
        string endLabel = NewLabel("or_end");

        _builder.Dup(); // left, left
        _builder.JumpIfNotZero(trueLabel); // pops top copy
        _builder.Pop(); // drop remaining left (false) before evaluating right
        Emit(b.Right);
        _builder.Jump(endLabel);

        _builder.Label(trueLabel);
        // left (truthy) remains on stack
        _builder.Label(endLabel);
    }

    private void EmitLogicalAnd(Binary b)
    {
        Emit(b.Left);
        string falseLabel = NewLabel("and_false");
        string endLabel = NewLabel("and_end");

        _builder.Dup(); // left,left
        _builder.JumpIfZero(falseLabel); // pops top copy
        _builder.Pop(); // remove remaining left (true path) before evaluating right
        Emit(b.Right);
        _builder.Jump(endLabel);

        _builder.Label(falseLabel);
        // left (zero) remains on stack as the result
        _builder.Label(endLabel);
    }
}
