using System.Security.Cryptography;
using Asn1Kit.Runtime;

namespace Asn1Kit.Cms.Demo;

internal static class EducationalCmsSigning
{
    public static readonly Asn1Oid Sha256 = Asn1Oid.Parse("2.16.840.1.101.3.4.2.1");
    public static readonly Asn1Oid RsaEncryption = Asn1Oid.Parse("1.2.840.113549.1.1.1");
    public static readonly byte[] PlaceholderSignature = CreatePlaceholder();

    public static byte[] DigestSha256(ReadOnlySpan<byte> content) => SHA256.HashData(content);

    private static byte[] CreatePlaceholder()
    {
        var value = new byte[32];
        Array.Fill(value, (byte)0x01);
        return value;
    }
}
