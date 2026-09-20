using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

/// <summary>
/// Layer 3: vendored external DER fragments with provenance (RFC / X.690 / BCL-regenerated).
/// </summary>
public sealed class ExternalVectorTests
{
    public static IEnumerable<object[]> ExternalCases() =>
        BerDerFixtures.AsTheoryData("runtime-csharp/fixtures/ber-der/external/rfc-and-dotnet.json");

    [Theory]
    [MemberData(nameof(ExternalCases))]
    public void External_DecodeAndOptionalEncode(BerDerCase c)
    {
        Assert.False(string.IsNullOrWhiteSpace(c.Source), $"External case '{c.Name}' must declare source.");

        var bytes = c.GetBytes();
        switch (c.Op)
        {
            case "boolean":
                RunPrimitive(c, bytes,
                    w => w.WriteBoolean(Asn1Tag.Boolean, BerDerFixtures.GetBoolean(c)),
                    r => Assert.Equal(BerDerFixtures.GetBoolean(c), r.ReadBoolean(Asn1Tag.Boolean)));
                break;
            case "null":
                RunPrimitive(c, bytes,
                    w => w.WriteNull(Asn1Tag.Null),
                    r => Assert.True(r.ReadNull(Asn1Tag.Null)));
                break;
            case "integer":
                RunPrimitive(c, bytes,
                    w => w.WriteInteger(Asn1Tag.Integer, BerDerFixtures.GetInteger(c)),
                    r => Assert.Equal(BerDerFixtures.GetInteger(c), r.ReadInteger(Asn1Tag.Integer)));
                break;
            case "octetString":
                RunPrimitive(c, bytes,
                    w => w.WriteOctetString(Asn1Tag.OctetString, BerDerFixtures.GetOctetValue(c)),
                    r => Assert.Equal(BerDerFixtures.GetOctetValue(c), r.ReadOctetString(Asn1Tag.OctetString)));
                break;
            case "oid":
                RunPrimitive(c, bytes,
                    w => w.WriteObjectIdentifier(Asn1Tag.ObjectIdentifier, BerDerFixtures.GetString(c)!),
                    r => Assert.Equal(BerDerFixtures.GetString(c), r.ReadObjectIdentifier(Asn1Tag.ObjectIdentifier)));
                break;
            case "bitString":
                RunPrimitive(c, bytes,
                    w => w.WriteBitString(
                        Asn1Tag.BitString,
                        new Asn1BitString(BerDerFixtures.GetOctetValue(c), c.UnusedBits ?? 0)),
                    r =>
                    {
                        var decoded = r.ReadBitString(Asn1Tag.BitString);
                        Assert.Equal(c.UnusedBits ?? 0, decoded.UnusedBits);
                        Assert.Equal(BerDerFixtures.GetOctetValue(c), decoded.Span.ToArray());
                    });
                break;
            case "time":
            {
                var form = Enum.Parse<Asn1TimeForm>(c.Form!, ignoreCase: true);
                var digits = c.FractionDigits ?? 3;
                RunPrimitive(c, bytes,
                    w => w.WriteTime(Asn1Time.DefaultTag(form), BerDerFixtures.GetTime(c), form, digits),
                    r => Assert.Equal(BerDerFixtures.GetTime(c), r.ReadTime(Asn1Time.DefaultTag(form), form)));
                break;
            }
            case "rawDecode":
                DecodeAlgorithmIdentifier(c, bytes);
                break;
            default:
                throw new InvalidOperationException($"Unsupported external op '{c.Op}' in '{c.Name}'.");
        }
    }

    private static void RunPrimitive(
        BerDerCase c,
        byte[] bytes,
        Action<Asn1Writer> encode,
        Action<Asn1Reader> decode)
    {
        if (c.ShouldEncode)
        {
            var writer = new Asn1Writer(Asn1Encoding.Der);
            encode(writer);
            Assert.Equal(Hex.Format(bytes), Hex.Format(writer.Encode()));
        }

        var reader = new Asn1Reader(bytes, c.Encoding);
        decode(reader);
        Assert.True(reader.Eof);
    }

    private static void DecodeAlgorithmIdentifier(BerDerCase c, byte[] bytes)
    {
        var expectedOid = c.Value.GetProperty("oid").GetString()
            ?? throw new InvalidOperationException($"Case '{c.Name}' missing oid.");
        var paramsTag = c.Value.GetProperty("paramsTag").GetString();

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        reader.ReadSequence(Asn1Tag.Sequence, inner =>
        {
            Assert.Equal(expectedOid, inner.ReadObjectIdentifier(Asn1Tag.ObjectIdentifier));
            if (paramsTag == "null")
            {
                Assert.True(inner.ReadNull(Asn1Tag.Null));
            }

            Assert.True(inner.Eof);
        });
        Assert.True(reader.Eof);
    }
}
