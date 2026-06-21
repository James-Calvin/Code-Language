using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ConsoleApp1;

static class Disassembler
{
    public static string Disassemble(byte[] bytes)
    {
        var header = BytecodeFormat.ReadHeader(bytes);
        var metadata = BytecodeMetadata.Read(bytes, header);
        var sb = new StringBuilder();
        int ip = BytecodeFormat.HeaderSize;
        int codeEnd = BytecodeFormat.HeaderSize + header.CodeSize;
        while (ip < codeEnd)
        {
            int offset = ip;
            OpCode op = (OpCode)bytes[ip++];
            sb.AppendFormat("{0:D4}: {1}", offset, op);
            switch (op)
            {
                case OpCode.PushConst:
                case OpCode.Jump:
                case OpCode.JumpIfZero:
                case OpCode.JumpIfNotZero:
                case OpCode.Load:
                case OpCode.Store:
                case OpCode.LoadGlobal:
                case OpCode.StoreGlobal:
                case OpCode.Call:
                case OpCode.NewArray:
                    if (ip + 4 > codeEnd) throw new InvalidOperationException("Truncated operand");
                    int operand = BitConverter.ToInt32(bytes, ip);
                    ip += 4;
                    sb.AppendFormat(" {0}", operand);
                    if (op == OpCode.Call)
                    {
                        if (ip + 8 > codeEnd) throw new InvalidOperationException("Truncated call operands");
                        int argc = BitConverter.ToInt32(bytes, ip); ip += 4;
                        int locals = BitConverter.ToInt32(bytes, ip); ip += 4;
                        sb.AppendFormat(" argc={0} locals={1}", argc, locals);
                    }
                    break;
                case OpCode.PushReal:
                    if (ip + 8 > codeEnd) throw new InvalidOperationException("Truncated real operand");
                    double realOperand = BitConverter.Int64BitsToDouble(BitConverter.ToInt64(bytes, ip));
                    ip += 8;
                    sb.AppendFormat(" {0}", realOperand);
                    break;
                case OpCode.PushWideInteger:
                    if (ip + 8 > codeEnd) throw new InvalidOperationException("Truncated wide integer operand");
                    long wideIntegerOperand = BitConverter.ToInt64(bytes, ip);
                    ip += 8;
                    sb.AppendFormat(" {0}", wideIntegerOperand);
                    break;
                case OpCode.CheckedSizedNumericCast:
                    if (ip + 1 > codeEnd) throw new InvalidOperationException("Truncated sized numeric cast operand");
                    var sizedKind = (SizedNumericKind)bytes[ip++];
                    sb.AppendFormat(" {0}", sizedKind);
                    break;
                case OpCode.InterfaceCall:
                    if (ip + 8 > codeEnd) throw new InvalidOperationException("Truncated interface call header");
                    int explicitArgCount = BitConverter.ToInt32(bytes, ip); ip += 4;
                    int entryCount = BitConverter.ToInt32(bytes, ip); ip += 4;
                    sb.AppendFormat(" explicitArgs={0} entries={1}", explicitArgCount, entryCount);
                    for (int i = 0; i < entryCount; i++)
                    {
                        if (ip + 12 > codeEnd) throw new InvalidOperationException("Truncated interface dispatch entry");
                        int typeId = BitConverter.ToInt32(bytes, ip); ip += 4;
                        if ((uint)typeId >= (uint)metadata.Types.Count) throw new InvalidOperationException("Interface type ID is out of range");
                        int target = BitConverter.ToInt32(bytes, ip); ip += 4;
                        int locals = BitConverter.ToInt32(bytes, ip); ip += 4;
                        sb.AppendFormat(" [{0}:{1}->{2},locals={3}]", typeId, metadata.Types[typeId].Name, target, locals);
                    }
                    break;
                case OpCode.Add:
                case OpCode.Sub:
                case OpCode.Mul:
                case OpCode.Div:
                case OpCode.IntDiv:
                case OpCode.Mod:
                case OpCode.Print:
                case OpCode.Dup:
                case OpCode.Swap:
                case OpCode.Pop:
                case OpCode.Eq:
                case OpCode.Lt:
                case OpCode.Gt:
                case OpCode.Ret:
                case OpCode.ArrayLength:
                case OpCode.ArrayGet:
                case OpCode.ArraySet:
                case OpCode.ArrayAppend:
                case OpCode.ArrayRemoveAt:
                case OpCode.NewMap:
                case OpCode.MapGet:
                case OpCode.MapSet:
                case OpCode.MapContains:
                case OpCode.MapRemove:
                case OpCode.NewSet:
                case OpCode.SetAdd:
                case OpCode.SetContains:
                case OpCode.SetRemove:
                case OpCode.NewQueue:
                case OpCode.QueueEnqueue:
                case OpCode.QueueDequeue:
                case OpCode.QueuePeek:
                case OpCode.NewStack:
                case OpCode.StackPush:
                case OpCode.StackPop:
                case OpCode.StackPeek:
                case OpCode.NewArrayN:
                case OpCode.OptionalNone:
                case OpCode.OptionalHas:
                case OpCode.OptionalValue:
                case OpCode.OptionalOr:
                case OpCode.FallibleSuccess:
                case OpCode.FallibleError:
                case OpCode.FallibleIsError:
                case OpCode.FallibleValue:
                case OpCode.FallibleErrorCode:
                case OpCode.FallibleErrorMessage:
                case OpCode.CastInteger:
                case OpCode.CastWhole:
                case OpCode.CastReal:
                case OpCode.ThrowError:
                case OpCode.GetTypeName:
                case OpCode.TimeUnixMs:
                case OpCode.TimeUnixUs:
                case OpCode.TimeMonoNs:
                case OpCode.TimeMonoTicks:
                case OpCode.TimeMonoTicksPerSecond:
                case OpCode.Halt:
                    break;
                case OpCode.PushString:
                case OpCode.NewObject:
                case OpCode.NewRecord:
                case OpCode.GetField:
                case OpCode.SetField:
                case OpCode.HostCall:
                    if (ip + 4 > codeEnd) throw new InvalidOperationException("Truncated metadata ID");
                    int id = BitConverter.ToInt32(bytes, ip); ip += 4;
                    switch (op)
                    {
                        case OpCode.PushString:
                            if ((uint)id >= (uint)metadata.Strings.Count) throw new InvalidOperationException("String ID is out of range");
                            sb.AppendFormat(" #{0} \"{1}\"", id, metadata.Strings[id]);
                            break;
                        case OpCode.NewObject:
                        case OpCode.NewRecord:
                            if ((uint)id >= (uint)metadata.Types.Count) throw new InvalidOperationException("Type ID is out of range");
                            sb.AppendFormat(" #{0} {1}", id, metadata.Types[id].Name);
                            break;
                        case OpCode.GetField:
                        case OpCode.SetField:
                            if ((uint)id >= (uint)metadata.Fields.Count) throw new InvalidOperationException("Field slot is out of range");
                            sb.AppendFormat(" slot={0} {1}", id, metadata.Fields[id]);
                            break;
                        case OpCode.HostCall:
                            if ((uint)id >= (uint)metadata.HostBindings.Count) throw new InvalidOperationException("Host binding ID is out of range");
                            var binding = metadata.HostBindings[id];
                            sb.AppendFormat(" #{0} {1} argc={2}", id, binding.Symbol, binding.Arity);
                            break;
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unknown opcode {op} at {offset}");
            }
            sb.AppendLine();
            if (op == OpCode.Halt) break;
        }
        if (header.DebugCount > 0)
        {
            sb.AppendLine("Debug map:");
            int debugOffset = BytecodeFormat.HeaderSize + header.CodeSize;
            for (int i = 0; i < header.DebugCount; i++)
            {
                int ipEntry = BitConverter.ToInt32(bytes, debugOffset); debugOffset += 4;
                int line = BitConverter.ToInt32(bytes, debugOffset); debugOffset += 4;
                int col = BitConverter.ToInt32(bytes, debugOffset); debugOffset += 4;
                sb.AppendLine($"  ip {ipEntry}: line {line}, col {col}");
            }
        }
        return sb.ToString();
    }

    public static void DisassembleTo(TextWriter writer, byte[] bytes)
    {
        writer.Write(Disassemble(bytes));
    }
}
