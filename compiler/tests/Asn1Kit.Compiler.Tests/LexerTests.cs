using Asn1Kit.Compiler;

namespace Asn1Kit.Tests;

public sealed class LexerTests
{
    [Fact]
    public void TokenizesRangeEllipsisDotAndStrings()
    {
        var tokens = new Asn1Lexer("A ::= INTEGER (1..MAX) ... 'A0'H \"hi\" /* c */ B.C").Tokenize();
        Assert.Contains(tokens, t => t.Kind == TokenKind.Range && t.Text == "..");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Ellipsis && t.Text == "...");
        Assert.Contains(tokens, t => t.Kind == TokenKind.Dot && t.Text == ".");
        Assert.Contains(tokens, t => t.Kind == TokenKind.HString && t.Text == "A0");
        Assert.Contains(tokens, t => t.Kind == TokenKind.CString && t.Text == "hi");
        Assert.DoesNotContain(tokens, t => t.Text.Contains("c", StringComparison.Ordinal) && t.Kind == TokenKind.Identifier);
    }

    [Fact]
    public void TokenizesNegativeNumberAndBString()
    {
        var tokens = new Asn1Lexer("v INTEGER ::= -1 '1010'B").Tokenize();
        Assert.Contains(tokens, t => t.Kind == TokenKind.Number && t.Text == "-1");
        Assert.Contains(tokens, t => t.Kind == TokenKind.BString && t.Text == "1010");
    }
}
