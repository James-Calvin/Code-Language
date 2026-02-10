using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace ConsoleApp1;

enum OpCode : byte
{
    PushConst = 0x01,
    Add = 0x02,
    Sub = 0x03,
    Mul = 0x04,
    Div = 0x05,
    Print = 0x06,
    Dup = 0x07,
    Swap = 0x08,
    Pop = 0x09,
    Jump = 0x0A,
    JumpIfZero = 0x0B,
    JumpIfNotZero = 0x0C,
    Load = 0x0D,
    Store = 0x0E,
    Eq = 0x0F,
    Lt = 0x10,
    Gt = 0x11,
    Call = 0x12,
    Ret = 0x13,
    PushString = 0x14,
    Halt = 0xFF
}

sealed class Vm
{
    private readonly byte[] _code;
    private readonly Stack<object> _stack = new();
    private object[] _locals;
    private int _ip;
    private readonly TextWriter _output;
    private readonly Stack<(int returnIp, object[] locals)> _callStack = new();

    public Vm(byte[] code, TextWriter? output = null, int initialLocals = 8)
    {
        BytecodeFormat.ValidateHeader(code);
        _code = code;
        _ip = BytecodeFormat.HeaderSize;
        _locals = new object[initialLocals];
        _output = output ?? Console.Out;
    }

    public void Run()
    {
        while (true)
        {
            if (_ip >= _code.Length)
                throw new InvalidOperationException("Execution fell off the end of the program.");

            var op = (OpCode)_code[_ip++];

            switch (op)
            {
                case OpCode.PushConst:
                    _stack.Push(ReadIntOperand());
                    break;
                case OpCode.PushString:
                {
                    int length = ReadIntOperand();
                    EnsureBytes(length);
                    string s = System.Text.Encoding.UTF8.GetString(_code, _ip, length);
                    _ip += length;
                    _stack.Push(s);
                    break;
                }

                case OpCode.Add:
                {
                    var (l, r) = PopAny2();
                    if (l is string || r is string)
                    {
                        _stack.Push(string.Concat(l, r));
                    }
                    else
                    {
                        _stack.Push(PopAsNumber(r) + PopAsNumber(l)); // l,r already popped; use helper
                    }
                    break;
                }

                case OpCode.Sub:
                    NumericBinary((a, b) => a - b);
                    break;

                case OpCode.Mul:
                    NumericBinary((a, b) => a * b);
                    break;

                case OpCode.Div:
                    NumericBinary((a, b) =>
                    {
                        if (b == 0)
                            throw new DivideByZeroException("Division by zero in bytecode.");
                        return a / b;
                    });
                    break;

                case OpCode.Print:
                    if (_stack.Count == 0)
                        throw new InvalidOperationException($"Stack underflow at {_ip - 1}");
                    var pv = _stack.Pop();
                    _output.WriteLine(pv);
                    break;

                case OpCode.Dup:
                    EnsureStack(1);
                    _stack.Push(_stack.Peek());
                    break;

                case OpCode.Swap:
                    EnsureStack(2);
                    var a = _stack.Pop();
                    var b = _stack.Pop();
                    _stack.Push(a);
                    _stack.Push(b);
                    break;

                case OpCode.Pop:
                    if (_stack.Count == 0)
                        throw new InvalidOperationException($"Stack underflow at {_ip - 1}");
                    _stack.Pop();
                    break;

                case OpCode.Jump:
                    _ip = ReadIntOperand();
                    break;

                case OpCode.JumpIfZero:
                {
                    double test = PopNumber();
                    int target = ReadIntOperand();
                    if (test == 0)
                        _ip = target;
                    break;
                }

                case OpCode.JumpIfNotZero:
                {
                    double test = PopNumber();
                    int target = ReadIntOperand();
                    if (test != 0)
                        _ip = target;
                    break;
                }

                case OpCode.Load:
                {
                    int slot = ReadIntOperand();
                    _stack.Push(ReadLocal(slot));
                    break;
                }

                case OpCode.Store:
                {
                    int slot = ReadIntOperand();
                    var value = _stack.Pop();
                    WriteLocal(slot, value);
                    break;
                }

                case OpCode.Eq:
                {
                    var (l, r) = PopAny2();
                    _stack.Push(Equals(l, r) ? 1.0 : 0.0);
                    break;
                }

                case OpCode.Lt:
                    NumericBinary((x, y) => x < y ? 1 : 0);
                    break;

                case OpCode.Gt:
                    NumericBinary((x, y) => x > y ? 1 : 0);
                    break;

                case OpCode.Call:
                {
                    int target = ReadIntOperand();
                    int argCount = ReadIntOperand();
                    int localCount = ReadIntOperand();
                    var newLocals = new object[Math.Max(localCount, argCount)];
                    for (int i = argCount - 1; i >= 0; i--)
                    {
                        if (_stack.Count == 0)
                            throw new InvalidOperationException($"Stack underflow at {_ip - 1} while reading args");
                        newLocals[i] = _stack.Pop();
                    }
                    _callStack.Push((_ip, _locals));
                    _locals = newLocals;
                    _ip = target;
                    break;
                }

                case OpCode.Ret:
                {
                    var retVal = _stack.Pop();
                    if (_callStack.Count == 0)
                        return;
                    var frame = _callStack.Pop();
                    _locals = frame.locals;
                    _ip = frame.returnIp;
                    _stack.Push(retVal);
                    break;
                }

                case OpCode.Halt:
                    return;

                default:
                    throw new InvalidOperationException($"Unknown opcode {(byte)op} at {_ip - 1}");
            }
        }
    }

    private void NumericBinary(Func<double, double, double> op)
    {
        double b = PopNumber();
        double a = PopNumber();
        _stack.Push(op(a, b));
    }

    private double PopAsNumber(object v) => v switch
    {
        double d => d,
        int i => i,
        _ => throw new InvalidOperationException($"Expected number on stack at {_ip - 1}, found {v?.GetType().Name}")
    };

    private double PopNumber()
    {
        if (_stack.Count == 0)
            throw new InvalidOperationException($"Stack underflow at {_ip - 1}");
        var v = _stack.Pop();
        return PopAsNumber(v);
    }

    private void EnsureBytes(int count)
    {
        if (_ip + count > _code.Length)
            throw new InvalidOperationException("Unexpected end of bytecode while reading operand.");
    }

    private int ReadIntOperand()
    {
        EnsureBytes(4);
        int value = BinaryPrimitives.ReadInt32LittleEndian(_code.AsSpan(_ip, 4));
        _ip += 4;
        return value;
    }

    private void EnsureStack(int needed)
    {
        if (_stack.Count < needed)
            throw new InvalidOperationException($"Stack underflow at {_ip - 1} (need {needed}, have {_stack.Count})");
    }

    private void EnsureLocals(int index)
    {
        if (index < 0)
            throw new InvalidOperationException($"Negative local index {index} at {_ip - 1}");
        if (index >= _locals.Length)
        {
            Array.Resize(ref _locals, Math.Max(index + 1, _locals.Length * 2));
        }
    }

    private object ReadLocal(int index)
    {
        EnsureLocals(index);
        return _locals[index];
    }

    private void WriteLocal(int index, object value)
    {
        EnsureLocals(index);
        _locals[index] = value;
    }

    private (object, object) PopAny2()
    {
        if (_stack.Count < 2)
            throw new InvalidOperationException($"Stack underflow at {_ip - 1}");
        var b = _stack.Pop();
        var a = _stack.Pop();
        return (a, b);
    }
}
