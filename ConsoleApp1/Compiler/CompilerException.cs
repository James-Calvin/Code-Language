using System;

namespace ConsoleApp1.Compiler;

sealed class CompilerException : Exception
{
    public int Line { get; }
    public int Column { get; }
    public CompilerException(string message, int line, int column) : base(message)
    {
        Line = line;
        Column = column;
    }
}