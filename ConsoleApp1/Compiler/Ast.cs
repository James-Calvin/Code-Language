using System.Collections.Generic;

namespace ConsoleApp1.Compiler;

abstract class Expr { }

sealed class Binary : Expr
{
    public Expr Left { get; }
    public Token Operator { get; }
    public Expr Right { get; }
    public Binary(Expr left, Token op, Expr right) { Left = left; Operator = op; Right = right; }
}

sealed class Unary : Expr
{
    public Token Operator { get; }
    public Expr Right { get; }
    public Unary(Token op, Expr right) { Operator = op; Right = right; }
}

sealed class Literal : Expr
{
    public object? Value { get; }
    public int Line { get; }
    public int Column { get; }
    public Literal(object? value, int line, int column) { Value = value; Line = line; Column = column; }
}

sealed class InterpString : Expr
{
    public IReadOnlyList<object> Parts { get; } // string segments or Expr
    public int Line { get; }
    public int Column { get; }
    public InterpString(IReadOnlyList<object> parts, int line, int column) { Parts = parts; Line = line; Column = column; }
}

sealed class ArrayLiteral : Expr
{
    public IReadOnlyList<Expr> Elements { get; }
    public int Line { get; }
    public int Column { get; }
    public TypeRef? ResolvedTypeRef { get; set; }
    public ArrayLiteral(IReadOnlyList<Expr> elements, int line, int column) { Elements = elements; Line = line; Column = column; }
}

sealed class NewArrayExpr : Expr
{
    public TypeRef ElementType { get; }
    public Expr Size { get; }
    public int Line { get; }
    public int Column { get; }
    public NewArrayExpr(TypeRef elementType, Expr size, int line, int column)
    {
        ElementType = elementType; Size = size; Line = line; Column = column;
    }
}

sealed class NewCollectionExpr : Expr
{
    public TypeRef CollectionType { get; }
    public int Line { get; }
    public int Column { get; }

    public NewCollectionExpr(TypeRef collectionType, int line, int column)
    {
        CollectionType = collectionType;
        Line = line;
        Column = column;
    }
}

sealed class ArrayLengthExpr : Expr
{
    public Expr Target { get; }
    public Token DotToken { get; }
    public ArrayLengthExpr(Expr target, Token dot) { Target = target; DotToken = dot; }
}

sealed class ArrayIndexExpr : Expr
{
    public Expr Array { get; }
    public Expr Index { get; }
    public TypeRef? ResolvedElementTypeRef { get; set; }
    public ArrayIndexExpr(Expr array, Expr index) { Array = array; Index = index; }
}

sealed class OptionalOrExpr : Expr
{
    public Expr Optional { get; }
    public Expr Fallback { get; }
    public OptionalOrExpr(Expr opt, Expr fallback) { Optional = opt; Fallback = fallback; }
}

sealed class OptionalHasValueExpr : Expr
{
    public Expr Target { get; }
    public OptionalHasValueExpr(Expr target) { Target = target; }
}

sealed class OptionalValueExpr : Expr
{
    public Expr Target { get; }
    public OptionalValueExpr(Expr target) { Target = target; }
}

sealed class FallibleErrorExpr : Expr
{
    public Token ErrorToken { get; }
    public IReadOnlyList<Expr> Arguments { get; }
    public TypeRef? ResolvedFallibleTypeRef { get; set; }
    public FallibleErrorExpr(Token errorToken, IReadOnlyList<Expr> arguments)
    {
        ErrorToken = errorToken;
        Arguments = arguments;
    }
}

sealed class OnErrorExpr : Expr
{
    public Expr Fallible { get; }
    public Token OnToken { get; }
    public Block Handler { get; }
    public TypeRef? ResolvedSuccessTypeRef { get; set; }
    public TypeRef? ResolvedErrorCodeTypeRef { get; set; }
    public OnErrorExpr(Expr fallible, Token onToken, Block handler)
    {
        Fallible = fallible;
        OnToken = onToken;
        Handler = handler;
    }
}

