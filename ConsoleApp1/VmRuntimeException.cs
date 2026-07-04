using System;

namespace ConsoleApp1;

sealed class VmRuntimeException : Exception
{
    public int InstructionPointer { get; }
    public VmFrame[] CallStack { get; }
    public int Line { get; }
    public int Column { get; }
    public string? SourcePath { get; }
    public VmError Error { get; }

    public VmRuntimeException(string message, int ip, VmFrame[] callStack, int line, int column, string? sourcePath, VmError error)
        : base(message)
    {
        InstructionPointer = ip;
        CallStack = callStack;
        Line = line;
        Column = column;
        SourcePath = sourcePath;
        Error = error;
    }
}

readonly record struct VmFrame(int Ip, int Line, int Column, string? Source);

sealed record VmError(string Type, string Message, int Line, int Column, string? Source, VmFrame[] Frames)
{
    public override string ToString()
    {
        string location = Source is not null ? $"{Source}:{Line}:{Column}" : $"{Line}:{Column}";
        return $"{Type}: {Message} at {location}";
    }
}
