namespace Asn1Kit.Runtime;

public enum Asn1Encoding
{
    Ber,
    Der
}

public enum Asn1TagClass
{
    Universal = 0,
    Application = 1,
    ContextSpecific = 2,
    Private = 3
}

public readonly struct Asn1Tag : IEquatable<Asn1Tag>
{
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

    public Asn1TagClass TagClass { get; }
    public int Number { get; }
    public bool Constructed { get; }

    public Asn1Tag AsConstructed() => new(TagClass, Number, constructed: true);

    public Asn1Tag AsPrimitive() => new(TagClass, Number, constructed: false);

    public static Asn1Tag Boolean { get; } = new(Asn1TagClass.Universal, 1);
    public static Asn1Tag Integer { get; } = new(Asn1TagClass.Universal, 2);
    public static Asn1Tag OctetString { get; } = new(Asn1TagClass.Universal, 4);
    public static Asn1Tag Null { get; } = new(Asn1TagClass.Universal, 5);
    public static Asn1Tag ObjectIdentifier { get; } = new(Asn1TagClass.Universal, 6);
    public static Asn1Tag Sequence { get; } = new(Asn1TagClass.Universal, 16, constructed: true);

    public bool Equals(Asn1Tag other) =>
        TagClass == other.TagClass && Number == other.Number && Constructed == other.Constructed;

    public bool MatchesIgnoreConstructed(Asn1Tag other) =>
        TagClass == other.TagClass && Number == other.Number;

    public override bool Equals(object? obj) => obj is Asn1Tag tag && Equals(tag);

    public override int GetHashCode() => HashCode.Combine(TagClass, Number, Constructed);

    public override string ToString() => $"{TagClass}-{Number}{(Constructed ? "C" : "P")}";
}

public sealed class Asn1Exception : Exception
{
    public Asn1Exception(string message) : base(message)
    {
    }
}
