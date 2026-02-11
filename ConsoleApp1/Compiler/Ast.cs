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

sealed class Variable : Expr
{
    public Token Name { get; }
    public Variable(Token name) { Name = name; }
}

sealed class Assign : Expr
{
    public Token Name { get; }
    public Expr Value { get; }
    public Assign(Token name, Expr value) { Name = name; Value = value; }
}

sealed class Call : Expr
{
    public Token Callee { get; }
    public IReadOnlyList<Expr> Arguments { get; }
    public Call(Token callee, IReadOnlyList<Expr> args) { Callee = callee; Arguments = args; }
}

sealed record Parameter(Token? TypeToken, Token Name);

abstract class Stmt { }

sealed class VarDecl : Stmt
{
    public Token TypeToken { get; }
    public Token Name { get; }
    public Expr? Initializer { get; }
    public VarDecl(Token typeToken, Token name, Expr? init) { TypeToken = typeToken; Name = name; Initializer = init; }
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
    public ForeachStmt(Token iterator, Expr iterable, Stmt body)
    {
        Iterator = iterator; Iterable = iterable; Body = body;
    }
}

sealed class FunctionDecl : Stmt
{
    public Token Name { get; }
    public Token? ReturnType { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public Block Body { get; }
    public FunctionDecl(Token name, Token? returnType, IReadOnlyList<Parameter> parameters, Block body)
    {
        Name = name; ReturnType = returnType; Parameters = parameters; Body = body;
    }
}
