namespace Asn1Kit.Runtime;

/// <summary>
/// Decoded ASN.1 value together with the complete TLV consumed from the input, when available.
/// </summary>
public readonly struct Asn1Value<T>
    where T : notnull
{
    /// <summary>Formats the decoded value, or reports an unset default reference value.</summary>
    public override string ToString() => Value is null ? "<unset>" : Asn1Formatting.Format(Value);

    /// <summary>Creates an application-provided value with no original encoding.</summary>
    public Asn1Value(T value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        OriginalEncoding = ReadOnlyMemory<byte>.Empty;
    }

    /// <summary>Creates a decoded value and retains a view of its original complete TLV.</summary>
    internal Asn1Value(T value, ReadOnlyMemory<byte> originalEncoding)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        OriginalEncoding = originalEncoding;
    }

    /// <summary>Decoded or application-provided value.</summary>
    public T Value { get; }

    /// <summary>
    /// Original complete TLV, or empty memory for an application-provided value.
    /// The memory may alias an <see cref="Asn1Reader"/> input buffer.
    /// </summary>
    public ReadOnlyMemory<byte> OriginalEncoding { get; }

    /// <summary>Wraps an application-provided value with no original encoding.</summary>
    public static implicit operator Asn1Value<T>(T value) => new(value);
}
