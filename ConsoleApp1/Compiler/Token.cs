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

    Integer, Whole, Real,
    Byte, Integer8, Integer16, Integer32, Whole8, Whole16, Whole32, Real32, Real64,
    Boolean, Void, Optional, Fallible,
    Array, Object, Record, Interface, Enum,
    Constant,
    If, Then, Else,
    Switch, Case, Default,
    While, Break, Continue,
    Return,
    Print,
    Function,
    Static,
    Constructor,
    Implement, Via,
    Import, Export, From, As,
    Package, Public, Private,
    And, Or, Not,
    For, Foreach, In,
    New,
    Panic,
    Error, On, Yield,
    None,

    Eof
}

sealed record Token(TokenType Type, string Lexeme, object? Literal, int Line, int Column);
