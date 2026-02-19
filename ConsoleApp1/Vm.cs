using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ConsoleApp1;

enum OpCode : byte
{
    PushConst = 0x01,
    Add = 0x02,
    Sub = 0x03,
    Mul = 0x04,
    Div = 0x05,
    Mod = 0x24,
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
    ThrowError = 0x15,
    NewArray = 0x16,
    ArrayLength = 0x17,
    ArrayGet = 0x18,
    NewArrayN = 0x19,
    OptionalNone = 0x1A,
    OptionalHas = 0x1B,
    OptionalValue = 0x1C,
    OptionalOr = 0x1D,
    ArraySet = 0x1E,
    NewObject = 0x1F,
    GetField = 0x20,
    SetField = 0x21,
    GetTypeName = 0x22,
    InterfaceCall = 0x23,
    TimeUnixMs = 0x25,
    TimeUnixUs = 0x26,
    TimeMonoNs = 0x27,
    TimeMonoTicks = 0x28,
    TimeMonoTicksPerSecond = 0x29,
    Halt = 0xFF
}

sealed class Vm
{
    private readonly byte[] _code;
    private readonly Stack<object> _stack = new();
    private object[] _locals;
    private int _ip;
    private readonly TextWriter _output;
    private readonly Stack<(int returnIp, int callIp, object[] locals)> _callStack = new();
    private readonly Dictionary<int, (int line, int column)> _debug = new();
    private readonly Dictionary<int, InterfaceDispatchTable> _interfaceDispatchCache = new();
    private readonly int _codeEnd;
    private readonly long _monoOriginTicks;
    private const long UnixEpochTicks = 621355968000000000L;

