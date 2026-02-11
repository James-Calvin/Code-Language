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
        if (Match(TokenType.Function)) return FunctionDeclaration();
        if (IsTypeToken(Peek()))
        {
            var typeTok = ParseTypeToken();
            return VarDeclaration(typeTok);
        }
        return Statement();
    }

    private Token ParseTypeToken()
    {
        Token t = ConsumeType("Expect type.");
        if (t.Type == TokenType.Array)
        {
            Consume(TokenType.Less, "Expect '<' after array.");
            Token inner = ConsumeType("Expect element type for array.");
            Consume(TokenType.Greater, "Expect '>' after array element type.");
            // store inner token type in Literal for mapping
            t = new Token(TokenType.Array, "array", inner, t.Line, t.Column);
        }
        return t;
    }

    private Stmt FunctionDeclaration()
    {
        Token? returnType = null;
        if (Match(TokenType.Less))
        {
            returnType = ParseTypeToken();
            Consume(TokenType.Greater, "Expect '>' after return type.");
        }
        else if (IsTypeToken(Peek()))
        {
            returnType = ParseTypeToken();
        }
        Token name = Consume(TokenType.Identifier, "Expect function name.");
        Consume(TokenType.LeftParen, "Expect '(' after function name.");
        var parameters = new List<Parameter>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                Token? paramType = null;
                if (IsTypeToken(Peek()))
                {
                    paramType = ParseTypeToken();
                }
                Token paramName = Consume(TokenType.Identifier, "Expect parameter name.");
                parameters.Add(new Parameter(paramType, paramName));
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after parameters.");
        Block body;
        if (Match(TokenType.LeftBrace))
        {
            body = new Block(BlockStatements());
        }
        else
        {
            throw Error(Peek(), "Expect function body block.");
        }
        return new FunctionDecl(name, returnType, parameters, body);
    }

    private Stmt VarDeclaration(Token typeToken)
    {
        Token name = Consume(TokenType.Identifier, "Expect variable name.");
        Expr? initializer = null;
        if (Match(TokenType.Equal))
        {
            initializer = Expression();
        }
        Consume(TokenType.Semicolon, "Expect ';' after variable declaration.");
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
        if (Match(TokenType.Panic)) return PanicStatement();

        // Fast path for assignment statements to reduce parse ambiguity
        if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Equal)
        {
            Token name = Advance();
            Advance(); // consume '='
            Expr value = Expression();
            Consume(TokenType.Semicolon, "Expect ';' after assignment.");
            return new ExprStmt(new Assign(name, value));
        }

        var expr = Expression();
        Consume(TokenType.Semicolon, "Expect ';' after expression.");
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
        Consume(TokenType.Then, "Expect 'then' after condition.");
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
        Consume(TokenType.Then, "Expect 'then' after condition.");
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

        Expr condition = Check(TokenType.Semicolon)
            ? new Literal(1, Peek().Line, Peek().Column)
            : Expression();
        Consume(TokenType.Semicolon, "Expect ';' after for condition.");

        Expr? increment = null;
        if (!Check(TokenType.Then) && !Check(TokenType.LeftBrace))
        {
            increment = Expression();
        }
        Consume(TokenType.Then, "Expect 'then' after for increment.");
        Stmt body = Statement();

        return new ForStmt(initializer, condition, increment, body);
    }

    private Stmt ForeachStatement()
    {
        Token iter = Consume(TokenType.Identifier, "Expect loop variable name.");
        Consume(TokenType.In, "Expect 'in' after loop variable.");
        Expr iterable = Expression();
        Consume(TokenType.Then, "Expect 'then' after iterable.");
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

    private Stmt PanicStatement()
    {
        Expr value = Expression();
        Consume(TokenType.Semicolon, "Expect ';' after panic expression.");
        return new PanicStmt(value);
    }

    private Expr Expression() => Assignment();

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
        Expr expr = Or();

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
        if (Match(TokenType.Number)) return new Literal(Previous().Literal, Previous().Line, Previous().Column);
        if (Match(TokenType.True)) return new Literal(true, Previous().Line, Previous().Column);
        if (Match(TokenType.False)) return new Literal(false, Previous().Line, Previous().Column);
        if (Match(TokenType.String)) return ParseStringLiteral(Previous(), Previous().Literal?.ToString() ?? "");
        if (Match(TokenType.LeftBrace)) return ParseArrayLiteral(Previous());
        if (Match(TokenType.New)) return ParseNewExpression(Previous());
        if (Match(TokenType.Identifier))
        {
            Token name = Previous();
            if (Match(TokenType.LeftParen))
            {
                var args = new List<Expr>();
                if (!Check(TokenType.RightParen))
                {
                    do
                    {
                        args.Add(Expression());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightParen, "Expect ')' after arguments.");
                return new Call(name, args);
            }
            return new Variable(name);
        }
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

    private bool IsTypeToken(Token token) =>
        token.Type is TokenType.Integer or TokenType.Whole or TokenType.Real or TokenType.Boolean or TokenType.Array;

    private Token ConsumeType(string message)
    {
        if (IsTypeToken(Peek())) return Advance();
        throw Error(Peek(), message);
    }

    private Exception Error(Token token, string message)
    {
        return new CompilerException(message, token.Line, token.Column);
    }

    private Expr ParseStringLiteral(Token stringToken, string raw)
    {
        if (!raw.Contains("{"))
            return new Literal(raw, stringToken.Line, stringToken.Column);

        var parts = new List<object>();
        int i = 0;
        while (i < raw.Length)
        {
            int brace = raw.IndexOf('{', i);
            if (brace == -1)
            {
                parts.Add(raw[i..]);
                break;
            }
            if (brace > i)
            {
                parts.Add(raw[i..brace]);
            }
            int close = raw.IndexOf('}', brace + 1);
            if (close == -1)
                throw new CompilerException("Unterminated interpolation in string literal", stringToken.Line, stringToken.Column);
            string exprText = raw.Substring(brace + 1, close - brace - 1).Trim();
            if (string.IsNullOrEmpty(exprText))
                throw new CompilerException("Empty interpolation expression", stringToken.Line, stringToken.Column);
            // For MVP, allow identifier or numeric literal as interpolation expression.
            parts.Add(ParseInlineExpression(exprText, stringToken.Line, stringToken.Column));
            i = close + 1;
        }
        return new InterpString(parts, stringToken.Line, stringToken.Column);
    }

    private Expr ParseArrayLiteral(Token start)
    {
        var elements = new List<Expr>();
        if (!Check(TokenType.RightBrace))
        {
            do
            {
                elements.Add(Expression());
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightBrace, "Expect '}' after array literal.");
        return new ArrayLiteral(elements, start.Line, start.Column);
    }

    private Expr ParseNewExpression(Token newTok)
    {
        Consume(TokenType.Array, "Expect 'array' after 'new'.");
        Consume(TokenType.Less, "Expect '<' after array.");
        var inner = ConsumeType("Expect element type.");
        Consume(TokenType.Greater, "Expect '>' after element type.");
        Consume(TokenType.LeftParen, "Expect '(' after array type.");
        Expr size = Expression();
        Consume(TokenType.RightParen, "Expect ')' after array size.");
        return new NewArrayExpr(inner, size, newTok.Line, newTok.Column);
    }

    private Expr ParseInlineExpression(string text, int line, int col)
    {
        var lexer = new Lexer(text);
        var tokens = lexer.ScanTokens();
        int idx = 0;

        Expr ParseExpr() => ParseTerm();

        Expr ParseTerm()
        {
            Expr expr = ParseFactor();
            while (MatchInline(TokenType.Plus, TokenType.Minus))
            {
                Token op = PrevInline();
                Expr right = ParseFactor();
                expr = new Binary(expr, op, right);
            }
            return expr;
        }

        Expr ParseFactor()
        {
            Expr expr = ParseUnary();
            while (MatchInline(TokenType.Star, TokenType.Slash))
            {
                Token op = PrevInline();
                Expr right = ParseUnary();
                expr = new Binary(expr, op, right);
            }
            return expr;
        }

        Expr ParseUnary()
        {
            if (MatchInline(TokenType.Minus, TokenType.Plus))
            {
                Token op = PrevInline();
                Expr right = ParseUnary();
                return new Unary(op, right);
            }
            return ParsePrimary();
        }

        Expr ParsePrimary()
        {
            if (MatchInline(TokenType.Number)) { var t = PrevInline(); return new Literal(t.Literal, t.Line, t.Column); }
            if (MatchInline(TokenType.True)) { var t = PrevInline(); return new Literal(true, t.Line, t.Column); }
            if (MatchInline(TokenType.False)) { var t = PrevInline(); return new Literal(false, t.Line, t.Column); }
            if (MatchInline(TokenType.Identifier)) return new Variable(PrevInline());
            if (MatchInline(TokenType.LeftParen))
            {
                Expr expr = ParseExpr();
                if (!MatchInline(TokenType.RightParen))
                    throw new CompilerException("Expect ')' in interpolation expression", line, col);
                return expr;
            }
            throw new CompilerException("Invalid interpolation expression", line, col);
        }

        bool MatchInline(params TokenType[] types)
        {
            foreach (var t in types)
            {
                if (CheckInline(t)) { idx++; return true; }
            }
            return false;
        }

        bool CheckInline(TokenType type)
        {
            if (idx >= tokens.Count) return false;
            return tokens[idx].Type == type;
        }

        Token PrevInline() => tokens[idx - 1];

        Expr result = ParseExpr();
        if (idx < tokens.Count - 1) // allow final EOF
            throw new CompilerException("Unexpected tokens in interpolation expression", line, col);
        return result;
    }

    private static bool IsIdentifier(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (!(char.IsLetter(text[0]) || text[0] == '_')) return false;
        for (int i = 1; i < text.Length; i++)
        {
            char c = text[i];
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        }
        return true;
    }
}
