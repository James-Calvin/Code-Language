namespace ConsoleApp1;

sealed class VmObject
{
    public int TypeId { get; }
    public string TypeName { get; }
    public bool IsRecord { get; }
    public object?[] Fields { get; }
    public bool[] InitializedFields { get; }
    public int[] HashFieldSlots { get; }

    public VmObject(int typeId, string typeName, bool isRecord, int fieldCount, int[]? hashFieldSlots = null)
    {
        TypeId = typeId;
        TypeName = typeName;
        IsRecord = isRecord;
        Fields = new object?[fieldCount];
        InitializedFields = new bool[fieldCount];
        HashFieldSlots = hashFieldSlots ?? [];
    }

    public override string ToString() => IsRecord ? $"{TypeName} value" : $"{TypeName} instance";
}
