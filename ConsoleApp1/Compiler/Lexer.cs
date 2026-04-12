using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConsoleApp1.Compiler;

sealed class Lexer
{
    private readonly string _source;
    private readonly List<Token> _tokens = new();
    private int _start;
    private int _current;
    private int _line = 1;
    private int _col = 1;

    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
    {
        { "integer", TokenType.Integer },
        { "whole", TokenType.Whole },
        { "real", TokenType.Real },
        { "boolean", TokenType.Boolean },
        { "void", TokenType.Void },
        { "optional", TokenType.Optional },
        { "fallible", TokenType.Fallible },
        { "array", TokenType.Array },
        { "object", TokenType.Object },
        { "record", TokenType.Record },
        { "interface", TokenType.Interface },
        { "enum", TokenType.Enum },
        { "constant", TokenType.Constant },
        { "if", TokenType.If },
        { "then", TokenType.Then },
        { "else", TokenType.Else },
        { "switch", TokenType.Switch },
        { "case", TokenType.Case },
        { "default", TokenType.Default },
        { "while", TokenType.While },
        { "break", TokenType.Break },
        { "continue", TokenType.Continue },
        { "return", TokenType.Return },
        { "print", TokenType.Print },
        { "function", TokenType.Function },
        { "constructor", TokenType.Constructor },
        { "implement", TokenType.Implement },
        { "via", TokenType.Via },
        { "import", TokenType.Import },
        { "export", TokenType.Export },
        { "from", TokenType.From },
        { "as", TokenType.As },
        { "package", TokenType.Package },
        { "public", TokenType.Public },
        { "private", TokenType.Private },
        { "new", TokenType.New },
        { "true", TokenType.True },
        { "false", TokenType.False },
        { "and", TokenType.And },
        { "or", TokenType.Or },
        { "not", TokenType.Not },
        { "for", TokenType.For },
        { "foreach", TokenType.Foreach },
        { "in", TokenType.In },
        { "panic", TokenType.Panic },
        { "error", TokenType.Error },
        { "on", TokenType.On },
        { "yield", TokenType.Yield },
        { "none", TokenType.None },
    };

    public Lexer(string source)
    {
        _source = source;
    }

    public IReadOnlyList<Token> ScanTokens()
    {
        while (!IsAtEnd())
        {
            _start = _current;
            ScanToken();
        }

        _tokens.Add(new Token(TokenType.Eof, string.Empty, null, _line, _col));
        return _tokens;
    }

    private void ScanToken()
    {
        char c = Advance();
        switch (c)
        {
            case '(': AddToken(TokenType.LeftParen); break;
            case ')': AddToken(TokenType.RightParen); break;
            case '{': AddToken(TokenType.LeftBrace); break;
            case '}': AddToken(TokenType.RightBrace); break;
            case '[': AddToken(TokenType.LeftBracket); break;
            case ']': AddToken(TokenType.RightBracket); break;
            case ',': AddToken(TokenType.Comma); break;
            case '.':
                if (IsDigit(Peek()))
                    Number();
                else
                    AddToken(TokenType.Dot);
                break;
            case ';': AddToken(TokenType.Semicolon); break;
            case '+':
                if (Match('+')) AddToken(TokenType.PlusPlus);
                else if (Match('=')) AddToken(TokenType.PlusEqual);
                else AddToken(TokenType.Plus);
                break;
            case '-':
                if (Match('-')) AddToken(TokenType.MinusMinus);
                else if (Match('=')) AddToken(TokenType.MinusEqual);
                else AddToken(TokenType.Minus);
                break;
            case '*':
                AddToken(Match('=') ? TokenType.StarEqual : TokenType.Star);
                break;
            case '/':
                if (Match('/')) // line comment
                {
                    while (Peek() != '\n' && !IsAtEnd()) Advance();
                }
                else if (Match('*')) // block comment
                {
                    while (!(Peek() == '*' && PeekNext() == '/') && !IsAtEnd())
                    {
                        if (Peek() == '\n') { _line++; _col = 1; }
                        Advance();
                    }
                    if (!IsAtEnd()) { Advance(); Advance(); } // consume */
                }
                else
                {
                    AddToken(Match('=') ? TokenType.SlashEqual : TokenType.Slash);
                }
                break;
            case '%':
                AddToken(Match('=') ? TokenType.PercentEqual : TokenType.Percent);
                break;
            case '=':
                AddToken(Match('=') ? TokenType.EqualEqual : TokenType.Equal);
                break;
            case '!':
                AddToken(Match('=') ? TokenType.BangEqual : throw Error("Unexpected '!'"));
                break;
            case '<':
                AddToken(Match('=') ? TokenType.LessEqual : TokenType.Less);
                break;
            case '>':
                AddToken(Match('=') ? TokenType.GreaterEqual : TokenType.Greater);
                break;
            case '"':
                StringLiteral();
                break;
            case ' ':
            case '\r':
            case '\t':
                break;
            case '\n':
                _line++;
                _col = 1;
                break;
            default:
                if (IsDigit(c))
                {
                    Number();
                }
                else if (IsAlpha(c))
                {
                    Identifier();
                }
                else
                {
                    throw Error($"Unexpected character '{c}'");
                }
                break;
        }
    }

