namespace Asn1Kit.Runtime;

/// <summary>An unrecognized extension TLV and its insertion point among known components.</summary>
public readonly struct Asn1Extension
{
    /// <summary>Creates an extension; position is supplied by the codec, independent of ASN.1 metadata.</summary>
    public Asn1Extension(int position, Asn1Any value)
    {
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        Position = position; Value = value;
    }
    /// <summary>Index before the next known component; preserves SEQUENCE extension ordering.</summary>
    public int Position { get; }
    /// <summary>Complete unrecognized TLV.</summary>
    public Asn1Any Value { get; }
}
