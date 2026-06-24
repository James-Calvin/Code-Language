using System.Buffers.Binary;
using System.Text;

namespace ConsoleApp1.Compiler;

enum DirectWasmValueType : byte
{
    I32 = 0x7f,
    I64 = 0x7e,
    F32 = 0x7d,
    F64 = 0x7c
}

sealed class DirectWasmFunctionBody
{
    private readonly List<DirectWasmValueType> _locals = [];
    private readonly List<byte> _instructions = [];

    public IReadOnlyList<DirectWasmValueType> Locals => _locals;
    public IReadOnlyList<byte> Instructions => _instructions;

    public int AddLocal(DirectWasmValueType type, int parameterCount)
    {
        int index = parameterCount + _locals.Count;
        _locals.Add(type);
        return index;
    }

    public void Op(byte opcode) => _instructions.Add(opcode);
    public void U32(uint value) => DirectWasmEncoding.WriteU32(_instructions, value);
    public void S32(int value) => DirectWasmEncoding.WriteS32(_instructions, value);
    public void S64(long value) => DirectWasmEncoding.WriteS64(_instructions, value);
    public void F64(double value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(value));
        _instructions.AddRange(bytes.ToArray());
    }

    public void LocalGet(int index) { Op(0x20); U32((uint)index); }
    public void LocalSet(int index) { Op(0x21); U32((uint)index); }
    public void LocalTee(int index) { Op(0x22); U32((uint)index); }
    public void GlobalGet(int index) { Op(0x23); U32((uint)index); }
    public void GlobalSet(int index) { Op(0x24); U32((uint)index); }
    public void Call(int index) { Op(0x10); U32((uint)index); }
    public void Branch(uint depth) { Op(0x0c); U32(depth); }
    public void BranchIf(uint depth) { Op(0x0d); U32(depth); }
    public void I32Const(int value) { Op(0x41); S32(value); }
    public void I64Const(long value) { Op(0x42); S64(value); }
    public void F64Const(double value) { Op(0x44); F64(value); }
    public void Load(byte opcode, uint alignment, uint offset = 0) { Op(opcode); U32(alignment); U32(offset); }
    public void Store(byte opcode, uint alignment, uint offset = 0) { Op(opcode); U32(alignment); U32(offset); }
}

sealed record DirectWasmSignature(
    IReadOnlyList<DirectWasmValueType> Parameters,
    IReadOnlyList<DirectWasmValueType> Results);

sealed record DirectWasmImport(string Module, string Name, int TypeIndex);
sealed record DirectWasmGlobal(DirectWasmValueType Type, bool Mutable, long IntegerInitialValue);
sealed record DirectWasmFunction(int TypeIndex, DirectWasmFunctionBody Body, string Name);
sealed record DirectWasmExport(string Name, byte Kind, int Index);
sealed record DirectWasmDataSegment(int Offset, byte[] Bytes);

sealed class DirectWasmModuleBuilder
{
    private readonly List<DirectWasmSignature> _types = [];
    private readonly List<DirectWasmImport> _imports = [];
    private readonly List<DirectWasmGlobal> _globals = [];
    private readonly List<DirectWasmFunction> _functions = [];
    private readonly List<DirectWasmExport> _exports = [];
    private readonly List<DirectWasmDataSegment> _dataSegments = [];
    private uint _memoryPages = 16;
    private int _nextDataOffset = 1024;

    public int StaticDataEnd => (_nextDataOffset + 7) & -8;

    public int ImportedFunctionCount => _imports.Count;

    public int AddType(IReadOnlyList<DirectWasmValueType> parameters, IReadOnlyList<DirectWasmValueType> results)
    {
        for (int index = 0; index < _types.Count; index++)
        {
            var candidate = _types[index];
            if (candidate.Parameters.SequenceEqual(parameters) && candidate.Results.SequenceEqual(results))
                return index;
        }
        _types.Add(new DirectWasmSignature(parameters.ToArray(), results.ToArray()));
        return _types.Count - 1;
    }

