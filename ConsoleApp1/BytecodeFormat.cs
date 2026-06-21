using System;
using System.Buffers.Binary;
using System.Text;

namespace ConsoleApp1;

static class BytecodeFormat
{
    public const string MagicText = "CODE";
    public const byte Version = 10;
    public const string MetadataMagicText = "META";

    // magic (4) + version (1) + codeSize (4) + debugCount (4)
    public const int HeaderSize = 4 + 1 + 4 + 4;
    public const int DebugEntrySize = 12; // ip, line, column (3 * 4 bytes)

    public readonly record struct Header(int CodeSize, int DebugCount);

    public static void WriteHeader(Span<byte> span, int codeSize, int debugCount)
    {
        if (span.Length < HeaderSize) throw new ArgumentException("Header span too small");
        Encoding.ASCII.GetBytes(MagicText, span);
        span[4] = Version;
        BitConverter.GetBytes(codeSize).AsSpan().CopyTo(span[5..9]);
        BitConverter.GetBytes(debugCount).AsSpan().CopyTo(span[9..13]);
    }

    public static Header ReadHeader(ReadOnlySpan<byte> bytes)
    {
        ValidateHeader(bytes);
        int codeSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(5, 4));
        int debugCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(9, 4));
        if (HeaderSize + codeSize + debugCount * DebugEntrySize + 8 > bytes.Length)
            throw new InvalidOperationException("Bytecode truncated: header sizes exceed file length.");
        return new Header(codeSize, debugCount);
    }

    public static int GetMetadataOffset(Header header)
        => HeaderSize + header.CodeSize + header.DebugCount * DebugEntrySize;

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
