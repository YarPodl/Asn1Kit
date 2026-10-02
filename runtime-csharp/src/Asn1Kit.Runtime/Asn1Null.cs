namespace Asn1Kit.Runtime;

/// <summary>ASN.1 NULL value (universal tag 5, empty contents).</summary>
public readonly struct Asn1Null : IEquatable<Asn1Null>
{
    /// <summary>Returns the ASN.1 NULL literal.</summary>
    public override string ToString() => "NULL";

    /// <summary>Gets the <c>Value</c> value.</summary>
    public static Asn1Null Value => default;

    /// <summary>Encodes the ASN.1 value.</summary>
    public static void Encode(Asn1Writer writer, Asn1Tag? tag = null) =>
        writer.WriteNull(tag ?? Asn1Tag.Null);

    /// <summary>Encodes the ASN.1 value.</summary>
    public static void Encode(Asn1Writer writer, Asn1Null _, Asn1Tag? tag = null) =>
        Encode(writer, tag);

    /// <summary>Decodes the ASN.1 value.</summary>
    public static Asn1Null Decode(Asn1Reader reader, Asn1Tag? tag = null)
    {
        reader.ReadNull(tag ?? Asn1Tag.Null);
        return default;
    }

    /// <summary>Provides the <c>Equals</c> operation.</summary>
    public bool Equals(Asn1Null other) => true;

    /// <summary>Provides the <c>Equals</c> operation.</summary>
    public override bool Equals(object? obj) => obj is Asn1Null;

    /// <summary>Provides the <c>GetHashCode</c> operation.</summary>
    public override int GetHashCode() => 0;

    /// <summary>Provides the <c>operator ==</c> operation.</summary>
    public static bool operator ==(Asn1Null left, Asn1Null right) => true;

    /// <summary>Provides the <c>operator !=</c> operation.</summary>
    public static bool operator !=(Asn1Null left, Asn1Null right) => false;
}
