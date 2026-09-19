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

            if (ch == '.' && Peek(1) == '.' && Peek(2) == '.')
            {
                Advance();
                Advance();
                Advance();
                tokens.Add(new Token(TokenKind.Ellipsis, "...", line, column));
                continue;
            }

            if (ch == '.' && Peek(1) == '.')
            {
                Advance();
                Advance();
                tokens.Add(new Token(TokenKind.Range, "..", line, column));
                continue;
            }

            if (ch == '.')
            {
                Advance();
                tokens.Add(new Token(TokenKind.Dot, ".", line, column));
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

            if (ch == '"')
            {
                tokens.Add(ReadCString(line, column));
                continue;
            }

            if (ch == '\'')
            {
                tokens.Add(ReadQuotedString(line, column));
                continue;
            }

            if (ch == '-' && char.IsDigit(Peek(1)))
            {
                tokens.Add(ReadNumber(line, column));
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
        if (_text[_index] == '-')
        {
            Advance();
        }

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

    private Token ReadCString(int line, int column)
    {
        Advance(); // "
        var start = _index;
        while (_index < _text.Length && _text[_index] != '"')
        {
            if (_text[_index] == '\n')
            {
                throw new CompileException("Unterminated character string.", line, column);
            }

            Advance();
        }

        if (_index >= _text.Length)
        {
            throw new CompileException("Unterminated character string.", line, column);
        }

        var value = _text[start.._index];
        Advance(); // "
        return new Token(TokenKind.CString, value, line, column);
    }

    private Token ReadQuotedString(int line, int column)
    {
        Advance(); // '
        var start = _index;
        while (_index < _text.Length && _text[_index] != '\'')
        {
            if (_text[_index] == '\n')
            {
                throw new CompileException("Unterminated binary/hex string.", line, column);
            }

            Advance();
        }

        if (_index >= _text.Length)
        {
            throw new CompileException("Unterminated binary/hex string.", line, column);
        }

        var value = _text[start.._index];
        Advance(); // '
        if (_index >= _text.Length)
        {
            throw new CompileException("Binary/hex string missing B or H suffix.", line, column);
        }

        var suffix = _text[_index];
        if (suffix is 'B' or 'b')
        {
            Advance();
            return new Token(TokenKind.BString, value, line, column);
        }

        if (suffix is 'H' or 'h')
        {
            Advance();
            return new Token(TokenKind.HString, value, line, column);
        }

        throw new CompileException($"Expected 'B' or 'H' after quoted string, found '{suffix}'.", _line, _column);
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

            if (ch == '/' && Peek(1) == '*')
            {
                Advance();
                Advance();
                while (_index < _text.Length)
                {
                    if (_text[_index] == '*' && Peek(1) == '/')
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
    public const string Set = "SET";
    public const string Of = "OF";
    public const string Choice = "CHOICE";
    public const string Optional = "OPTIONAL";
    public const string Default = "DEFAULT";
    public const string Integer = "INTEGER";
    public const string Boolean = "BOOLEAN";
    public const string Bit = "BIT";
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
    public const string Enumerated = "ENUMERATED";
    public const string Any = "ANY";
    public const string Defined = "DEFINED";
    public const string By = "BY";
    public const string Size = "SIZE";
    public const string Max = "MAX";
    public const string Min = "MIN";
    public const string True = "TRUE";
    public const string False = "FALSE";
    public const string Components = "COMPONENTS";
    public const string Class = "CLASS";
    public const string Real = "REAL";
    public const string External = "EXTERNAL";
    public const string UtcTime = "UTCTime";
    public const string GeneralizedTime = "GeneralizedTime";
    public const string Utf8String = "UTF8String";
    public const string PrintableString = "PrintableString";
    public const string TeletexString = "TeletexString";
    public const string T61String = "T61String";
    public const string Ia5String = "IA5String";
    public const string NumericString = "NumericString";
    public const string VisibleString = "VisibleString";
    public const string BmpString = "BMPString";
    public const string UniversalString = "UniversalString";
    public const string GeneralString = "GeneralString";
    public const string GraphicString = "GraphicString";
    public const string VideotexString = "VideotexString";
}
