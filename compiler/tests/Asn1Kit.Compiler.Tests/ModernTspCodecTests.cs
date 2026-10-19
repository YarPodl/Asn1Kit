using Asn1Kit.Modern.AlgorithmInformation2009;
using Asn1Kit.Modern.CryptographicMessageSyntax2009;
using Asn1Kit.Modern.PKIXTSP2009;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class ModernTspCodecTests
{
    private static readonly Asn1Oid Sha256 = Asn1Oid.Parse("2.16.840.1.101.3.4.2.1");
    private static readonly Asn1Oid Policy = Asn1Oid.Parse("1.2.3");

    [Fact]
    public void TimeStampReq_WithNonce_MatchesDerAndRoundTrips()
    {
        var request = new TimeStampReq
        {
            Version = 1,
            MessageImprint = CreateImprint(),
            Nonce = Asn1Integer.FromInt32(9)
        };

        var encoded = Encode(request);
        Assert.Equal(
            "30190201013011300B06096086480165030402010402AABB020109",
            Convert.ToHexString(encoded));

        var decoded = TimeStampReq.Decode(new Asn1Reader(encoded, Asn1Encoding.Der));
        Assert.Equal(1, decoded.Version);
        Assert.Equal("2.16.840.1.101.3.4.2.1", decoded.MessageImprint.HashAlgorithm.Algorithm.ToString());
        Assert.Equal(new byte[] { 0xAA, 0xBB }, decoded.MessageImprint.HashedMessage.ToArray());
        Assert.Equal(new byte[] { 0x09 }, decoded.Nonce!.Value.ToArray());
        Assert.False(decoded.CertReq);
        Assert.Equal(encoded, Encode(decoded));
    }

    [Fact]
    public void TimeStampResp_RejectionWithoutToken_MatchesDerAndRoundTrips()
    {
        var response = new TimeStampResp
        {
            Status = new PKIStatusInfo { Status = PKIStatus.Rejection }
        };

        var encoded = Encode(response);
        Assert.Equal("30053003020102", Convert.ToHexString(encoded));

        var decoded = TimeStampResp.Decode(new Asn1Reader(encoded, Asn1Encoding.Der));
        Assert.Equal(PKIStatus.Rejection, decoded.Status.Status);
        Assert.Null(decoded.TimeStampToken);
        Assert.Equal(encoded, Encode(decoded));
    }

    [Fact]
    public void TstInfo_WithAccuracy_MatchesDerAndRoundTrips()
    {
        var info = new TSTInfo
        {
            Version = 1,
            Policy = Policy,
            MessageImprint = CreateImprint(),
            SerialNumber = Asn1Integer.FromInt32(42),
            GenTime = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero),
            Accuracy = new Accuracy
            {
                Seconds = Asn1Integer.FromInt32(1),
                Millis = 2,
                Micros = 3
            }
        };

        var encoded = Encode(info);
        Assert.Equal(
            "303902010106022A033011300B06096086480165030402010402AABB02012A180F32303235303130323033303430355A3009020101800102810103",
            Convert.ToHexString(encoded));

        var decoded = TSTInfo.Decode(new Asn1Reader(encoded, Asn1Encoding.Der));
        Assert.Equal(1, decoded.Version);
        Assert.Equal("1.2.3", decoded.Policy.ToString());
        Assert.Equal(new byte[] { 0x2A }, decoded.SerialNumber.ToArray());
        Assert.Equal(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero), decoded.GenTime);
        Assert.Equal(new byte[] { 0x01 }, decoded.Accuracy!.Seconds!.Value.ToArray());
        Assert.Equal(2, decoded.Accuracy.Millis);
        Assert.Equal(3, decoded.Accuracy.Micros);
        Assert.Equal(encoded, Encode(decoded));
    }

    [Fact]
    public void EncapsulatedContentInfo_CarriesTstInfoOpaqueEContent()
    {
        var info = new TSTInfo
        {
            Version = 1,
            Policy = Policy,
            MessageImprint = CreateImprint(),
            SerialNumber = Asn1Integer.FromInt32(7),
            GenTime = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero)
        };
        var tstInfoDer = Encode(info);

        var encap = new EncapsulatedContentInfo
        {
            EContentType = PKIXTSP2009Oids.IdCtTSTInfo,
            EContent = tstInfoDer
        };

        var encoded = Encode(encap);
        var decoded = EncapsulatedContentInfo.Decode(new Asn1Reader(encoded, Asn1Encoding.Der));
        Assert.Equal(PKIXTSP2009Oids.IdCtTSTInfoString, decoded.EContentType.ToString());
        Assert.Equal(tstInfoDer, decoded.EContent!.Value.ToArray());

        var roundTrip = TSTInfo.Decode(new Asn1Reader(decoded.EContent.Value, Asn1Encoding.Der));
        Assert.Equal(new byte[] { 0x07 }, roundTrip.SerialNumber.ToArray());
        Assert.Equal(encoded, Encode(decoded));
    }

    [Theory]
    [InlineData("3100")]
    [InlineData("3005020101")]
    public void TimeStampReq_RejectsWrongTagAndTruncation(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.ThrowsAny<Asn1Exception>(() => TimeStampReq.Decode(new Asn1Reader(bytes, Asn1Encoding.Der)));
    }

    private static MessageImprint CreateImprint() => new()
    {
        HashAlgorithm = new AlgorithmIdentifier { Algorithm = Sha256 },
        HashedMessage = new byte[] { 0xAA, 0xBB }
    };

    private static byte[] Encode(TimeStampReq value)
    {
        var writer = new Asn1Writer();
        value.Encode(writer);
        return writer.Encode();
    }

    private static byte[] Encode(TimeStampResp value)
    {
        var writer = new Asn1Writer();
        value.Encode(writer);
        return writer.Encode();
    }

    private static byte[] Encode(TSTInfo value)
    {
        var writer = new Asn1Writer();
        value.Encode(writer);
        return writer.Encode();
    }

    private static byte[] Encode(EncapsulatedContentInfo value)
    {
        var writer = new Asn1Writer();
        value.Encode(writer);
        return writer.Encode();
    }
}
