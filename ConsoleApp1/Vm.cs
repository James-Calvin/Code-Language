using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ConsoleApp1;

enum OpCode : byte
{
    PushConst = 0x01,
    Add = 0x02,
    Sub = 0x03,
    Mul = 0x04,
    Div = 0x05,
    IntDiv = 0x49,
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
    HostCall = 0x2A,
    ArrayAppend = 0x2B,
    ArrayRemoveAt = 0x2C,
    NewMap = 0x2D,
    MapGet = 0x2E,
    MapSet = 0x2F,
    MapContains = 0x30,
    MapRemove = 0x31,
    NewSet = 0x32,
    SetAdd = 0x33,
    SetContains = 0x34,
    SetRemove = 0x35,
    NewQueue = 0x36,
    QueueEnqueue = 0x37,
    QueueDequeue = 0x38,
    QueuePeek = 0x39,
    NewStack = 0x3A,
    StackPush = 0x3B,
    StackPop = 0x3C,
    StackPeek = 0x3D,
    NewRecord = 0x3E,
    FallibleSuccess = 0x3F,
    FallibleError = 0x40,
    FallibleIsError = 0x41,
    FallibleValue = 0x42,
    FallibleErrorCode = 0x43,
    FallibleErrorMessage = 0x44,
    PushReal = 0x45,
    CastInteger = 0x46,
    CastWhole = 0x47,
    CastReal = 0x48,
    PushWideInteger = 0x4A,
    CheckedSizedNumericCast = 0x4B,
    LoadGlobal = 0x4C,
    StoreGlobal = 0x4D,
    Halt = 0xFF
}

enum VmHostTarget
{
    Native,
    Web
}

sealed class Vm
{
    private readonly byte[] _code;
    private readonly Stack<object> _stack = new();
    private object[] _locals;
    private object[] _globals;
    private int _ip;
    private readonly TextWriter _output;
    private readonly TextReader _input;
    private readonly Stack<(int returnIp, int callIp, object[] locals)> _callStack = new();
    private readonly Dictionary<int, (int line, int column, string? source)> _debug = new();
    private readonly Dictionary<int, InterfaceDispatchTable> _interfaceDispatchCache = new();
    private readonly Dictionary<string, HostBinding> _hostBindings = new(StringComparer.Ordinal);
    private readonly BytecodeMetadata _metadata;
    private readonly int _codeEnd;
    private readonly VmHostTarget _hostTarget;
    private readonly long _monoOriginTicks;
    private long _nextWindowHandle = 1;
    private const long UnixEpochTicks = 621355968000000000L;

