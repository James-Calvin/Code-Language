using System;
using System.Collections.Generic;

namespace ConsoleApp1.Compiler;

sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _current;

    public Parser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens;
    }

    public IList<Stmt> Parse()
    {
        var statements = new List<Stmt>();
        while (!IsAtEnd())
        {
            statements.Add(Declaration());
        }
        return statements;
    }

    private Stmt Declaration()
    {
        if (Match(TokenType.Integer, TokenType.Whole, TokenType.Real, TokenType.Boolean))
            return VarDeclaration(Previous());
        return Statement();
    }

    private Stmt VarDeclaration(Token typeToken)
    {
        Token name = Consume(TokenType.Identifier, "Expect variable name.");
        Expr? initializer = null;
        if (Match(TokenType.Equal))
        {
            initializer = Expression();
        }
        Match(TokenType.Semicolon); // tolerate missing ';' for now
        return new VarDecl(typeToken, name, initializer);
    }

    private Stmt Statement()
    {
        if (Match(TokenType.If)) return IfStatement();
        if (Match(TokenType.While)) return WhileStatement();
        if (Match(TokenType.For)) return ForStatement();
        if (Match(TokenType.Foreach)) return ForeachStatement();
        if (Match(TokenType.LeftBrace)) return new Block(BlockStatements());
        if (Match(TokenType.Return)) return ReturnStatement();
        if (Match(TokenType.Print)) return PrintStatement();

        // Fast path for assignment statements to reduce parse ambiguity
        if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Equal)
        {
            Token name = Advance();
            Advance(); // consume '='
            Expr value = Expression();
            Match(TokenType.Semicolon);
            return new ExprStmt(new Assign(name, value));
        }

        var expr = Expression();
        Match(TokenType.Semicolon); // tolerate missing ';' for now
        return new ExprStmt(expr);
    }

    private IList<Stmt> BlockStatements()
    {
        var stmts = new List<Stmt>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            stmts.Add(Declaration());
        }
        Consume(TokenType.RightBrace, "Expect '}' after block.");
        return stmts;
    }

    private Stmt IfStatement()
    {
        Expr condition = Expression();
        if (Match(TokenType.Then) || Check(TokenType.LeftBrace) || Check(TokenType.If) || Check(TokenType.While) || Check(TokenType.For) || Check(TokenType.Foreach) || Check(TokenType.Return) || Check(TokenType.Print) || Check(TokenType.Identifier))
        {
            // ok
        }
        else
        {
            throw Error(Peek(), "Expect 'then' after condition.");
        }
        Stmt thenBranch = Statement();
        Stmt? elseBranch = null;
        if (Match(TokenType.Else))
        {
            elseBranch = Statement();
        }
        return new IfStmt(condition, thenBranch, elseBranch);
    }

    private Stmt WhileStatement()
    {
        Expr condition = Expression();
        if (!(Match(TokenType.Then) || Check(TokenType.LeftBrace) || Check(TokenType.If) || Check(TokenType.While) || Check(TokenType.For) || Check(TokenType.Foreach) || Check(TokenType.Return) || Check(TokenType.Print) || Check(TokenType.Identifier)))
            throw Error(Peek(), "Expect 'then' after condition.");
        Stmt body = Statement();
        return new WhileStmt(condition, body);
    }

    private Stmt ForStatement()
    {
        // for init; condition; increment then stmt
        Stmt? initializer = null;
        if (!Check(TokenType.Semicolon))
        {
            if (Match(TokenType.Integer, TokenType.Whole, TokenType.Real, TokenType.Boolean))
                initializer = VarDeclaration(Previous());
            else
            {
                var expr = Expression();
                Consume(TokenType.Semicolon, "Expect ';' after for initializer.");
                initializer = new ExprStmt(expr);
            }
        }
        else
        {
            Consume(TokenType.Semicolon, "Expect ';' after for initializer.");
        }

        Expr condition = Check(TokenType.Semicolon) ? new Literal(1) : Expression();
        Consume(TokenType.Semicolon, "Expect ';' after for condition.");

        Expr? increment = null;
        if (!Check(TokenType.Then) && !Check(TokenType.LeftBrace))
        {
            increment = Expression();
        }
        if (!(Match(TokenType.Then) || Check(TokenType.LeftBrace)))
            throw Error(Peek(), "Expect 'then' after for increment.");
        Stmt body = Statement();

        return new ForStmt(initializer, condition, increment, body);
    }

    private Stmt ForeachStatement()
    {
        Token iter = Consume(TokenType.Identifier, "Expect loop variable name.");
        Consume(TokenType.In, "Expect 'in' after loop variable.");
        Expr iterable = Expression();
        if (!(Match(TokenType.Then) || Check(TokenType.LeftBrace)))
            throw Error(Peek(), "Expect 'then' after iterable.");
        Stmt body = Statement();
        return new ForeachStmt(iter, iterable, body);
    }

    private Stmt PrintStatement()
    {
        Expr value = Expression();
        Consume(TokenType.Semicolon, "Expect ';' after print value.");
        return new PrintStmt(value);
    }

    private Stmt ReturnStatement()
    {
        Expr? value = null;
        if (!Check(TokenType.Semicolon))
        {
            value = Expression();
        }
        Consume(TokenType.Semicolon, "Expect ';' after return value.");
        return new ReturnStmt(value);
    }

    private Expr Expression() => Or();

    private Expr Or()
    {
        Expr expr = And();
        while (Match(TokenType.Or))
        {
            Token op = Previous();
            Expr right = And();
            expr = new Binary(expr, op, right);
        }
        return expr;
    }

    private Expr And()
    {
        Expr expr = Equality();
        while (Match(TokenType.And))
        {
            Token op = Previous();
            Expr right = Equality();
            expr = new Binary(expr, op, right);
        }
        return expr;
    }

    private Expr Assignment()
    {
        Expr expr = Equality();

        if (Match(TokenType.Equal))
        {
            Token equals = Previous();
            Expr value = Assignment();

            if (expr is Variable variable)
            {
                return new Assign(variable.Name, value);
            }

            throw Error(equals, "Invalid assignment target.");
        }

        return expr;
    }

    private Expr Equality()
    {
        Expr expr = Comparison();

        while (Match(TokenType.EqualEqual, TokenType.BangEqual))
        {
            Token op = Previous();
            Expr right = Comparison();
            expr = new Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Comparison()
    {
        Expr expr = Term();
        while (Match(TokenType.Less, TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual))
        {
            Token op = Previous();
            Expr right = Term();
            expr = new Binary(expr, op, right);
        }
        return expr;
    }

    private Expr Term()
    {
        Expr expr = Factor();
        while (Match(TokenType.Plus, TokenType.Minus))
        {
            Token op = Previous();
            Expr right = Factor();
            expr = new Binary(expr, op, right);
        }
        return expr;
    }

    private Expr Factor()
    {
        Expr expr = Unary();
        while (Match(TokenType.Star, TokenType.Slash))
        {
            Token op = Previous();
            Expr right = Unary();
            expr = new Binary(expr, op, right);
        }
        return expr;
    }

    private Expr Unary()
    {
        if (Match(TokenType.Minus, TokenType.Plus, TokenType.Not))
        {
            Token op = Previous();
            Expr right = Unary();
            return new Unary(op, right);
        }
        return Primary();
    }

    private Expr Primary()
    {
        if (Match(TokenType.Number)) return new Literal(Previous().Literal);
        if (Match(TokenType.Identifier)) return new Variable(Previous());
        if (Match(TokenType.LeftParen))
        {
            Expr expr = Expression();
            Consume(TokenType.RightParen, "Expect ')' after expression.");
            return expr;
        }

        throw Error(Peek(), "Expect expression.");
    }

    private bool Match(params TokenType[] types)
    {
        foreach (var type in types)
        {
            if (Check(type)) { Advance(); return true; }
        }
        return false;
    }

    private Token Consume(TokenType type, string message)
    {
        if (Check(type)) return Advance();
        throw Error(Peek(), message);
    }

    private bool Check(TokenType type)
    {
        if (IsAtEnd()) return false;
        return Peek().Type == type;
    }

    private Token PeekNext()
    {
        if (_current + 1 >= _tokens.Count) return _tokens[^1];
        return _tokens[_current + 1];
    }

    private Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }

    private bool IsAtEnd() => Peek().Type == TokenType.Eof;

    private Token Peek() => _tokens[_current];

    private Token Previous() => _tokens[_current - 1];

    private Exception Error(Token token, string message)
    {
        return new CompilerException(message, token.Line, token.Column);
    }
}
