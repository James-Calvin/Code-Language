using System;
using System.Collections.Generic;

namespace ConsoleApp1.Compiler;

sealed class CodeGenerator
{
    private readonly BytecodeBuilder _builder = BytecodeBuilder.New();
    private Dictionary<string, int> _locals = new(StringComparer.Ordinal);
    private int _nextLocalIndex;
    private int _functionLocalHighWater;
    private readonly Stack<Dictionary<string, int>> _scopeStack = new();
    private readonly Stack<int> _nextLocalStack = new();
    private readonly List<int> _freeTemps = new();
    private readonly Dictionary<string, (string Label, int ParamCount, int LocalCount)> _functions = new(StringComparer.Ordinal);
    private int _labelCounter;

    public byte[] Generate(IList<Stmt> statements)
    {
        var functionDecls = new List<FunctionDecl>();
        var topLevel = new List<Stmt>();
        foreach (var stmt in statements)
        {
            if (stmt is FunctionDecl fd) functionDecls.Add(fd);
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

        return _builder.ToArray();
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
            _functionLocalHighWater = Math.Max(_functionLocalHighWater, _nextLocalIndex);
        }

        Emit(fn.Body);

        // If function body didn't end with a return, emit implicit return 0
        _builder.PushInt(0);
        _builder.Ret();

        _functions[fn.Name.Lexeme] = (label, fn.Parameters.Count, _functionLocalHighWater);
        PopScope();
    }

    private void Emit(Stmt stmt)
    {
        switch (stmt)
        {
            case VarDecl v:
                SetLoc(v.Name);
                int slot = GetOrAllocate(v.Name.Lexeme);
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
                if (e.Expression is Assign)
                {
                    Emit(e.Expression);
                }
                else
                {
                    Emit(e.Expression);
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
                _builder.Print();
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
                if (f.Increment is not null) Emit(f.Increment);
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
            case ArrayLengthExpr alen:
                Emit(alen.Target);
                _builder.ArrayLength();
                break;
            case ArrayIndexExpr aidx:
                Emit(aidx.Array);
                Emit(aidx.Index);
                _builder.ArrayGet();
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
            case Variable v:
                SetLoc(v.Name);
                _builder.Load(GetSlot(v.Name));
                break;

            case Assign a:
                Emit(a.Value);
                SetLoc(a.Name);
                _builder.Store(GetSlot(a.Name));
                break;
            case ArraySetExpr aset:
                Emit(aset.Target.Array);
                Emit(aset.Target.Index);
                Emit(aset.Value);
                _builder.ArraySet();
                break;

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
                }
            }
        }
        return false;
    }

    private void EmitCall(Call call)
    {
        if (!_functions.TryGetValue(call.Callee.Lexeme, out var info))
            throw new InvalidOperationException($"Call to undefined function '{call.Callee.Lexeme}' at line {call.Callee.Line}, col {call.Callee.Column}");
        foreach (var arg in call.Arguments)
        {
            Emit(arg);
        }
        int frameSize = Math.Max(info.LocalCount, info.ParamCount); // include params in frame size
        _builder.Call(info.Label, call.Arguments.Count, frameSize);
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
        _nextLocalStack.Push(_nextLocalIndex);
        _locals = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    private void PopScope()
    {
        _locals = _scopeStack.Pop();
        _nextLocalIndex = _nextLocalStack.Pop();
    }

}
