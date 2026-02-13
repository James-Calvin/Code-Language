namespace ConsoleApp1.Compiler;

enum TokenType
{
    LeftParen, RightParen,
    LeftBrace, RightBrace,
    LeftBracket, RightBracket,
    Comma,
    Dot,
    Semicolon,
    Plus, Minus, Star, Slash,
    Equal, Less, Greater,

    EqualEqual, BangEqual,
    LessEqual, GreaterEqual,

    Identifier, Number, String,
    True, False,

    Integer, Whole, Real, Boolean, Optional,
    Array, Object, Interface,
    If, Then, Else,
    While,
    Return,
    Print,
    Function,
    Constructor,
    Implement, Via,
    Import, Export, From, As,
    And, Or, Not,
    For, Foreach, In,
    New,
    Panic,
    None,

    Eof
}

sealed record Token(TokenType Type, string Lexeme, object? Literal, int Line, int Column);
