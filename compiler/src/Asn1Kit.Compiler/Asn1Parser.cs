using System.Globalization;
using System.Text;

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
        var nameTok = ExpectIdentifier("module name");
        List<int>? oid = null;
        if (Check(TokenKind.LBrace))
        {
            oid = ParseNumericOidValue();
        }

        ExpectKeyword(Keywords.Definitions);
        var tagDefault = ParseTagDefault();
        Expect(TokenKind.Assign, "::=");
        ExpectKeyword(Keywords.Begin);

        var module = new ModuleAst
        {
            Name = nameTok.Text,
            Oid = oid,
            Source = _source,
            TagDefault = tagDefault,
            Line = nameTok.Line,
            Column = nameTok.Column
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
            ParseAssignment(module);
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
            var symbols = new List<Token> { ExpectIdentifier("imported symbol") };
            while (Check(TokenKind.Comma))
            {
                Advance();
                symbols.Add(ExpectIdentifier("imported symbol"));
            }

            ExpectKeyword(Keywords.From);
            var from = ExpectIdentifier("imported module");
            if (Check(TokenKind.LBrace))
            {
                ParseOidValueAst();
            }

            var import = new ImportAst
            {
                Module = from.Text,
                Line = from.Line,
                Column = from.Column
            };
            foreach (var symbol in symbols)
            {
                if (char.IsUpper(symbol.Text[0]))
                {
                    import.Types.Add(symbol.Text);
                }
                else
                {
                    import.Values.Add(symbol.Text);
                }
            }

            module.Imports.Add(import);
        }

        Expect(TokenKind.Semicolon, ";");
    }

    private void ParseAssignment(ModuleAst module)
    {
        RejectOutOfProfile();
        var name = ExpectIdentifier("assignment name");
        if (char.IsLower(name.Text[0]))
        {
            var type = ParseType();
            Expect(TokenKind.Assign, "::=");
            var value = ParseValue();
            module.ValueAssignments.Add(new ValueAssignmentAst
            {
                Name = name.Text,
                Type = type,
                Value = value,
                Line = name.Line,
                Column = name.Column
            });
            return;
        }

        Expect(TokenKind.Assign, "::=");
        module.TypeAssignments.Add(new TypeAssignmentAst
        {
            Name = name.Text,
            Type = ParseType(),
            Line = name.Line,
            Column = name.Column
        });
    }

    private void RejectOutOfProfile()
    {
        if (IsKeyword(Keywords.Class) || IsKeyword(Keywords.Real) || IsKeyword(Keywords.External))
        {
            throw Error($"'{Peek().Text}' is outside the Asn1Kit compiler profile.");
        }
    }

    private TypeAst ParseType()
    {
        RejectOutOfProfile();
        if (IsKeyword(Keywords.Components))
        {
            throw Error("COMPONENTS OF is outside the Asn1Kit compiler profile.");
        }

        TypeAst type;
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

            tag = new TagAst
            {
                Class = tag.Class,
                Number = tag.Number,
                Mode = mode,
                Line = tag.Line,
                Column = tag.Column
            };
            type = new TaggedTypeAst(tag, ParseType())
            {
                Line = tag.Line,
                Column = tag.Column
            };
            return ApplyTrailingConstraint(type);
        }

        if (IsKeyword(Keywords.Sequence))
        {
            type = ParseSequenceOrSequenceOf();
        }
        else if (IsKeyword(Keywords.Set))
        {
            type = ParseSetOrSetOf();
        }
        else if (IsKeyword(Keywords.Choice))
        {
            Advance();
            type = ParseChoiceBody();
        }
        else if (IsKeyword(Keywords.Integer))
        {
            var tok = Advance();
            List<NamedNumberAst>? named = null;
            if (Check(TokenKind.LBrace))
            {
                named = ParseNamedNumberList();
            }

            type = new BuiltinTypeAst("INTEGER", named) { Line = tok.Line, Column = tok.Column };
        }
        else if (IsKeyword(Keywords.Enumerated))
        {
            var tok = Advance();
            type = new EnumeratedTypeAst(ParseNamedNumberList()) { Line = tok.Line, Column = tok.Column };
        }
        else if (IsKeyword(Keywords.Boolean))
        {
            var tok = Advance();
            type = new BuiltinTypeAst("BOOLEAN") { Line = tok.Line, Column = tok.Column };
        }
        else if (IsKeyword(Keywords.Null))
        {
            var tok = Advance();
            type = new BuiltinTypeAst("NULL") { Line = tok.Line, Column = tok.Column };
        }
        else if (IsKeyword(Keywords.Bit))
        {
            var tok = Advance();
            ExpectKeyword(Keywords.String);
            List<NamedNumberAst>? namedBits = null;
            if (Check(TokenKind.LBrace))
            {
                namedBits = ParseNamedNumberList();
            }

            type = new BitStringTypeAst(namedBits) { Line = tok.Line, Column = tok.Column };
        }
        else if (IsKeyword(Keywords.Octet))
        {
            var tok = Advance();
            ExpectKeyword(Keywords.String);
            type = new BuiltinTypeAst("OCTET STRING") { Line = tok.Line, Column = tok.Column };
        }
        else if (IsKeyword(Keywords.Object))
        {
            var tok = Advance();
            ExpectKeyword(Keywords.Identifier);
            type = new BuiltinTypeAst("OBJECT IDENTIFIER") { Line = tok.Line, Column = tok.Column };
        }
        else if (IsKeyword(Keywords.Any))
        {
            var tok = Advance();
            string? definedBy = null;
            if (IsKeyword(Keywords.Defined))
            {
                Advance();
                ExpectKeyword(Keywords.By);
                definedBy = ExpectIdentifier("DEFINED BY field").Text;
            }

            type = new AnyTypeAst(definedBy) { Line = tok.Line, Column = tok.Column };
        }
        else if (TryParseStringType(out var stringType))
        {
            type = stringType;
        }
        else if (TryParseTimeType(out var timeType))
        {
            type = timeType;
        }
        else
        {
            var ident = ExpectIdentifier("type");
            string? module = null;
            string name = ident.Text;
            if (Check(TokenKind.Dot))
            {
                Advance();
                module = ident.Text;
                name = ExpectIdentifier("type name").Text;
            }

            type = new TypeReferenceAst(name, module) { Line = ident.Line, Column = ident.Column };
        }

        return ApplyTrailingConstraint(type);
    }

    private TypeAst ApplyTrailingConstraint(TypeAst type)
    {
        if (!Check(TokenKind.LParen))
        {
            return type;
        }

        type.Constraint = ParseConstraint();
        return type;
    }

    private TypeAst ParseSequenceOrSequenceOf()
    {
        var tok = Advance();
        ConstraintAst? sizeConstraint = null;
        if (IsKeyword(Keywords.Size))
        {
            sizeConstraint = ParseSizeConstraintKeyword();
        }

        if (IsKeyword(Keywords.Of))
        {
            Advance();
            var sequenceOf = new SequenceOfTypeAst(ParseType())
            {
                Line = tok.Line,
                Column = tok.Column,
                Constraint = sizeConstraint
            };
            return sequenceOf;
        }

        if (sizeConstraint is not null)
        {
            throw Error("SIZE is only valid on SEQUENCE OF / SET OF in this profile.");
        }

        return ParseSequenceBody(tok);
    }

    private TypeAst ParseSetOrSetOf()
    {
        var tok = Advance();
        ConstraintAst? sizeConstraint = null;
        if (IsKeyword(Keywords.Size))
        {
            sizeConstraint = ParseSizeConstraintKeyword();
        }

        if (IsKeyword(Keywords.Of))
        {
            Advance();
            return new SetOfTypeAst(ParseType())
            {
                Line = tok.Line,
                Column = tok.Column,
                Constraint = sizeConstraint
            };
        }

        if (sizeConstraint is not null)
        {
            throw Error("SIZE is only valid on SEQUENCE OF / SET OF in this profile.");
        }

        return ParseSetBody(tok);
    }

    private SequenceTypeAst ParseSequenceBody(Token tok)
    {
        var seq = new SequenceTypeAst { Line = tok.Line, Column = tok.Column };
        Expect(TokenKind.LBrace, "{");
        if (!Check(TokenKind.RBrace))
        {
            ParseComponentList(seq.Fields, allowOptional: true, out var extensible);
            seq.Extensible = extensible;
        }

        Expect(TokenKind.RBrace, "}");
        return seq;
    }

    private SetTypeAst ParseSetBody(Token tok)
    {
        var set = new SetTypeAst { Line = tok.Line, Column = tok.Column };
        Expect(TokenKind.LBrace, "{");
        if (!Check(TokenKind.RBrace))
        {
            ParseComponentList(set.Fields, allowOptional: true, out var extensible);
            set.Extensible = extensible;
        }

        Expect(TokenKind.RBrace, "}");
        return set;
    }

    private ChoiceTypeAst ParseChoiceBody()
    {
        var choice = new ChoiceTypeAst { Line = Peek().Line, Column = Peek().Column };
        Expect(TokenKind.LBrace, "{");
        ParseComponentList(choice.Fields, allowOptional: false, out var extensible);
        choice.Extensible = extensible;
        Expect(TokenKind.RBrace, "}");
        return choice;
    }

    private void ParseComponentList(List<FieldAst> fields, bool allowOptional, out bool extensible)
    {
        extensible = false;
        var first = true;
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.EndOfFile))
        {
            if (!first)
            {
                Expect(TokenKind.Comma, ",");
            }

            first = false;
            if (Check(TokenKind.Ellipsis))
            {
                Advance();
                extensible = true;
                if (Check(TokenKind.Comma))
                {
                    Advance();
                    if (Check(TokenKind.RBrace))
                    {
                        break;
                    }

                    // extension additions after ... are accepted as normal fields
                    first = true;
                    continue;
                }

                break;
            }

            if (IsKeyword(Keywords.Components))
            {
                throw Error("COMPONENTS OF is outside the Asn1Kit compiler profile.");
            }

            fields.Add(ParseComponentType(allowOptional));
        }
    }

    private FieldAst ParseComponentType(bool allowOptional)
    {
        var named = ParseNamedType();
        if (allowOptional && IsKeyword(Keywords.Optional))
        {
            Advance();
            named.Optional = true;
        }

        if (allowOptional && IsKeyword(Keywords.Default))
        {
            Advance();
            named.Default = ParseValue();
            named.Optional = true;
        }

        return named;
    }

    private FieldAst ParseNamedType()
    {
        var name = ExpectIdentifier("field name");
        return new FieldAst
        {
            Name = name.Text,
            Type = ParseType(),
            Optional = false,
            Line = name.Line,
            Column = name.Column
        };
    }

    private bool TryParseStringType(out TypeAst type)
    {
        type = null!;
        string? stringType = null;
        if (IsKeyword(Keywords.Utf8String))
        {
            stringType = StringTypeNames.Utf8;
        }
        else if (IsKeyword(Keywords.PrintableString))
        {
            stringType = StringTypeNames.Printable;
        }
        else if (IsKeyword(Keywords.TeletexString) || IsKeyword(Keywords.T61String))
        {
            stringType = IsKeyword(Keywords.T61String) ? StringTypeNames.T61 : StringTypeNames.Teletex;
        }
        else if (IsKeyword(Keywords.Ia5String))
        {
            stringType = StringTypeNames.Ia5;
        }
        else if (IsKeyword(Keywords.NumericString))
        {
            stringType = StringTypeNames.Numeric;
        }
        else if (IsKeyword(Keywords.VisibleString))
        {
            stringType = StringTypeNames.Visible;
        }
        else if (IsKeyword(Keywords.BmpString))
        {
            stringType = StringTypeNames.Bmp;
        }
        else if (IsKeyword(Keywords.UniversalString))
        {
            stringType = StringTypeNames.Universal;
        }
        else if (IsKeyword(Keywords.GeneralString))
        {
            stringType = StringTypeNames.General;
        }
        else if (IsKeyword(Keywords.GraphicString))
        {
            stringType = StringTypeNames.Graphic;
        }
        else if (IsKeyword(Keywords.VideotexString))
        {
            stringType = StringTypeNames.Videotex;
        }

        if (stringType is null)
        {
            return false;
        }

        var tok = Advance();
        type = new StringTypeAst(stringType) { Line = tok.Line, Column = tok.Column };
        return true;
    }

    private bool TryParseTimeType(out TypeAst type)
    {
        type = null!;
        if (IsKeyword(Keywords.UtcTime))
        {
            var tok = Advance();
            type = new TimeTypeAst(TimeTypeNames.Utc) { Line = tok.Line, Column = tok.Column };
            return true;
        }

        if (IsKeyword(Keywords.GeneralizedTime))
        {
            var tok = Advance();
            type = new TimeTypeAst(TimeTypeNames.Generalized) { Line = tok.Line, Column = tok.Column };
            return true;
        }

        return false;
    }

    private TagAst ParseTag()
    {
        var open = Expect(TokenKind.LBracket, "[");
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
            Number = int.Parse(number.Text, CultureInfo.InvariantCulture),
            Line = open.Line,
            Column = open.Column
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
            if (Check(TokenKind.Ellipsis))
            {
                Advance();
                break;
            }

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
            Value = long.Parse(value.Text, CultureInfo.InvariantCulture),
            Line = name.Line,
            Column = name.Column
        };
    }

    private ConstraintAst ParseConstraint()
    {
        var open = Expect(TokenKind.LParen, "(");
        if (IsKeyword(Keywords.Size))
        {
            var size = ParseSizeConstraintKeyword();
            Expect(TokenKind.RParen, ")");
            size.Line = open.Line;
            size.Column = open.Column;
            return size;
        }

        if (LooksLikeSimpleBoundConstraint() && !HasUnionBeforeMatchingParen())
        {
            var constraint = new ConstraintAst
            {
                HasValue = true,
                Line = open.Line,
                Column = open.Column
            };
            ParseBoundRange(out var min, out var max);
            constraint.ValueMin = min;
            constraint.ValueMax = max;
            Expect(TokenKind.RParen, ")");
            return constraint;
        }

        var raw = CaptureUntilMatchingParen(open);
        return new ConstraintAst
        {
            Unsupported = raw,
            Line = open.Line,
            Column = open.Column
        };
    }

    private bool HasUnionBeforeMatchingParen()
    {
        var depth = 1;
        for (var i = _index; i < _tokens.Count && depth > 0; i++)
        {
            var kind = _tokens[i].Kind;
            if (kind == TokenKind.LParen)
            {
                depth++;
            }
            else if (kind == TokenKind.RParen)
            {
                depth--;
            }
            else if (kind == TokenKind.Union && depth == 1)
            {
                return true;
            }
        }

        return false;
    }

    private ConstraintAst ParseSizeConstraintKeyword()
    {
        var tok = ExpectKeywordToken(Keywords.Size);
        Expect(TokenKind.LParen, "(");
        ParseBoundRange(out var min, out var max);
        Expect(TokenKind.RParen, ")");
        return new ConstraintAst
        {
            HasSize = true,
            SizeMin = min,
            SizeMax = max,
            Line = tok.Line,
            Column = tok.Column
        };
    }

    private bool LooksLikeSimpleBoundConstraint()
    {
        return Check(TokenKind.Number)
               || IsKeyword(Keywords.Min)
               || IsKeyword(Keywords.Max)
               || (Check(TokenKind.Identifier) && char.IsLower(Peek().Text[0]));
    }

    private void ParseBoundRange(out BoundAst min, out BoundAst max)
    {
        min = ParseBound();
        if (Check(TokenKind.Range))
        {
            Advance();
            max = ParseBound();
        }
        else
        {
            max = min;
        }
    }

    private BoundAst ParseBound()
    {
        var tok = Peek();
        if (IsKeyword(Keywords.Min))
        {
            Advance();
            return new BoundAst { IsMin = true, Line = tok.Line, Column = tok.Column };
        }

        if (IsKeyword(Keywords.Max))
        {
            Advance();
            return new BoundAst { IsMax = true, Line = tok.Line, Column = tok.Column };
        }

        if (Check(TokenKind.Number))
        {
            var number = Advance();
            return new BoundAst
            {
                Number = long.Parse(number.Text, CultureInfo.InvariantCulture),
                Line = number.Line,
                Column = number.Column
            };
        }

        var ident = ExpectIdentifier("constraint bound");
        return new BoundAst
        {
            Reference = ident.Text,
            Line = ident.Line,
            Column = ident.Column
        };
    }

    private string CaptureUntilMatchingParen(Token open)
    {
        var depth = 1;
        var sb = new StringBuilder();
        sb.Append('(');
        while (!Check(TokenKind.EndOfFile) && depth > 0)
        {
            var tok = Advance();
            if (tok.Kind == TokenKind.LParen)
            {
                depth++;
            }
            else if (tok.Kind == TokenKind.RParen)
            {
                depth--;
            }

            if (depth == 0)
            {
                sb.Append(')');
                break;
            }

            if (sb.Length > 1)
            {
                sb.Append(' ');
            }

            sb.Append(tok.Text);
        }

        if (depth != 0)
        {
            throw new CompileException("Unterminated constraint.", open.Line, open.Column);
        }

        return sb.ToString();
    }

    private ValueAst ParseValue()
    {
        if (IsKeyword(Keywords.True))
        {
            var tok = Advance();
            return new BooleanValueAst(true) { Line = tok.Line, Column = tok.Column };
        }

        if (IsKeyword(Keywords.False))
        {
            var tok = Advance();
            return new BooleanValueAst(false) { Line = tok.Line, Column = tok.Column };
        }

        if (IsKeyword(Keywords.Null))
        {
            var tok = Advance();
            return new NullValueAst { Line = tok.Line, Column = tok.Column };
        }

        if (Check(TokenKind.Number))
        {
            var number = Advance();
            return new IntegerValueAst(long.Parse(number.Text, CultureInfo.InvariantCulture))
            {
                Line = number.Line,
                Column = number.Column
            };
        }

        if (Check(TokenKind.CString))
        {
            var tok = Advance();
            return new CStringValueAst(tok.Text) { Line = tok.Line, Column = tok.Column };
        }

        if (Check(TokenKind.BString))
        {
            var tok = Advance();
            return new BStringValueAst(tok.Text) { Line = tok.Line, Column = tok.Column };
        }

        if (Check(TokenKind.HString))
        {
            var tok = Advance();
            return new HStringValueAst(tok.Text) { Line = tok.Line, Column = tok.Column };
        }

        if (Check(TokenKind.LBrace))
        {
            return ParseOidValueAst();
        }

        var ident = ExpectIdentifier("value");
        string? module = null;
        var name = ident.Text;
        if (Check(TokenKind.Dot))
        {
            Advance();
            module = ident.Text;
            name = ExpectIdentifier("value name").Text;
        }

        return new ValueReferenceAst(name, module) { Line = ident.Line, Column = ident.Column };
    }

    private OidValueAst ParseOidValueAst()
    {
        var open = Expect(TokenKind.LBrace, "{");
        var arcs = new List<OidArcAst>();
        while (!Check(TokenKind.RBrace))
        {
            if (Check(TokenKind.Number))
            {
                var number = Advance();
                arcs.Add(new OidArcAst
                {
                    Number = int.Parse(number.Text, CultureInfo.InvariantCulture),
                    Line = number.Line,
                    Column = number.Column
                });
                continue;
            }

            var name = ExpectIdentifier("OID component");
            if (Check(TokenKind.LParen))
            {
                Advance();
                var n = Expect(TokenKind.Number, "OID number");
                Expect(TokenKind.RParen, ")");
                arcs.Add(new OidArcAst
                {
                    Name = name.Text,
                    Number = int.Parse(n.Text, CultureInfo.InvariantCulture),
                    Line = name.Line,
                    Column = name.Column
                });
            }
            else
            {
                arcs.Add(new OidArcAst
                {
                    Name = name.Text,
                    Line = name.Line,
                    Column = name.Column
                });
            }
        }

        Expect(TokenKind.RBrace, "}");
        return new OidValueAst(arcs) { Line = open.Line, Column = open.Column };
    }

    private List<int> ParseNumericOidValue()
    {
        var oid = ParseOidValueAst();
        var arcs = new List<int>();
        foreach (var arc in oid.Arcs)
        {
            if (arc.Number is null)
            {
                throw new CompileException(
                    "Module OID must use numeric arcs or name(number).",
                    arc.Line,
                    arc.Column);
            }

            arcs.Add(arc.Number.Value);
        }

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

    private void ExpectKeyword(string keyword) => ExpectKeywordToken(keyword);

    private Token ExpectKeywordToken(string keyword)
    {
        if (!IsKeyword(keyword))
        {
            throw Error($"Expected '{keyword}', found {Peek()}.");
        }

        return Advance();
    }

    private CompileException Error(string message) =>
        new(message, Peek().Line, Peek().Column);
}

internal static class StringTypeNames
{
    public const string Utf8 = "utf8";
    public const string Printable = "printable";
    public const string Teletex = "teletex";
    public const string T61 = "t61";
    public const string Ia5 = "ia5";
    public const string Numeric = "numeric";
    public const string Visible = "visible";
    public const string Bmp = "bmp";
    public const string Universal = "universal";
    public const string General = "general";
    public const string Graphic = "graphic";
    public const string Videotex = "videotex";
}

internal static class TimeTypeNames
{
    public const string Utc = "utc";
    public const string Generalized = "generalized";
}
