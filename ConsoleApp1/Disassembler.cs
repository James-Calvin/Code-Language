using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ConsoleApp1;

static class Disassembler
{
    public static string Disassemble(byte[] bytes)
    {
        BytecodeFormat.ValidateHeader(bytes);
        var sb = new StringBuilder();
        int ip = BytecodeFormat.HeaderSize;
        while (ip < bytes.Length)
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
                    if (ip + 4 > bytes.Length) throw new InvalidOperationException("Truncated operand");
                    int operand = BitConverter.ToInt32(bytes, ip);
                    ip += 4;
                    sb.AppendFormat(" {0}", operand);
                    if (op == OpCode.Call)
                    {
                        if (ip + 8 > bytes.Length) throw new InvalidOperationException("Truncated call operands");
                        int argc = BitConverter.ToInt32(bytes, ip); ip += 4;
                        int locals = BitConverter.ToInt32(bytes, ip); ip += 4;
                        sb.AppendFormat(" argc={0} locals={1}", argc, locals);
                    }
                    break;
                case OpCode.Add:
                case OpCode.Sub:
                case OpCode.Mul:
                case OpCode.Div:
                case OpCode.Print:
                case OpCode.Dup:
                case OpCode.Swap:
                case OpCode.Pop:
                case OpCode.Eq:
                case OpCode.Lt:
                case OpCode.Gt:
                case OpCode.Ret:
                case OpCode.PushString:
                case OpCode.Halt:
                    break;
                default:
                    throw new InvalidOperationException($"Unknown opcode {op} at {offset}");
            }
            sb.AppendLine();
            if (op == OpCode.Halt) break;
        }
        return sb.ToString();
    }

    public static void DisassembleTo(TextWriter writer, byte[] bytes)
    {
        writer.Write(Disassemble(bytes));
    }
}
