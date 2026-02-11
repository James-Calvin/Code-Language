using System;

namespace ConsoleApp1;

sealed class VmRuntimeException : Exception
{
    public int InstructionPointer { get; }
    public VmFrame[] CallStack { get; }
    public int Line { get; }
    public int Column { get; }
    public VmError Error { get; }

    public VmRuntimeException(string message, int ip, VmFrame[] callStack, int line, int column, VmError error)
        : base(message)
    {
        InstructionPointer = ip;
        CallStack = callStack;
        Line = line;
        Column = column;
        Error = error;
    }
}

readonly record struct VmFrame(int Ip, int Line, int Column);

sealed record VmError(string Type, string Message, int Line, int Column, VmFrame[] Frames)
{
    public override string ToString()
    {
        return $"{Type}: {Message} at {Line}:{Column}";
    }
}