sealed class FieldAccessExpr : Expr
{
    public Expr Target { get; }
    public Token Name { get; }
    public TypeRef? ResolvedEnumTypeRef { get; set; }
    public int? ResolvedEnumValue { get; set; }
    public TypeRef? ResolvedFallibleErrorFieldTypeRef { get; set; }
    public bool ResolvesToEnumMember => ResolvedEnumTypeRef is not null;
    public bool ResolvesToFallibleErrorField => ResolvedFallibleErrorFieldTypeRef is not null;
    public FieldAccessExpr(Expr target, Token name) { Target = target; Name = name; }
}

sealed class FieldSetExpr : Expr
{
    public FieldAccessExpr Target { get; }
    public Expr Value { get; }
    public FieldSetExpr(FieldAccessExpr target, Expr value) { Target = target; Value = value; }
}

sealed class NewObjectExpr : Expr
{
    public Token TypeName { get; }
    public IReadOnlyList<Expr> Arguments { get; }
    public string? ResolvedConstructorKey { get; set; }
    public NewObjectExpr(Token typeName, IReadOnlyList<Expr> args) { TypeName = typeName; Arguments = args; }
}

sealed class ArraySetExpr : Expr
{
    public ArrayIndexExpr Target { get; }
    public Expr Value { get; }
    public ArraySetExpr(ArrayIndexExpr target, Expr value) { Target = target; Value = value; }
}

sealed class Variable : Expr
{
    public Token Name { get; }
    public TypeRef? ResolvedImplicitFieldTypeRef { get; set; }
    public bool ResolvesToImplicitField => ResolvedImplicitFieldTypeRef is not null;
    public Variable(Token name) { Name = name; }
}

sealed class Assign : Expr
{
    public Token Name { get; }
    public Expr Value { get; }
    public TypeRef? ResolvedImplicitFieldTypeRef { get; set; }
    public bool ResolvesToImplicitField => ResolvedImplicitFieldTypeRef is not null;
    public Assign(Token name, Expr value) { Name = name; Value = value; }
}

sealed class CompoundAssignExpr : Expr
{
    public Expr Target { get; }
    public Token Operator { get; }
    public Expr Value { get; }

    public CompoundAssignExpr(Expr target, Token op, Expr value)
    {
        Target = target;
        Operator = op;
        Value = value;
    }
}

sealed class Call : Expr
{
    public Token Callee { get; }
    public IReadOnlyList<Expr> Arguments { get; }
    public string? ResolvedImplicitMethodOwnerTypeName { get; set; }
    public string? ResolvedImplicitMethodKey { get; set; }
    public TypeRef? ResolvedImplicitMethodReturnTypeRef { get; set; }
    public bool ResolvesToImplicitMethod => ResolvedImplicitMethodKey is not null;
    public Call(Token callee, IReadOnlyList<Expr> args) { Callee = callee; Arguments = args; }
}

sealed class MethodCallExpr : Expr
{
    public Expr Target { get; }
    public Token MethodName { get; }
    public IReadOnlyList<Expr> Arguments { get; }
    public string? ResolvedBuiltInCollectionMethodName { get; set; }
    public string? ResolvedMethodKey { get; set; }
    public string? ResolvedInterfaceName { get; set; }
    public string? ResolvedInterfaceMethodKey { get; set; }
    public TypeRef? ResolvedReturnTypeRef { get; set; }
    public bool ResolvesToBuiltInCollectionMethod => ResolvedBuiltInCollectionMethodName is not null;
    public MethodCallExpr(Expr target, Token methodName, IReadOnlyList<Expr> args)
    {
        Target = target; MethodName = methodName; Arguments = args;
    }
}

sealed class ImportDecl : Stmt
{
    public IReadOnlyList<ImportBinding> Bindings { get; }
    public Token Source { get; }
    public bool IsExported { get; }
    public string SourcePath => Source.Literal?.ToString() ?? string.Empty;
    public ImportDecl(IReadOnlyList<ImportBinding> bindings, Token source, bool isExported = false)
    {
        Bindings = bindings; Source = source; IsExported = isExported;
    }
}

sealed record ImportBinding(Token Name, Token? Alias, bool IsNamespace = false);

sealed class ExportDecl : Stmt
{
    public Stmt Declaration { get; }
    public ExportDecl(Stmt declaration)
    {
        Declaration = declaration;
    }
}

