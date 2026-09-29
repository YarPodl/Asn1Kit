namespace Asn1Kit.Runtime;

/// <summary>Specifies the ASN.1 encoding rules used for reading or writing.</summary>
public enum Asn1Encoding
{
    /// <summary>Basic Encoding Rules (BER).</summary>
    Ber,
    /// <summary>Distinguished Encoding Rules (DER).</summary>
    Der
}

/// <summary>Identifies an ASN.1 tag class.</summary>
public enum Asn1TagClass
{
    /// <summary>Gets the <c>Universal</c> value.</summary>
    Universal = 0,
    /// <summary>Gets the <c>Application</c> value.</summary>
    Application = 1,
    /// <summary>Gets the <c>ContextSpecific</c> value.</summary>
    ContextSpecific = 2,
    /// <summary>Gets the <c>Private</c> value.</summary>
    Private = 3
}

/// <summary>Identifies a supported ASN.1 character string type.</summary>
public enum Asn1StringForm
{
    /// <summary>Gets the <c>Utf8</c> value.</summary>
    Utf8,
    /// <summary>Gets the <c>Printable</c> value.</summary>
    Printable,
    /// <summary>Gets the <c>Teletex</c> value.</summary>
    Teletex,
    /// <summary>Gets the <c>T61</c> value.</summary>
    T61,
    /// <summary>Gets the <c>Ia5</c> value.</summary>
    Ia5,
    /// <summary>Gets the <c>Numeric</c> value.</summary>
    Numeric,
    /// <summary>Gets the <c>Visible</c> value.</summary>
    Visible,
    /// <summary>Gets the <c>Bmp</c> value.</summary>
    Bmp,
    /// <summary>Gets the <c>Universal</c> value.</summary>
    Universal,
    /// <summary>Gets the <c>General</c> value.</summary>
    General,
    /// <summary>Gets the <c>Graphic</c> value.</summary>
    Graphic,
    /// <summary>Gets the <c>Videotex</c> value.</summary>
    Videotex
}

/// <summary>Identifies a supported ASN.1 time type.</summary>
public enum Asn1TimeForm
{
    /// <summary>Gets the <c>Utc</c> value.</summary>
    Utc,
    /// <summary>Gets the <c>Generalized</c> value.</summary>
    Generalized
}

