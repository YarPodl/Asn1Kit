using System.Globalization;

namespace Asn1Kit.Compiler;

internal sealed class Asn1Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly string? _source;
    private int _index;

    private Asn1Parser(IReadOnlyList<Token> tokens, string? source)
    {
        _tokens = tokens;
        _source = source;
    }

    public static ModuleAst Parse(string text, string? source = null)
    {
        var tokens = new Asn1Lexer(text).Tokenize();
        return new Asn1Parser(tokens, source).ParseModule();
    }

    private ModuleAst ParseModule()
    {
        var name = ExpectIdentifier("module name");
        List<int>? oid = null;
        if (Check(TokenKind.LBrace))
        {
            oid = ParseOidValue();
        }

        ExpectKeyword(Keywords.Definitions);
        var tagDefault = ParseTagDefault();
        Expect(TokenKind.Assign, "::=");
        ExpectKeyword(Keywords.Begin);

        var module = new ModuleAst
        {
            Name = name.Text,
            Oid = oid,
            Source = _source,
            TagDefault = tagDefault
        };

        if (IsKeyword(Keywords.Exports))
        {
            SkipExports();
        }

        if (IsKeyword(Keywords.Imports))
        {
            ParseImports(module);
        }

        while (!IsKeyword(Keywords.End) && !Check(TokenKind.EndOfFile))
        {
            module.Assignments.Add(ParseAssignment());
        }

        ExpectKeyword(Keywords.End);
        return module;
    }

    private TagDefaultKind ParseTagDefault()
    {
        if (IsKeyword(Keywords.Explicit))
        {
            Advance();
            ExpectKeyword(Keywords.Tags);
            return TagDefaultKind.Explicit;
        }

        if (IsKeyword(Keywords.Implicit))
        {
            Advance();
            ExpectKeyword(Keywords.Tags);
            return TagDefaultKind.Implicit;
        }

        if (IsKeyword(Keywords.Automatic))
        {
            Advance();
            ExpectKeyword(Keywords.Tags);
            return TagDefaultKind.Automatic;
        }

        return TagDefaultKind.Explicit;
    }

    private void SkipExports()
    {
        ExpectKeyword(Keywords.Exports);
        if (IsKeyword(Keywords.All))
        {
            Advance();
        }
        else
        {
            while (!Check(TokenKind.Semicolon) && !Check(TokenKind.EndOfFile))
            {
                Advance();
            }
        }

        Expect(TokenKind.Semicolon, ";");
    }

    private void ParseImports(ModuleAst module)
    {
        ExpectKeyword(Keywords.Imports);
        while (!Check(TokenKind.Semicolon) && !Check(TokenKind.EndOfFile))
        {
            var types = new List<string> { ExpectIdentifier("imported type").Text };
            while (Check(TokenKind.Comma))
            {
                Advance();
                types.Add(ExpectIdentifier("imported type").Text);
            }

            ExpectKeyword(Keywords.From);
            var from = ExpectIdentifier("imported module").Text;
            if (Check(TokenKind.LBrace))
            {
                ParseOidValue();
            }

            var import = new ImportAst { Module = from };
            import.Types.AddRange(types);
            module.Imports.Add(import);
        }

        Expect(TokenKind.Semicolon, ";");
    }

    private AssignmentAst ParseAssignment()
    {
        var name = ExpectIdentifier("type assignment");
        Expect(TokenKind.Assign, "::=");
        return new AssignmentAst { Name = name.Text, Type = ParseType() };
    }

    private TypeAst ParseType()
    {
        if (Check(TokenKind.LBracket))
        {
            var tag = ParseTag();
            string? mode = null;
            if (IsKeyword(Keywords.Implicit))
            {
                Advance();
                mode = "implicit";
            }
            else if (IsKeyword(Keywords.Explicit))
            {
                Advance();
                mode = "explicit";
            }

            tag = new TagAst { Class = tag.Class, Number = tag.Number, Mode = mode };
            return new TaggedTypeAst(tag, ParseType());
        }

        if (IsKeyword(Keywords.Sequence))
        {
            Advance();
            if (IsKeyword(Keywords.Of))
            {
                Advance();
                return new SequenceOfTypeAst(ParseType());
            }

            return ParseSequenceBody();
        }

        if (IsKeyword(Keywords.Choice))
        {
            Advance();
            return ParseChoiceBody();
        }

        if (IsKeyword(Keywords.Integer))
        {
            Advance();
            List<NamedNumberAst>? named = null;
            if (Check(TokenKind.LBrace))
            {
                named = ParseNamedNumberList();
            }

            return new BuiltinTypeAst("INTEGER", named);
        }

        if (IsKeyword(Keywords.Enumerated))
        {
            Advance();
            return new EnumeratedTypeAst(ParseNamedNumberList());
        }

        if (IsKeyword(Keywords.Boolean))
        {
            Advance();
            return new BuiltinTypeAst("BOOLEAN");
        }

        if (IsKeyword(Keywords.Null))
        {
            Advance();
            return new BuiltinTypeAst("NULL");
        }

        if (IsKeyword(Keywords.Octet))
        {
            Advance();
            ExpectKeyword(Keywords.String);
            return new BuiltinTypeAst("OCTET STRING");
        }

        if (IsKeyword(Keywords.Object))
        {
            Advance();
            ExpectKeyword(Keywords.Identifier);
            return new BuiltinTypeAst("OBJECT IDENTIFIER");
        }

        var ident = ExpectIdentifier("type");
        return new TypeReferenceAst(ident.Text);
    }

    private SequenceTypeAst ParseSequenceBody()
    {
        var seq = new SequenceTypeAst();
        Expect(TokenKind.LBrace, "{");
        if (!Check(TokenKind.RBrace))
        {
            seq.Fields.Add(ParseComponentType());
            while (Check(TokenKind.Comma))
            {
                Advance();
                seq.Fields.Add(ParseComponentType());
            }
        }

        Expect(TokenKind.RBrace, "}");
        return seq;
    }

    private ChoiceTypeAst ParseChoiceBody()
    {
        var choice = new ChoiceTypeAst();
        Expect(TokenKind.LBrace, "{");
        choice.Fields.Add(ParseNamedType());
        while (Check(TokenKind.Comma))
        {
            Advance();
            choice.Fields.Add(ParseNamedType());
        }

        Expect(TokenKind.RBrace, "}");
        return choice;
    }

    private FieldAst ParseComponentType()
    {
        var named = ParseNamedType();
        var optional = false;
        if (IsKeyword(Keywords.Optional))
        {
            Advance();
            optional = true;
        }

        return new FieldAst { Name = named.Name, Type = named.Type, Optional = optional };
    }

    private FieldAst ParseNamedType()
    {
        var name = ExpectIdentifier("field name");
        return new FieldAst { Name = name.Text, Type = ParseType(), Optional = false };
    }

    private TagAst ParseTag()
    {
        Expect(TokenKind.LBracket, "[");
        var cls = "context";
        if (IsKeyword(Keywords.Universal))
        {
            Advance();
            cls = "universal";
        }
        else if (IsKeyword(Keywords.Application))
        {
            Advance();
            cls = "application";
        }
        else if (IsKeyword(Keywords.Private))
        {
            Advance();
            cls = "private";
        }

        var number = Expect(TokenKind.Number, "tag number");
        Expect(TokenKind.RBracket, "]");
        return new TagAst
        {
            Class = cls,
            Number = int.Parse(number.Text, CultureInfo.InvariantCulture)
        };
    }

    private List<NamedNumberAst> ParseNamedNumberList()
    {
        var list = new List<NamedNumberAst>();
        Expect(TokenKind.LBrace, "{");
        list.Add(ParseNamedNumber());
        while (Check(TokenKind.Comma))
        {
            Advance();
            list.Add(ParseNamedNumber());
        }

        Expect(TokenKind.RBrace, "}");
        return list;
    }

    private NamedNumberAst ParseNamedNumber()
    {
        var name = ExpectIdentifier("named number");
        Expect(TokenKind.LParen, "(");
        var value = Expect(TokenKind.Number, "integer value");
        Expect(TokenKind.RParen, ")");
        return new NamedNumberAst
        {
            Name = name.Text,
            Value = long.Parse(value.Text, CultureInfo.InvariantCulture)
        };
    }

    private List<int> ParseOidValue()
    {
        var arcs = new List<int>();
        Expect(TokenKind.LBrace, "{");
        while (!Check(TokenKind.RBrace))
        {
            if (Check(TokenKind.Number))
            {
                arcs.Add(int.Parse(Advance().Text, CultureInfo.InvariantCulture));
                continue;
            }

            ExpectIdentifier("OID component");
            if (!Check(TokenKind.LParen))
            {
                throw Error("OID component must be a number or name(number).");
            }

            Advance();
            var n = Expect(TokenKind.Number, "OID number");
            Expect(TokenKind.RParen, ")");
            arcs.Add(int.Parse(n.Text, CultureInfo.InvariantCulture));
        }

        Expect(TokenKind.RBrace, "}");
        return arcs;
    }

    private Token Peek() => _tokens[_index];

    private bool Check(TokenKind kind) => Peek().Kind == kind;

    private bool IsKeyword(string keyword) =>
        Peek().Kind == TokenKind.Identifier && Peek().Text == keyword;

    private Token Advance()
    {
        var token = Peek();
        if (token.Kind != TokenKind.EndOfFile)
        {
            _index++;
        }

        return token;
    }

    private Token Expect(TokenKind kind, string what)
    {
        if (Peek().Kind != kind)
        {
            throw Error($"Expected {what}, found {Peek()}.");
        }

        return Advance();
    }

    private Token ExpectIdentifier(string what) => Expect(TokenKind.Identifier, what);

    private void ExpectKeyword(string keyword)
    {
        if (!IsKeyword(keyword))
        {
            throw Error($"Expected '{keyword}', found {Peek()}.");
        }

        Advance();
    }

    private CompileException Error(string message) =>
        new(message, Peek().Line, Peek().Column);
}
