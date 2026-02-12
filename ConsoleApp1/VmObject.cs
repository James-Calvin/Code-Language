using System.Collections.Generic;

namespace ConsoleApp1;

sealed class VmObject
{
    public string TypeName { get; }
    public Dictionary<string, object> Fields { get; } = new(System.StringComparer.Ordinal);

    public VmObject(string typeName)
    {
        TypeName = typeName;
    }

    public override string ToString() => $"{TypeName} instance";
}
