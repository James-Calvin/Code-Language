using System.Collections.Generic;
using System.Linq;

namespace ConsoleApp1.Compiler;

sealed class TypeRef
{
    public string Name { get; }
    public IReadOnlyList<TypeRef> TypeArguments { get; }
    public int Line { get; }
    public int Column { get; }

    public TypeRef(string name, IReadOnlyList<TypeRef>? typeArguments, int line, int column)
    {
        Name = name;
        TypeArguments = typeArguments ?? [];
        Line = line;
        Column = column;
    }

    public bool IsOptional => Name == "optional";
    public bool IsArray => Name == "array";

    public override string ToString()
    {
        if (TypeArguments.Count == 0) return Name;
        return $"{Name}<{string.Join(", ", TypeArguments.Select(t => t.ToString()))}>";
    }
}
