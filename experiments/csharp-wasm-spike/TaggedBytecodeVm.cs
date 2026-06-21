using System.Buffers.Binary;
using System.Text;

internal sealed class TaggedBytecodeVm
{
    private const int HeaderSize = 13;
    private readonly Instruction[] instructions;
    private readonly HostKind[] hostBindings;
    private readonly int fieldCount;
    private readonly Value[] stack = new Value[2048];
    private readonly Value[] locals = new Value[8192];
    private readonly Value[] globals = new Value[256];
    private readonly CallFrame[] callFrames = new CallFrame[256];
    private readonly Value[][] arrays = new Value[32][];
    private readonly Value[][] objects = new Value[2048][];
    private int stackPointer;
    private int framePointer;
    private int frameBase;
    private int frameSize = 32;
    private int localsTop = 32;
    private int arrayCount;
    private int objectCount;

    public TaggedBytecodeVm(byte[] bytecode)
    {
        if (bytecode.Length < HeaderSize || bytecode[0] != (byte)'C' || bytecode[1] != (byte)'O'
            || bytecode[2] != (byte)'D' || bytecode[3] != (byte)'E' || bytecode[4] != 10)
            throw new InvalidOperationException("The spike requires bytecode v10.");

        int codeSize = ReadInt(bytecode, 5);
        int debugCount = ReadInt(bytecode, 9);
        int codeEnd = checked(HeaderSize + codeSize);
        int metadataOffset = checked(codeEnd + debugCount * 12);
        (hostBindings, fieldCount) = ReadMetadata(bytecode, metadataOffset);
        instructions = Decode(bytecode, codeEnd);
    }

