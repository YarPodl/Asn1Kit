namespace Asn1Kit.Runtime;

/// <summary>ASN.1 NULL value (universal tag 5, empty contents).</summary>
public readonly struct Asn1Null : IEquatable<Asn1Null>
{
    public static Asn1Null Value => default;

    public static void Encode(Asn1Writer writer, Asn1Tag? tag = null) =>
        writer.WriteNull(tag ?? Asn1Tag.Null);

    public static void Encode(Asn1Writer writer, Asn1Null _, Asn1Tag? tag = null) =>
        Encode(writer, tag);

    public static Asn1Null Decode(Asn1Reader reader, Asn1Tag? tag = null)
    {
        reader.ReadNull(tag ?? Asn1Tag.Null);
        return default;
    }

    public bool Equals(Asn1Null other) => true;

    public override bool Equals(object? obj) => obj is Asn1Null;

    public override int GetHashCode() => 0;

    public static bool operator ==(Asn1Null left, Asn1Null right) => true;

    public static bool operator !=(Asn1Null left, Asn1Null right) => false;
}
