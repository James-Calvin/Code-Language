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
    private readonly List<(int Position, string Label, int MinimumSize)> _frameSizeFixups = new();
    private readonly List<(int ip, int line, int column)> _debug = new();
    private readonly List<string> _strings = new();
    private readonly Dictionary<string, int> _stringIds = new(StringComparer.Ordinal);
    private readonly List<string> _fields = new();
    private readonly Dictionary<string, int> _fieldSlots = new(StringComparer.Ordinal);
    private readonly List<(string Symbol, int Arity)> _hostBindings = new();
    private readonly Dictionary<string, int> _hostBindingIds = new(StringComparer.Ordinal);
    private readonly List<(string Name, bool IsRecord, IReadOnlyList<string> Fields)> _types = new();
    private readonly Dictionary<string, int> _typeIds = new(StringComparer.Ordinal);
    private readonly List<(string Label, int FrameSize, string Name)> _callables = new();
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
        _bytes.AddRange(BitConverter.GetBytes(InternString(value)));
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

    public BytecodeBuilder NewObject(string typeName) => AddTypeOperand(OpCode.NewObject, typeName, false);
    public BytecodeBuilder NewRecord(string typeName) => AddTypeOperand(OpCode.NewRecord, typeName, true);
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
    public BytecodeBuilder GetField(string fieldName) => AddFieldOperand(OpCode.GetField, fieldName);
    public BytecodeBuilder SetField(string fieldName) => AddFieldOperand(OpCode.SetField, fieldName);
    public BytecodeBuilder HostCall(string symbol, int argCount)
    {
        RecordDebug();
        _bytes.Add((byte)OpCode.HostCall);
        _bytes.AddRange(BitConverter.GetBytes(InternHostBinding(symbol, argCount)));
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
            _bytes.AddRange(BitConverter.GetBytes(InternType(entry.RuntimeTypeName, false)));

            int targetPos = _bytes.Count;
            _fixups.Add((targetPos, entry.TargetLabel));
            _bytes.AddRange(new byte[4]); // target placeholder
            _frameSizeFixups.Add((_bytes.Count, entry.TargetLabel, entry.LocalCount));
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
        _frameSizeFixups.Add((_bytes.Count, label, localCount));
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

    public void RegisterTypeLayout(string name, bool isRecord, IReadOnlyList<string> fields)
    {
        int typeId = InternType(name, isRecord);
        foreach (string field in fields) InternField(field);
        _types[typeId] = (name, isRecord, fields.ToArray());
    }

    public void RegisterCallable(string label, int frameSize, string name)
    {
        InternString(name);
        _callables.Add((label, frameSize, name));
    }

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

        var callableFrameSizes = _callables.ToDictionary(callable => callable.Label, callable => callable.FrameSize, StringComparer.Ordinal);
        foreach (var (position, label, minimumSize) in _frameSizeFixups)
        {
            if (!callableFrameSizes.TryGetValue(label, out int finalSize))
                finalSize = minimumSize;
            BitConverter.GetBytes(Math.Max(minimumSize, finalSize)).CopyTo(body, position);
        }

        int codeSize = body.Length;
        byte[] metadata = BuildMetadata();
        int debugBytes = _debug.Count * BytecodeFormat.DebugEntrySize;
        var result = new byte[BytecodeFormat.HeaderSize + codeSize + debugBytes + metadata.Length];
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
        Array.Copy(metadata, 0, result, offset, metadata.Length);
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

    private BytecodeBuilder AddFieldOperand(OpCode op, string value)
    {
        RecordDebug();
        _bytes.Add((byte)op);
        _bytes.AddRange(BitConverter.GetBytes(InternField(value)));
        return this;
    }

    private BytecodeBuilder AddTypeOperand(OpCode op, string typeName, bool isRecord)
    {
        RecordDebug();
        _bytes.Add((byte)op);
        _bytes.AddRange(BitConverter.GetBytes(InternType(typeName, isRecord)));
        return this;
    }

    private int InternString(string value)
    {
        if (_stringIds.TryGetValue(value, out int id)) return id;
        id = _strings.Count;
        _strings.Add(value);
        _stringIds[value] = id;
        return id;
    }

    private int InternField(string name)
    {
        if (_fieldSlots.TryGetValue(name, out int slot)) return slot;
        slot = _fields.Count;
        _fields.Add(name);
        _fieldSlots[name] = slot;
        InternString(name);
        return slot;
    }

    private int InternHostBinding(string symbol, int arity)
    {
        if (_hostBindingIds.TryGetValue(symbol, out int id))
        {
            if (_hostBindings[id].Arity != arity) throw new InvalidOperationException($"Host binding '{symbol}' used with inconsistent arity.");
            return id;
        }
        id = _hostBindings.Count;
        _hostBindings.Add((symbol, arity));
        _hostBindingIds[symbol] = id;
        InternString(symbol);
        return id;
    }

    private int InternType(string name, bool isRecord)
    {
        if (_typeIds.TryGetValue(name, out int id)) return id;
        id = _types.Count;
        _types.Add((name, isRecord, Array.Empty<string>()));
        _typeIds[name] = id;
        InternString(name);
        return id;
    }

    private byte[] BuildMetadata()
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true);
        writer.Write(_strings.Count);
        foreach (string value in _strings)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            writer.Write(utf8.Length);
            writer.Write(utf8);
        }
        writer.Write(_fields.Count);
        foreach (string field in _fields) writer.Write(_stringIds[field]);
        writer.Write(_hostBindings.Count);
        foreach (var host in _hostBindings)
        {
            writer.Write(_stringIds[host.Symbol]);
            writer.Write(host.Arity);
        }
        writer.Write(_types.Count);
        foreach (var type in _types)
        {
            writer.Write(_stringIds[type.Name]);
            writer.Write(type.IsRecord ? (byte)1 : (byte)0);
            writer.Write(type.Fields.Count);
            foreach (string field in type.Fields) writer.Write(_fieldSlots[field]);
        }
        writer.Write(_callables.Count);
        foreach (var callable in _callables)
        {
            if (!_labels.TryGetValue(callable.Label, out int target)) throw new InvalidOperationException($"Undefined callable label '{callable.Label}'.");
            writer.Write(target);
            writer.Write(callable.FrameSize);
            writer.Write(_stringIds[callable.Name]);
        }
        writer.Flush();
        byte[] body = payload.ToArray();
        using var metadata = new MemoryStream();
        metadata.Write(Encoding.ASCII.GetBytes(BytecodeFormat.MetadataMagicText));
        metadata.Write(BitConverter.GetBytes(body.Length));
        metadata.Write(body);
        return metadata.ToArray();
    }
}