    public Vm(
        byte[] code,
        TextWriter? output = null,
        int initialLocals = 8,
        VmHostTarget hostTarget = VmHostTarget.Native,
        TextReader? input = null)
    {
        var header = BytecodeFormat.ReadHeader(code);
        _metadata = BytecodeMetadata.Read(code, header);
        _code = code;
        _ip = BytecodeFormat.HeaderSize;
        _codeEnd = BytecodeFormat.HeaderSize + header.CodeSize;
        _locals = new object[initialLocals];
        _globals = new object[8];
        _output = output ?? Console.Out;
        _input = input ?? Console.In;
        _hostTarget = hostTarget;
        _monoOriginTicks = Stopwatch.GetTimestamp();
        InitializeDefaultHostBindings();

        int debugOffset = _codeEnd;
        for (int i = 0; i < header.DebugCount; i++)
        {
            int ip = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(debugOffset, 4));
            int line = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(debugOffset + 4, 4));
            int col = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(debugOffset + 8, 4));
            int sourceId = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(debugOffset + 12, 4));
            string? source = sourceId >= 0 && sourceId < _metadata.Sources.Count ? _metadata.Sources[sourceId] : null;
            _debug[ip] = (line, col, source);
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
                case OpCode.PushReal:
                    _stack.Push(ReadDoubleOperand());
                    break;
                case OpCode.PushWideInteger:
                    _stack.Push(ReadLongOperand());
                    break;
                case OpCode.PushString:
                {
                    _stack.Push(ReadMetadataString());
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

                case OpCode.IntDiv:
                    IntegerDiv();
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

                case OpCode.LoadGlobal:
                {
                    int slot = ReadIntOperand();
                    _stack.Push(ReadGlobal(slot));
                    break;
                }

                case OpCode.StoreGlobal:
                {
                    int slot = ReadIntOperand();
                    var value = _stack.Pop();
                    WriteGlobal(slot, value);
                    break;
                }

                case OpCode.Eq:
                {
                    var (l, r) = PopAny2();
                    _stack.Push(VmValueSemantics.ValuesEqual(l, r) ? 1.0 : 0.0);
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
                    if (!TryGetCollectionLength(obj, out int count))
                    {
                        throwRuntimeType("Length expects array, map, set, queue, or stack");
                        break;
                    }
                    _stack.Push(count);
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

                case OpCode.ArrayAppend:
                {
                    EnsureStack(2);
                    var value = _stack.Pop();
                    var arrObj = _stack.Pop();
                    if (arrObj is not List<object> arr)
                    {
                        throwRuntimeType("ArrayAppend expects array");
                        break;
                    }
                    arr.Add(value);
                    _stack.Push(0);
                    break;
                }

                case OpCode.ArrayRemoveAt:
                {
                    EnsureStack(2);
                    double idxNum = PopNumber();
                    var arrObj = _stack.Pop();
                    if (arrObj is not List<object> arr)
                    {
                        throwRuntimeType("ArrayRemoveAt expects array");
                        break;
                    }
                    int idx = (int)idxNum;
                    if (idx < 0 || idx >= arr.Count)
                        ThrowRuntime("Array index out of range");
                    arr.RemoveAt(idx);
                    _stack.Push(0);
                    break;
                }

                case OpCode.NewMap:
                    _stack.Push(new VmMap());
                    break;

                case OpCode.MapGet:
                {
                    EnsureStack(2);
                    var key = _stack.Pop();
                    var mapObj = _stack.Pop();
                    if (mapObj is not VmMap map)
                    {
                        throwRuntimeType("MapGet expects map");
                        break;
                    }
                    if (!map.Entries.TryGetValue(key, out var value))
                        ThrowRuntime("Map key not found");
                    _stack.Push(value!);
                    break;
                }

                case OpCode.MapSet:
                {
                    EnsureStack(3);
                    var value = _stack.Pop();
                    var key = _stack.Pop();
                    var mapObj = _stack.Pop();
                    if (mapObj is not VmMap map)
                    {
                        throwRuntimeType("MapSet expects map");
                        break;
                    }
                    map.Entries[VmValueSemantics.SnapshotHashKey(key) ?? OptionalNone.Value] = value;
                    _stack.Push(value);
                    break;
                }

                case OpCode.MapContains:
                {
                    EnsureStack(2);
                    var key = _stack.Pop();
                    var mapObj = _stack.Pop();
                    if (mapObj is not VmMap map)
                    {
                        throwRuntimeType("MapContains expects map");
                        break;
                    }
                    _stack.Push(map.Entries.ContainsKey(key) ? 1 : 0);
                    break;
                }

                case OpCode.MapRemove:
                {
                    EnsureStack(2);
                    var key = _stack.Pop();
                    var mapObj = _stack.Pop();
                    if (mapObj is not VmMap map)
                    {
                        throwRuntimeType("MapRemove expects map");
                        break;
                    }
                    map.Entries.Remove(key);
                    _stack.Push(0);
                    break;
                }

                case OpCode.NewSet:
                    _stack.Push(new VmSet());
                    break;

                case OpCode.SetAdd:
                {
                    EnsureStack(2);
                    var value = _stack.Pop();
                    var setObj = _stack.Pop();
                    if (setObj is not VmSet set)
                    {
                        throwRuntimeType("SetAdd expects set");
                        break;
                    }
                    set.Entries.Add(VmValueSemantics.SnapshotHashKey(value) ?? OptionalNone.Value);
                    _stack.Push(0);
                    break;
                }

                case OpCode.SetContains:
                {
                    EnsureStack(2);
                    var value = _stack.Pop();
                    var setObj = _stack.Pop();
                    if (setObj is not VmSet set)
                    {
                        throwRuntimeType("SetContains expects set");
                        break;
                    }
                    _stack.Push(set.Entries.Contains(value) ? 1 : 0);
                    break;
                }

                case OpCode.SetRemove:
                {
                    EnsureStack(2);
                    var value = _stack.Pop();
                    var setObj = _stack.Pop();
                    if (setObj is not VmSet set)
                    {
                        throwRuntimeType("SetRemove expects set");
                        break;
                    }
                    set.Entries.Remove(value);
                    _stack.Push(0);
                    break;
                }

                case OpCode.NewQueue:
                    _stack.Push(new VmQueue());
                    break;

                case OpCode.QueueEnqueue:
                {
                    EnsureStack(2);
                    var value = _stack.Pop();
                    var queueObj = _stack.Pop();
                    if (queueObj is not VmQueue queue)
                    {
                        throwRuntimeType("QueueEnqueue expects queue");
                        break;
                    }
                    queue.Items.Enqueue(value);
                    _stack.Push(0);
                    break;
                }

                case OpCode.QueueDequeue:
                {
                    EnsureStack(1);
                    var queueObj = _stack.Pop();
                    if (queueObj is not VmQueue queue)
                    {
                        throwRuntimeType("QueueDequeue expects queue");
                        break;
                    }
                    if (queue.Items.Count == 0)
                        ThrowRuntime("Queue is empty");
                    _stack.Push(queue.Items.Dequeue());
                    break;
                }

                case OpCode.QueuePeek:
                {
                    EnsureStack(1);
                    var queueObj = _stack.Pop();
                    if (queueObj is not VmQueue queue)
                    {
                        throwRuntimeType("QueuePeek expects queue");
                        break;
                    }
                    if (queue.Items.Count == 0)
                        ThrowRuntime("Queue is empty");
                    _stack.Push(queue.Items.Peek());
                    break;
                }

                case OpCode.NewStack:
                    _stack.Push(new VmStack());
                    break;

                case OpCode.StackPush:
                {
                    EnsureStack(2);
                    var value = _stack.Pop();
                    var stackObj = _stack.Pop();
                    if (stackObj is not VmStack stack)
                    {
                        throwRuntimeType("StackPush expects stack");
                        break;
                    }
                    stack.Items.Push(value);
                    _stack.Push(0);
                    break;
                }

                case OpCode.StackPop:
                {
                    EnsureStack(1);
                    var stackObj = _stack.Pop();
                    if (stackObj is not VmStack stack)
                    {
                        throwRuntimeType("StackPop expects stack");
                        break;
                    }
                    if (stack.Items.Count == 0)
                        ThrowRuntime("Stack is empty");
                    _stack.Push(stack.Items.Pop());
                    break;
                }

                case OpCode.StackPeek:
                {
                    EnsureStack(1);
                    var stackObj = _stack.Pop();
                    if (stackObj is not VmStack stack)
                    {
                        throwRuntimeType("StackPeek expects stack");
                        break;
                    }
                    if (stack.Items.Count == 0)
                        ThrowRuntime("Stack is empty");
                    _stack.Push(stack.Items.Peek());
                    break;
                }

                case OpCode.NewObject:
                {
                    int typeId = ReadMetadataIndex(_metadata.Types.Count, "type");
                    var type = _metadata.Types[typeId];
                    _stack.Push(new VmObject(typeId, type.Name, isRecord: false, _metadata.Fields.Count, type.HashFieldSlots));
                    break;
                }

                case OpCode.NewRecord:
                {
                    int typeId = ReadMetadataIndex(_metadata.Types.Count, "type");
                    var type = _metadata.Types[typeId];
                    _stack.Push(new VmObject(typeId, type.Name, isRecord: true, _metadata.Fields.Count, type.HashFieldSlots));
                    break;
                }

                case OpCode.GetField:
                {
                    int fieldSlot = ReadMetadataIndex(_metadata.Fields.Count, "field");
                    string fieldName = _metadata.Fields[fieldSlot];
                    EnsureStack(1);
                    var obj = _stack.Pop();
                    if (obj is not VmObject vmObj)
                    {
                        throwRuntimeType("GetField expects object");
                        break;
                    }
                    if (!vmObj.InitializedFields[fieldSlot])
                        ThrowRuntime($"Field '{fieldName}' is not initialized on object '{vmObj.TypeName}'");
                    _stack.Push(vmObj.Fields[fieldSlot]!);
                    break;
                }

                case OpCode.SetField:
                {
                    int fieldSlot = ReadMetadataIndex(_metadata.Fields.Count, "field");
                    EnsureStack(2);
                    var value = _stack.Pop();
                    var obj = _stack.Pop();
                    if (obj is not VmObject vmObj)
                    {
                        throwRuntimeType("SetField expects object");
                        break;
                    }
                    vmObj.Fields[fieldSlot] = value;
                    vmObj.InitializedFields[fieldSlot] = true;
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

                    if (!dispatchTable.Entries.TryGetValue(targetObject.TypeId, out var targetEntry))
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
                        ThrowRuntime("Optional value is none");
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

                case OpCode.FallibleSuccess:
                {
                    EnsureStack(1);
                    _stack.Push(VmFallible.Success(_stack.Pop()));
                    break;
                }

                case OpCode.FallibleError:
                {
                    EnsureStack(2);
                    var message = _stack.Pop()?.ToString() ?? string.Empty;
                    var code = _stack.Pop();
                    _stack.Push(VmFallible.Error(code, message));
                    break;
                }

                case OpCode.FallibleIsError:
                {
                    EnsureStack(1);
                    var fallible = PopFallible(OpCode.FallibleIsError);
                    _stack.Push(fallible.IsError ? 1 : 0);
                    break;
                }

                case OpCode.FallibleValue:
                {
                    EnsureStack(1);
                    var fallible = PopFallible(OpCode.FallibleValue);
                    if (fallible.IsError)
                        ThrowRuntime("Cannot unwrap failed fallible value without handling");
                    _stack.Push(fallible.Value ?? 0);
                    break;
                }

                case OpCode.FallibleErrorCode:
                {
                    EnsureStack(1);
                    var fallible = PopFallible(OpCode.FallibleErrorCode);
                    if (!fallible.IsError)
                        ThrowRuntime("Cannot read error code from successful fallible value");
                    _stack.Push(fallible.Code ?? 0);
                    break;
                }

                case OpCode.FallibleErrorMessage:
                {
                    EnsureStack(1);
                    var fallible = PopFallible(OpCode.FallibleErrorMessage);
                    if (!fallible.IsError)
                        ThrowRuntime("Cannot read error message from successful fallible value");
                    _stack.Push(fallible.Message);
                    break;
                }

                case OpCode.CastInteger:
                    _stack.Push(CoerceNumericCastToInt(allowNegative: true, "integer"));
                    break;

                case OpCode.CastWhole:
                    _stack.Push(CoerceNumericCastToInt(allowNegative: false, "whole"));
                    break;

                case OpCode.CastReal:
                    _stack.Push(PopNumber());
                    break;

                case OpCode.CheckedSizedNumericCast:
                    _stack.Push(CoerceCheckedSizedNumeric((SizedNumericKind)ReadByteOperand()));
                    break;

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

                case OpCode.HostCall:
                {
                    int bindingId = ReadMetadataIndex(_metadata.HostBindings.Count, "host binding");
                    var bindingMetadata = _metadata.HostBindings[bindingId];
                    string symbol = bindingMetadata.Symbol;
                    int argCount = bindingMetadata.Arity;
                    if (!_hostBindings.TryGetValue(symbol, out var binding))
                    {
                        ThrowRuntime($"Missing host binding '{symbol}'", type: "HostBindingError");
                        break;
                    }

                    if (binding.ArgCount != argCount)
                    {
                        ThrowRuntime(
                            $"Host binding '{symbol}' expects {binding.ArgCount} args, got {argCount}",
                            type: "HostBindingError");
                        break;
                    }

                    EnsureStack(argCount);
                    var args = new object?[argCount];
                    for (int i = argCount - 1; i >= 0; i--)
                        args[i] = _stack.Pop();

                    object? result = binding.Handler(args);
                    _stack.Push(result ?? 0);
                    break;
                }

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

    private void IntegerDiv()
    {
        long b = PopIntegralOperand();
        long a = PopIntegralOperand();
        if (b == 0)
            ThrowRuntime("Division by zero in bytecode.");
        _stack.Push(a / b);
    }

    private long PopIntegralOperand()
    {
        if (_stack.Count == 0)
            ThrowRuntime("Stack underflow");

        object value = _stack.Pop();
        switch (value)
        {
            case int i:
                return i;
            case long l:
                return l;
            case double d:
            {
                if (!double.IsFinite(d))
                    ThrowRuntime("Integer division requires finite numeric operands.");
                double truncated = Math.Truncate(d);
                if (truncated < long.MinValue || truncated > long.MaxValue)
                    ThrowRuntime("Integer division operand is out of range.");
                return checked((long)truncated);
            }
            default:
                ThrowRuntime("Integer division requires numeric operands.");
                return 0;
        }
    }

    private static bool IsNumber(object? v) => v is int or long or double;
    private static double ToDouble(object? v) => v is double d ? d : Convert.ToDouble(v);

    private sealed record HostBinding(int ArgCount, Func<object?[], object?> Handler);

    private void InitializeDefaultHostBindings()
    {
        InitializeCommonHostBindings();

        switch (_hostTarget)
        {
            case VmHostTarget.Native:
                InitializeNativeHostBindings();
                break;
            case VmHostTarget.Web:
                InitializeWebHostBindings();
                break;
            default:
                throw new InvalidOperationException($"Unsupported host target '{_hostTarget}'.");
        }
    }

    private void InitializeCommonHostBindings()
    {
        HostBinding printBinding = new(1, args =>
        {
            var value = args[0];
            if (value is VmError err)
                _output.WriteLine(err.ToString());
            else
                _output.WriteLine(value);
            return 0;
        });
        _hostBindings["standard.input_output.print"] = printBinding;
        _hostBindings["std.io.print"] = printBinding;

        _hostBindings["std.time.unix_ms"] = new HostBinding(0, _ => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _hostBindings["std.time.unix_us"] = new HostBinding(0, _ => (DateTime.UtcNow.Ticks - UnixEpochTicks) / 10);
        _hostBindings["std.time.mono_ns"] = new HostBinding(0, _ =>
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - _monoOriginTicks;
            return (long)(elapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency));
        });
        _hostBindings["std.time.mono_ticks"] = new HostBinding(0, _ => Stopwatch.GetTimestamp());
        _hostBindings["std.time.mono_ticks_per_second"] = new HostBinding(0, _ => (long)Stopwatch.Frequency);
        _hostBindings["std.math.minimum"] = new HostBinding(2, args => Math.Min(ToDouble(args[0]), ToDouble(args[1])));
        _hostBindings["std.math.maximum"] = new HostBinding(2, args => Math.Max(ToDouble(args[0]), ToDouble(args[1])));
        _hostBindings["std.math.absolute"] = new HostBinding(1, args => Math.Abs(ToDouble(args[0])));
        _hostBindings["std.math.sign"] = new HostBinding(1, args => Math.Sign(ToDouble(args[0])));
        _hostBindings["std.math.lerp"] = new HostBinding(3, args =>
        {
            double start = ToDouble(args[0]);
            double end = ToDouble(args[1]);
            double amount = ToDouble(args[2]);
            return start + ((end - start) * amount);
        });
        _hostBindings["std.math.sine"] = new HostBinding(1, args => Math.Sin(ToDouble(args[0])));
        _hostBindings["std.math.cosine"] = new HostBinding(1, args => Math.Cos(ToDouble(args[0])));
        _hostBindings["std.math.square_root"] = new HostBinding(1, args => Math.Sqrt(ToDouble(args[0])));
        _hostBindings["std.math.random"] = new HostBinding(0, _ => Random.Shared.NextDouble());

        _hostBindings["engine.window.create"] = new HostBinding(3, _ => _nextWindowHandle++);
        _hostBindings["engine.window.should_close"] = new HostBinding(1, _ => 1);
        _hostBindings["engine.window.present"] = new HostBinding(1, _ => 0);

        _hostBindings["engine.input.key_down"] = new HostBinding(2, _ => 0);
        _hostBindings["engine.input.key_down_scene"] = new HostBinding(1, _ => 0);
        _hostBindings["engine.input.pointer_world_x_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.input.pointer_world_y_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.input.pointer_screen_x_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.input.pointer_screen_y_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.input.pointer_is_down_scene"] = new HostBinding(0, _ => 0);
        _hostBindings["engine.input.pointer_was_pressed_scene"] = new HostBinding(0, _ => 0);
        _hostBindings["engine.input.pointer_was_released_scene"] = new HostBinding(0, _ => 0);
        _hostBindings["engine.window.camera_view_left_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.window.camera_view_top_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.window.camera_view_width_scene"] = new HostBinding(0, _ => 640.0);
        _hostBindings["engine.window.camera_view_height_scene"] = new HostBinding(0, _ => 360.0);
        _hostBindings["engine.window.camera_view_right_scene"] = new HostBinding(0, _ => 640.0);
        _hostBindings["engine.window.camera_view_bottom_scene"] = new HostBinding(0, _ => 360.0);
        _hostBindings["engine.window.camera_safe_left_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.window.camera_safe_top_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.window.camera_safe_width_scene"] = new HostBinding(0, _ => 640.0);
        _hostBindings["engine.window.camera_safe_height_scene"] = new HostBinding(0, _ => 360.0);
        _hostBindings["engine.window.camera_safe_right_scene"] = new HostBinding(0, _ => 640.0);
        _hostBindings["engine.window.camera_safe_bottom_scene"] = new HostBinding(0, _ => 360.0);
        _hostBindings["engine.window.screen_width_scene"] = new HostBinding(0, _ => 640.0);
        _hostBindings["engine.window.screen_height_scene"] = new HostBinding(0, _ => 360.0);

        _hostBindings["engine.gfx.clear"] = new HostBinding(5, _ => 0);
        _hostBindings["engine.gfx.clear_scene"] = new HostBinding(4, _ => 0);
        _hostBindings["engine.gfx.draw_rect"] = new HostBinding(9, _ => 0);
        _hostBindings["engine.gfx.draw_rect_scene"] = new HostBinding(8, _ => 0);
        _hostBindings["engine.gfx.draw_rectangle_scene"] = new HostBinding(8, _ => 0);
        _hostBindings["engine.gfx.draw_rectangle_outline_scene"] = new HostBinding(9, _ => 0);
        _hostBindings["engine.gfx.draw_circle_scene"] = new HostBinding(7, _ => 0);
        _hostBindings["engine.gfx.draw_circle_outline_scene"] = new HostBinding(8, _ => 0);
        _hostBindings["engine.gfx.draw_polygon_scene"] = new HostBinding(5, _ => 0);
        _hostBindings["engine.gfx.draw_polygon_outline_scene"] = new HostBinding(6, _ => 0);
        _hostBindings["engine.gfx.draw_line_scene"] = new HostBinding(8, _ => 0);
        _hostBindings["engine.gfx.draw_text_scene"] = new HostBinding(10, _ => 0);
        _hostBindings["engine.gfx.draw_image_scene"] = new HostBinding(6, _ => 0);
        _hostBindings["engine.gfx.draw_sprite_scene"] = new HostBinding(10, _ => 0);

        _hostBindings["engine.diagnostics.last_frame_interval_milliseconds_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.diagnostics.estimated_frames_per_second_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.diagnostics.last_frame_work_milliseconds_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.diagnostics.last_update_work_milliseconds_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.diagnostics.last_draw_work_milliseconds_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.diagnostics.last_draw_hud_work_milliseconds_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.diagnostics.last_update_steps_scene"] = new HostBinding(0, _ => 0);
        _hostBindings["engine.diagnostics.last_dropped_update_steps_scene"] = new HostBinding(0, _ => 0);
        _hostBindings["engine.diagnostics.last_update_interval_milliseconds_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.diagnostics.update_delta_milliseconds_scene"] = new HostBinding(0, _ => 0.0);
        _hostBindings["engine.runtime.use_continuous_updates_scene"] = new HostBinding(0, _ => 0);
        _hostBindings["engine.runtime.set_fixed_update_rate_scene"] = new HostBinding(1, args => { CoercePositiveIntArg(args[0], "engine.runtime.set_fixed_update_rate_scene"); return 0; });
        _hostBindings["engine.runtime.set_maximum_render_rate_scene"] = new HostBinding(1, args => { CoercePositiveIntArg(args[0], "engine.runtime.set_maximum_render_rate_scene"); return 0; });
        _hostBindings["engine.runtime.use_display_synchronized_rendering_scene"] = new HostBinding(0, _ => 0);

        _hostBindings["engine.audio.can_play_sound_scene"] = new HostBinding(0, _ => 0);
        _hostBindings["engine.audio.play_sound_scene"] = new HostBinding(2, _ => 0);
        _hostBindings["engine.audio.play_looping_sound_scene"] = new HostBinding(2, _ => 0);
        _hostBindings["engine.audio.stop_sound_scene"] = new HostBinding(1, _ => 0);
        _hostBindings["engine.audio.set_sound_volume_scene"] = new HostBinding(2, _ => 0);
        _hostBindings["engine.audio.sound_is_playing_scene"] = new HostBinding(1, _ => 0);
        _hostBindings["engine.audio.stop_all_sounds_scene"] = new HostBinding(0, _ => 0);
    }

    private void InitializeNativeHostBindings()
    {
        HostBinding readLineBinding = new(0, _ => _input.ReadLine() ?? string.Empty);
        _hostBindings["standard.input_output.read_line"] = readLineBinding;
        _hostBindings["std.io.read_line"] = readLineBinding;
        _hostBindings["std.time.sleep_ms"] = new HostBinding(1, args =>
        {
            int ms = CoerceNonNegativeIntArg(args[0], "std.time.sleep_ms");
            Thread.Sleep(ms);
            return 0;
        });
    }

    private void InitializeWebHostBindings()
    {
        RegisterUnsupportedBinding(
            "standard.input_output.read_line",
            0,
            "this host API is native-only and cannot run on vm-web.");
        RegisterUnsupportedBinding(
            "std.io.read_line",
            0,
            "this host API is native-only and cannot run on vm-web.");

        RegisterUnsupportedBinding(
            "std.time.sleep_ms",
            1,
            "this host API is native-only and cannot run on vm-web.");
    }

    private void RegisterUnsupportedBinding(string symbol, int arity, string reason)
    {
        _hostBindings[symbol] = new HostBinding(arity, _ =>
        {
            ThrowRuntime(
                $"Host binding '{symbol}' is not available on target '{HostTargetName()}': {reason}",
                type: "HostBindingError");
            return 0;
        });
    }

    private int CoerceNonNegativeIntArg(object? value, string symbol)
    {
        double numeric = value switch
        {
            int i => i,
            long l => l,
            double d => d,
            _ => ThrowHostBindingArgumentType(symbol)
        };

        if (double.IsNaN(numeric) || double.IsInfinity(numeric))
        {
            ThrowRuntime(
                $"Host binding '{symbol}' expected a finite numeric argument",
                type: "HostBindingError");
        }

        if (numeric < 0)
        {
            ThrowRuntime(
                $"Host binding '{symbol}' expected a non-negative argument",
                type: "HostBindingError");
        }

        try
        {
            return checked((int)Math.Truncate(numeric));
        }
        catch (OverflowException)
        {
            ThrowRuntime(
                $"Host binding '{symbol}' argument was out of range",
                type: "HostBindingError");
            return 0; // unreachable
        }
    }

    private string HostTargetName() => _hostTarget switch
    {
        VmHostTarget.Native => "vm-native",
        VmHostTarget.Web => "vm-web",
        _ => _hostTarget.ToString()
    };

    private double ThrowHostBindingArgumentType(string symbol)
    {
        ThrowRuntime(
            $"Host binding '{symbol}' expected numeric argument",
            type: "HostBindingError");
        return 0; // unreachable
    }

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

    private int CoerceNumericCastToInt(bool allowNegative, string targetType)
    {
        double value = PopNumber();
        if (double.IsNaN(value) || double.IsInfinity(value))
            ThrowRuntime($"Cannot cast non-finite value to {targetType}");

        double truncated = Math.Truncate(value);
        if (!allowNegative && truncated < 0)
            ThrowRuntime("Cannot cast negative value to whole");
        if (truncated < int.MinValue || truncated > int.MaxValue)
            ThrowRuntime($"Cannot cast value outside integer range to {targetType}");

        return checked((int)truncated);
    }

    private object CoerceCheckedSizedNumeric(SizedNumericKind kind)
    {
        double value = PopNumber();
        if (!double.IsFinite(value))
            ThrowRuntime($"Cannot cast non-finite value to {SizedNumericName(kind)}");

        if (kind == SizedNumericKind.Real32)
        {
            float rounded = (float)value;
            if (float.IsInfinity(rounded))
                ThrowRuntime("Cannot cast value outside real32 range to real32");
            return (double)rounded;
        }

        double truncated = Math.Truncate(value);
        var (minimum, maximum) = SizedNumericIntegralRange(kind);
        if (truncated < minimum || truncated > maximum)
            ThrowRuntime($"Cannot cast value outside {SizedNumericName(kind)} range");

        long result = checked((long)truncated);
        return result is >= int.MinValue and <= int.MaxValue ? (int)result : result;
    }

    private static string SizedNumericName(SizedNumericKind kind) => kind switch
    {
        SizedNumericKind.Integer8 => "integer8",
        SizedNumericKind.Integer16 => "integer16",
        SizedNumericKind.Integer32 => "integer32",
        SizedNumericKind.Whole8 => "whole8",
        SizedNumericKind.Whole16 => "whole16",
        SizedNumericKind.Whole32 => "whole32",
        SizedNumericKind.Real32 => "real32",
        _ => $"unknown sized numeric kind {(byte)kind}"
    };

    private static (long Minimum, long Maximum) SizedNumericIntegralRange(SizedNumericKind kind) => kind switch
    {
        SizedNumericKind.Integer8 => (sbyte.MinValue, sbyte.MaxValue),
        SizedNumericKind.Integer16 => (short.MinValue, short.MaxValue),
        SizedNumericKind.Integer32 => (int.MinValue, int.MaxValue),
        SizedNumericKind.Whole8 => (byte.MinValue, byte.MaxValue),
        SizedNumericKind.Whole16 => (ushort.MinValue, ushort.MaxValue),
        SizedNumericKind.Whole32 => (0L, uint.MaxValue),
        _ => throw new InvalidOperationException($"Sized numeric kind '{kind}' is not integral.")
    };

    private VmFallible PopFallible(OpCode op)
    {
        var value = _stack.Pop();
        if (value is VmFallible fallible)
            return fallible;

        ThrowRuntime($"{op} expects fallible value");
        return null!; // unreachable
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

    private byte ReadByteOperand()
    {
        EnsureBytes(1);
        return _code[_ip++];
    }

    private long ReadLongOperand()
    {
        EnsureBytes(8);
        long value = BinaryPrimitives.ReadInt64LittleEndian(_code.AsSpan(_ip, 8));
        _ip += 8;
        return value;
    }

    private double ReadDoubleOperand()
    {
        EnsureBytes(8);
        long bits = BinaryPrimitives.ReadInt64LittleEndian(_code.AsSpan(_ip, 8));
        _ip += 8;
        return BitConverter.Int64BitsToDouble(bits);
    }

    private int ReadMetadataIndex(int count, string name)
    {
        int value = ReadIntOperand();
        if (value < 0 || value >= count) ThrowRuntime($"Bytecode {name} index {value} is out of range");
        return value;
    }

    private int CoercePositiveIntArg(object? value, string symbol)
    {
        int result = CoerceNonNegativeIntArg(value, symbol);
        if (result == 0)
        {
            ThrowRuntime($"Host binding '{symbol}' expected a positive argument", type: "HostBindingError");
        }
        return result;
    }

    private string ReadMetadataString() => _metadata.Strings[ReadMetadataIndex(_metadata.Strings.Count, "string")];

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

    private void EnsureGlobals(int index)
    {
        if (index < 0)
            ThrowRuntime($"Negative global index {index}");
        if (index >= _globals.Length)
        {
            Array.Resize(ref _globals, Math.Max(index + 1, _globals.Length * 2));
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

    private object ReadGlobal(int index)
    {
        EnsureGlobals(index);
        return _globals[index];
    }

    private void WriteGlobal(int index, object value)
    {
        EnsureGlobals(index);
        _globals[index] = value;
    }

    private (object, object) PopAny2()
    {
        if (_stack.Count < 2)
            ThrowRuntime($"Stack underflow (need 2, have {_stack.Count})");
        var b = _stack.Pop();
        var a = _stack.Pop();
        return (a, b);
    }

    private static bool TryGetCollectionLength(object obj, out int count)
    {
        switch (obj)
        {
            case List<object> array:
                count = array.Count;
                return true;
            case VmMap map:
                count = map.Entries.Count;
                return true;
            case VmSet set:
                count = set.Entries.Count;
                return true;
            case VmQueue queue:
                count = queue.Items.Count;
                return true;
            case VmStack stack:
                count = stack.Items.Count;
                return true;
            default:
                count = 0;
                return false;
        }
    }

    private void ThrowRuntime(string message, string type = "RuntimeError")
    {
        var calls = new List<VmFrame>();
        foreach (var frame in _callStack)
        {
            int frameLine = -1, frameCol = -1;
            string? frameSource = null;
            if (_debug.TryGetValue(frame.callIp, out var locFrame))
            {
                frameLine = locFrame.line;
                frameCol = locFrame.column;
                frameSource = locFrame.source;
            }
            calls.Add(new VmFrame(frame.callIp, frameLine, frameCol, frameSource));
        }
        int faultIp = _ip - 1;
        int faultLine = -1, faultCol = -1;
        string? faultSource = null;
        if (_debug.TryGetValue(faultIp, out var loc))
        {
            faultLine = loc.line;
            faultCol = loc.column;
            faultSource = loc.source;
        }
        var error = new VmError(type, message, faultLine, faultCol, faultSource, calls.ToArray());
        throw new VmRuntimeException(message, faultIp, calls.ToArray(), faultLine, faultCol, faultSource, error);
    }

    private InterfaceDispatchTable ReadInterfaceDispatchTable()
    {
        int explicitArgCount = ReadIntOperand();
        int entryCount = ReadIntOperand();
        var entries = new Dictionary<int, InterfaceDispatchEntry>();
        for (int i = 0; i < entryCount; i++)
        {
            int runtimeTypeId = ReadMetadataIndex(_metadata.Types.Count, "interface type");
            int targetIp = ReadIntOperand();
            int localCount = ReadIntOperand();
            entries[runtimeTypeId] = new InterfaceDispatchEntry(targetIp, localCount);
        }
        return new InterfaceDispatchTable(_ip, explicitArgCount, entries);
    }

    private sealed record InterfaceDispatchEntry(int TargetIp, int LocalCount);
    private sealed record InterfaceDispatchTable(
        int NextIp,
        int ExplicitArgCount,
        Dictionary<int, InterfaceDispatchEntry> Entries);
}

sealed class VmFallible
{
    private VmFallible(bool isError, object? value, object? code, string message)
    {
        IsError = isError;
        Value = value;
        Code = code;
        Message = message;
    }

    public bool IsError { get; }
    public object? Value { get; }
    public object? Code { get; }
    public string Message { get; }

    public static VmFallible Success(object? value) => new(false, value, null, string.Empty);
    public static VmFallible Error(object? code, string message) => new(true, null, code, message);
}

file sealed class VmMap
{
    public Dictionary<object, object> Entries { get; } = new(new VmValueComparer());
}

file sealed class VmSet
{
    public HashSet<object> Entries { get; } = new(new VmValueComparer());
}

file sealed class VmQueue
{
    public Queue<object> Items { get; } = new();
}

file sealed class VmStack
{
    public Stack<object> Items { get; } = new();
}

file sealed class VmValueComparer : IEqualityComparer<object>
{
    bool IEqualityComparer<object>.Equals(object? x, object? y)
    {
        return VmValueSemantics.ValuesEqual(x, y);
    }

    int IEqualityComparer<object>.GetHashCode(object obj)
    {
        return VmValueSemantics.ValueHash(obj);
    }
}

file static class VmValueSemantics
{
    public static bool ValuesEqual(object? x, object? y)
    {
        if (ReferenceEquals(x, y))
            return true;

        if (x is null || y is null)
            return false;

        if (IsNumeric(x) && IsNumeric(y))
            return Convert.ToDouble(x) == Convert.ToDouble(y);

        if (x is VmObject leftObject && y is VmObject rightObject)
        {
            if (leftObject.IsRecord || rightObject.IsRecord)
                return RecordEquals(leftObject, rightObject);

            return ReferenceEquals(leftObject, rightObject);
        }

        if (x is List<object> leftArray && y is List<object> rightArray)
            return OrderedValuesEqual(leftArray, rightArray);

        if (x is VmQueue leftQueue && y is VmQueue rightQueue)
            return OrderedValuesEqual(leftQueue.Items, rightQueue.Items);

        if (x is VmStack leftStack && y is VmStack rightStack)
            return OrderedValuesEqual(leftStack.Items, rightStack.Items);

        if (x is VmSet leftSet && y is VmSet rightSet)
            return leftSet.Entries.SetEquals(rightSet.Entries);

        if (x is VmMap leftMap && y is VmMap rightMap)
            return MapsEqual(leftMap, rightMap);

        return EqualityComparer<object>.Default.Equals(x, y);
    }

    public static int ValueHash(object? value)
    {
        if (value is null)
            return 0;

        if (IsNumeric(value))
            return Convert.ToDouble(value).GetHashCode();

        if (value is VmObject vmObject)
        {
            if (vmObject.IsRecord)
                return RecordHash(vmObject);

            return RuntimeHelpers.GetHashCode(vmObject);
        }

        if (value is List<object> array)
            return OrderedHash("array", array);

        if (value is VmQueue queue)
            return OrderedHash("queue", queue.Items);

        if (value is VmStack stack)
            return OrderedHash("stack", stack.Items);

        if (value is VmSet set)
            return SetHash(set);

        if (value is VmMap map)
            return MapHash(map);

        return value.GetHashCode();
    }

    public static object? SnapshotHashKey(object? value)
        => SnapshotHashKey(value, new Dictionary<object, object>(ReferenceEqualityComparer.Instance));

    private static object? SnapshotHashKey(object? value, Dictionary<object, object> visited)
    {
        if (value is null)
            return null;

        if (value is int or long or double or string or bool || value == OptionalNone.Value)
            return value;

        if (value is VmObject vmObject)
        {
            if (!vmObject.IsRecord)
                return vmObject;

            if (visited.TryGetValue(vmObject, out var existing))
                return existing;

            var clone = new VmObject(vmObject.TypeId, vmObject.TypeName, isRecord: true, vmObject.Fields.Length, vmObject.HashFieldSlots);
            visited[vmObject] = clone;
            for (int slot = 0; slot < vmObject.Fields.Length; slot++)
            {
                if (!vmObject.InitializedFields[slot])
                    continue;
                clone.Fields[slot] = SnapshotHashKey(vmObject.Fields[slot], visited);
                clone.InitializedFields[slot] = true;
            }
            return clone;
        }

        if (value is List<object> array)
        {
            if (visited.TryGetValue(array, out var existing))
                return existing;
            var clone = new List<object>(array.Count);
            visited[array] = clone;
            foreach (var item in array)
                clone.Add(SnapshotHashKey(item, visited) ?? OptionalNone.Value);
            return clone;
        }

        if (value is VmQueue queue)
        {
            if (visited.TryGetValue(queue, out var existing))
                return existing;
            var clone = new VmQueue();
            visited[queue] = clone;
            foreach (var item in queue.Items)
                clone.Items.Enqueue(SnapshotHashKey(item, visited) ?? OptionalNone.Value);
            return clone;
        }

        if (value is VmStack stack)
        {
            if (visited.TryGetValue(stack, out var existing))
                return existing;
            var clone = new VmStack();
            visited[stack] = clone;
            foreach (var item in stack.Items.Reverse())
                clone.Items.Push(SnapshotHashKey(item, visited) ?? OptionalNone.Value);
            return clone;
        }

        if (value is VmSet set)
        {
            if (visited.TryGetValue(set, out var existing))
                return existing;
            var clone = new VmSet();
            visited[set] = clone;
            foreach (var item in set.Entries)
                clone.Entries.Add(SnapshotHashKey(item, visited) ?? OptionalNone.Value);
            return clone;
        }

        if (value is VmMap map)
        {
            if (visited.TryGetValue(map, out var existing))
                return existing;
            var clone = new VmMap();
            visited[map] = clone;
            foreach (var entry in map.Entries)
                clone.Entries[SnapshotHashKey(entry.Key, visited) ?? OptionalNone.Value] = SnapshotHashKey(entry.Value, visited) ?? OptionalNone.Value;
            return clone;
        }

        return value;
    }

    private static bool RecordEquals(VmObject left, VmObject right)
    {
        if (!left.IsRecord || !right.IsRecord)
            return false;
        if (!string.Equals(left.TypeName, right.TypeName, StringComparison.Ordinal))
            return false;
        if (left.Fields.Length != right.Fields.Length)
            return false;

        foreach (int slot in left.HashFieldSlots)
        {
            if (left.InitializedFields[slot] != right.InitializedFields[slot])
                return false;
            if (left.InitializedFields[slot] && !ValuesEqual(left.Fields[slot]!, right.Fields[slot]!))
                return false;
        }

        return true;
    }

    private static int RecordHash(VmObject record)
    {
        var hash = new HashCode();
        hash.Add(record.TypeName, StringComparer.Ordinal);

        foreach (int slot in record.HashFieldSlots)
        {
            if (!record.InitializedFields[slot]) continue;
            hash.Add(slot);
            hash.Add(ValueHash(record.Fields[slot]));
        }

        return hash.ToHashCode();
    }

    private static bool OrderedValuesEqual(IEnumerable<object> left, IEnumerable<object> right)
    {
        using var leftEnumerator = left.GetEnumerator();
        using var rightEnumerator = right.GetEnumerator();
        while (true)
        {
            bool leftNext = leftEnumerator.MoveNext();
            bool rightNext = rightEnumerator.MoveNext();
            if (leftNext != rightNext)
                return false;
            if (!leftNext)
                return true;
            if (!ValuesEqual(leftEnumerator.Current, rightEnumerator.Current))
                return false;
        }
    }

    private static bool MapsEqual(VmMap left, VmMap right)
    {
        if (left.Entries.Count != right.Entries.Count)
            return false;
        foreach (var entry in left.Entries)
        {
            if (!right.Entries.TryGetValue(entry.Key, out var rightValue))
                return false;
            if (!ValuesEqual(entry.Value, rightValue))
                return false;
        }
        return true;
    }

    private static int OrderedHash(string kind, IEnumerable<object> values)
    {
        var hash = new HashCode();
        hash.Add(kind, StringComparer.Ordinal);
        foreach (var value in values)
            hash.Add(ValueHash(value));
        return hash.ToHashCode();
    }

    private static int SetHash(VmSet set)
    {
        var hash = new HashCode();
        hash.Add("set", StringComparer.Ordinal);
        hash.Add(set.Entries.Count);
        int entriesHash = 0;
        foreach (var value in set.Entries)
            entriesHash += ValueHash(value);
        hash.Add(entriesHash);
        return hash.ToHashCode();
    }

    private static int MapHash(VmMap map)
    {
        var hash = new HashCode();
        hash.Add("map", StringComparer.Ordinal);
        hash.Add(map.Entries.Count);
        int entriesHash = 0;
        foreach (var entry in map.Entries)
            entriesHash += HashCode.Combine(ValueHash(entry.Key), ValueHash(entry.Value));
        hash.Add(entriesHash);
        return hash.ToHashCode();
    }

    private static bool IsNumeric(object value) => value is int or long or double;
}
