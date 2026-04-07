using System;
using System.Collections.Generic;

namespace ConsoleApp1.Compiler;

sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _current;
    private int _blockDepth;

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
        if (Match(TokenType.Public, TokenType.Private))
        {
            if (_blockDepth > 0) throw Error(Previous(), $"'{Previous().Lexeme}' is only valid at module scope.");
            return VisibilityDeclaration(Previous());
        }
        if (Check(TokenType.Package) && IsPackageVisibilityModifier())
        {
            if (_blockDepth > 0) throw Error(Peek(), "'package' visibility is only valid at module scope.");
            Token visibilityToken = Advance();
            return VisibilityDeclaration(visibilityToken);
        }
        if (Match(TokenType.Import))
        {
            if (_blockDepth > 0) throw Error(Previous(), "'import' is only valid at module scope.");
            return ImportDeclaration();
        }
        if (Match(TokenType.Export))
        {
            if (_blockDepth > 0) throw Error(Previous(), "'export' is only valid at module scope.");
            return ExportDeclaration();
        }
        if (Match(TokenType.Package))
        {
            if (_blockDepth > 0) throw Error(Previous(), "'package' is only valid at module scope.");
            return PackageDeclaration();
        }
        if (Match(TokenType.Enum))
        {
            if (_blockDepth > 0) throw Error(Previous(), "'enum' is only valid at module scope.");
            return EnumDeclaration();
        }
        if (Match(TokenType.Function)) return FunctionDeclaration();
        if (Match(TokenType.Object)) return ObjectDeclaration(isRecord: false);
        if (Match(TokenType.Record)) return ObjectDeclaration(isRecord: true);
        if (Match(TokenType.Interface)) return InterfaceDeclaration();
        if (Match(TokenType.Implement)) return ImplementDeclaration();
        if (Match(TokenType.Constant)) return ConstantDeclaration();
        if (LooksLikeTypeThenIdentifier(_current))
        {
            var typeRef = ParseTypeRef();
            return VarDeclaration(typeRef);
        }
        return Statement();
    }

    private Stmt VisibilityDeclaration(Token visibilityToken)
    {
        DeclarationVisibility visibility = visibilityToken.Type switch
        {
            TokenType.Public => DeclarationVisibility.Public,
            TokenType.Package => DeclarationVisibility.Package,
            TokenType.Private => DeclarationVisibility.Private,
            _ => throw Error(visibilityToken, $"Unsupported visibility modifier '{visibilityToken.Lexeme}'.")
        };

        Stmt declaration = visibilityToken.Type switch
        {
            _ when Match(TokenType.Function) => FunctionDeclaration(),
            _ when Match(TokenType.Object) => ObjectDeclaration(isRecord: false),
            _ when Match(TokenType.Record) => ObjectDeclaration(isRecord: true),
            _ when Match(TokenType.Interface) => InterfaceDeclaration(),
            _ when Match(TokenType.Enum) => EnumDeclaration(),
            _ => throw Error(Peek(), $"Expect function/object/record/interface/enum declaration after '{visibilityToken.Lexeme}'.")
        };

        return new VisibilityDecl(visibilityToken, visibility, declaration);
    }

    private Stmt ImportDeclaration(bool isExported = false)
    {
        var bindings = new List<ImportBinding>();
        if (Match(TokenType.LeftBrace))
        {
            if (Check(TokenType.RightBrace))
                throw Error(Peek(), "Expect at least one import name in grouped import.");
            do
            {
                bindings.Add(ParseImportBinding());
            } while (Match(TokenType.Comma));
            Consume(TokenType.RightBrace, "Expect '}' after grouped import bindings.");
        }
        else if (Check(TokenType.Identifier) &&
                 string.Equals(Peek().Lexeme, "everything", System.StringComparison.Ordinal) &&
                 CheckNext(TokenType.As))
        {
            Token everything = Advance();
            Consume(TokenType.As, "Expect 'as' after 'everything' in namespace import.");
            Token alias = Consume(TokenType.Identifier, "Expect namespace alias after 'as'.");
            bindings.Add(new ImportBinding(everything, alias, IsNamespace: true));
        }
        else
        {
            bindings.Add(ParseImportBinding());
        }
        Consume(TokenType.From, "Expect 'from' in import declaration.");
        Token source = Consume(TokenType.String, "Expect string module path in import declaration.");
        Consume(TokenType.Semicolon, "Expect ';' after import declaration.");
        return new ImportDecl(bindings, source, isExported);
    }

    private ImportBinding ParseImportBinding()
    {
        Token name = Consume(TokenType.Identifier, "Expect imported declaration name.");
        Token? alias = null;
        if (Match(TokenType.As))
            alias = Consume(TokenType.Identifier, "Expect alias name after 'as'.");
        return new ImportBinding(name, alias);
    }

    private bool CheckNext(TokenType type)
    {
        if (_current + 1 >= _tokens.Count)
            return false;
        return _tokens[_current + 1].Type == type;
    }

    private bool CheckNextAny(params TokenType[] types)
    {
        if (_current + 1 >= _tokens.Count)
            return false;

        TokenType nextType = _tokens[_current + 1].Type;
        for (int i = 0; i < types.Length; i++)
        {
            if (nextType == types[i])
                return true;
        }

        return false;
    }

    private bool IsPackageVisibilityModifier()
    {
        return CheckNextAny(
            TokenType.Function,
            TokenType.Object,
            TokenType.Record,
            TokenType.Interface,
            TokenType.Enum);
    }

    private Stmt ExportDeclaration()
    {
        if (Match(TokenType.Import))
            return ImportDeclaration(isExported: true);
        if (Match(TokenType.Function))
            return new ExportDecl(FunctionDeclaration());
        if (Match(TokenType.Object))
            return new ExportDecl(ObjectDeclaration(isRecord: false));
        if (Match(TokenType.Record))
            return new ExportDecl(ObjectDeclaration(isRecord: true));
        if (Match(TokenType.Interface))
            return new ExportDecl(InterfaceDeclaration());
        if (Match(TokenType.Enum))
            return new ExportDecl(EnumDeclaration());
        throw Error(Peek(), "Expect function/object/record/interface/enum declaration after 'export'.");
    }

    private Stmt PackageDeclaration()
    {
        Token start = Consume(TokenType.Identifier, "Expect package name.");
        string name = start.Lexeme;
        while (Match(TokenType.Dot))
        {
            Token part = Consume(TokenType.Identifier, "Expect package name segment after '.'.");
            name += "." + part.Lexeme;
        }
        Consume(TokenType.Semicolon, "Expect ';' after package declaration.");
        return new PackageDecl(start, name);
    }

    private Stmt EnumDeclaration()
    {
        Token name = Consume(TokenType.Identifier, "Expect enum name.");
        Consume(TokenType.LeftBrace, "Expect '{' after enum name.");
        var members = new List<EnumMemberDecl>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            Token memberName = Consume(TokenType.Identifier, "Expect enum member name.");
            int? explicitValue = null;
            if (Match(TokenType.Equal))
                explicitValue = ParseEnumMemberValue();
            Consume(TokenType.Semicolon, "Expect ';' after enum member.");
            members.Add(new EnumMemberDecl(memberName, explicitValue));
        }
        Consume(TokenType.RightBrace, "Expect '}' after enum body.");
        return new EnumDecl(name, members);
    }

    private int ParseEnumMemberValue()
    {
        bool negative = false;
        if (Match(TokenType.Minus))
            negative = true;
        else
            Match(TokenType.Plus);

        Token number = Consume(TokenType.Number, "Expect integer literal for enum member value.");
        int value = Convert.ToInt32(number.Literal ?? 0);
        return negative ? -value : value;
    }

    private TypeRef ParseTypeRef()
    {
        Token t = ConsumeTypeStart("Expect type.");
        string name = t.Type switch
        {
            TokenType.Integer => "integer",
            TokenType.Whole => "whole",
            TokenType.Real => "real",
            TokenType.Boolean => "boolean",
            TokenType.Void => "void",
            TokenType.Array => "array",
            TokenType.Optional => "optional",
            TokenType.Fallible => "fallible",
            TokenType.Identifier => t.Lexeme,
            _ => throw Error(t, "Expect type.")
        };

        var args = new List<TypeRef>();
        if (Match(TokenType.Less))
        {
            do
            {
                args.Add(ParseTypeRef());
            } while (Match(TokenType.Comma));
            Consume(TokenType.Greater, "Expect '>' after type arguments.");
        }
        return new TypeRef(name, args, t.Line, t.Column);
    }

    private Stmt FunctionDeclaration()
    {
        var sig = ParseCallableSignature("function");
        Block body = ParseCallableBody("function");
        return new FunctionDecl(sig.Name, sig.ReturnType, sig.Parameters, body);
    }

    private (Token Name, TypeRef? ReturnType, IReadOnlyList<Parameter> Parameters) ParseCallableSignature(string kind)
    {
        TypeRef? returnType = null;
        if (Match(TokenType.Less))
        {
            returnType = ParseTypeRef();
            Consume(TokenType.Greater, $"Expect '>' after {kind} return type.");
        }
        else if (LooksLikeTypeThenIdentifier(_current))
        {
            returnType = ParseTypeRef();
        }
        Token name = Consume(TokenType.Identifier, $"Expect {kind} name.");
        Consume(TokenType.LeftParen, $"Expect '(' after {kind} name.");
        var parameters = new List<Parameter>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                TypeRef? paramType = null;
                if (LooksLikeTypeThenIdentifier(_current))
                {
                    paramType = ParseTypeRef();
                }
                Token paramName = Consume(TokenType.Identifier, $"Expect {kind} parameter name.");
                parameters.Add(new Parameter(paramType, paramName));
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, $"Expect ')' after {kind} parameters.");
        return (name, returnType, parameters);
    }

    private Block ParseCallableBody(string kind)
    {
        if (Match(TokenType.LeftBrace))
        {
            return new Block(BlockStatements());
        }
        throw Error(Peek(), $"Expect {kind} body block.");
    }

    private MethodDecl ParseMethodDeclaration(DeclarationVisibility visibility = DeclarationVisibility.Public)
    {
        var sig = ParseCallableSignature("method");
        Block body = ParseCallableBody("method");
        return new MethodDecl(sig.Name, sig.ReturnType, sig.Parameters, body, visibility);
    }

    private ConstructorDecl ParseConstructor(Token ctorKeyword, DeclarationVisibility visibility = DeclarationVisibility.Public)
    {
        Consume(TokenType.LeftParen, "Expect '(' after constructor.");
        var parameters = new List<Parameter>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                TypeRef paramType = ParseTypeRef();
                Token paramName = Consume(TokenType.Identifier, "Expect constructor parameter name.");
                parameters.Add(new Parameter(paramType, paramName));
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after constructor parameters.");
        Block body = ParseCallableBody("constructor");
        return new ConstructorDecl(ctorKeyword, parameters, body, visibility);
    }

    private Stmt ObjectDeclaration(bool isRecord)
    {
        Token name = Consume(TokenType.Identifier, "Expect object name.");
        Consume(TokenType.LeftBrace, $"Expect '{{' after {(isRecord ? "record" : "object")} name.");
        var fields = new List<FieldDecl>();
        var constructors = new List<ConstructorDecl>();
        var methods = new List<MethodDecl>();
        var inlineInterfaceMethods = new List<InlineImplementMethodDecl>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            var visibility = ParseMemberVisibility();
            if (Match(TokenType.Constructor))
            {
                constructors.Add(ParseConstructor(Previous(), visibility));
                continue;
            }
            if (Match(TokenType.Function))
            {
                methods.Add(ParseMethodDeclaration(visibility));
                continue;
            }
            if (Match(TokenType.Implement))
            {
                inlineInterfaceMethods.Add(ParseInlineImplementMethod(visibility));
                continue;
            }

            var fType = ParseTypeRef();
            Token fname = Consume(TokenType.Identifier, "Expect field name.");
            Consume(TokenType.Semicolon, "Expect ';' after field.");
            fields.Add(new FieldDecl(fType, fname, visibility));
        }
        Consume(TokenType.RightBrace, $"Expect '}}' after {(isRecord ? "record" : "object")} fields.");
        return new ObjectDecl(name, isRecord, fields, constructors, methods, inlineInterfaceMethods);
    }

    private DeclarationVisibility ParseMemberVisibility()
    {
        if (Match(TokenType.Public)) return DeclarationVisibility.Public;
        if (Match(TokenType.Package)) return DeclarationVisibility.Package;
        if (Match(TokenType.Private)) return DeclarationVisibility.Private;
        return DeclarationVisibility.Public;
    }

    private InlineImplementMethodDecl ParseInlineImplementMethod(DeclarationVisibility visibility = DeclarationVisibility.Public)
    {
        Token interfaceName = Consume(TokenType.Identifier, "Expect interface name after 'implement'.");
        Consume(TokenType.Dot, "Expect '.' after interface name in inline implement method.");
        Token methodName = Consume(TokenType.Identifier, "Expect interface method name after '.'.");
        Consume(TokenType.LeftParen, "Expect '(' after interface method name.");
        var parameters = new List<Parameter>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                TypeRef parameterType = ParseTypeRef();
                Token parameterName = Consume(TokenType.Identifier, "Expect inline implement parameter name.");
                parameters.Add(new Parameter(parameterType, parameterName));
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after inline implement parameters.");
        Block body = ParseCallableBody("inline implement method");
        return new InlineImplementMethodDecl(interfaceName, methodName, parameters, body, visibility);
    }

    private Stmt InterfaceDeclaration()
    {
        Token name = Consume(TokenType.Identifier, "Expect interface name.");
        Consume(TokenType.LeftBrace, "Expect '{' after interface name.");
        var methods = new List<InterfaceMethodDecl>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            Consume(TokenType.Function, "Expect 'function' in interface body.");
            var sig = ParseCallableSignature("interface method");
            if (sig.ReturnType is null)
                throw Error(sig.Name, $"Interface method '{sig.Name.Lexeme}' must declare a return type.");
            for (int i = 0; i < sig.Parameters.Count; i++)
            {
                if (sig.Parameters[i].Type is null)
                    throw Error(sig.Parameters[i].Name, $"Interface method '{sig.Name.Lexeme}' has an untyped parameter.");
            }
            Consume(TokenType.Semicolon, "Expect ';' after interface method signature.");
            methods.Add(new InterfaceMethodDecl(sig.Name, sig.ReturnType, sig.Parameters));
        }
        Consume(TokenType.RightBrace, "Expect '}' after interface body.");
        return new InterfaceDecl(name, methods);
    }

    private Stmt ImplementDeclaration()
    {
        Token interfaceName = Consume(TokenType.Identifier, "Expect interface name after 'implement'.");
        Consume(TokenType.For, "Expect 'for' after interface name in implement declaration.");
        Token objectName = Consume(TokenType.Identifier, "Expect object name after 'for'.");
        Consume(TokenType.LeftBrace, "Expect '{' after implement header.");
        var maps = new List<ImplementMethodMap>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            maps.Add(ParseImplementMethodMap());
        }
        Consume(TokenType.RightBrace, "Expect '}' after implement body.");
        return new ImplementDecl(interfaceName, objectName, maps);
    }

    private ImplementMethodMap ParseImplementMethodMap()
    {
        Token interfaceMethod = Consume(TokenType.Identifier, "Expect interface method name in implement mapping.");
        Consume(TokenType.LeftParen, "Expect '(' after interface method name.");
        var parameters = new List<Parameter>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                TypeRef pType = ParseTypeRef();
                Token pName = Check(TokenType.Identifier)
                    ? Advance()
                    : new Token(TokenType.Identifier, $"_p{parameters.Count}", null, pType.Line, pType.Column);
                parameters.Add(new Parameter(pType, pName));
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after mapped interface method parameters.");
        Consume(TokenType.Via, "Expect 'via' in implement mapping.");
        Token viaObject = Consume(TokenType.Identifier, "Expect object name after 'via'.");
        Consume(TokenType.Dot, "Expect '.' after object name in implement mapping.");
        Token viaMethod = Consume(TokenType.Identifier, "Expect method name after '.'.");
        Consume(TokenType.Semicolon, "Expect ';' after implement mapping.");
        return new ImplementMethodMap(interfaceMethod, parameters, viaObject, viaMethod);
    }

    private Stmt VarDeclaration(TypeRef typeRef)
    {
        return VarDeclaration(typeRef, isConstant: false);
    }

    private Stmt VarDeclaration(TypeRef typeRef, bool isConstant)
    {
        Token name = Consume(TokenType.Identifier, "Expect variable name.");
        Expr? initializer = null;
        if (Match(TokenType.Equal))
        {
            initializer = Expression();
        }
        if (isConstant && initializer is null)
            throw Error(name, $"Constant '{name.Lexeme}' must be initialized.");
        Consume(TokenType.Semicolon, "Expect ';' after variable declaration.");
        return new VarDecl(typeRef, name, initializer, isConstant);
    }

    private Stmt ConstantDeclaration()
    {
        if (!IsTypeStart(Peek()))
            throw Error(Peek(), "Expect type after 'constant'.");
        var typeRef = ParseTypeRef();
        return VarDeclaration(typeRef, isConstant: true);
    }

    private Stmt Statement()
    {
        if (Match(TokenType.If)) return IfStatement();
        if (Match(TokenType.Switch)) return SwitchStatement();
        if (Match(TokenType.While)) return WhileStatement();
        if (Match(TokenType.For)) return ForStatement();
        if (Match(TokenType.Foreach)) return ForeachStatement();
        if (Match(TokenType.LeftBrace)) return new Block(BlockStatements());
        if (Match(TokenType.Return)) return ReturnStatement();
        if (Match(TokenType.Print)) return PrintStatement();
        if (Match(TokenType.Panic)) return PanicStatement();
        if (Match(TokenType.Yield)) return YieldStatement();
        if (Match(TokenType.Object)) return ObjectDeclaration(isRecord: false);
        if (Match(TokenType.Record)) return ObjectDeclaration(isRecord: true);

        var expr = Expression();
        Consume(TokenType.Semicolon, "Expect ';' after expression.");
        return new ExprStmt(expr);
    }

    private IList<Stmt> BlockStatements()
    {
        _blockDepth++;
        var stmts = new List<Stmt>();
        try
        {
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                stmts.Add(Declaration());
            }
            Consume(TokenType.RightBrace, "Expect '}' after block.");
            return stmts;
        }
        finally
        {
            _blockDepth--;
        }
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

    private Stmt SwitchStatement()
    {
        Token switchKeyword = Previous();
        Expr value = Expression();
        Consume(TokenType.Then, "Expect 'then' after switch value.");
        Consume(TokenType.LeftBrace, "Expect '{' after switch header.");

        var cases = new List<SwitchCase>();
        Stmt? defaultBranch = null;
        bool sawDefault = false;

        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            if (Match(TokenType.Case))
            {
                if (sawDefault)
                    throw Error(Previous(), "'case' cannot appear after 'default' in switch.");

                Token caseKeyword = Previous();
                Expr caseValue = Expression();
                Consume(TokenType.Then, "Expect 'then' after switch case value.");
                Stmt body = Statement();
                cases.Add(new SwitchCase(caseKeyword, caseValue, body));
                continue;
            }

            if (Match(TokenType.Default))
            {
                if (sawDefault)
                    throw Error(Previous(), "Switch already has a 'default' branch.");

                sawDefault = true;
                Consume(TokenType.Then, "Expect 'then' after 'default'.");
                defaultBranch = Statement();
                continue;
            }

            throw Error(Peek(), "Expect 'case' or 'default' in switch body.");
        }

        Consume(TokenType.RightBrace, "Expect '}' after switch body.");

        if (cases.Count == 0 && defaultBranch is null)
            throw Error(switchKeyword, "Switch must contain at least one 'case' or 'default'.");

        return new SwitchStmt(switchKeyword, value, cases, defaultBranch);
    }

    private Stmt ForStatement()
    {
        // for init; condition; increment then stmt
        Stmt? initializer = null;
        if (!Check(TokenType.Semicolon))
        {
            if (LooksLikeTypeThenIdentifier(_current))
                initializer = VarDeclaration(ParseTypeRef());
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

    private Stmt YieldStatement()
    {
        Token yieldToken = Previous();
        Expr value = Expression();
        Consume(TokenType.Semicolon, "Expect ';' after yield value.");
        return new YieldStmt(yieldToken, value);
    }

    private Expr Expression() => Assignment();

    private Expr OnError()
    {
        Expr expr = Or();
        if (Match(TokenType.On))
        {
            Token onToken = Previous();
            Consume(TokenType.Error, "Expect 'error' after 'on'.");
            Consume(TokenType.LeftBrace, "Expect '{' before on error handler.");
            expr = new OnErrorExpr(expr, onToken, new Block(BlockStatements()));
        }

        return expr;
    }

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
        Expr expr = OnError();

        if (Match(TokenType.Equal))
        {
            Token equals = Previous();
            Expr value = Assignment();

            if (expr is Variable variable)
                return new Assign(variable.Name, value);
            if (expr is ArrayIndexExpr aidx)
                return new ArraySetExpr(aidx, value);
            if (expr is FieldAccessExpr fa)
                return new FieldSetExpr(fa, value);

            throw Error(equals, "Invalid assignment target.");
        }
        if (Match(TokenType.PlusEqual, TokenType.MinusEqual, TokenType.StarEqual, TokenType.SlashEqual, TokenType.PercentEqual))
        {
            Token op = Previous();
            Expr value = Assignment();
            return BuildCompoundAssignment(expr, op, value);
        }
        if (Match(TokenType.PlusPlus, TokenType.MinusMinus))
        {
            Token op = Previous();
            return BuildIncrementAssignment(expr, op);
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
        while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
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
        if (Match(TokenType.None)) return new Literal(OptionalNone.Value, Previous().Line, Previous().Column);
        if (Match(TokenType.Error))
        {
            Token errorToken = Previous();
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
                Consume(TokenType.RightParen, "Expect ')' after error arguments.");
                return new FallibleErrorExpr(errorToken, args);
            }

            Expr expr = new Variable(new Token(TokenType.Identifier, "error", null, errorToken.Line, errorToken.Column));
            expr = FinishPostfix(expr);
            return expr;
        }
        if (Match(TokenType.Identifier))
        {
            Expr expr = new Variable(Previous());
            expr = FinishPostfix(expr);
            return expr;
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

    private bool IsTypeStart(Token token) =>
        token.Type is TokenType.Integer or TokenType.Whole or TokenType.Real or TokenType.Boolean or TokenType.Void or TokenType.Array or TokenType.Optional or TokenType.Fallible or TokenType.Identifier;

    private Token ConsumeTypeStart(string message)
    {
        if (IsTypeStart(Peek())) return Advance();
        throw Error(Peek(), message);
    }

    private bool LooksLikeTypeThenIdentifier(int start)
    {
        if (start >= _tokens.Count) return false;
        if (!IsTypeStart(_tokens[start])) return false;

        int idx = start + 1;
        if (idx < _tokens.Count && _tokens[idx].Type == TokenType.Less)
        {
            int depth = 0;
            while (idx < _tokens.Count)
            {
                var type = _tokens[idx].Type;
                if (type == TokenType.Less)
                {
                    depth++;
                }
                else if (type == TokenType.Greater)
                {
                    depth--;
                    if (depth == 0)
                    {
                        idx++;
                        break;
                    }
                }
                idx++;
            }
            if (depth != 0) return false;
        }

        return idx < _tokens.Count && _tokens[idx].Type == TokenType.Identifier;
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
        if (!IsTypeStart(Peek()))
            throw Error(Peek(), "Expect type name after 'new'.");

        TypeRef newType = ParseTypeRef();
        if (newType.IsArray)
        {
            Consume(TokenType.LeftParen, "Expect '(' after array type.");
            Expr size = Expression();
            Consume(TokenType.RightParen, "Expect ')' after array size.");
            return new NewArrayExpr(newType.TypeArguments[0], size, newTok.Line, newTok.Column);
        }

        if (newType.IsMap || newType.IsSet || newType.IsQueue || newType.IsStack)
        {
            Consume(TokenType.LeftParen, "Expect '(' after collection type.");
            Consume(TokenType.RightParen, "Expect ')' after collection constructor.");
            return new NewCollectionExpr(newType, newTok.Line, newTok.Column);
        }

        if (newType.TypeArguments.Count > 0)
            throw Error(Peek(), $"Type '{newType.Name}' does not support constructor type arguments.");

        Token typeName = new Token(TokenType.Identifier, newType.Name, null, newType.Line, newType.Column);
        Consume(TokenType.LeftParen, "Expect '(' after type name.");
        var args = new List<Expr>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                args.Add(Expression());
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after constructor arguments.");
        return new NewObjectExpr(typeName, args);
    }

    private Expr FinishPostfix(Expr expr)
    {
        while (true)
        {
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
                if (expr is Variable v)
                    expr = new Call(v.Name, args);
                else if (expr is FieldAccessExpr fa)
                    expr = new MethodCallExpr(fa.Target, fa.Name, args);
                else
                    throw Error(Peek(), "Cannot call non-variable expression");
            }
            else if (Match(TokenType.Dot))
            {
                Token dot = Previous();
                Token prop;
                if (Match(TokenType.Identifier))
                    prop = Previous();
                else if (Match(TokenType.Or)) // allow keyword 'or' as property name
                    prop = new Token(TokenType.Identifier, "or", null, dot.Line, dot.Column + 1);
                else
                    throw Error(Peek(), "Expect property name after '.'.");
                if (prop.Lexeme == "length")
                {
                    expr = new ArrayLengthExpr(expr, dot);
                }
                else if (prop.Lexeme == "hasValue")
                {
                    expr = new OptionalHasValueExpr(expr);
                }
                else if (prop.Lexeme == "value")
                {
                    expr = new OptionalValueExpr(expr);
                }
                else if (prop.Lexeme == "or")
                {
                    Consume(TokenType.LeftParen, "Expect '(' after '.or'.");
                    var fb = Expression();
                    Consume(TokenType.RightParen, "Expect ')' after fallback.");
                    expr = new OptionalOrExpr(expr, fb);
                }
                else
                {
                    expr = new FieldAccessExpr(expr, prop);
                }
            }
            else if (Match(TokenType.LeftBracket))
            {
                Expr index = Expression();
                Consume(TokenType.RightBracket, "Expect ']' after index.");
                expr = new ArrayIndexExpr(expr, index);
            }
            else break;
        }
        return expr;
    }

    private Expr ParseInlineExpression(string text, int line, int col)
    {
        try
        {
            var lexer = new Lexer(text);
            var tokens = lexer.ScanTokens();
            var inlineParser = new Parser(tokens);
            return inlineParser.ParseExpressionOnly();
        }
        catch (CompilerException ex)
        {
            throw new CompilerException(ex.Message, line, col);
        }
    }

    public Expr ParseExpressionOnly()
    {
        Expr expr = Expression();
        if (!IsAtEnd())
            throw Error(Peek(), "Unexpected tokens in interpolation expression");
        return expr;
    }

    private Expr BuildCompoundAssignment(Expr target, Token operatorToken, Expr value)
    {
        if (!IsAssignmentTarget(target))
            throw Error(operatorToken, "Invalid assignment target.");

        TokenType binaryType = operatorToken.Type switch
        {
            TokenType.PlusEqual => TokenType.Plus,
            TokenType.MinusEqual => TokenType.Minus,
            TokenType.StarEqual => TokenType.Star,
            TokenType.SlashEqual => TokenType.Slash,
            TokenType.PercentEqual => TokenType.Percent,
            _ => throw Error(operatorToken, $"Unsupported compound operator '{operatorToken.Lexeme}'.")
        };

        var binaryOp = new Token(binaryType, operatorToken.Lexeme, null, operatorToken.Line, operatorToken.Column);
        return new CompoundAssignExpr(target, binaryOp, value);
    }

    private Expr BuildIncrementAssignment(Expr target, Token operatorToken)
    {
        if (!IsAssignmentTarget(target))
            throw Error(operatorToken, "Invalid increment/decrement target.");

        TokenType binaryType = operatorToken.Type == TokenType.PlusPlus ? TokenType.Plus : TokenType.Minus;
        var binaryOp = new Token(binaryType, operatorToken.Lexeme, null, operatorToken.Line, operatorToken.Column);
        var one = new Literal(1, operatorToken.Line, operatorToken.Column);
        return new CompoundAssignExpr(target, binaryOp, one);
    }

    private static bool IsAssignmentTarget(Expr expr)
        => expr is Variable or ArrayIndexExpr or FieldAccessExpr;
}
