namespace Asn1Kit.Runtime;

/// <summary>
/// Eager-decoded value that retains the original TLV for bit-exact re-encode until <see cref="Value"/> is replaced.
/// </summary>
public sealed class Asn1Retained<T>
{
    private ReadOnlyMemory<byte> _encoded;
    private T _value;
    private bool _hasEncoded;

    private Asn1Retained(ReadOnlyMemory<byte> encoded, T value, bool hasEncoded)
    {
        _encoded = encoded;
        _value = value;
        _hasEncoded = hasEncoded;
    }

    /// <summary>Builds from a complete TLV and an already-decoded value (views may alias the reader buffer).</summary>
    public static Asn1Retained<T> FromEncoded(ReadOnlyMemory<byte> encoded, T value)
    {
        if (encoded.Length == 0)
        {
            throw new Asn1Exception("Retained encoded TLV must not be empty.");
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new Asn1Retained<T>(encoded, value, hasEncoded: true);
    }

    /// <summary>Builds from a materialized object with no stored TLV (encode must walk <see cref="Value"/>).</summary>
    public static Asn1Retained<T> FromValue(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new Asn1Retained<T>(ReadOnlyMemory<byte>.Empty, value, hasEncoded: false);
    }

    /// <summary>Trusted wrap used by <see cref="Asn1Reader.ReadRetained{T}"/>.</summary>
    internal static Asn1Retained<T> Wrap(ReadOnlyMemory<byte> encoded, T value) =>
        FromEncoded(encoded, value);

    /// <summary>Whether a complete TLV is available for bit-exact re-encode.</summary>
    public bool HasEncoded => _hasEncoded;

    /// <summary>Complete TLV as memory (may alias an <see cref="Asn1Reader"/> buffer).</summary>
    public ReadOnlyMemory<byte> EncodedMemory =>
        _hasEncoded
            ? _encoded
            : throw new InvalidOperationException("Retained value has no encoded TLV.");

    /// <summary>
    /// Decoded value. Replacing the value invalidates any retained TLV so encode walks the object graph.
    /// </summary>
    public T Value
    {
        get => _value;
        set
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            _value = value;
            _hasEncoded = false;
            _encoded = ReadOnlyMemory<byte>.Empty;
        }
    }

    /// <summary>Detaches the complete TLV into a new array.</summary>
    public byte[] ToArray() => EncodedMemory.ToArray();

    /// <summary>Returns a copy that does not alias an external buffer.</summary>
    public Asn1Retained<T> Clone()
    {
        if (_hasEncoded)
        {
            return new Asn1Retained<T>(_encoded.ToArray(), _value, hasEncoded: true);
        }

        return FromValue(_value);
    }

    /// <summary>Writes the stored TLV when present; otherwise throws.</summary>
    public void WriteTo(Asn1Writer writer)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (!_hasEncoded)
        {
            throw new InvalidOperationException(
                "Retained value has no encoded TLV; encode Value explicitly.");
        }

        writer.WriteRaw(_encoded.Span);
    }
}