    public int AddFunctionImport(string module, string name, IReadOnlyList<DirectWasmValueType> parameters, IReadOnlyList<DirectWasmValueType> results)
    {
        int typeIndex = AddType(parameters, results);
        int functionIndex = _imports.Count;
        _imports.Add(new DirectWasmImport(module, name, typeIndex));
        return functionIndex;
    }

    public int ReserveFunction(string name, IReadOnlyList<DirectWasmValueType> parameters, IReadOnlyList<DirectWasmValueType> results)
    {
        int typeIndex = AddType(parameters, results);
        int functionIndex = _imports.Count + _functions.Count;
        _functions.Add(new DirectWasmFunction(typeIndex, new DirectWasmFunctionBody(), name));
        return functionIndex;
    }

    public DirectWasmFunctionBody GetFunctionBody(int functionIndex)
        => _functions[functionIndex - _imports.Count].Body;

    public int AddGlobal(DirectWasmValueType type, bool mutable = true, long initialValue = 0)
    {
        _globals.Add(new DirectWasmGlobal(type, mutable, initialValue));
        return _globals.Count - 1;
    }

    public void ExportFunction(string name, int functionIndex) => _exports.Add(new DirectWasmExport(name, 0, functionIndex));
    public void ExportMemory(string name = "memory") => _exports.Add(new DirectWasmExport(name, 2, 0));
    public void SetMemoryPages(uint pages) => _memoryPages = pages;

    public int AddData(ReadOnlySpan<byte> bytes, int alignment = 1)
    {
        _nextDataOffset = (_nextDataOffset + alignment - 1) & -alignment;
        int offset = _nextDataOffset;
        var copy = bytes.ToArray();
        _dataSegments.Add(new DirectWasmDataSegment(offset, copy));
        _nextDataOffset += copy.Length;
        return offset;
    }

    public byte[] Build()
    {
        var module = new List<byte> { 0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00 };

        AddSection(module, 1, section =>
        {
            DirectWasmEncoding.WriteU32(section, (uint)_types.Count);
            foreach (var type in _types)
            {
                section.Add(0x60);
                WriteTypes(section, type.Parameters);
                WriteTypes(section, type.Results);
            }
        });

        if (_imports.Count > 0)
        {
            AddSection(module, 2, section =>
            {
                DirectWasmEncoding.WriteU32(section, (uint)_imports.Count);
                foreach (var import in _imports)
                {
                    DirectWasmEncoding.WriteString(section, import.Module);
                    DirectWasmEncoding.WriteString(section, import.Name);
                    section.Add(0x00);
                    DirectWasmEncoding.WriteU32(section, (uint)import.TypeIndex);
                }
            });
        }

        AddSection(module, 3, section =>
        {
            DirectWasmEncoding.WriteU32(section, (uint)_functions.Count);
            foreach (var function in _functions)
                DirectWasmEncoding.WriteU32(section, (uint)function.TypeIndex);
        });

        AddSection(module, 5, section =>
        {
            DirectWasmEncoding.WriteU32(section, 1);
            section.Add(0x00);
            DirectWasmEncoding.WriteU32(section, _memoryPages);
        });

        if (_globals.Count > 0)
        {
            AddSection(module, 6, section =>
            {
                DirectWasmEncoding.WriteU32(section, (uint)_globals.Count);
                foreach (var global in _globals)
                {
                    section.Add((byte)global.Type);
                    section.Add(global.Mutable ? (byte)1 : (byte)0);
                    if (global.Type == DirectWasmValueType.I32)
                    {
                        section.Add(0x41);
                        DirectWasmEncoding.WriteS32(section, checked((int)global.IntegerInitialValue));
                    }
                    else if (global.Type == DirectWasmValueType.I64)
                    {
                        section.Add(0x42);
                        DirectWasmEncoding.WriteS64(section, global.IntegerInitialValue);
                    }
                    else if (global.Type == DirectWasmValueType.F64)
                    {
                        section.Add(0x44);
                        section.AddRange(BitConverter.GetBytes((double)global.IntegerInitialValue));
                    }
                    else throw new InvalidOperationException("Unsupported global type in direct Wasm spike.");
                    section.Add(0x0b);
                }
            });
        }

        ExportMemory();
        AddSection(module, 7, section =>
        {
            DirectWasmEncoding.WriteU32(section, (uint)_exports.Count);
            foreach (var export in _exports)
            {
                DirectWasmEncoding.WriteString(section, export.Name);
                section.Add(export.Kind);
                DirectWasmEncoding.WriteU32(section, (uint)export.Index);
            }
        });

        AddSection(module, 10, section =>
        {
            DirectWasmEncoding.WriteU32(section, (uint)_functions.Count);
            foreach (var function in _functions)
            {
                var body = new List<byte>();
                var groups = new List<(DirectWasmValueType Type, int Count)>();
                foreach (var local in function.Body.Locals)
                {
                    if (groups.Count > 0 && groups[^1].Type == local)
                        groups[^1] = (local, groups[^1].Count + 1);
                    else
                        groups.Add((local, 1));
                }
                DirectWasmEncoding.WriteU32(body, (uint)groups.Count);
                foreach (var group in groups)
                {
                    DirectWasmEncoding.WriteU32(body, (uint)group.Count);
                    body.Add((byte)group.Type);
                }
                body.AddRange(function.Body.Instructions);
                body.Add(0x0b);
                DirectWasmEncoding.WriteU32(section, (uint)body.Count);
                section.AddRange(body);
            }
        });

        if (_dataSegments.Count > 0)
        {
            AddSection(module, 11, section =>
            {
                DirectWasmEncoding.WriteU32(section, (uint)_dataSegments.Count);
                foreach (var segment in _dataSegments)
                {
                    section.Add(0x00);
                    section.Add(0x41);
                    DirectWasmEncoding.WriteS32(section, segment.Offset);
                    section.Add(0x0b);
                    DirectWasmEncoding.WriteU32(section, (uint)segment.Bytes.Length);
                    section.AddRange(segment.Bytes);
                }
            });
        }

        AddNameSection(module);
        return module.ToArray();
    }

