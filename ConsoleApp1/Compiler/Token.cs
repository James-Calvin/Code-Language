namespace ConsoleApp1.Compiler;

enum TokenType
{
    LeftParen, RightParen,
    LeftBrace, RightBrace,
    LeftBracket, RightBracket,
    Comma,
    Dot,
    Semicolon,
    Plus, Minus, Star, Slash, Percent,
    PlusEqual, MinusEqual, StarEqual, SlashEqual, PercentEqual,
    PlusPlus, MinusMinus,
    Equal, Less, Greater,

    EqualEqual, BangEqual,
    LessEqual, GreaterEqual,

    Identifier, Number, String,
    True, False,

    Integer, Whole, Real, Boolean, Void, Optional,
    Array, Object, Record, Interface, Enum,
    Constant,
    If, Then, Else,
    Switch, Case, Default,
    While,
    Return,
    Print,
    Function,
    Constructor,
    Implement, Via,
    Import, Export, From, As,
    Package, Public, Private,
    And, Or, Not,
    For, Foreach, In,
    New,
    Panic,
    None,

    Eof
}

sealed record Token(TokenType Type, string Lexeme, object? Literal, int Line, int Column);