    private void Identifier()
    {
        while (IsAlphaNumeric(Peek())) Advance();
        string text = _source[_start.._current];
        if (Keywords.TryGetValue(text, out var type))
        {
            AddToken(type);
        }
        else
        {
            AddToken(TokenType.Identifier);
        }
    }

    private void Number()
    {
        if (_source[_start] == '.')
        {
            while (IsDigit(Peek())) Advance();
            AddRealToken();
            return;
        }

        if (_source[_start] == '0')
        {
            char prefix = Peek();
            if (prefix is 'b' or 'B' or 'o' or 'O' or 'x' or 'X')
            {
                Advance();
                int numberBase = prefix is 'b' or 'B'
                    ? 2
                    : prefix is 'o' or 'O'
                        ? 8
                        : 16;
                string baseName = numberBase == 2
                    ? "binary"
                    : numberBase == 8
                        ? "octal"
                        : "hexadecimal";
                int digitsStart = _current;
                while (IsDigitForBase(Peek(), numberBase)) Advance();

                if (_current == digitsStart)
                    throw Error($"Invalid {baseName} integer literal '{_source[_start.._current]}'");
                if (IsAlphaNumeric(Peek()))
                    throw Error($"Invalid digit '{Peek()}' in {baseName} integer literal");

                string digits = _source[digitsStart.._current];
                try
                {
                    AddToken(TokenType.Number, Convert.ToInt32(digits, numberBase));
                    return;
                }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                {
                    throw Error($"Invalid integer literal '{_source[_start.._current]}'");
                }
            }
        }

        while (IsDigit(Peek())) Advance();
        if (Peek() == '.')
        {
            Advance();
            while (IsDigit(Peek())) Advance();
            AddRealToken();
            return;
        }
        if (IsAlpha(Peek()))
            throw Error($"Invalid integer literal suffix '{Peek()}'");

        string text = _source[_start.._current];
        if (!int.TryParse(text, out int value))
            throw Error($"Invalid integer literal '{text}'");
        AddToken(TokenType.Number, value);
    }

    private void AddRealToken()
    {
        if (IsAlpha(Peek()))
            throw Error($"Invalid real literal suffix '{Peek()}'");

        string text = _source[_start.._current];
        if (!double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value))
            throw Error($"Invalid real literal '{text}'");
        AddToken(TokenType.Number, value);
    }

    private void StringLiteral()
    {
        while (Peek() != '"' && !IsAtEnd())
        {
            if (Peek() == '\n') { _line++; _col = 1; }
            Advance();
        }
        if (IsAtEnd()) throw Error("Unterminated string.");
        Advance(); // closing quote
        string value = _source.Substring(_start + 1, (_current - 1) - (_start + 1));
        value = value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        AddToken(TokenType.String, value);
    }

    private bool Match(char expected)
    {
        if (IsAtEnd()) return false;
        if (_source[_current] != expected) return false;
        _current++; _col++;
        return true;
    }

    private char Peek() => IsAtEnd() ? '\0' : _source[_current];
    private char PeekNext() => (_current + 1 >= _source.Length) ? '\0' : _source[_current + 1];

    private bool IsAlpha(char c) => char.IsLetter(c) || c == '_';
    private bool IsDigit(char c) => c is >= '0' and <= '9';
    private bool IsAlphaNumeric(char c) => IsAlpha(c) || IsDigit(c);
    private static bool IsDigitForBase(char c, int numberBase) => numberBase switch
    {
        2 => c is '0' or '1',
        8 => c is >= '0' and <= '7',
        16 => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F'),
        _ => false
    };

    private char Advance()
    {
        char c = _source[_current++];
        _col++;
        return c;
    }

    private bool IsAtEnd() => _current >= _source.Length;

    private void AddToken(TokenType type, object? literal = null)
    {
        string text = _source[_start.._current];
        _tokens.Add(new Token(type, text, literal, _line, _col - (text.Length)));
    }

    private Exception Error(string message) => new CompilerException(message, _line, _col);
}