/// <summary>Represents an ASN.1 tag, including its class, number, and primitive or constructed form.</summary>
public readonly struct Asn1Tag : IEquatable<Asn1Tag>
{
    /// <summary>Initializes a new instance of <c>Asn1Tag</c>.</summary>
    public Asn1Tag(Asn1TagClass tagClass, int number, bool constructed = false)
    {
        if (number < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        TagClass = tagClass;
        Number = number;
        Constructed = constructed;
    }

    /// <summary>Gets the tag class.</summary>
    public Asn1TagClass TagClass { get; }
    /// <summary>Gets the non-negative tag number.</summary>
    public int Number { get; }
    /// <summary>Gets whether the tag uses constructed encoding.</summary>
    public bool Constructed { get; }

    /// <summary>Returns this tag in constructed form.</summary>
    public Asn1Tag AsConstructed() => new(TagClass, Number, constructed: true);

    /// <summary>Returns this tag in primitive form.</summary>
    public Asn1Tag AsPrimitive() => new(TagClass, Number, constructed: false);

    /// <summary>Gets the <c>Boolean</c> value.</summary>
    public static Asn1Tag Boolean { get; } = new(Asn1TagClass.Universal, 1);
    /// <summary>Gets the <c>Integer</c> value.</summary>
    public static Asn1Tag Integer { get; } = new(Asn1TagClass.Universal, 2);
    /// <summary>Gets the <c>BitString</c> value.</summary>
    public static Asn1Tag BitString { get; } = new(Asn1TagClass.Universal, 3);
    /// <summary>Gets the <c>OctetString</c> value.</summary>
    public static Asn1Tag OctetString { get; } = new(Asn1TagClass.Universal, 4);
    /// <summary>Gets the <c>Null</c> value.</summary>
    public static Asn1Tag Null { get; } = new(Asn1TagClass.Universal, 5);
    /// <summary>Gets the <c>ObjectIdentifier</c> value.</summary>
    public static Asn1Tag ObjectIdentifier { get; } = new(Asn1TagClass.Universal, 6);
    /// <summary>Gets the <c>Enumerated</c> value.</summary>
    public static Asn1Tag Enumerated { get; } = new(Asn1TagClass.Universal, 10);
    /// <summary>Gets the <c>Utf8String</c> value.</summary>
    public static Asn1Tag Utf8String { get; } = new(Asn1TagClass.Universal, 12);
    /// <summary>Gets the <c>Sequence</c> value.</summary>
    public static Asn1Tag Sequence { get; } = new(Asn1TagClass.Universal, 16, constructed: true);
    /// <summary>Gets the <c>Set</c> value.</summary>
    public static Asn1Tag Set { get; } = new(Asn1TagClass.Universal, 17, constructed: true);
    /// <summary>Gets the <c>NumericString</c> value.</summary>
    public static Asn1Tag NumericString { get; } = new(Asn1TagClass.Universal, 18);
    /// <summary>Gets the <c>PrintableString</c> value.</summary>
    public static Asn1Tag PrintableString { get; } = new(Asn1TagClass.Universal, 19);
    /// <summary>Gets the <c>TeletexString</c> value.</summary>
    public static Asn1Tag TeletexString { get; } = new(Asn1TagClass.Universal, 20);
    /// <summary>Gets the <c>VideotexString</c> value.</summary>
    public static Asn1Tag VideotexString { get; } = new(Asn1TagClass.Universal, 21);
    /// <summary>Gets the <c>Ia5String</c> value.</summary>
    public static Asn1Tag Ia5String { get; } = new(Asn1TagClass.Universal, 22);
    /// <summary>Gets the <c>UtcTime</c> value.</summary>
    public static Asn1Tag UtcTime { get; } = new(Asn1TagClass.Universal, 23);
    /// <summary>Gets the <c>GeneralizedTime</c> value.</summary>
    public static Asn1Tag GeneralizedTime { get; } = new(Asn1TagClass.Universal, 24);
    /// <summary>Gets the <c>GraphicString</c> value.</summary>
    public static Asn1Tag GraphicString { get; } = new(Asn1TagClass.Universal, 25);
    /// <summary>Gets the <c>VisibleString</c> value.</summary>
    public static Asn1Tag VisibleString { get; } = new(Asn1TagClass.Universal, 26);
    /// <summary>Gets the <c>GeneralString</c> value.</summary>
    public static Asn1Tag GeneralString { get; } = new(Asn1TagClass.Universal, 27);
    /// <summary>Gets the <c>UniversalString</c> value.</summary>
    public static Asn1Tag UniversalString { get; } = new(Asn1TagClass.Universal, 28);
    /// <summary>Gets the <c>BmpString</c> value.</summary>
    public static Asn1Tag BmpString { get; } = new(Asn1TagClass.Universal, 30);

    /// <summary>Provides the <c>Equals</c> operation.</summary>
    public bool Equals(Asn1Tag other) =>
        TagClass == other.TagClass && Number == other.Number && Constructed == other.Constructed;

    /// <summary>Determines whether class and number match, ignoring the constructed bit.</summary>
    public bool MatchesIgnoreConstructed(Asn1Tag other) =>
        TagClass == other.TagClass && Number == other.Number;

    /// <summary>Provides the <c>Equals</c> operation.</summary>
    public override bool Equals(object? obj) => obj is Asn1Tag tag && Equals(tag);

    /// <summary>Provides the <c>GetHashCode</c> operation.</summary>
    public override int GetHashCode() => HashCode.Combine(TagClass, Number, Constructed);

    /// <summary>Provides the <c>ToString</c> operation.</summary>
    public override string ToString() => $"{TagClass}-{Number}{(Constructed ? "C" : "P")}";
}

/// <summary>Represents an ASN.1 encoding or decoding error.</summary>
public sealed class Asn1Exception : Exception
{
    /// <summary>Initializes a new instance of <c>Asn1Exception</c>.</summary>
    public Asn1Exception(string message) : base(message)
    {
    }
}