    private void AddNameSection(List<byte> module)
    {
        var payload = new List<byte>();
        DirectWasmEncoding.WriteString(payload, "name");
        var functionNames = new List<byte>();
        DirectWasmEncoding.WriteU32(functionNames, (uint)_functions.Count);
        for (int index = 0; index < _functions.Count; index++)
        {
            DirectWasmEncoding.WriteU32(functionNames, (uint)(_imports.Count + index));
            DirectWasmEncoding.WriteString(functionNames, _functions[index].Name);
        }
        payload.Add(1);
        DirectWasmEncoding.WriteU32(payload, (uint)functionNames.Count);
        payload.AddRange(functionNames);
        module.Add(0);
        DirectWasmEncoding.WriteU32(module, (uint)payload.Count);
        module.AddRange(payload);
    }

    private static void WriteTypes(List<byte> bytes, IReadOnlyList<DirectWasmValueType> types)
    {
        DirectWasmEncoding.WriteU32(bytes, (uint)types.Count);
        foreach (var type in types) bytes.Add((byte)type);
    }

    private static void AddSection(List<byte> module, byte id, Action<List<byte>> writer)
    {
        var section = new List<byte>();
        writer(section);
        module.Add(id);
        DirectWasmEncoding.WriteU32(module, (uint)section.Count);
        module.AddRange(section);
    }
}

static class DirectWasmEncoding
{
    public static void WriteString(List<byte> bytes, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        WriteU32(bytes, (uint)encoded.Length);
        bytes.AddRange(encoded);
    }

    public static void WriteU32(List<byte> bytes, uint value)
    {
        do
        {
            byte next = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0) next |= 0x80;
            bytes.Add(next);
        } while (value != 0);
    }

    public static void WriteS32(List<byte> bytes, int value) => WriteSigned(bytes, value);
    public static void WriteS64(List<byte> bytes, long value) => WriteSigned(bytes, value);

    private static void WriteSigned(List<byte> bytes, long value)
    {
        bool more;
        do
        {
            byte next = (byte)(value & 0x7f);
            value >>= 7;
            bool sign = (next & 0x40) != 0;
            more = !((value == 0 && !sign) || (value == -1 && sign));
            if (more) next |= 0x80;
            bytes.Add(next);
        } while (more);
    }
}
