using System.Collections.Generic;

namespace ConsoleApp1;

sealed class VmObject
{
    public string TypeName { get; }
    public bool IsRecord { get; }
    public Dictionary<string, object> Fields { get; } = new(System.StringComparer.Ordinal);

    public VmObject(string typeName, bool isRecord = false)
    {
        TypeName = typeName;
        IsRecord = isRecord;
    }

    public override string ToString() => IsRecord ? $"{TypeName} value" : $"{TypeName} instance";
}
