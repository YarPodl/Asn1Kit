using Asn1Kit.Runtime;

namespace Asn1Kit.Cms.Demo;

internal static class CmsDemoOidNames
{
    public static string Format(Asn1Oid oid)
    {
        var dotted = oid.ToString();
        return ShortName(dotted) is { } name ? $"{name} ({dotted})" : dotted;
    }

    public static string? ShortName(string dotted) => dotted switch
    {
        "1.2.840.113549.1.7.1" => "pkcs7-data",
        "1.2.840.113549.1.7.2" => "pkcs7-signedData",
        "1.2.840.113549.1.9.3" => "contentType",
        "1.2.840.113549.1.9.4" => "messageDigest",
        "1.2.840.113549.1.9.5" => "signingTime",
        "1.2.840.113549.1.1.1" => "rsaEncryption",
        "1.2.840.113549.1.1.11" => "sha256WithRSAEncryption",
        "2.16.840.1.101.3.4.2.1" => "sha256",
        "2.5.29.14" => "X509v3 Subject Key Identifier",
        "2.5.29.15" => "X509v3 Key Usage",
        "2.5.29.17" => "X509v3 Subject Alternative Name",
        "2.5.29.19" => "X509v3 Basic Constraints",
        "2.5.29.35" => "X509v3 Authority Key Identifier",
        _ => null
    };
}