    public Vm(byte[] code, TextWriter? output = null, int initialLocals = 8)
    {
        var header = BytecodeFormat.ReadHeader(code);
        _code = code;
        _ip = BytecodeFormat.HeaderSize;
        _codeEnd = BytecodeFormat.HeaderSize + header.CodeSize;
        _locals = new object[initialLocals];
        _output = output ?? Console.Out;
        _monoOriginTicks = Stopwatch.GetTimestamp();

        int debugOffset = _codeEnd;
        for (int i = 0; i < header.DebugCount; i++)
        {
            int ip = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(debugOffset, 4));
            int line = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(debugOffset + 4, 4));
            int col = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(debugOffset + 8, 4));
            _debug[ip] = (line, col);
            debugOffset += BytecodeFormat.DebugEntrySize;
        }
    }

    public void Run()
    {
        while (true)
        {
            if (_ip >= _codeEnd)
                ThrowRuntime("Execution fell off the end of the program.");

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
                        ThrowRuntime("Division by zero in bytecode.");
                        return a / b;
                    });
                    break;

                case OpCode.Mod:
                    NumericBinary((a, b) =>
                    {
                        if (b == 0)
                            ThrowRuntime("Modulo by zero in bytecode.");
                        return a % b;
                    });
                    break;

                case OpCode.Print:
                    if (_stack.Count == 0)
                        ThrowRuntime("Stack underflow");
                    var pv = _stack.Pop();
                    if (pv is VmError err)
                        _output.WriteLine(err.ToString());
                    else
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
                        ThrowRuntime("Stack underflow");
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
                    if (IsNumber(l) && IsNumber(r))
                    {
                        _stack.Push(ToDouble(l) == ToDouble(r) ? 1.0 : 0.0);
                    }
                    else
                    {
                        _stack.Push(Equals(l, r) ? 1.0 : 0.0);
                    }
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
                    int callIp = _ip - 1;
                    int target = ReadIntOperand();
                    int argCount = ReadIntOperand();
                    int localCount = ReadIntOperand();
                    var newLocals = new object[Math.Max(localCount, argCount)];
                    for (int i = argCount - 1; i >= 0; i--)
                    {
                        if (_stack.Count == 0)
                            ThrowRuntime("Stack underflow while reading args");
                        newLocals[i] = _stack.Pop();
                    }
                    _callStack.Push((_ip, callIp, _locals));
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

                case OpCode.NewArray:
                {
                    int count = ReadIntOperand();
                    EnsureStack(count);
                    var list = new List<object>(count);
                    for (int i = 0; i < count; i++)
                    {
                        list.Add(_stack.Pop());
                    }
                    list.Reverse();
                    _stack.Push(list);
                    break;
                }

                case OpCode.ArrayLength:
                {
                    EnsureStack(1);
                    var obj = _stack.Pop();
                    if (obj is not List<object> arr)
                    {
                        throwRuntimeType("ArrayLength expects array");
                        break;
                    }
                    _stack.Push(arr.Count);
                    break;
                }

                case OpCode.ArrayGet:
                {
                    EnsureStack(2);
                    double idxNum = PopNumber();
                    var arrObj = _stack.Pop();
                    if (arrObj is not List<object> arr)
                    {
                        throwRuntimeType("ArrayGet expects array");
                        break;
                    }
                    int idx = (int)idxNum;
                    if (idx < 0 || idx >= arr.Count)
                        ThrowRuntime("Array index out of range");
                    _stack.Push(arr[idx]);
                    break;
                }

                case OpCode.ArraySet:
                {
                    EnsureStack(3);
                    var value = _stack.Pop();
                    double idxNum = PopNumber();
                    var arrObj = _stack.Pop();
                    if (arrObj is not List<object> arr)
                    {
                        throwRuntimeType("ArraySet expects array");
                        break;
                    }
                    int idx = (int)idxNum;
                    if (idx < 0 || idx >= arr.Count)
                        ThrowRuntime("Array index out of range");
                    arr[idx] = value;
                    _stack.Push(value);
                    break;
                }

                case OpCode.NewObject:
                {
                    string typeName = ReadStringOperand();
                    _stack.Push(new VmObject(typeName));
                    break;
                }

                case OpCode.GetField:
                {
                    string fieldName = ReadStringOperand();
                    EnsureStack(1);
                    var obj = _stack.Pop();
                    if (obj is not VmObject vmObj)
                    {
                        throwRuntimeType("GetField expects object");
                        break;
                    }
                    if (!vmObj.Fields.TryGetValue(fieldName, out var value))
                        ThrowRuntime($"Field '{fieldName}' is not initialized on object '{vmObj.TypeName}'");
                    _stack.Push(value!);
                    break;
                }

                case OpCode.SetField:
                {
                    string fieldName = ReadStringOperand();
                    EnsureStack(2);
                    var value = _stack.Pop();
                    var obj = _stack.Pop();
                    if (obj is not VmObject vmObj)
                    {
                        throwRuntimeType("SetField expects object");
                        break;
                    }
                    vmObj.Fields[fieldName] = value;
                    _stack.Push(value);
                    break;
                }

                case OpCode.GetTypeName:
                {
                    EnsureStack(1);
                    var obj = _stack.Pop();
                    if (obj is not VmObject vmObj)
                    {
                        throwRuntimeType("GetTypeName expects object");
                        break;
                    }
                    _stack.Push(vmObj.TypeName);
                    break;
                }

                case OpCode.InterfaceCall:
                {
                    int callIp = _ip - 1;
                    if (!_interfaceDispatchCache.TryGetValue(callIp, out var dispatchTable))
                    {
                        dispatchTable = ReadInterfaceDispatchTable();
                        _interfaceDispatchCache[callIp] = dispatchTable;
                    }
                    else
                    {
                        _ip = dispatchTable.NextIp;
                    }

                    EnsureStack(dispatchTable.ExplicitArgCount + 1);
                    var args = new object[dispatchTable.ExplicitArgCount];
                    for (int i = dispatchTable.ExplicitArgCount - 1; i >= 0; i--)
                    {
                        args[i] = _stack.Pop();
                    }

                    var targetValue = _stack.Pop();
                    if (targetValue is not VmObject targetObject)
                    {
                        throwRuntimeType("InterfaceCall expects object target");
                        break;
                    }

                    if (!dispatchTable.Entries.TryGetValue(targetObject.TypeName, out var targetEntry))
                    {
                        ThrowRuntime($"No implementation for interface call on runtime object '{targetObject.TypeName}'");
                        break;
                    }

                    int totalArgCount = dispatchTable.ExplicitArgCount + 1; // include target as implicit this
                    var newLocals = new object[Math.Max(targetEntry.LocalCount, totalArgCount)];
                    newLocals[0] = targetObject;
                    for (int i = 0; i < args.Length; i++)
                    {
                        newLocals[i + 1] = args[i];
                    }

                    _callStack.Push((_ip, callIp, _locals));
                    _locals = newLocals;
                    _ip = targetEntry.TargetIp;
                    break;
                }

                case OpCode.NewArrayN:
                {
                    EnsureStack(1);
                    int size = (int)PopNumber();
                    if (size < 0) ThrowRuntime("Array size must be non-negative");
                    var list = new List<object>(size);
                    for (int i = 0; i < size; i++) list.Add(0);
                    _stack.Push(list);
                    break;
                }

                case OpCode.OptionalNone:
                    _stack.Push(OptionalNone.Value);
                    break;

                case OpCode.OptionalHas:
                {
                    EnsureStack(1);
                    var opt = _stack.Pop();
                    _stack.Push(opt == OptionalNone.Value ? 0 : 1);
                    break;
                }

                case OpCode.OptionalValue:
                {
                    EnsureStack(1);
                    var opt = _stack.Pop();
                    if (opt == OptionalNone.Value)
                        ThrowRuntime("Optional has no value");
                    _stack.Push(opt);
                    break;
                }

                case OpCode.OptionalOr:
                {
                    EnsureStack(2);
                    var fallback = _stack.Pop();
                    var opt = _stack.Pop();
                    _stack.Push(opt == OptionalNone.Value ? fallback : opt);
                    break;
                }

                case OpCode.ThrowError:
                {
                    EnsureStack(1);
                    var msgObj = _stack.Pop();
                    string msg = msgObj?.ToString() ?? "error";
                    ThrowRuntime(msg, type: "UserError");
                    break;
                }

                case OpCode.TimeUnixMs:
                {
                    long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    _stack.Push(ms);
                    break;
                }

                case OpCode.TimeUnixUs:
                {
                    long us = (DateTime.UtcNow.Ticks - UnixEpochTicks) / 10;
                    _stack.Push(us);
                    break;
                }

                case OpCode.TimeMonoNs:
                {
                    long elapsedTicks = Stopwatch.GetTimestamp() - _monoOriginTicks;
                    long ns = (long)(elapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency));
                    _stack.Push(ns);
                    break;
                }

                case OpCode.TimeMonoTicks:
                    _stack.Push(Stopwatch.GetTimestamp());
                    break;

                case OpCode.TimeMonoTicksPerSecond:
                    _stack.Push((long)Stopwatch.Frequency);
                    break;

                case OpCode.Halt:
                    return;

            default:
                ThrowRuntime($"Unknown opcode {(byte)op} at {_ip - 1}");
                break;
        }
    }
    }

    private void NumericBinary(Func<double, double, double> op)
    {
        double b = PopNumber();
        double a = PopNumber();
        _stack.Push(op(a, b));
    }

    private static bool IsNumber(object v) => v is int or long or double;
    private static double ToDouble(object v) => v is double d ? d : Convert.ToDouble(v);

    private object throwRuntimeType(string message)
    {
        ThrowRuntime(message);
        return null!; // unreachable
    }

    private double PopAsNumber(object v)
    {
        switch (v)
        {
            case double d: return d;
            case int i: return i;
            case long l: return l;
            default:
                ThrowRuntime($"Expected number on stack at {_ip - 1}, found {v?.GetType().Name}");
                return 0; // unreachable
        }
    }

    private double PopNumber()
    {
        if (_stack.Count == 0)
            ThrowRuntime($"Stack underflow at {_ip - 1}");
        var v = _stack.Pop();
        return PopAsNumber(v);
    }

    private void EnsureBytes(int count)
    {
        if (_ip + count > _codeEnd)
            ThrowRuntime("Unexpected end of bytecode while reading operand.");
    }

    private int ReadIntOperand()
    {
        EnsureBytes(4);
        int value = BinaryPrimitives.ReadInt32LittleEndian(_code.AsSpan(_ip, 4));
        _ip += 4;
        return value;
    }

    private string ReadStringOperand()
    {
        int length = ReadIntOperand();
        EnsureBytes(length);
        string value = System.Text.Encoding.UTF8.GetString(_code, _ip, length);
        _ip += length;
        return value;
    }

    private void EnsureStack(int needed)
    {
        if (_stack.Count < needed)
            ThrowRuntime($"Stack underflow (need {needed}, have {_stack.Count})");
    }

    private void EnsureLocals(int index)
    {
        if (index < 0)
            ThrowRuntime($"Negative local index {index}");
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
            ThrowRuntime($"Stack underflow (need 2, have {_stack.Count})");
        var b = _stack.Pop();
        var a = _stack.Pop();
        return (a, b);
    }

    private void ThrowRuntime(string message, string type = "RuntimeError")
    {
        var calls = new List<VmFrame>();
        foreach (var frame in _callStack)
        {
            int frameLine = -1, frameCol = -1;
            if (_debug.TryGetValue(frame.callIp, out var locFrame))
            {
                frameLine = locFrame.line;
                frameCol = locFrame.column;
            }
            calls.Add(new VmFrame(frame.callIp, frameLine, frameCol));
        }
        int faultIp = _ip - 1;
        int faultLine = -1, faultCol = -1;
        if (_debug.TryGetValue(faultIp, out var loc))
        {
            faultLine = loc.line;
            faultCol = loc.column;
        }
        var error = new VmError(type, message, faultLine, faultCol, calls.ToArray());
        throw new VmRuntimeException(message, faultIp, calls.ToArray(), faultLine, faultCol, error);
    }

    private InterfaceDispatchTable ReadInterfaceDispatchTable()
    {
        int explicitArgCount = ReadIntOperand();
        int entryCount = ReadIntOperand();
        var entries = new Dictionary<string, InterfaceDispatchEntry>(StringComparer.Ordinal);
        for (int i = 0; i < entryCount; i++)
        {
            string runtimeTypeName = ReadStringOperand();
            int targetIp = ReadIntOperand();
            int localCount = ReadIntOperand();
            entries[runtimeTypeName] = new InterfaceDispatchEntry(targetIp, localCount);
        }
        return new InterfaceDispatchTable(_ip, explicitArgCount, entries);
    }

    private sealed record InterfaceDispatchEntry(int TargetIp, int LocalCount);
    private sealed record InterfaceDispatchTable(
        int NextIp,
        int ExplicitArgCount,
        Dictionary<string, InterfaceDispatchEntry> Entries);
}
