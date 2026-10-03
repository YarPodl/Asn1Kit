using System.Globalization;
using Asn1Kit.Ir;

namespace Asn1Kit.Compiler;

internal sealed partial class Asn1Parser
{
    internal static TypeAst ReadType(IReadOnlyList<Token> tokens, out int consumed)
    {
        var parser = Fragment(tokens);
        var type = parser.ParseType();
        consumed = parser._index;
        return type;
    }

    internal static ValueAst ReadValue(IReadOnlyList<Token> tokens, TypeAst? type, out int consumed)
    {
        var parser = Fragment(tokens);
        var value = parser.ParseConcreteValue(type);
        consumed = parser._index;
        return value;
    }

    private static Asn1Parser Fragment(IReadOnlyList<Token> tokens)
    {
        var list = tokens.ToList();
        var last = list.LastOrDefault();
        list.Add(new Token(TokenKind.EndOfFile, "", last.Line, last.Column));
        return new Asn1Parser(list, null);
    }

    private Token ParseImportedSymbol()
    {
        var symbol = ExpectIdentifier("imported symbol");
        if (Check(TokenKind.LBrace))
        {
            Advance();
            Expect(TokenKind.RBrace, "} after parameterized import");
        }
        return symbol;
    }

    private List<FormalParameterAst> ParseFormalParameters()
    {
        var groups = SplitTopLevel(CaptureGroup(TokenKind.LBrace, TokenKind.RBrace), TokenKind.Comma);
        var result = new List<FormalParameterAst>();
        foreach (var group in groups)
        {
            var colon = group.FindIndex(t => t.Kind == TokenKind.Colon);
            var name = group.LastOrDefault();
            if (name.Kind != TokenKind.Identifier || (colon >= 0 && colon != group.Count - 2))
                throw Error("Invalid formal parameter.");
            if (result.Any(p => p.Name == name.Text)) throw Error($"Duplicate formal parameter '{name.Text}'.");
            result.Add(new FormalParameterAst
            {
                Name = name.Text, Governor = colon < 0 ? null : group.Take(colon).ToArray(),
                Line = name.Line, Column = name.Column
            });
        }
        return result;
    }

    private InformationClassAst ParseInformationClass()
    {
        var token = ExpectKeywordToken("CLASS");
        var result = new InformationClassAst { Line = token.Line, Column = token.Column };
        if (!Check(TokenKind.LBrace)) throw Error("Incomplete CLASS declaration is outside the Asn1Kit compiler profile.");
        Expect(TokenKind.LBrace, "{");
        while (!Check(TokenKind.RBrace))
        {
            Expect(TokenKind.Ampersand, "& before class field");
            var name = ExpectIdentifier("class field");
            TypeAst? governor = null;
            if (!Check(TokenKind.Comma) && !Check(TokenKind.RBrace) &&
                !IsKeyword("OPTIONAL") && !IsKeyword("DEFAULT"))
                governor = ParseType();
            var field = new InformationFieldAst
            {
                Name = name.Text, Governor = governor, Line = name.Line, Column = name.Column
            };
            if (IsKeyword("UNIQUE")) { Advance(); field.Unique = true; }
            if (IsKeyword("OPTIONAL")) { Advance(); field.Optional = true; }
            else if (IsKeyword("DEFAULT"))
            {
                Advance();
                var start = _index;
                if (field.Governor is null) ParseType();
                else if (char.IsUpper(field.Name[0])) ParseValue();
                else ParseConcreteValue(field.Governor);
                field.Default = new RawValueAst { Tokens = _tokens.Skip(start).Take(_index - start).ToArray(), Line = name.Line, Column = name.Column };
            }
            if (result.Fields.Any(f => f.Name == field.Name))
                throw Error($"Duplicate class field '&{field.Name}'.");
            result.Fields.Add(field);
            if (!Check(TokenKind.RBrace)) Expect(TokenKind.Comma, ",");
        }
        Advance();
        if (IsKeyword("WITH"))
        {
            Advance(); ExpectKeyword("SYNTAX"); Expect(TokenKind.LBrace, "{");
            result.Syntax = ParseSyntax(TokenKind.RBrace);
            Expect(TokenKind.RBrace, "}");
        }
        return result;
    }

    private List<SyntaxElementAst> ParseSyntax(TokenKind terminator)
    {
        var result = new List<SyntaxElementAst>();
        while (!Check(terminator))
        {
            var token = Peek();
            if (Check(TokenKind.EndOfFile)) throw Error("Unterminated WITH SYNTAX template.");
            if (Check(TokenKind.LBracket))
            {
                Advance();
                var group = ParseSyntax(TokenKind.RBracket);
                Expect(TokenKind.RBracket, "]");
                result.Add(new SyntaxElementAst { OptionalGroup = group, Line = token.Line, Column = token.Column });
            }
            else if (Check(TokenKind.Ampersand))
            {
                Advance();
                result.Add(new SyntaxElementAst { Field = ExpectIdentifier("syntax field").Text, Line = token.Line, Column = token.Column });
            }
            else
            {
                result.Add(new SyntaxElementAst { Literal = Advance().Text, Line = token.Line, Column = token.Column });
            }
        }
        return result;
    }

