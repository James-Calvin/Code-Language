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

    public BytecodeBuilder PushReal(double value)
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.PushReal);
        _bytes.AddRange(BitConverter.GetBytes(value));
        return this;
    }
    public BytecodeBuilder PushWideInteger(long value)
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.PushWideInteger);
        _bytes.AddRange(BitConverter.GetBytes(value));
        return this;
    }

    public BytecodeBuilder Add() { RecordDebug(); _bytes.Add((byte)OpCode.Add); return this; }
    public BytecodeBuilder Sub() { RecordDebug(); _bytes.Add((byte)OpCode.Sub); return this; }
    public BytecodeBuilder Mul() { RecordDebug(); _bytes.Add((byte)OpCode.Mul); return this; }
    public BytecodeBuilder Div() { RecordDebug(); _bytes.Add((byte)OpCode.Div); return this; }
    public BytecodeBuilder IntDiv() { RecordDebug(); _bytes.Add((byte)OpCode.IntDiv); return this; }
    public BytecodeBuilder Mod() { RecordDebug(); _bytes.Add((byte)OpCode.Mod); return this; }
    public BytecodeBuilder Print() { RecordDebug(); _bytes.Add((byte)OpCode.Print); return this; }
    public BytecodeBuilder Dup() { RecordDebug(); _bytes.Add((byte)OpCode.Dup); return this; }
    public BytecodeBuilder Swap() { RecordDebug(); _bytes.Add((byte)OpCode.Swap); return this; }
    public BytecodeBuilder Pop() { RecordDebug(); _bytes.Add((byte)OpCode.Pop); return this; }
    public BytecodeBuilder Jump(string label) => AddJump(OpCode.Jump, label);
    public BytecodeBuilder JumpIfZero(string label) => AddJump(OpCode.JumpIfZero, label);
    public BytecodeBuilder JumpIfNotZero(string label) => AddJump(OpCode.JumpIfNotZero, label);
    public BytecodeBuilder Load(int slot) => AddSlot(OpCode.Load, slot);
    public BytecodeBuilder Store(int slot) => AddSlot(OpCode.Store, slot);
    public BytecodeBuilder LoadGlobal(int slot) => AddSlot(OpCode.LoadGlobal, slot);
    public BytecodeBuilder StoreGlobal(int slot) => AddSlot(OpCode.StoreGlobal, slot);
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

    public BytecodeBuilder ArrayAppend()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.ArrayAppend);
        return this;
    }

    public BytecodeBuilder ArrayRemoveAt()
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.ArrayRemoveAt);
        return this;
    }

    public BytecodeBuilder NewMap() { RecordDebug(); _bytes.Add((byte)OpCode.NewMap); return this; }
    public BytecodeBuilder MapGet() { RecordDebug(); _bytes.Add((byte)OpCode.MapGet); return this; }
    public BytecodeBuilder MapSet() { RecordDebug(); _bytes.Add((byte)OpCode.MapSet); return this; }
    public BytecodeBuilder MapContains() { RecordDebug(); _bytes.Add((byte)OpCode.MapContains); return this; }
    public BytecodeBuilder MapRemove() { RecordDebug(); _bytes.Add((byte)OpCode.MapRemove); return this; }
    public BytecodeBuilder NewSet() { RecordDebug(); _bytes.Add((byte)OpCode.NewSet); return this; }
    public BytecodeBuilder SetAdd() { RecordDebug(); _bytes.Add((byte)OpCode.SetAdd); return this; }
    public BytecodeBuilder SetContains() { RecordDebug(); _bytes.Add((byte)OpCode.SetContains); return this; }
    public BytecodeBuilder SetRemove() { RecordDebug(); _bytes.Add((byte)OpCode.SetRemove); return this; }
    public BytecodeBuilder NewQueue() { RecordDebug(); _bytes.Add((byte)OpCode.NewQueue); return this; }
    public BytecodeBuilder QueueEnqueue() { RecordDebug(); _bytes.Add((byte)OpCode.QueueEnqueue); return this; }
    public BytecodeBuilder QueueDequeue() { RecordDebug(); _bytes.Add((byte)OpCode.QueueDequeue); return this; }
    public BytecodeBuilder QueuePeek() { RecordDebug(); _bytes.Add((byte)OpCode.QueuePeek); return this; }
    public BytecodeBuilder NewStack() { RecordDebug(); _bytes.Add((byte)OpCode.NewStack); return this; }
    public BytecodeBuilder StackPush() { RecordDebug(); _bytes.Add((byte)OpCode.StackPush); return this; }
    public BytecodeBuilder StackPop() { RecordDebug(); _bytes.Add((byte)OpCode.StackPop); return this; }
    public BytecodeBuilder StackPeek() { RecordDebug(); _bytes.Add((byte)OpCode.StackPeek); return this; }

    public BytecodeBuilder NewObject(string typeName) => AddStringOperand(OpCode.NewObject, typeName);
    public BytecodeBuilder NewRecord(string typeName) => AddStringOperand(OpCode.NewRecord, typeName);
    public BytecodeBuilder FallibleSuccess() { RecordDebug(); _bytes.Add((byte)OpCode.FallibleSuccess); return this; }
    public BytecodeBuilder FallibleError() { RecordDebug(); _bytes.Add((byte)OpCode.FallibleError); return this; }
    public BytecodeBuilder FallibleIsError() { RecordDebug(); _bytes.Add((byte)OpCode.FallibleIsError); return this; }
    public BytecodeBuilder FallibleValue() { RecordDebug(); _bytes.Add((byte)OpCode.FallibleValue); return this; }
    public BytecodeBuilder FallibleErrorCode() { RecordDebug(); _bytes.Add((byte)OpCode.FallibleErrorCode); return this; }
    public BytecodeBuilder FallibleErrorMessage() { RecordDebug(); _bytes.Add((byte)OpCode.FallibleErrorMessage); return this; }
    public BytecodeBuilder CastInteger() { RecordDebug(); _bytes.Add((byte)OpCode.CastInteger); return this; }
    public BytecodeBuilder CastWhole() { RecordDebug(); _bytes.Add((byte)OpCode.CastWhole); return this; }
    public BytecodeBuilder CastReal() { RecordDebug(); _bytes.Add((byte)OpCode.CastReal); return this; }
    public BytecodeBuilder CheckedSizedNumericCast(SizedNumericKind kind)
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.CheckedSizedNumericCast);
        _bytes.Add((byte)kind);
        return this;
    }
    public BytecodeBuilder GetField(string fieldName) => AddStringOperand(OpCode.GetField, fieldName);
    public BytecodeBuilder SetField(string fieldName) => AddStringOperand(OpCode.SetField, fieldName);
    public BytecodeBuilder HostCall(string symbol, int argCount)
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.HostCall);
        var utf8 = Encoding.UTF8.GetBytes(symbol);
        _bytes.AddRange(BitConverter.GetBytes(utf8.Length));
        _bytes.AddRange(utf8);
        _bytes.AddRange(BitConverter.GetBytes(argCount));
        return this;
    }
    public BytecodeBuilder TimeUnixMs() { RecordDebug(); _bytes.Add((byte)OpCode.TimeUnixMs); return this; }
    public BytecodeBuilder TimeUnixUs() { RecordDebug(); _bytes.Add((byte)OpCode.TimeUnixUs); return this; }
    public BytecodeBuilder TimeMonoNs() { RecordDebug(); _bytes.Add((byte)OpCode.TimeMonoNs); return this; }
    public BytecodeBuilder TimeMonoTicks() { RecordDebug(); _bytes.Add((byte)OpCode.TimeMonoTicks); return this; }
    public BytecodeBuilder TimeMonoTicksPerSecond() { RecordDebug(); _bytes.Add((byte)OpCode.TimeMonoTicksPerSecond); return this; }
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

    public bool TryGetLabelAddress(string name, out int address) => _labels.TryGetValue(name, out address);

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
