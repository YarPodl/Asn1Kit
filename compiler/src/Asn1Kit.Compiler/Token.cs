namespace Asn1Kit.Compiler;

public enum TokenKind
{
    Identifier,
    Number,
    Assign,
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    LParen,
    RParen,
    Comma,
    Semicolon,
    Dot,
    Range,
    Ellipsis,
    Union,
    Ampersand,
    At,
    Colon,
    CString,
    BString,
    HString,
    EndOfFile
}

public readonly struct Token
{
    public Token(TokenKind kind, string text, int line, int column)
    {
        Kind = kind;
        Text = text;
        Line = line;
        Column = column;
    }

    public TokenKind Kind { get; }
    public string Text { get; }
    public int Line { get; }
    public int Column { get; }

    public override string ToString() => $"{Kind} '{Text}' @{Line}:{Column}";
}

public sealed class CompileException : Exception
{
    public CompileException(string message, int line, int column, string? source = null)
        : base($"{message} ({(source is null ? "" : source + ":")}{line}:{column})")
    {
        Detail = message;
        SourceFile = source;
        Line = line;
        Column = column;
    }

    public int Line { get; }
    public string Detail { get; }
    public string? SourceFile { get; }
    public int Column { get; }
}
