namespace ConsoleApp1.Compiler;

enum TokenType
{
    LeftParen, RightParen,
    LeftBrace, RightBrace,
    Comma,
    Semicolon,
    Plus, Minus, Star, Slash,
    Equal, Less, Greater,

    EqualEqual, BangEqual,
    LessEqual, GreaterEqual,

    Identifier, Number,

    Integer, Whole, Real, Boolean,
    If, Then, Else,
    While,
    Return,
    Print,
    Function,
    And, Or, Not,
    For, Foreach, In,

    Eof
}

sealed record Token(TokenType Type, string Lexeme, object? Literal, int Line, int Column);
