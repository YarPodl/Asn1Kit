namespace Asn1Kit.Runtime;

/// <summary>
/// BER constructed string and BIT STRING decoding over independent cursor windows.
/// Constructed forms remain soft-accepted under DER for compatibility.
/// </summary>
internal static class Asn1ConstructedDecoder
{
    public static ReadOnlyMemory<byte> ReadOctetLike(ref Asn1DecodeCursor cursor, Asn1Tag expected)
    {
        var tlv = cursor.ReadTlv();
        EnsureExpectedTag(tlv.Tag, expected);
        if (!tlv.Tag.Constructed)
        {
            return tlv.Contents;
        }

        return ConcatOctetLike(cursor, tlv.Contents, expected.AsPrimitive());
    }

    public static bool TryReadOctetLike(
        ref Asn1DecodeCursor cursor,
        Asn1Tag expected,
        Span<byte> destination,
        out int bytesWritten)
    {
        var tlv = cursor.ReadTlv();
        EnsureExpectedTag(tlv.Tag, expected);
        if (!tlv.Tag.Constructed)
        {
            if (destination.Length < tlv.Contents.Length)
            {
                bytesWritten = 0;
                return false;
            }

            tlv.Contents.Span.CopyTo(destination);
            bytesWritten = tlv.Contents.Length;
            return true;
        }

        return TryCopyConcatOctetLike(
            cursor,
            tlv.Contents,
            expected.AsPrimitive(),
            destination,
            out bytesWritten);
    }

    public static Asn1BitString ReadBitString(
        ref Asn1DecodeCursor cursor,
        Asn1Tag expected,
        bool rejectTrailingBits)
    {
        var tlv = cursor.ReadTlv();
        EnsureExpectedTag(tlv.Tag, expected);
        if (!tlv.Tag.Constructed)
        {
            return Asn1BitString.ParsePrimitive(tlv.Contents, rejectTrailingBits);
        }

        return ReadConstructedBitString(cursor, tlv.Contents, rejectTrailingBits);
    }

    private static ReadOnlyMemory<byte> ConcatOctetLike(
        Asn1DecodeCursor parent,
        ReadOnlyMemory<byte> constructedContents,
        Asn1Tag segmentTag)
    {
        var segments = CollectOctetLikeSegments(parent, constructedContents, segmentTag, out var total);
        if (total == 0)
        {
            return Array.Empty<byte>();
        }

        var result = new byte[total];
        CopySegments(segments, result);
        return result;
    }

    private static bool TryCopyConcatOctetLike(
        Asn1DecodeCursor parent,
        ReadOnlyMemory<byte> constructedContents,
        Asn1Tag segmentTag,
        Span<byte> destination,
        out int bytesWritten)
    {
        var segments = CollectOctetLikeSegments(parent, constructedContents, segmentTag, out var total);
        if (destination.Length < total)
        {
            bytesWritten = 0;
            return false;
        }

        CopySegments(segments, destination);
        bytesWritten = total;
        return true;
    }

    private static List<ReadOnlyMemory<byte>> CollectOctetLikeSegments(
        Asn1DecodeCursor parent,
        ReadOnlyMemory<byte> constructedContents,
        Asn1Tag segmentTag,
        out int totalLength)
    {
        var nested = parent.CreateNested(constructedContents);
        var segments = new List<ReadOnlyMemory<byte>>();
        var length = 0;
        while (!nested.Eof)
        {
            var segment = ReadOctetLike(ref nested, segmentTag);
            if (segment.Length > int.MaxValue - length)
            {
                throw new Asn1Exception("Constructed value exceeds Int32.");
            }

            segments.Add(segment);
            length += segment.Length;
        }

        totalLength = length;
        return segments;
    }

    private static void CopySegments(List<ReadOnlyMemory<byte>> segments, Span<byte> destination)
    {
        var offset = 0;
        foreach (var segment in segments)
        {
            segment.Span.CopyTo(destination.Slice(offset));
            offset += segment.Length;
        }
    }

    private static Asn1BitString ReadConstructedBitString(
        Asn1DecodeCursor parent,
        ReadOnlyMemory<byte> constructedContents,
        bool rejectTrailingBits)
    {
        var nested = parent.CreateNested(constructedContents);
        var segments = new List<Asn1BitString>();
        var unusedBits = 0;
        while (!nested.Eof)
        {
            var segment = ReadBitString(ref nested, Asn1Tag.BitString, rejectTrailingBits);
            if (segments.Count > 0 && unusedBits != 0)
            {
                throw new Asn1Exception("Only the last BIT STRING segment may have unused bits.");
            }

            segments.Add(segment);
            unusedBits = segment.UnusedBits;
        }

        if (segments.Count == 0)
        {
            throw new Asn1Exception("Constructed BIT STRING has no segments.");
        }

        var total = 0;
        foreach (var segment in segments)
        {
            if (segment.Span.Length > int.MaxValue - total)
            {
                throw new Asn1Exception("Constructed BIT STRING exceeds Int32.");
            }

            total += segment.Span.Length;
        }

        var concatenated = total == 0 ? Array.Empty<byte>() : new byte[total];
        var offset = 0;
        foreach (var segment in segments)
        {
            segment.Span.CopyTo(concatenated.AsSpan(offset));
            offset += segment.Span.Length;
        }

        var value = new Asn1BitString(concatenated, unusedBits);
        if (rejectTrailingBits)
        {
            Asn1TextCodec.EnsureTrailingBitsZero(value.Span, value.UnusedBits);
        }

        return value;
    }

    private static void EnsureExpectedTag(Asn1Tag actual, Asn1Tag expected)
    {
        if (!actual.MatchesIgnoreConstructed(expected))
        {
            throw new Asn1Exception($"Expected tag {expected}, found {actual}.");
        }
    }
}
