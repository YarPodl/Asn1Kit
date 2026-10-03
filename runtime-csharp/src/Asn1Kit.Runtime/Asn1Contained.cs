namespace Asn1Kit.Runtime;

/// <summary>Octets of a CONTAINING value and, when its structure is known, its decoded value.</summary>
public readonly struct Asn1Contained<T>
{
    private readonly T _value;
    private Asn1Contained(ReadOnlyMemory<byte> contents, int unusedBits, bool hasValue, T value)
    {
        if ((uint)unusedBits > 7 || (contents.IsEmpty && unusedBits != 0))
            throw new ArgumentOutOfRangeException(nameof(unusedBits));
        Contents = contents; UnusedBits = unusedBits; HasValue = hasValue; _value = value;
    }

    /// <summary>Original content octets, excluding the BIT STRING unused-bits octet.</summary>
    public ReadOnlyMemory<byte> Contents { get; }
    /// <summary>Unused bits for an opaque BIT STRING; typed contents are octet-aligned.</summary>
    public int UnusedBits { get; }
    /// <summary>Whether the typed content was decoded or supplied.</summary>
    public bool HasValue { get; }
    /// <summary>Gets the typed content; throws for opaque content.</summary>
    public T Value => HasValue ? _value : throw new InvalidOperationException("Contained value is opaque.");
    /// <summary>Creates typed content. Its encoding is produced by the writer.</summary>
    public static Asn1Contained<T> FromValue(T value) => new(default, 0, true, value);
    /// <summary>Wraps opaque octets without copying.</summary>
    public static Asn1Contained<T> FromEncoded(ReadOnlyMemory<byte> contents, int unusedBits = 0) => new(contents, unusedBits, false, default!);
    internal static Asn1Contained<T> Decoded(ReadOnlyMemory<byte> contents, T value) => new(contents, 0, true, value);
}
