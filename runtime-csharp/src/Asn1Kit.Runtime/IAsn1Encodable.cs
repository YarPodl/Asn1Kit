namespace Asn1Kit.Runtime;

/// <summary>
/// Warm marker for generated named types that expose instance <see cref="Encode(Asn1Writer)"/>.
/// Used by <see cref="Asn1Utils"/> encode helpers and type-driven OF writer overloads; codegen hot path
/// continues to call concrete <c>Encode</c> methods directly.
/// </summary>
public interface IAsn1Encodable
{
    /// <summary>Encodes this value with its default tag into <paramref name="writer"/>.</summary>
    void Encode(Asn1Writer writer);
}