    public void Run()
    {
        int ip = 0;
        while ((uint)ip < (uint)instructions.Length)
        {
            Instruction instruction = instructions[ip++];
            switch (instruction.Op)
            {
                case 0x01: Push(Value.NumberValue(instruction.A)); break;
                case 0x02: { double right = PopNumber(); double left = PopNumber(); Push(Value.NumberValue(left + right)); break; }
                case 0x03: { double right = PopNumber(); double left = PopNumber(); Push(Value.NumberValue(left - right)); break; }
                case 0x04: { double right = PopNumber(); double left = PopNumber(); Push(Value.NumberValue(left * right)); break; }
                case 0x05: { double right = PopNumber(); double left = PopNumber(); Push(Value.NumberValue(left / right)); break; }
                case 0x07: Push(Peek()); break;
                case 0x08:
                {
                    Value right = Pop();
                    Value left = Pop();
                    Push(right);
                    Push(left);
                    break;
                }
                case 0x09: Pop(); break;
                case 0x0A: ip = instruction.A; break;
                case 0x0B: if (PopNumber() == 0) ip = instruction.A; break;
                case 0x0C: if (PopNumber() != 0) ip = instruction.A; break;
                case 0x0D: Push(locals[frameBase + instruction.A]); break;
                case 0x0E: locals[frameBase + instruction.A] = Pop(); break;
                case 0x0F:
                {
                    Value right = Pop();
                    Value left = Pop();
                    Push(Value.NumberValue(left.Kind == right.Kind && left.Number == right.Number && left.Handle == right.Handle ? 1 : 0));
                    break;
                }
                case 0x10: { double right = PopNumber(); double left = PopNumber(); Push(Value.NumberValue(left < right ? 1 : 0)); break; }
                case 0x11: { double right = PopNumber(); double left = PopNumber(); Push(Value.NumberValue(left > right ? 1 : 0)); break; }
                case 0x12:
                {
                    int newFrameSize = Math.Max(instruction.C, instruction.B);
                    int newFrameBase = localsTop;
                    Array.Clear(locals, newFrameBase, newFrameSize);
                    for (int argument = instruction.B - 1; argument >= 0; argument--)
                        locals[newFrameBase + argument] = Pop();
                    callFrames[framePointer++] = new CallFrame(ip, frameBase, frameSize, localsTop);
                    frameBase = newFrameBase;
                    frameSize = newFrameSize;
                    localsTop = newFrameBase + newFrameSize;
                    ip = instruction.A;
                    break;
                }
                case 0x13:
                {
                    Value result = Pop();
                    if (framePointer == 0) return;
                    CallFrame frame = callFrames[--framePointer];
                    ip = frame.ReturnIp;
                    frameBase = frame.FrameBase;
                    frameSize = frame.FrameSize;
                    localsTop = frame.LocalsTop;
                    Push(result);
                    break;
                }
                case 0x17:
                {
                    Value array = PopKind(ValueKind.Array);
                    Push(Value.NumberValue(arrays[array.Handle].Length));
                    break;
                }
                case 0x18:
                {
                    int index = (int)PopNumber();
                    Value array = PopKind(ValueKind.Array);
                    Push(arrays[array.Handle][index]);
                    break;
                }
                case 0x19:
                {
                    int size = (int)PopNumber();
                    int handle = arrayCount++;
                    arrays[handle] = new Value[size];
                    Push(Value.HandleValue(ValueKind.Array, handle));
                    break;
                }
                case 0x1E:
                {
                    Value value = Pop();
                    int index = (int)PopNumber();
                    Value array = PopKind(ValueKind.Array);
                    arrays[array.Handle][index] = value;
                    Push(value);
                    break;
                }
                case 0x1F:
                {
                    int handle = objectCount++;
                    objects[handle] = new Value[Math.Max(1, fieldCount)];
                    Push(Value.HandleValue(ValueKind.Object, handle));
                    break;
                }
                case 0x20:
                {
                    Value target = PopKind(ValueKind.Object);
                    Push(objects[target.Handle][instruction.A]);
                    break;
                }
                case 0x21:
                {
                    Value value = Pop();
                    Value target = PopKind(ValueKind.Object);
                    objects[target.Handle][instruction.A] = value;
                    Push(value);
                    break;
                }
                case 0x24: { double right = PopNumber(); double left = PopNumber(); Push(Value.NumberValue(left % right)); break; }
                case 0x2A:
                {
                    HostKind host = hostBindings[instruction.A];
                    if (host == HostKind.SquareRoot) Push(Value.NumberValue(Math.Sqrt(PopNumber())));
                    else if (host == HostKind.Print) { Pop(); Push(Value.NumberValue(0)); }
                    else throw new InvalidOperationException($"Unsupported spike host binding {instruction.A}.");
                    break;
                }
                case 0x45: Push(Value.NumberValue(instruction.Real)); break;
                case 0x4C: Push(globals[instruction.A]); break;
                case 0x4D: globals[instruction.A] = Pop(); break;
                case 0xFF: return;
                default: throw new InvalidOperationException($"Unsupported spike opcode 0x{instruction.Op:X2}.");
            }
        }
    }

    private void Push(Value value) => stack[stackPointer++] = value;
    private Value Peek() => stack[stackPointer - 1];
    private Value Pop() => stack[--stackPointer];
    private double PopNumber()
    {
        Value value = Pop();
        if (value.Kind != ValueKind.Number) throw new InvalidOperationException("Expected numeric value.");
        return value.Number;
    }
    private Value PopKind(ValueKind kind)
    {
        Value value = Pop();
        if (value.Kind != kind) throw new InvalidOperationException($"Expected {kind} value.");
        return value;
    }

