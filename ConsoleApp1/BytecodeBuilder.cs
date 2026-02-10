using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ConsoleApp1;

sealed class BytecodeBuilder
{
    private readonly List<byte> _bytes = new();
    private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
    private readonly List<(int position, string label)> _fixups = new();

    public static BytecodeBuilder New() => new();

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
    public BytecodeBuilder Dup() { _bytes.Add((byte)OpCode.Dup); return this; }
    public BytecodeBuilder Swap() { _bytes.Add((byte)OpCode.Swap); return this; }
    public BytecodeBuilder Pop() { _bytes.Add((byte)OpCode.Pop); return this; }
    public BytecodeBuilder Jump(string label) => AddJump(OpCode.Jump, label);
    public BytecodeBuilder JumpIfZero(string label) => AddJump(OpCode.JumpIfZero, label);
    public BytecodeBuilder JumpIfNotZero(string label) => AddJump(OpCode.JumpIfNotZero, label);
    public BytecodeBuilder Load(int slot) => AddSlot(OpCode.Load, slot);
    public BytecodeBuilder Store(int slot) => AddSlot(OpCode.Store, slot);
    public BytecodeBuilder Eq() { _bytes.Add((byte)OpCode.Eq); return this; }
    public BytecodeBuilder Lt() { _bytes.Add((byte)OpCode.Lt); return this; }
    public BytecodeBuilder Gt() { _bytes.Add((byte)OpCode.Gt); return this; }
    public BytecodeBuilder PushString(string value)
    {
        _bytes.Add((byte)OpCode.PushString);
        var utf8 = System.Text.Encoding.UTF8.GetBytes(value);
        _bytes.AddRange(BitConverter.GetBytes(utf8.Length));
        _bytes.AddRange(utf8);
        return this;
    }
    public BytecodeBuilder Call(string label, int argCount, int localCount)
    {
        _bytes.Add((byte)OpCode.Call);
        _fixups.Add((_bytes.Count, label));
        _bytes.AddRange(new byte[4]); // target placeholder
        _bytes.AddRange(BitConverter.GetBytes(argCount));
        _bytes.AddRange(BitConverter.GetBytes(localCount));
        return this;
    }
    public BytecodeBuilder Ret() { _bytes.Add((byte)OpCode.Ret); return this; }
    public BytecodeBuilder Label(string name)
    {
        _labels[name] = _bytes.Count + BytecodeFormat.HeaderSize;
        return this;
    }
    public BytecodeBuilder Halt() { _bytes.Add((byte)OpCode.Halt); return this; }

    public byte[] ToArray()
    {
        // Resolve jumps before writing header.
        byte[] body = _bytes.ToArray();
        foreach (var (position, label) in _fixups)
        {
            if (!_labels.TryGetValue(label, out var target))
                throw new InvalidOperationException($"Undefined label '{label}'.");

            var bytes = BitConverter.GetBytes(target);
            Array.Copy(bytes, 0, body, position, 4);
        }

        var result = new byte[BytecodeFormat.HeaderSize + body.Length];
        BytecodeFormat.WriteHeader(result.AsSpan(0, BytecodeFormat.HeaderSize));
        Array.Copy(body, 0, result, BytecodeFormat.HeaderSize, body.Length);
        return result;
    }

    private BytecodeBuilder AddJump(OpCode op, string label)
    {
        _bytes.Add((byte)op);
        int operandPos = _bytes.Count;
        _fixups.Add((operandPos, label));
        _bytes.AddRange(new byte[4]);
        return this;
    }

    private BytecodeBuilder AddSlot(OpCode op, int slot)
    {
        _bytes.Add((byte)op);
        _bytes.AddRange(BitConverter.GetBytes(slot));
        return this;
    }
}
