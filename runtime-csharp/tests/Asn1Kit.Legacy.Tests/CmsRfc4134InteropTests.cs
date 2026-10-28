using System.Formats.Asn1;
using System.Security.Cryptography.Pkcs;
using System.Text;
using Asn1Kit.Legacy.Cms;
using Asn1Kit.Modern.CryptographicMessageSyntax2009;
using Asn1Kit.Runtime;
using LegacyCms = Asn1Kit.Legacy.Cms;
using ModernCms = Asn1Kit.Modern.CryptographicMessageSyntax2009;

namespace Asn1Kit.Tests;

public sealed class CmsRfc4134InteropTests
{
    [Fact]
    public void DataContentInfo_3_2_MatchesExpectedAndRoundTrips()
    {
        const string fileName = "3.2.bin";
        var der = CmsRfc4134Fixtures.ReadDer(fileName);
        var expected = CmsRfc4134Fixtures.RequireMessage(fileName);
        Assert.Equal("data", expected.Kind);
        Assert.Equal("1.2.840.113549.1.7.1", expected.ContentType);

        var modern = ModernCms.ContentInfo.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        Assert.Equal(expected.ContentType, modern.ContentType.ToString());
        var modernOctets = ReadOctetStringContent(modern.Content);
        Assert.Equal(expected.DataOctetLength, modernOctets.Length);
        Assert.Equal(expected.DataOctetUtf8, Encoding.ASCII.GetString(modernOctets));
        Assert.Equal(expected.DataOctetSha1Hex, CmsRfc4134Fixtures.Sha1Hex(modernOctets));
        AssertRoundTrip(der, writer => modern.Encode(writer));

        var legacy = LegacyCms.ContentInfo.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        Assert.Equal(expected.ContentType, legacy.ContentType.ToString());
        Assert.True(legacy.TryDecodeContent(
            ContentInfoContentBindings.Oid12840113549171, out ReadOnlyMemory<byte> legacyOctets));
        Assert.Equal(modernOctets, legacyOctets.ToArray());
        AssertRoundTrip(der, writer => legacy.Encode(writer));
    }

    [Theory]
    [InlineData("4.2.bin")]
    [InlineData("4.3.bin")]
    [InlineData("4.6.bin")]
    [InlineData("4.7.bin")]
    [InlineData("4.10.bin")]
    public void SignedData_MatchesExternalExpectedAndRoundTrips(string fileName)
    {
        var der = CmsRfc4134Fixtures.ReadDer(fileName);
        var expected = CmsRfc4134Fixtures.RequireMessage(fileName);
        Assert.Equal("signedData", expected.Kind);
        Assert.NotNull(expected.SignedData);
        var expectedSd = expected.SignedData!;

        byte[]? detached = fileName == "4.3.bin"
            ? CmsRfc4134Fixtures.ReadDer("detached-content.bin")
            : null;
        if (detached is not null)
        {
            Assert.Equal(
                CmsRfc4134Fixtures.LoadExpected().DetachedContentSha1Hex,
                CmsRfc4134Fixtures.Sha1Hex(detached));
        }

        AssertModernSignedData(der, expected, expectedSd, detached);
        AssertLegacySignedData(der, expected, expectedSd);
        AssertSignedCmsOracle(der, expected, expectedSd, detached);
    }

    [Fact]
    public void Detached_4_3_HasNoEContent()
    {
        var der = CmsRfc4134Fixtures.ReadDer("4.3.bin");
        var modern = DecodeModernSignedData(der);
        Assert.Null(modern.EncapContentInfo.EContent);
        Assert.Equal("1.2.840.113549.1.7.1", modern.EncapContentInfo.EContentType.ToString());
    }

