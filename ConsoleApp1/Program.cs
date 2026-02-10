using System;
using System.Buffers.Binary;
using System.Collections.Generic;

// Minimal stack-based bytecode VM for experimenting with the Code language.

enum OpCode : byte
{
    PushConst = 0x01,
    Add = 0x02,
    Sub = 0x03,
    Mul = 0x04,
    Div = 0x05,
    Print = 0x06,
    Halt = 0xFF
}

sealed class Vm
{
    private readonly byte[] _code;
    private readonly Stack<double> _stack = new();
    private int _ip;

    public Vm(byte[] code)
    {
        _code = code;
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
                    EnsureBytes(4);
                    int value = BinaryPrimitives.ReadInt32LittleEndian(_code.AsSpan(_ip, 4));
                    _ip += 4;
                    _stack.Push(value);
                    break;

                case OpCode.Add:
                    BinaryOp((a, b) => a + b);
                    break;

                case OpCode.Sub:
                    BinaryOp((a, b) => a - b);
                    break;

                case OpCode.Mul:
                    BinaryOp((a, b) => a * b);
                    break;

                case OpCode.Div:
                    BinaryOp((a, b) =>
                    {
                        if (b == 0)
                            throw new DivideByZeroException("Division by zero in bytecode.");
                        return a / b;
                    });
                    break;

                case OpCode.Print:
                    Console.WriteLine(Pop());
                    break;

                case OpCode.Halt:
                    return;

                default:
                    throw new InvalidOperationException($"Unknown opcode {(byte)op} at {_ip - 1}");
            }
        }
    }

    private void BinaryOp(Func<double, double, double> op)
    {
        double b = Pop();
        double a = Pop();
        _stack.Push(op(a, b));
    }

    private double Pop()
    {
        if (_stack.Count == 0)
            throw new InvalidOperationException($"Stack underflow at {_ip - 1}");
        return _stack.Pop();
    }

    private void EnsureBytes(int count)
    {
        if (_ip + count > _code.Length)
            throw new InvalidOperationException("Unexpected end of bytecode while reading operand.");
    }
}

sealed class BytecodeBuilder
{
    private readonly List<byte> _bytes = new();

    public static BytecodeBuilder New() => new BytecodeBuilder();

    public BytecodeBuilder PushInt(int value)
    {
        _bytes.Add((byte)OpCode.PushConst);
        _bytes.AddRange(BitConverter.GetBytes(value));
        return this;
    }

    public BytecodeBuilder Add() { _bytes.Add((byte)OpCode.Add); return this; }
    public BytecodeBuilder Sub() { _bytes.Add((byte)OpCode.Sub); return this; }
    public BytecodeBuilder Mul() { _bytes.Add((byte)OpCode.Mul); return this; }
    public BytecodeBuilder Div() { _bytes.Add((byte)OpCode.Div); return this; }
    public BytecodeBuilder Print() { _bytes.Add((byte)OpCode.Print); return this; }
    public BytecodeBuilder Halt() { _bytes.Add((byte)OpCode.Halt); return this; }

    public byte[] ToArray() => _bytes.ToArray();
}

static class Program
{
    // Demo program: computes (2 + 3) * 4 and prints 20.
    public static void Main()
    {
        var program = BytecodeBuilder.New()
            .PushInt(2)
            .PushInt(3)
            .Add()
            .PushInt(4)
            .Mul()
            .Print()
            .Halt()
            .ToArray();

        Console.WriteLine("Running bytecode: (2 + 3) * 4");
        new Vm(program).Run();
    }
}
