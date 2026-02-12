using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ConsoleApp1;

sealed class BytecodeBuilder
{
    public readonly record struct InterfaceDispatchEntry(string RuntimeTypeName, string TargetLabel, int LocalCount);

    private readonly List<byte> _bytes = new();
    private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
    private readonly List<(int position, string label)> _fixups = new();
    private readonly List<(int ip, int line, int column)> _debug = new();
    private int _currentLine;
    private int _currentColumn;

    public static BytecodeBuilder New() => new();

    public void SetDebugLocation(int line, int column)
    {
        _currentLine = line;
        _currentColumn = column;
    }

    private void RecordDebug()
    {
        if (_currentLine <= 0) return;
        int ip = BytecodeFormat.HeaderSize + _bytes.Count;
        _debug.Add((ip, _currentLine, _currentColumn));
    }

    public BytecodeBuilder PushInt(int value)
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.PushConst);
        _bytes.AddRange(BitConverter.GetBytes(value));
        return this;
    }

    public BytecodeBuilder Add() { RecordDebug(); _bytes.Add((byte)OpCode.Add); return this; }
    public BytecodeBuilder Sub() { RecordDebug(); _bytes.Add((byte)OpCode.Sub); return this; }
    public BytecodeBuilder Mul() { RecordDebug(); _bytes.Add((byte)OpCode.Mul); return this; }
    public BytecodeBuilder Div() { RecordDebug(); _bytes.Add((byte)OpCode.Div); return this; }
    public BytecodeBuilder Print() { RecordDebug(); _bytes.Add((byte)OpCode.Print); return this; }
    public BytecodeBuilder Dup() { RecordDebug(); _bytes.Add((byte)OpCode.Dup); return this; }
    public BytecodeBuilder Swap() { RecordDebug(); _bytes.Add((byte)OpCode.Swap); return this; }
    public BytecodeBuilder Pop() { RecordDebug(); _bytes.Add((byte)OpCode.Pop); return this; }
    public BytecodeBuilder Jump(string label) => AddJump(OpCode.Jump, label);
    public BytecodeBuilder JumpIfZero(string label) => AddJump(OpCode.JumpIfZero, label);
    public BytecodeBuilder JumpIfNotZero(string label) => AddJump(OpCode.JumpIfNotZero, label);
    public BytecodeBuilder Load(int slot) => AddSlot(OpCode.Load, slot);
    public BytecodeBuilder Store(int slot) => AddSlot(OpCode.Store, slot);
    public BytecodeBuilder Eq() { RecordDebug(); _bytes.Add((byte)OpCode.Eq); return this; }
    public BytecodeBuilder Lt() { RecordDebug(); _bytes.Add((byte)OpCode.Lt); return this; }
    public BytecodeBuilder Gt() { RecordDebug(); _bytes.Add((byte)OpCode.Gt); return this; }
    public BytecodeBuilder PushString(string value)
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.PushString);
        var utf8 = System.Text.Encoding.UTF8.GetBytes(value);
        _bytes.AddRange(BitConverter.GetBytes(utf8.Length));
        _bytes.AddRange(utf8);
        return this;
    }
    public BytecodeBuilder ThrowError()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.ThrowError);
        return this;
    }

    public BytecodeBuilder NewArray(int count)
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.NewArray);
        _bytes.AddRange(BitConverter.GetBytes(count));
        return this;
    }

    public BytecodeBuilder ArrayLength()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.ArrayLength);
        return this;
    }

    public BytecodeBuilder ArrayGet()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.ArrayGet);
        return this;
    }

    public BytecodeBuilder NewArrayN()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.NewArrayN);
        return this;
    }

    public BytecodeBuilder OptionalHas()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.OptionalHas);
        return this;
    }

    public BytecodeBuilder OptionalValue()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.OptionalValue);
        return this;
    }

    public BytecodeBuilder OptionalOr()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.OptionalOr);
        return this;
    }

    public BytecodeBuilder PushNone()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.OptionalNone);
        return this;
    }

    public BytecodeBuilder ArraySet()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.ArraySet);
        return this;
    }

    public BytecodeBuilder NewObject(string typeName) => AddStringOperand(OpCode.NewObject, typeName);
    public BytecodeBuilder GetField(string fieldName) => AddStringOperand(OpCode.GetField, fieldName);
    public BytecodeBuilder SetField(string fieldName) => AddStringOperand(OpCode.SetField, fieldName);
    public BytecodeBuilder GetTypeName()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.GetTypeName);
        return this;
    }

    public BytecodeBuilder InterfaceCall(int explicitArgCount, IReadOnlyList<InterfaceDispatchEntry> entries)
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.InterfaceCall);
        _bytes.AddRange(BitConverter.GetBytes(explicitArgCount));
        _bytes.AddRange(BitConverter.GetBytes(entries.Count));
        foreach (var entry in entries)
        {
            var typeBytes = Encoding.UTF8.GetBytes(entry.RuntimeTypeName);
            _bytes.AddRange(BitConverter.GetBytes(typeBytes.Length));
            _bytes.AddRange(typeBytes);

            int targetPos = _bytes.Count;
            _fixups.Add((targetPos, entry.TargetLabel));
            _bytes.AddRange(new byte[4]); // target placeholder
            _bytes.AddRange(BitConverter.GetBytes(entry.LocalCount));
        }
        return this;
    }

    public BytecodeBuilder Call(string label, int argCount, int localCount)
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.Call);
        _fixups.Add((_bytes.Count, label));
        _bytes.AddRange(new byte[4]); // target placeholder
        _bytes.AddRange(BitConverter.GetBytes(argCount));
        _bytes.AddRange(BitConverter.GetBytes(localCount));
        return this;
    }
    public BytecodeBuilder Ret() { RecordDebug(); _bytes.Add((byte)OpCode.Ret); return this; }
    public BytecodeBuilder Label(string name)
    {
        _labels[name] = _bytes.Count + BytecodeFormat.HeaderSize;
        return this;
    }
    public BytecodeBuilder Halt() { RecordDebug(); _bytes.Add((byte)OpCode.Halt); return this; }

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

        int codeSize = body.Length;
        int debugBytes = _debug.Count * BytecodeFormat.DebugEntrySize;
        var result = new byte[BytecodeFormat.HeaderSize + codeSize + debugBytes];
        BytecodeFormat.WriteHeader(result.AsSpan(0, BytecodeFormat.HeaderSize), codeSize, _debug.Count);
        Array.Copy(body, 0, result, BytecodeFormat.HeaderSize, body.Length);

        int offset = BytecodeFormat.HeaderSize + codeSize;
        foreach (var entry in _debug)
        {
            BitConverter.GetBytes(entry.ip).CopyTo(result, offset);
            BitConverter.GetBytes(entry.line).CopyTo(result, offset + 4);
            BitConverter.GetBytes(entry.column).CopyTo(result, offset + 8);
            offset += BytecodeFormat.DebugEntrySize;
        }
        return result;
    }

    private BytecodeBuilder AddJump(OpCode op, string label)
    {
        RecordDebug();
        _bytes.Add((byte)op);
        int operandPos = _bytes.Count;
        _fixups.Add((operandPos, label));
        _bytes.AddRange(new byte[4]);
        return this;
    }

    private BytecodeBuilder AddSlot(OpCode op, int slot)
    {
        RecordDebug();
        _bytes.Add((byte)op);
        _bytes.AddRange(BitConverter.GetBytes(slot));
        return this;
    }

    private BytecodeBuilder AddStringOperand(OpCode op, string value)
    {
        RecordDebug();
        _bytes.Add((byte)op);
        var utf8 = Encoding.UTF8.GetBytes(value);
        _bytes.AddRange(BitConverter.GetBytes(utf8.Length));
        _bytes.AddRange(utf8);
        return this;
    }
}
