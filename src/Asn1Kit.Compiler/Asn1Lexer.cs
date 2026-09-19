namespace Asn1Kit.Compiler;

internal sealed class Asn1Lexer
{
    private readonly string _text;
    private int _index;
    private int _line = 1;
    private int _column = 1;

    public Asn1Lexer(string text)
    {
        _text = text;
    }

    public IReadOnlyList<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (true)
        {
            SkipTrivia();
            if (_index >= _text.Length)
            {
                tokens.Add(new Token(TokenKind.EndOfFile, "", _line, _column));
                return tokens;
            }

            var line = _line;
            var column = _column;
            var ch = _text[_index];

            if (ch == ':' && Peek(1) == ':' && Peek(2) == '=')
            {
                Advance();
                Advance();
                Advance();
                tokens.Add(new Token(TokenKind.Assign, "::=", line, column));
                continue;
            }

            switch (ch)
            {
                case '{':
                    Advance();
                    tokens.Add(new Token(TokenKind.LBrace, "{", line, column));
                    continue;
                case '}':
                    Advance();
                    tokens.Add(new Token(TokenKind.RBrace, "}", line, column));
                    continue;
                case '[':
                    Advance();
                    tokens.Add(new Token(TokenKind.LBracket, "[", line, column));
                    continue;
                case ']':
                    Advance();
                    tokens.Add(new Token(TokenKind.RBracket, "]", line, column));
                    continue;
                case '(':
                    Advance();
                    tokens.Add(new Token(TokenKind.LParen, "(", line, column));
                    continue;
                case ')':
                    Advance();
                    tokens.Add(new Token(TokenKind.RParen, ")", line, column));
                    continue;
                case ',':
                    Advance();
                    tokens.Add(new Token(TokenKind.Comma, ",", line, column));
                    continue;
                case ';':
                    Advance();
                    tokens.Add(new Token(TokenKind.Semicolon, ";", line, column));
                    continue;
            }

            if (char.IsDigit(ch))
            {
                tokens.Add(ReadNumber(line, column));
                continue;
            }

            if (IsIdentStart(ch))
            {
                tokens.Add(ReadIdentifier(line, column));
                continue;
            }

            throw new CompileException($"Unexpected character '{ch}'.", line, column);
        }
    }

    private Token ReadNumber(int line, int column)
    {
        var start = _index;
        while (_index < _text.Length && char.IsDigit(_text[_index]))
        {
            Advance();
        }

        return new Token(TokenKind.Number, _text[start.._index], line, column);
    }

    private Token ReadIdentifier(int line, int column)
    {
        var start = _index;
        Advance();
        while (_index < _text.Length && IsIdentPart(_text[_index]))
        {
            Advance();
        }

        return new Token(TokenKind.Identifier, _text[start.._index], line, column);
    }

    private void SkipTrivia()
    {
        while (_index < _text.Length)
        {
            var ch = _text[_index];
            if (char.IsWhiteSpace(ch))
            {
                Advance();
                continue;
            }

            if (ch == '-' && Peek(1) == '-')
            {
                Advance();
                Advance();
                while (_index < _text.Length && _text[_index] != '\n')
                {
                    if (_text[_index] == '-' && Peek(1) == '-')
                    {
                        Advance();
                        Advance();
                        break;
                    }

                    Advance();
                }

                continue;
            }

            break;
        }
    }

    private char Peek(int offset)
    {
        var i = _index + offset;
        return i < _text.Length ? _text[i] : '\0';
    }

    private void Advance()
    {
        if (_index >= _text.Length)
        {
            return;
        }

        if (_text[_index] == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        _index++;
    }

    private static bool IsIdentStart(char ch) => ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    private static bool IsIdentPart(char ch) => IsIdentStart(ch) || char.IsDigit(ch) || ch == '-';
}

internal static class Keywords
{
    public const string Definitions = "DEFINITIONS";
    public const string Begin = "BEGIN";
    public const string End = "END";
    public const string Sequence = "SEQUENCE";
    public const string Of = "OF";
    public const string Choice = "CHOICE";
    public const string Optional = "OPTIONAL";
    public const string Integer = "INTEGER";
    public const string Boolean = "BOOLEAN";
    public const string Octet = "OCTET";
    public const string String = "STRING";
    public const string Null = "NULL";
    public const string Object = "OBJECT";
    public const string Identifier = "IDENTIFIER";
    public const string Implicit = "IMPLICIT";
    public const string Explicit = "EXPLICIT";
    public const string Automatic = "AUTOMATIC";
    public const string Tags = "TAGS";
    public const string Imports = "IMPORTS";
    public const string From = "FROM";
    public const string Exports = "EXPORTS";
    public const string All = "ALL";
    public const string Universal = "UNIVERSAL";
    public const string Application = "APPLICATION";
    public const string Private = "PRIVATE";
}