    private TypeAst ParseReferenceType()
    {
        var token = ExpectIdentifier("type");
        var owner = token.Text;
        string? module = null;
        if (Check(TokenKind.Dot))
        {
            Advance();
            if (!Check(TokenKind.Ampersand))
            {
                module = owner; owner = ExpectIdentifier("type name").Text;
                if (Check(TokenKind.Dot)) Advance();
            }
            if (Check(TokenKind.Ampersand))
            {
                var field = new ObjectFieldTypeAst { Owner = owner, Module = module, Line = token.Line, Column = token.Column };
                ParseFieldPath(field.Fields);
                return field;
            }
        }
        var reference = new TypeReferenceAst(owner, module) { Line = token.Line, Column = token.Column };
        if (Check(TokenKind.LBrace))
            reference.Arguments = SplitTopLevel(CaptureGroup(TokenKind.LBrace, TokenKind.RBrace), TokenKind.Comma)
                .Select(g => (IReadOnlyList<Token>)g).ToList();
        return reference;
    }

    private ObjectFieldValueAst ParseObjectFieldValue(string owner, string? module, Token token)
    {
        var value = new ObjectFieldValueAst { Owner = owner, Module = module, Line = token.Line, Column = token.Column };
        ParseFieldPath(value.Fields);
        return value;
    }

    private void ParseFieldPath(List<string> fields)
    {
        do
        {
            Expect(TokenKind.Ampersand, "&");
            fields.Add(ExpectIdentifier("object field name").Text);
            if (!Check(TokenKind.Dot) || _tokens[_index + 1].Kind != TokenKind.Ampersand) break;
            Advance();
        } while (true);
    }

    private void ParseTableConstraint(ObjectFieldTypeAst field)
    {
        Expect(TokenKind.LParen, "(");
        field.Table = CaptureGroup(TokenKind.LBrace, TokenKind.RBrace, includeDelimiters: true);
        if (Check(TokenKind.LBrace))
        {
            Advance(); Expect(TokenKind.At, "@ component relation");
            var selector = new IrOpenTypeSelector();
            while (Check(TokenKind.Dot)) { Advance(); selector.Levels++; }
            field.SelectorOutermost = selector.Levels == 0;
            selector.Levels = Math.Max(0, selector.Levels - 1);
            selector.Path.Add(ExpectIdentifier("selector component").Text);
            while (Check(TokenKind.Dot)) { Advance(); selector.Path.Add(ExpectIdentifier("selector component").Text); }
            Expect(TokenKind.RBrace, "}");
            field.Selector = selector;
        }
        Expect(TokenKind.RParen, ")");
    }

    private EnumeratedTypeAst ParseEnumerated(Token token)
    {
        Expect(TokenKind.LBrace, "{");
        var items = new List<(Token Name, long? Number, bool Extension)>();
        var extensible = false;
        while (!Check(TokenKind.RBrace))
        {
            if (Check(TokenKind.Ellipsis))
            {
                if (extensible) throw Error("Duplicate ENUMERATED extension marker.");
                Advance(); extensible = true;
            }
            else
            {
                var name = ExpectIdentifier("enumeration value");
                long? number = null;
                if (Check(TokenKind.LParen))
                {
                    Advance(); number = long.Parse(Expect(TokenKind.Number, "enumeration number").Text, CultureInfo.InvariantCulture);
                    Expect(TokenKind.RParen, ")");
                }
                if (items.Any(i => i.Name.Text == name.Text)) throw Error("Duplicate ENUMERATED identifier.");
                items.Add((name, number, extensible));
            }
            if (!Check(TokenKind.RBrace)) Expect(TokenKind.Comma, ",");
        }
        Advance();
        var values = new List<NamedNumberAst>();
        var reserved = items.Where(i => !i.Extension && i.Number.HasValue).Select(i => i.Number!.Value).ToHashSet();
        var used = new HashSet<long>();
        long nextRoot = 0;
        long? previousExtension = null;
        foreach (var item in items)
        {
            long number;
            if (item.Number.HasValue) number = item.Number.Value;
            else if (item.Extension)
            {
                number = previousExtension.HasValue ? checked(previousExtension.Value + 1) : 0;
                while (used.Contains(number)) number = checked(number + 1);
            }
            else
            {
                while (reserved.Contains(nextRoot)) nextRoot = checked(nextRoot + 1);
                number = nextRoot; nextRoot = checked(nextRoot + 1);
            }
            if (!used.Add(number)) throw new CompileException("Duplicate ENUMERATED number.", item.Name.Line, item.Name.Column);
            if (item.Extension && previousExtension.HasValue && number <= previousExtension.Value)
                throw new CompileException("ENUMERATED extension numbers must increase.", item.Name.Line, item.Name.Column);
            if (item.Extension) previousExtension = number;
            values.Add(new NamedNumberAst { Name = item.Name.Text, Value = number, Line = item.Name.Line, Column = item.Name.Column });
        }
        return new EnumeratedTypeAst(values) { Extensible = extensible, Line = token.Line, Column = token.Column };
    }

