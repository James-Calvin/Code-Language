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
    public bool IsMap => Name == "map";
    public bool IsSet => Name == "set";
    public bool IsQueue => Name == "queue";
    public bool IsStack => Name == "stack";
    public bool IsFallible => Name == "fallible";
    public bool IsError => Name == "__error";
    public bool IsBuiltInCollection => IsArray || IsMap || IsSet || IsQueue || IsStack;
    public bool IsIndexableCollection => IsArray || IsMap;

    public TypeRef NormalizeBuiltInShorthands()
    {
        if (IsFallible && TypeArguments.Count == 1)
        {
            return new TypeRef(
                "fallible",
                [
                    TypeArguments[0].NormalizeBuiltInShorthands(),
                    new TypeRef("integer", null, Line, Column)
                ],
                Line,
                Column);
        }

        if (TypeArguments.Count == 0)
            return this;

        return new TypeRef(Name, TypeArguments.Select(t => t.NormalizeBuiltInShorthands()).ToList(), Line, Column);
    }

    public bool TryGetFallibleTypeArguments(out TypeRef successTypeRef, out TypeRef errorCodeTypeRef)
    {
        var normalized = NormalizeBuiltInShorthands();
        if (normalized.IsFallible && normalized.TypeArguments.Count == 2)
        {
            successTypeRef = normalized.TypeArguments[0];
            errorCodeTypeRef = normalized.TypeArguments[1];
            return true;
        }

        successTypeRef = null!;
        errorCodeTypeRef = null!;
        return false;
    }

    public override string ToString()
    {
        if (TypeArguments.Count == 0) return Name;
        return $"{Name}<{string.Join(", ", TypeArguments.Select(t => t.ToString()))}>";
    }
}
