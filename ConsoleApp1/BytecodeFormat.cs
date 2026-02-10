using System;
using System.Text;

namespace ConsoleApp1;

static class BytecodeFormat
{
    public const string MagicText = "CODE";
    public const byte Version = 1;
    public const int HeaderSize = 4 + 1; // magic + version

    public static void WriteHeader(Span<byte> span)
    {
        if (span.Length < HeaderSize) throw new ArgumentException("Header span too small");
        Encoding.ASCII.GetBytes(MagicText, span);
        span[4] = Version;
    }

    public static void ValidateHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
            throw new InvalidOperationException("Bytecode too short for header");
        if (!bytes[..4].SequenceEqual(Encoding.ASCII.GetBytes(MagicText)))
            throw new InvalidOperationException("Invalid bytecode magic");
        if (bytes[4] != Version)
            throw new InvalidOperationException($"Unsupported bytecode version {bytes[4]}, expected {Version}");
    }
}