using System;

namespace ConsoleApp1;

sealed class VmRuntimeException : Exception
{
    public int InstructionPointer { get; }
    public int[] CallStack { get; }

    public VmRuntimeException(string message, int ip, int[] callStack)
        : base(message)
    {
        InstructionPointer = ip;
        CallStack = callStack;
    }
}