    private static Instruction[] Decode(byte[] bytes, int codeEnd)
    {
        var decoded = new List<(int ByteIp, Instruction Instruction)>();
        int[] byteToInstruction = new int[codeEnd + 1];
        Array.Fill(byteToInstruction, -1);
        int offset = HeaderSize;
        while (offset < codeEnd)
        {
            int byteIp = offset;
            byte op = bytes[offset++];
            int a = 0, b = 0, c = 0;
            double real = 0;
            switch (op)
            {
                case 0x01: case 0x0A: case 0x0B: case 0x0C: case 0x0D: case 0x0E:
                case 0x14: case 0x16: case 0x1F: case 0x20: case 0x21: case 0x2A:
                case 0x3E: case 0x4C: case 0x4D:
                    a = ReadInt(bytes, offset); offset += 4; break;
                case 0x12:
                    a = ReadInt(bytes, offset); b = ReadInt(bytes, offset + 4); c = ReadInt(bytes, offset + 8); offset += 12; break;
                case 0x23:
                {
                    a = ReadInt(bytes, offset); b = ReadInt(bytes, offset + 4); offset += 8 + b * 12; break;
                }
                case 0x45: real = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, 8))); offset += 8; break;
                case 0x4A: offset += 8; break;
                case 0x4B: offset += 1; break;
            }
            byteToInstruction[byteIp] = decoded.Count;
            decoded.Add((byteIp, new Instruction(op, a, b, c, real)));
        }

        var result = new Instruction[decoded.Count];
        for (int index = 0; index < decoded.Count; index++)
        {
            Instruction instruction = decoded[index].Instruction;
            if (instruction.Op is 0x0A or 0x0B or 0x0C or 0x12)
            {
                int target = instruction.A < byteToInstruction.Length ? byteToInstruction[instruction.A] : -1;
                if (target < 0) throw new InvalidOperationException("Invalid branch/call target in spike bytecode.");
                instruction = instruction with { A = target };
            }
            result[index] = instruction;
        }
        return result;
    }

    private static (HostKind[] Hosts, int FieldCount) ReadMetadata(byte[] bytes, int offset)
    {
        if (!bytes.AsSpan(offset, 4).SequenceEqual("META"u8)) throw new InvalidOperationException("Missing v10 metadata.");
        int payloadSize = ReadInt(bytes, offset + 4);
        var reader = new MetadataReader(bytes.AsSpan(offset + 8, payloadSize));
        int stringCount = reader.ReadInt();
        var strings = new string[stringCount];
        for (int index = 0; index < stringCount; index++) strings[index] = reader.ReadString();
        int fieldCount = reader.ReadInt();
        for (int index = 0; index < fieldCount; index++) reader.ReadInt();
        int hostCount = reader.ReadInt();
        var hosts = new HostKind[hostCount];
        for (int index = 0; index < hostCount; index++)
        {
            string symbol = strings[reader.ReadInt()];
            reader.ReadInt();
            hosts[index] = symbol switch
            {
                "std.math.square_root" => HostKind.SquareRoot,
                "standard.input_output.print" => HostKind.Print,
                _ => HostKind.Unsupported
            };
        }
        return (hosts, fieldCount);
    }

    private static int ReadInt(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));

    private enum ValueKind : byte { Number, Array, Object }
    private enum HostKind : byte { Unsupported, Print, SquareRoot }
    private readonly record struct Instruction(byte Op, int A, int B, int C, double Real);
    private readonly record struct CallFrame(int ReturnIp, int FrameBase, int FrameSize, int LocalsTop);
    private readonly record struct Value(ValueKind Kind, double Number, int Handle)
    {
        public static Value NumberValue(double value) => new(ValueKind.Number, value, 0);
        public static Value HandleValue(ValueKind kind, int handle) => new(kind, 0, handle);
    }

    private ref struct MetadataReader
    {
        private readonly ReadOnlySpan<byte> bytes;
        private int offset;
        public MetadataReader(ReadOnlySpan<byte> bytes) { this.bytes = bytes; offset = 0; }
        public int ReadInt()
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
            offset += 4;
            return value;
        }
        public string ReadString()
        {
            int length = ReadInt();
            string value = Encoding.UTF8.GetString(bytes.Slice(offset, length));
            offset += length;
            return value;
        }
    }
}
