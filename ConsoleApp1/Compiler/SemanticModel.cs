using System.Collections.Generic;
using System.Linq;

namespace ConsoleApp1.Compiler;

sealed record SemanticExpressionInfo(TypeSymbol Type, TypeRef TypeRef);

sealed class SemanticModel
{
    private readonly Dictionary<Expr, SemanticExpressionInfo> _expressions = new(ReferenceEqualityComparer.Instance);

    public void Record(Expr expression, TypeSymbol type, TypeRef typeRef)
        => _expressions[expression] = new SemanticExpressionInfo(type, typeRef);

    public SemanticExpressionInfo Get(Expr expression)
        => _expressions.TryGetValue(expression, out var info)
            ? info
            : throw new InvalidOperationException($"No semantic type was recorded for {Describe(expression)}.");

    public bool TryGet(Expr expression, out SemanticExpressionInfo info)
        => _expressions.TryGetValue(expression, out info!);

    private static string Describe(Expr expression) => expression switch
    {
        Variable variable => $"Variable '{variable.Name.Lexeme}' at {variable.Name.Line}:{variable.Name.Column}",
        _ => expression.GetType().Name
    };
}

sealed record TypedFieldLayout(string Name, TypeRef Type, int Index, FieldHashRole HashRole);
sealed record TypedTypeLayout(string Name, bool IsRecord, IReadOnlyList<TypedFieldLayout> Fields);

sealed class TypedProgram
{
    public IList<Stmt> Statements { get; }
    public SemanticModel Semantics { get; }
    public IReadOnlyDictionary<string, TypedTypeLayout> Types { get; }

    private TypedProgram(
        IList<Stmt> statements,
        SemanticModel semantics,
        IReadOnlyDictionary<string, TypedTypeLayout> types)
    {
        Statements = statements;
        Semantics = semantics;
        Types = types;
    }

    public static TypedProgram Lower(IList<Stmt> statements, SemanticModel semantics)
    {
        var types = new Dictionary<string, TypedTypeLayout>(StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            if (statement is not ObjectDecl type)
                continue;
            var instanceFields = type.Fields.Where(field => !field.IsStatic).ToList();
            var fields = new List<TypedFieldLayout>(instanceFields.Count);
            for (int index = 0; index < instanceFields.Count; index++)
                fields.Add(new TypedFieldLayout(instanceFields[index].Name.Lexeme, instanceFields[index].Type, index, instanceFields[index].HashRole));
            types[type.Name.Lexeme] = new TypedTypeLayout(type.Name.Lexeme, type.IsRecord, fields);
        }
        return new TypedProgram(statements, semantics, types);
    }
}