    private void ParseExtensionGroup(List<FieldAst> fields, bool allowOptional)
    {
        Expect(TokenKind.LBracket, "["); Expect(TokenKind.LBracket, "[");
        var previous = fields.Where(f => f.ExtensionGroup.HasValue).Select(f => f.ExtensionGroup!.Value).DefaultIfEmpty(-1).Max();
        int group;
        if (Check(TokenKind.Number))
        {
            var number = Advance();
            if (!int.TryParse(number.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out group) || group < 0 || group <= previous)
                throw new CompileException("Extension group versions must be nonnegative and increase.", number.Line, number.Column);
            Expect(TokenKind.Colon, ":");
        }
        else
        {
            if (previous == int.MaxValue) throw Error("Extension group version exceeds the compiler profile.");
            group = Math.Max(1, previous + 1);
        }
        if (Check(TokenKind.RBracket)) throw Error("Empty extension group is outside the Asn1Kit compiler profile.");
        while (!Check(TokenKind.RBracket))
        {
            var field = ParseComponentType(allowOptional);
            field.ExtensionAddition = true; field.ExtensionGroup = group;
            fields.Add(field);
            if (!Check(TokenKind.RBracket)) Expect(TokenKind.Comma, ",");
        }
        Advance(); Expect(TokenKind.RBracket, "]");
    }

    internal IReadOnlyList<Token> CaptureGroup(TokenKind open, TokenKind close, bool includeDelimiters = false)
    {
        var start = _index;
        var token = Expect(open, open.ToString());
        var depth = 1;
        while (depth > 0)
        {
            if (Check(TokenKind.EndOfFile)) throw new CompileException("Unterminated group.", token.Line, token.Column);
            var item = Advance();
            if (item.Kind == open) depth++;
            if (item.Kind == close) depth--;
        }
        return _tokens.Skip(start + (includeDelimiters ? 0 : 1))
            .Take(_index - start - (includeDelimiters ? 0 : 2)).ToArray();
    }

    internal static List<List<Token>> SplitTopLevel(IReadOnlyList<Token> tokens, TokenKind separator)
    {
        var result = new List<List<Token>>();
        var current = new List<Token>();
        var depth = 0;
        foreach (var token in tokens)
        {
            if (depth == 0 && token.Kind == separator) { result.Add(current); current = new List<Token>(); continue; }
            current.Add(token);
            if (token.Kind is TokenKind.LBrace or TokenKind.LParen or TokenKind.LBracket) depth++;
            if (token.Kind is TokenKind.RBrace or TokenKind.RParen or TokenKind.RBracket) depth--;
        }
        if (current.Count > 0) result.Add(current);
        return result;
    }

    private ValueAst ParseConcreteValue(TypeAst? type)
    {
        if (type is TaggedTypeAst tagged) return ParseConcreteValue(tagged.Inner);
        if (Check(TokenKind.Identifier) && char.IsUpper(Peek().Text[0]))
        {
            var start = _index;
            var concrete = ParseType();
            if (Check(TokenKind.Colon))
            {
                Advance();
                return new TypedValueAst { Type = concrete, Value = ParseConcreteValue(concrete), Line = concrete.Line, Column = concrete.Column };
            }
            _index = start;
        }
        if (!Check(TokenKind.LBrace))
        {
            if (Check(TokenKind.Identifier) && _tokens[_index + 1].Kind == TokenKind.Colon)
            {
                var name = Advance(); Advance();
                var alternative = (type as ChoiceTypeAst)?.Fields.SingleOrDefault(f => f.Name == name.Text);
                return new ChoiceValueAst { Alternative = name.Text, Value = ParseConcreteValue(alternative?.Type), Line = name.Line, Column = name.Column };
            }
            return ParseValue();
        }
        if (type is BuiltinTypeAst { Name: "OBJECT IDENTIFIER" }) return ParseOidValueAst();
        List<FieldAst>? fields = type switch { SequenceTypeAst seq => seq.Fields, SetTypeAst set => set.Fields, _ => null };
        if (fields is not null)
        {
            var token = Advance();
            var value = new StructuredValueAst { Line = token.Line, Column = token.Column };
            while (!Check(TokenKind.RBrace))
            {
                var name = ExpectIdentifier("value component");
                var field = fields.SingleOrDefault(f => f.Name == name.Text) ?? throw Error($"Unknown component '{name.Text}'.");
                if (!value.Fields.TryAdd(name.Text, ParseConcreteValue(field.Type))) throw Error($"Duplicate component '{name.Text}'.");
                if (!Check(TokenKind.RBrace)) Expect(TokenKind.Comma, ",");
            }
            Advance(); return value;
        }
        var element = type switch { SequenceOfTypeAst seq => seq.Element, SetOfTypeAst set => set.Element, _ => null };
        if (element is not null)
        {
            var token = Advance();
            var value = new CollectionValueAst { Line = token.Line, Column = token.Column };
            while (!Check(TokenKind.RBrace))
            {
                value.Items.Add(ParseConcreteValue(element));
                if (!Check(TokenKind.RBrace)) Expect(TokenKind.Comma, ",");
            }
            Advance(); return value;
        }
        return ParseValue();
    }
}
