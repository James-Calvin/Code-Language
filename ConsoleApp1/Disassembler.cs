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
                case OpCode.InterfaceCall:
                    if (ip + 8 > codeEnd) throw new InvalidOperationException("Truncated interface call header");
                    int explicitArgCount = BitConverter.ToInt32(bytes, ip); ip += 4;
                    int entryCount = BitConverter.ToInt32(bytes, ip); ip += 4;
                    sb.AppendFormat(" explicitArgs={0} entries={1}", explicitArgCount, entryCount);
                    for (int i = 0; i < entryCount; i++)
                    {
                        if (ip + 4 > codeEnd) throw new InvalidOperationException("Truncated interface dispatch type length");
                        int typeLen = BitConverter.ToInt32(bytes, ip); ip += 4;
                        if (ip + typeLen > codeEnd) throw new InvalidOperationException("Truncated interface dispatch type");
                        string runtimeType = Encoding.UTF8.GetString(bytes, ip, typeLen); ip += typeLen;
                        if (ip + 8 > codeEnd) throw new InvalidOperationException("Truncated interface dispatch target/locals");
                        int target = BitConverter.ToInt32(bytes, ip); ip += 4;
                        int locals = BitConverter.ToInt32(bytes, ip); ip += 4;
                        sb.AppendFormat(" [{0}->{1},locals={2}]", runtimeType, target, locals);
                    }
                    break;
                case OpCode.Add:
                case OpCode.Sub:
                case OpCode.Mul:
                case OpCode.Div:
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
                case OpCode.GetField:
                case OpCode.SetField:
                case OpCode.HostCall:
                    if (ip + 4 > codeEnd) throw new InvalidOperationException("Truncated string length");
                    int len = BitConverter.ToInt32(bytes, ip); ip += 4;
                    if (ip + len > codeEnd) throw new InvalidOperationException("Truncated string data");
                    string str = Encoding.UTF8.GetString(bytes, ip, len);
                    ip += len;
                    sb.AppendFormat(" \"{0}\"", str);
                    if (op == OpCode.HostCall)
                    {
                        if (ip + 4 > codeEnd) throw new InvalidOperationException("Truncated host call arg count");
                        int argc = BitConverter.ToInt32(bytes, ip); ip += 4;
                        sb.AppendFormat(" argc={0}", argc);
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