sealed class PackageDecl : Stmt
{
    public Token NameToken { get; }
    public string Name { get; }
    public PackageDecl(Token nameToken, string name)
    {
        NameToken = nameToken;
        Name = name;
    }
}

sealed record Parameter(TypeRef? Type, Token Name);

sealed record FieldDecl(TypeRef Type, Token Name);

sealed record EnumMemberDecl(Token Name, int? ExplicitValue);

abstract class Stmt { }

enum DeclarationVisibility
{
    Public,
    Package,
    Private
}

sealed class VisibilityDecl : Stmt
{
    public Token VisibilityToken { get; }
    public DeclarationVisibility Visibility { get; }
    public Stmt Declaration { get; }

    public VisibilityDecl(Token visibilityToken, DeclarationVisibility visibility, Stmt declaration)
    {
        VisibilityToken = visibilityToken;
        Visibility = visibility;
        Declaration = declaration;
    }
}

sealed class VarDecl : Stmt
{
    public TypeRef Type { get; }
    public Token Name { get; }
    public Expr? Initializer { get; }
    public bool IsConstant { get; }
    public VarDecl(TypeRef type, Token name, Expr? init, bool isConstant = false)
    {
        Type = type;
        Name = name;
        Initializer = init;
        IsConstant = isConstant;
    }
}

sealed class ExprStmt : Stmt
{
    public Expr Expression { get; }
    public ExprStmt(Expr expr) { Expression = expr; }
}

sealed class Block : Stmt
{
    public IList<Stmt> Statements { get; }
    public Block(IList<Stmt> statements) { Statements = statements; }
}

sealed class IfStmt : Stmt
{
    public Expr Condition { get; }
    public Stmt ThenBranch { get; }
    public Stmt? ElseBranch { get; }
    public IfStmt(Expr condition, Stmt thenBranch, Stmt? elseBranch)
    {
        Condition = condition; ThenBranch = thenBranch; ElseBranch = elseBranch;
    }
}

sealed class SwitchCase
{
    public Token Keyword { get; }
    public Expr Value { get; }
    public Stmt Body { get; }

    public SwitchCase(Token keyword, Expr value, Stmt body)
    {
        Keyword = keyword;
        Value = value;
        Body = body;
    }
}

sealed class SwitchStmt : Stmt
{
    public Token Keyword { get; }
    public Expr Value { get; }
    public IReadOnlyList<SwitchCase> Cases { get; }
    public Stmt? DefaultBranch { get; }

    public SwitchStmt(Token keyword, Expr value, IReadOnlyList<SwitchCase> cases, Stmt? defaultBranch)
    {
        Keyword = keyword;
        Value = value;
        Cases = cases;
        DefaultBranch = defaultBranch;
    }
}

sealed class WhileStmt : Stmt
{
    public Expr Condition { get; }
    public Stmt Body { get; }
    public WhileStmt(Expr condition, Stmt body)
    {
        Condition = condition; Body = body;
    }
}

sealed class ReturnStmt : Stmt
{
    public Expr? Value { get; }
    public ReturnStmt(Expr? value) { Value = value; }
}

sealed class PrintStmt : Stmt
{
    public Expr Value { get; }
    public PrintStmt(Expr value) { Value = value; }
}

sealed class PanicStmt : Stmt
{
    public Expr Value { get; }
    public PanicStmt(Expr value) { Value = value; }
}

sealed class YieldStmt : Stmt
{
    public Token Keyword { get; }
    public Expr Value { get; }
    public YieldStmt(Token keyword, Expr value)
    {
        Keyword = keyword;
        Value = value;
    }
}

sealed class ForStmt : Stmt
{
    public Stmt? Initializer { get; }
    public Expr Condition { get; }
    public Expr? Increment { get; }
    public Stmt Body { get; }
    public ForStmt(Stmt? init, Expr condition, Expr? increment, Stmt body)
    {
        Initializer = init; Condition = condition; Increment = increment; Body = body;
    }
}

sealed class ForeachStmt : Stmt
{
    public Token Iterator { get; }
    public Expr Iterable { get; }
    public Stmt Body { get; }
    public bool IsArray { get; set; }
    public TypeRef? IteratorTypeRef { get; set; }
    public ForeachStmt(Token iterator, Expr iterable, Stmt body)
    {
        Iterator = iterator; Iterable = iterable; Body = body;
    }
}