    [Fact]
    public void MultipleSigners_4_6_HasDistinctSids()
    {
        var expected = CmsRfc4134Fixtures.RequireMessage("4.6.bin").SignedData!;
        Assert.True(expected.SignerCount >= 2);
        var serials = expected.SignerInfos.Select(s => s.SerialNumberHex).ToArray();
        Assert.Equal(2, serials.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var modern = DecodeModernSignedData(CmsRfc4134Fixtures.ReadDer("4.6.bin"));
        Assert.Equal(2, modern.SignerInfos.Length);
        var modernSerials = modern.SignerInfos
            .Select(s => CmsRfc4134Fixtures.IntegerHex(s.Sid.IssuerAndSerialNumber!.SerialNumber))
            .ToArray();
        Assert.Equal(
            serials.Select(s => CmsRfc4134Fixtures.NormalizeIntegerHex(s!)).OrderBy(s => s).ToArray(),
            modernSerials.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void SubjectKeyIdentifier_4_7_MatchesExpected()
    {
        var expected = CmsRfc4134Fixtures.RequireMessage("4.7.bin").SignedData!.SignerInfos[0];
        Assert.Equal("subjectKeyIdentifier", expected.SidKind);
        Assert.Equal(3, CmsRfc4134Fixtures.RequireMessage("4.7.bin").SignedData!.Version);

        var modern = DecodeModernSignedData(CmsRfc4134Fixtures.ReadDer("4.7.bin"));
        Assert.Equal(ModernCms.SignerIdentifierKind.SubjectKeyIdentifier, modern.SignerInfos[0].Sid.Kind);
        Assert.Equal(
            expected.SubjectKeyIdentifierHex,
            CmsRfc4134Fixtures.ToHex(modern.SignerInfos[0].Sid.SubjectKeyIdentifier!.Value.Span));
    }

    [Fact]
    public void SignedAttrs_4_10_RetainWireTlvAndUniversalSetContents()
    {
        var expectedSigner = CmsRfc4134Fixtures.RequireMessage("4.10.bin").SignedData!.SignerInfos[0];
        Assert.True(expectedSigner.SignedAttrsPresent);
        Assert.NotNull(expectedSigner.SignedAttrsTlvHex);
        Assert.NotNull(expectedSigner.SignedAttrs);

        var modern = DecodeModernSignedData(CmsRfc4134Fixtures.ReadDer("4.10.bin"));
        var retained = modern.SignerInfos[0].SignedAttrs!.Value.OriginalEncoding;
        Assert.False(retained.IsEmpty);
        Assert.Equal(expectedSigner.SignedAttrsTlvHex, CmsRfc4134Fixtures.ToHex(retained.Span));
        Assert.Equal(0xA0, retained.Span[0]);

        var universal = retained.ToArray();
        universal[0] = 0x31;
        Assert.Equal(0x31, universal[0]);
        Assert.Equal(
            new Asn1Any(retained).ContentsMemory.ToArray(),
            new Asn1Any(universal).ContentsMemory.ToArray());

        var attrs = modern.SignerInfos[0].SignedAttrs!.Value.Value;
        Assert.Equal(
            expectedSigner.SignedAttrs!.AttributeOids,
            attrs.Select(a => a.AttrType.ToString()).ToList());

        Assert.True(attrs[0].TryDecodeAttrValues(
            SignedAttributesSetBindings.ContentType, out Asn1Oid[] contentTypes));
        Assert.Equal(expectedSigner.SignedAttrs.ContentType, Assert.Single(contentTypes).ToString());

        Assert.True(attrs[1].TryDecodeAttrValues(
            SignedAttributesSetBindings.MessageDigest, out ReadOnlyMemory<byte>[] digests));
        Assert.Equal(
            expectedSigner.SignedAttrs.MessageDigestHex,
            CmsRfc4134Fixtures.ToHex(Assert.Single(digests).Span));
    }

    [Fact]
    public void SignedAttrs_4_10_UnsortedPermutationEncodesCanonicalDer()
    {
        var der = CmsRfc4134Fixtures.ReadDer("4.10.bin");
        var info = ModernCms.ContentInfo.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        Assert.True(info.TryDecodeContent(
            ModernCms.ContentSetContentBindings.CtSignedData, out ModernCms.SignedData signedData));

        var signer = Assert.Single(signedData.SignerInfos);
        var canonicalAttrs = signer.SignedAttrs!.Value.Value;
        var unsortedAttrs = canonicalAttrs.Reverse().ToArray();
        Assert.False(canonicalAttrs.Select(a => a.AttrType).SequenceEqual(
            unsortedAttrs.Select(a => a.AttrType)));

        signer.SignedAttrs = unsortedAttrs;
        info.SetContent(ModernCms.ContentSetContentBindings.CtSignedData, signedData);

        AssertRoundTrip(der, writer => info.Encode(writer));
    }

    private static void AssertModernSignedData(
        byte[] der,
        ExpectedCmsMessage expected,
        ExpectedCmsSignedData expectedSd,
        byte[]? detached)
    {
        var info = ModernCms.ContentInfo.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        Assert.Equal(expected.ContentType, info.ContentType.ToString());
        Assert.True(info.TryDecodeContent(
            ModernCms.ContentSetContentBindings.CtSignedData, out ModernCms.SignedData signedData));

        Assert.Equal(expectedSd.Version, signedData.Version);
        Assert.Equal(
            expectedSd.DigestAlgorithms,
            signedData.DigestAlgorithms.Select(a => a.Algorithm.ToString()).ToList());
        Assert.Equal(expectedSd.EContentType, signedData.EncapContentInfo.EContentType.ToString());
        Assert.Equal(expectedSd.EContentPresent, signedData.EncapContentInfo.EContent is not null);
        if (expectedSd.EContentPresent)
        {
            var eContent = signedData.EncapContentInfo.EContent!.Value.ToArray();
            Assert.Equal(expectedSd.EContentLength, eContent.Length);
            Assert.Equal(expectedSd.EContentSha1Hex, CmsRfc4134Fixtures.Sha1Hex(eContent));
            if (detached is not null)
                Assert.Equal(detached, eContent);
        }
        else
        {
            Assert.Null(signedData.EncapContentInfo.EContent);
            Assert.NotNull(detached);
        }

        Assert.Equal(expectedSd.CertificateCount, signedData.Certificates?.Length ?? 0);
        Assert.Equal(expectedSd.SignerCount, signedData.SignerInfos.Length);
        AssertSignerInfosModern(expectedSd, signedData);
        AssertRoundTrip(der, writer => info.Encode(writer));
    }

    private static void AssertLegacySignedData(
        byte[] der,
        ExpectedCmsMessage expected,
        ExpectedCmsSignedData expectedSd)
    {
        var info = LegacyCms.ContentInfo.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        Assert.Equal(expected.ContentType, info.ContentType.ToString());
        Assert.True(info.TryDecodeContent(
            LegacyCms.ContentInfoContentBindings.SignedData, out LegacyCms.SignedData signedData));

        Assert.Equal(expectedSd.Version, signedData.Version);
        Assert.Equal(
            expectedSd.DigestAlgorithms,
            signedData.DigestAlgorithms.Select(a => a.Algorithm.ToString()).ToList());
        Assert.Equal(expectedSd.EContentType, signedData.EncapContentInfo.EContentType.ToString());
        Assert.Equal(expectedSd.EContentPresent, signedData.EncapContentInfo.EContent is not null);
        Assert.Equal(expectedSd.CertificateCount, signedData.Certificates?.Length ?? 0);
        Assert.Equal(expectedSd.SignerCount, signedData.SignerInfos.Length);

        for (var i = 0; i < expectedSd.SignerInfos.Count; i++)
        {
            var expectedSigner = expectedSd.SignerInfos[i];
            var actual = signedData.SignerInfos[i];
            Assert.Equal(expectedSigner.DigestAlgorithm, actual.DigestAlgorithm.Algorithm.ToString());
            Assert.Equal(expectedSigner.SignatureAlgorithm, actual.SignatureAlgorithm.Algorithm.ToString());
            Assert.Equal(expectedSigner.SignatureByteLength, actual.Signature.Length);
            Assert.Equal(expectedSigner.SignedAttrsPresent, actual.SignedAttrs is not null);
            if (expectedSigner.SidKind == "issuerAndSerial")
            {
                Assert.Equal(LegacyCms.SignerIdentifierKind.IssuerAndSerialNumber, actual.Sid.Kind);
                Assert.Equal(
                    CmsRfc4134Fixtures.NormalizeIntegerHex(expectedSigner.SerialNumberHex!),
                    CmsRfc4134Fixtures.IntegerHex(actual.Sid.IssuerAndSerialNumber!.SerialNumber));
            }
            else
            {
                Assert.Equal(LegacyCms.SignerIdentifierKind.SubjectKeyIdentifier, actual.Sid.Kind);
                Assert.Equal(
                    expectedSigner.SubjectKeyIdentifierHex,
                    CmsRfc4134Fixtures.ToHex(actual.Sid.SubjectKeyIdentifier!.Value.Span));
            }
        }

        AssertRoundTrip(der, writer => info.Encode(writer));
    }

    private static void AssertSignerInfosModern(ExpectedCmsSignedData expectedSd, ModernCms.SignedData signedData)
    {
        for (var i = 0; i < expectedSd.SignerInfos.Count; i++)
        {
            var expectedSigner = expectedSd.SignerInfos[i];
            var actual = signedData.SignerInfos[i];
            Assert.Equal(expectedSigner.DigestAlgorithm, actual.DigestAlgorithm.Algorithm.ToString());
            Assert.Equal(expectedSigner.SignatureAlgorithm, actual.SignatureAlgorithm.Algorithm.ToString());
            Assert.Equal(expectedSigner.SignatureByteLength, actual.Signature.Length);
            Assert.Equal(expectedSigner.SignedAttrsPresent, actual.SignedAttrs is not null);

            if (expectedSigner.SidKind == "issuerAndSerial")
            {
                Assert.Equal(ModernCms.SignerIdentifierKind.IssuerAndSerialNumber, actual.Sid.Kind);
                Assert.Equal(
                    CmsRfc4134Fixtures.NormalizeIntegerHex(expectedSigner.SerialNumberHex!),
                    CmsRfc4134Fixtures.IntegerHex(actual.Sid.IssuerAndSerialNumber!.SerialNumber));
            }
            else
            {
                Assert.Equal(ModernCms.SignerIdentifierKind.SubjectKeyIdentifier, actual.Sid.Kind);
                Assert.Equal(
                    expectedSigner.SubjectKeyIdentifierHex,
                    CmsRfc4134Fixtures.ToHex(actual.Sid.SubjectKeyIdentifier!.Value.Span));
            }

            if (expectedSigner.SignedAttrsPresent)
            {
                Assert.Equal(
                    expectedSigner.SignedAttrsTlvHex,
                    CmsRfc4134Fixtures.ToHex(actual.SignedAttrs!.Value.OriginalEncoding.Span));
            }
        }
    }

    private static void AssertSignedCmsOracle(
        byte[] der,
        ExpectedCmsMessage expected,
        ExpectedCmsSignedData expectedSd,
        byte[]? detached)
    {
        var signed = CmsRfc4134Fixtures.DecodeSignedCms(der, detached);
        Assert.Equal(expectedSd.CertificateCount, signed.Certificates.Count);
        Assert.Equal(expectedSd.SignerCount, signed.SignerInfos.Count);
        Assert.Equal(expectedSd.EContentType, signed.ContentInfo.ContentType.Value);
        if (expectedSd.EContentPresent)
            Assert.Equal(expectedSd.EContentLength!.Value, signed.ContentInfo.Content.Length);
        else
        {
            // Detached SignedCms is constructed with external content; Content mirrors that buffer.
            Assert.NotNull(detached);
            Assert.Equal(detached, signed.ContentInfo.Content);
        }

        for (var i = 0; i < expectedSd.SignerInfos.Count; i++)
        {
            var expectedSigner = expectedSd.SignerInfos[i];
            var actual = signed.SignerInfos[i];
            Assert.Equal(expectedSigner.DigestAlgorithm, actual.DigestAlgorithm.Value);
            Assert.Equal(expectedSigner.SignatureByteLength, actual.GetSignature().Length);
            if (expectedSigner.SidKind == "issuerAndSerial")
            {
                Assert.Equal(
                    CmsRfc4134Fixtures.NormalizeIntegerHex(expectedSigner.SerialNumberHex!),
                    CmsRfc4134Fixtures.NormalizeIntegerHex(actual.Certificate!.SerialNumber));
            }
        }

        // Keep outer contentType assertion independent of SignedCms's eContent-facing ContentInfo.
        Assert.Equal("1.2.840.113549.1.7.2", expected.ContentType);
    }

    private static ModernCms.SignedData DecodeModernSignedData(byte[] der)
    {
        var info = ModernCms.ContentInfo.Decode(new Asn1Reader(der, Asn1Encoding.Der));
        Assert.True(info.TryDecodeContent(
            ModernCms.ContentSetContentBindings.CtSignedData, out ModernCms.SignedData signedData));
        return signedData;
    }

    private static byte[] ReadOctetStringContent(Asn1Any content)
    {
        var reader = new AsnReader(content.EncodedMemory, AsnEncodingRules.DER);
        return reader.ReadOctetString();
    }

    private static void AssertRoundTrip(byte[] der, Action<Asn1Writer> encode)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        encode(writer);
        Assert.Equal(der, writer.Encode());
    }
}
