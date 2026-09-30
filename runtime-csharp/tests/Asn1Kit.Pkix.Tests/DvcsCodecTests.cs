using Asn1Kit.Cmp;
using Asn1Kit.Dvcs;
using Asn1Kit.Pkix;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class DvcsCodecTests
{
    [Fact]
    public void MinimalRequest_Message_MatchesDerAndRoundTrips()
    {
        var request = new DVCSRequest
        {
            RequestInformation = new DVCSRequestInformation { Service = ServiceType.Cpd },
            Data = Data.FromMessage(new byte[] { 0x01, 0x02 })
        };

        var encoded = Encode(request);
        Assert.Equal("300930030A010104020102", Convert.ToHexString(encoded));

        var decoded = DVCSRequest.Decode(new Asn1Reader(encoded, Asn1Encoding.Der));
        Assert.Equal(ServiceType.Cpd, decoded.RequestInformation.Service);
        Assert.Equal(DataKind.Message, decoded.Data.Kind);
        Assert.Equal(new byte[] { 0x01, 0x02 }, decoded.Data.Message!.Value.ToArray());
        Assert.Equal(encoded, Encode(decoded));
    }

    [Fact]
    public void Request_MessageImprintAndNonce_MatchesDerAndRoundTrips()
    {
        var request = new DVCSRequest
        {
            RequestInformation = new DVCSRequestInformation
            {
                Service = ServiceType.Ccpd,
                Nonce = Asn1Integer.FromInt32(9)
            },
            Data = Data.FromMessageImprint(CreateImprint())
        };

        var encoded = Encode(request);
        Assert.Equal(
            "301B30060A01040201093011300B06096086480165030402010402AABB",
            Convert.ToHexString(encoded));

        var decoded = DVCSRequest.Decode(new Asn1Reader(encoded, Asn1Encoding.Der));
        Assert.Equal(ServiceType.Ccpd, decoded.RequestInformation.Service);
        Assert.Equal(new byte[] { 0x09 }, decoded.RequestInformation.Nonce!.Value.ToArray());
        Assert.Equal(DataKind.MessageImprint, decoded.Data.Kind);
        Assert.Equal("2.16.840.1.101.3.4.2.1", decoded.Data.MessageImprint!.DigestAlgorithm.Algorithm.ToString());
        Assert.Equal(new byte[] { 0xAA, 0xBB }, decoded.Data.MessageImprint.Digest.ToArray());
        Assert.Equal(encoded, Encode(decoded));
    }

    [Fact]
    public void Response_Error_MatchesDerAndRoundTrips()
    {
        var response = DVCSResponse.FromDvErrorNote(new DVCSErrorNotice
        {
            TransactionStatus = new PKIStatusInfo { Status = PKIStatus.Rejection }
        });

        var encoded = Encode(response);
        Assert.Equal("A0053003020102", Convert.ToHexString(encoded));

        var decoded = DVCSResponse.Decode(new Asn1Reader(encoded, Asn1Encoding.Der));
        Assert.Equal(DVCSResponseKind.DvErrorNote, decoded.Kind);
        Assert.Equal(PKIStatus.Rejection, decoded.DvErrorNote!.TransactionStatus.Status);
        Assert.Equal(encoded, Encode(decoded));
    }

    [Fact]
    public void Response_CertificateInfo_RoundTrips()
    {
        var response = DVCSResponse.FromDvCertInfo(new DVCSCertInfo
        {
            DvReqInfo = new DVCSRequestInformation { Service = ServiceType.Cpd },
            MessageImprint = CreateImprint(),
            SerialNumber = Asn1Integer.FromInt32(42),
            ResponseTime = DVCSTime.FromGenTime(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero))
        });

        var encoded = Encode(response);
        Assert.Equal(0x30, encoded[0]);

        var decoded = DVCSResponse.Decode(new Asn1Reader(encoded, Asn1Encoding.Der));
        Assert.Equal(DVCSResponseKind.DvCertInfo, decoded.Kind);
        Assert.Equal(ServiceType.Cpd, decoded.DvCertInfo!.DvReqInfo.Service);
        Assert.Equal(new byte[] { 0x2A }, decoded.DvCertInfo.SerialNumber.ToArray());
        Assert.Equal(DVCSTimeKind.GenTime, decoded.DvCertInfo.ResponseTime.Kind);
        Assert.Equal(encoded, Encode(decoded));
    }

    [Theory]
    [InlineData("3100")]
    [InlineData("300930030A0101040201")]
    public void Request_RejectsWrongTagAndTruncation(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.ThrowsAny<Asn1Exception>(() =>
            DVCSRequest.Decode(new Asn1Reader(bytes, Asn1Encoding.Der)));
    }

    private static DigestInfo CreateImprint() => new()
    {
        DigestAlgorithm = new AlgorithmIdentifier
        {
            Algorithm = Asn1Oid.Parse("2.16.840.1.101.3.4.2.1")
        },
        Digest = new byte[] { 0xAA, 0xBB }
    };

    private static byte[] Encode(DVCSRequest value)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        value.Encode(writer);
        return writer.Encode();
    }

    private static byte[] Encode(DVCSResponse value)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        value.Encode(writer);
        return writer.Encode();
    }
}
