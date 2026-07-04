using System.Buffers.Binary;
using System.Text;

namespace ConsoleApp1;

sealed record BytecodeHostBindingMetadata(string Symbol, int Arity);
sealed record BytecodeTypeMetadata(string Name, bool IsRecord, int[] FieldSlots, int[] HashFieldSlots);
sealed record BytecodeCallableMetadata(int TargetIp, int FrameSize, string Name);

sealed class BytecodeMetadata
{
    public IReadOnlyList<string> Strings { get; }
    public IReadOnlyList<string> Fields { get; }
    public IReadOnlyList<BytecodeHostBindingMetadata> HostBindings { get; }
    public IReadOnlyList<BytecodeTypeMetadata> Types { get; }
    public IReadOnlyList<BytecodeCallableMetadata> Callables { get; }

    public BytecodeMetadata(
        IReadOnlyList<string> strings,
        IReadOnlyList<string> fields,
        IReadOnlyList<BytecodeHostBindingMetadata> hostBindings,
        IReadOnlyList<BytecodeTypeMetadata> types,
        IReadOnlyList<BytecodeCallableMetadata> callables)
    {
        Strings = strings;
        Fields = fields;
        HostBindings = hostBindings;
        Types = types;
        Callables = callables;
    }

    public static BytecodeMetadata Read(ReadOnlySpan<byte> bytes, BytecodeFormat.Header header)
    {
        int offset = BytecodeFormat.GetMetadataOffset(header);
        if (offset + 8 > bytes.Length)
            throw new InvalidOperationException("Bytecode metadata header is truncated.");
        if (!bytes.Slice(offset, 4).SequenceEqual(Encoding.ASCII.GetBytes(BytecodeFormat.MetadataMagicText)))
            throw new InvalidOperationException("Bytecode metadata magic is missing.");
        int payloadSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 4, 4));
        if (payloadSize < 0 || offset + 8 + payloadSize != bytes.Length)
            throw new InvalidOperationException("Bytecode metadata size is invalid.");

        var reader = new MetadataReader(bytes.Slice(offset + 8, payloadSize));
        int stringCount = reader.ReadCount("string");
        var strings = new List<string>(stringCount);
        for (int i = 0; i < stringCount; i++) strings.Add(reader.ReadString());

        int fieldCount = reader.ReadCount("field");
        var fields = new List<string>(fieldCount);
        for (int i = 0; i < fieldCount; i++) fields.Add(strings[reader.ReadIndex(strings.Count, "field string")]);

        int hostCount = reader.ReadCount("host binding");
        var hosts = new List<BytecodeHostBindingMetadata>(hostCount);
        for (int i = 0; i < hostCount; i++)
        {
            string symbol = strings[reader.ReadIndex(strings.Count, "host symbol")];
            int arity = reader.ReadNonNegative("host arity");
            hosts.Add(new BytecodeHostBindingMetadata(symbol, arity));
        }

        int typeCount = reader.ReadCount("type");
        var types = new List<BytecodeTypeMetadata>(typeCount);
        for (int i = 0; i < typeCount; i++)
        {
            string name = strings[reader.ReadIndex(strings.Count, "type name")];
            bool isRecord = reader.ReadByte() switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidOperationException("Bytecode type kind is invalid.")
            };
            int declaredCount = reader.ReadCount("declared field");
            var slots = new int[declaredCount];
            for (int field = 0; field < declaredCount; field++)
                slots[field] = reader.ReadIndex(fields.Count, "field slot");
            int hashCount = reader.ReadCount("hash field");
            var hashSlots = new int[hashCount];
            for (int field = 0; field < hashCount; field++)
                hashSlots[field] = reader.ReadIndex(fields.Count, "hash field slot");
            types.Add(new BytecodeTypeMetadata(name, isRecord, slots, hashSlots));
        }

        int callableCount = reader.ReadCount("callable");
        var callables = new List<BytecodeCallableMetadata>(callableCount);
        for (int i = 0; i < callableCount; i++)
        {
            int targetIp = reader.ReadNonNegative("callable target");
            if (targetIp < BytecodeFormat.HeaderSize || targetIp >= BytecodeFormat.HeaderSize + header.CodeSize)
                throw new InvalidOperationException("Bytecode callable target is outside code.");
            int frameSize = reader.ReadNonNegative("callable frame size");
            string name = strings[reader.ReadIndex(strings.Count, "callable name")];
            callables.Add(new BytecodeCallableMetadata(targetIp, frameSize, name));
        }

        reader.EnsureEnd();
        return new BytecodeMetadata(strings, fields, hosts, types, callables);
    }

    private ref struct MetadataReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;

        public MetadataReader(ReadOnlySpan<byte> bytes) { _bytes = bytes; _offset = 0; }
        public byte ReadByte() { Ensure(1); return _bytes[_offset++]; }
        public int ReadNonNegative(string name)
        {
            Ensure(4);
            int value = BinaryPrimitives.ReadInt32LittleEndian(_bytes.Slice(_offset, 4));
            _offset += 4;
            if (value < 0) throw new InvalidOperationException($"Bytecode metadata {name} is negative.");
            return value;
        }
        public int ReadCount(string name) => ReadNonNegative($"{name} count");
        public int ReadIndex(int count, string name)
        {
            int value = ReadNonNegative(name);
            if (value >= count) throw new InvalidOperationException($"Bytecode metadata {name} is out of range.");
            return value;
        }
        public string ReadString()
        {
            int length = ReadNonNegative("string length");
            Ensure(length);
            string value = Encoding.UTF8.GetString(_bytes.Slice(_offset, length));
            _offset += length;
            return value;
        }
        public void EnsureEnd()
        {
            if (_offset != _bytes.Length) throw new InvalidOperationException("Bytecode metadata has trailing data.");
        }
        private void Ensure(int count)
        {
            if (count < 0 || _offset + count > _bytes.Length)
                throw new InvalidOperationException("Bytecode metadata is truncated.");
        }
    }
}