sealed class FunctionDecl : Stmt
{
    public Token Name { get; }
    public TypeRef? ReturnType { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public Block Body { get; }
    public FunctionDecl(Token name, TypeRef? returnType, IReadOnlyList<Parameter> parameters, Block body)
    {
        Name = name; ReturnType = returnType; Parameters = parameters; Body = body;
    }
}

sealed class ObjectDecl : Stmt
{
    public Token Name { get; }
    public bool IsRecord { get; }
    public IReadOnlyList<FieldDecl> Fields { get; }
    public IReadOnlyList<ConstructorDecl> Constructors { get; }
    public IReadOnlyList<MethodDecl> Methods { get; }
    public IReadOnlyList<InlineImplementMethodDecl> InlineInterfaceMethods { get; }
    public ObjectDecl(
        Token name,
        bool isRecord,
        IReadOnlyList<FieldDecl> fields,
        IReadOnlyList<ConstructorDecl> constructors,
        IReadOnlyList<MethodDecl> methods,
        IReadOnlyList<InlineImplementMethodDecl>? inlineInterfaceMethods = null)
    {
        Name = name;
        IsRecord = isRecord;
        Fields = fields;
        Constructors = constructors;
        Methods = methods;
        InlineInterfaceMethods = inlineInterfaceMethods ?? [];
    }
}

sealed class EnumDecl : Stmt
{
    public Token Name { get; }
    public IReadOnlyList<EnumMemberDecl> Members { get; }

    public EnumDecl(Token name, IReadOnlyList<EnumMemberDecl> members)
    {
        Name = name;
        Members = members;
    }
}

sealed class ConstructorDecl
{
    public Token Keyword { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public Block Body { get; }
    public ConstructorDecl(Token keyword, IReadOnlyList<Parameter> parameters, Block body)
    {
        Keyword = keyword; Parameters = parameters; Body = body;
    }
}

sealed class MethodDecl
{
    public Token Name { get; }
    public TypeRef? ReturnType { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public Block Body { get; }
    public MethodDecl(Token name, TypeRef? returnType, IReadOnlyList<Parameter> parameters, Block body)
    {
        Name = name; ReturnType = returnType; Parameters = parameters; Body = body;
    }
}

sealed class InlineImplementMethodDecl
{
    public Token InterfaceName { get; }
    public Token MethodName { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public Block Body { get; }
    public InlineImplementMethodDecl(Token interfaceName, Token methodName, IReadOnlyList<Parameter> parameters, Block body)
    {
        InterfaceName = interfaceName;
        MethodName = methodName;
        Parameters = parameters;
        Body = body;
    }
}

sealed class InterfaceDecl : Stmt
{
    public Token Name { get; }
    public IReadOnlyList<InterfaceMethodDecl> Methods { get; }
    public InterfaceDecl(Token name, IReadOnlyList<InterfaceMethodDecl> methods)
    {
        Name = name; Methods = methods;
    }
}

sealed class InterfaceMethodDecl
{
    public Token Name { get; }
    public TypeRef ReturnType { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public InterfaceMethodDecl(Token name, TypeRef returnType, IReadOnlyList<Parameter> parameters)
    {
        Name = name; ReturnType = returnType; Parameters = parameters;
    }
}

sealed class ImplementDecl : Stmt
{
    public Token InterfaceName { get; }
    public Token ObjectName { get; }
    public IReadOnlyList<ImplementMethodMap> Methods { get; }
    public ImplementDecl(Token interfaceName, Token objectName, IReadOnlyList<ImplementMethodMap> methods)
    {
        InterfaceName = interfaceName; ObjectName = objectName; Methods = methods;
    }
}

sealed class ImplementMethodMap
{
    public Token InterfaceMethodName { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public Token ViaObjectName { get; }
    public Token ViaMethodName { get; }
    public ImplementMethodMap(Token interfaceMethodName, IReadOnlyList<Parameter> parameters, Token viaObjectName, Token viaMethodName)
    {
        InterfaceMethodName = interfaceMethodName;
        Parameters = parameters;
        ViaObjectName = viaObjectName;
        ViaMethodName = viaMethodName;
    }
}
