namespace Asn1Kit.Runtime;

/// <summary>
/// Decode-time strictness knobs. Encode stays canonical regardless of these flags.
/// Soft-accept inventory: <c>docs/status.md</c> § Runtime; policy: <c>docs/decisions.md</c>.
/// </summary>
public sealed class Asn1ReaderOptions
{
    /// <summary>Default maximum depth for recursive indefinite scans and constructed materialization.</summary>
    public const int DefaultMaxRecursionDepth = 64;

    /// <summary>Default maximum payload allocated for one materialized constructed value.</summary>
    public const int DefaultMaxMaterializedConstructedValueBytes = 100 * 1024 * 1024;

    private int _maxRecursionDepth = DefaultMaxRecursionDepth;
    private int _maxMaterializedConstructedValueBytes = DefaultMaxMaterializedConstructedValueBytes;

    /// <summary>
    /// Soft profile: non-minimal INTEGER and nonzero BIT STRING trailing bits are accepted;
    /// non-minimal length and overlong OID base-128 are rejected.
    /// </summary>
    public static Asn1ReaderOptions Default { get; } = new();

    /// <summary>All optional rejects enabled (audit / DER-strict consumers).</summary>
    public static Asn1ReaderOptions Strict { get; } = new()
    {
        RejectNonMinimalInteger = true,
        RejectBitStringTrailingBits = true,
        RejectNonMinimalLength = true,
        RejectOverlongOidBase128 = true
    };

    /// <summary>
    /// Softened length checks only (still soft on INTEGER / BIT STRING; OID overlong still rejected).
    /// </summary>
    public static Asn1ReaderOptions AllowNonMinimalLength { get; } = new()
    {
        RejectNonMinimalLength = false
    };

    /// <summary>
    /// Softened OID base-128 checks only (still soft on INTEGER / BIT STRING; non-minimal length still rejected).
    /// </summary>
    public static Asn1ReaderOptions AllowOverlongOidBase128 { get; } = new()
    {
        RejectOverlongOidBase128 = false
    };

    /// <summary>
    /// When true, reject INTEGER/ENUMERATED contents that are not minimally encoded (X.690 §8.3.2).
    /// Default false (soft-accept).
    /// </summary>
    public bool RejectNonMinimalInteger { get; init; }

    /// <summary>
    /// When true, reject BIT STRING with nonzero unused trailing bits (X.690 §11.2), including under DER.
    /// Default false (soft-accept).
    /// </summary>
    public bool RejectBitStringTrailingBits { get; init; }

    /// <summary>
    /// When true, reject non-minimal definite length encodings (unnecessary long form / leading zero length octets).
    /// Default true — not part of the soft profile.
    /// </summary>
    public bool RejectNonMinimalLength { get; init; } = true;

    /// <summary>
    /// When true, reject overlong OID base-128 subidentifiers (leading <c>0x80</c>).
    /// Default true — not part of the soft profile.
    /// </summary>
    public bool RejectOverlongOidBase128 { get; init; } = true;

    /// <summary>
    /// Maximum recursive constructed-value depth. This bounds BER indefinite-length EOC scans and
    /// nested constructed string materialization. Default 64.
    /// </summary>
    public int MaxRecursionDepth
    {
        get => _maxRecursionDepth;
        init => _maxRecursionDepth = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Recursion depth must be positive.");
    }

    /// <summary>
    /// Maximum payload size, in bytes, allocated for one materialized constructed string or BIT STRING.
    /// Default 100 MiB. Zero permits only empty values.
    /// </summary>
    public int MaxMaterializedConstructedValueBytes
    {
        get => _maxMaterializedConstructedValueBytes;
        init => _maxMaterializedConstructedValueBytes = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Materialized size limit cannot be negative.");
    }
}
