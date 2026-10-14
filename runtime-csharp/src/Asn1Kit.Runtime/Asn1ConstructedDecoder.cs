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
        var segments = new List<ReadOnlyMemory<byte>>();
        var total = 0;
        CollectOctetLikeSegments(parent, constructedContents, segmentTag, segments, ref total, depth: 1);
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
        var segments = new List<ReadOnlyMemory<byte>>();
        var total = 0;
        CollectOctetLikeSegments(parent, constructedContents, segmentTag, segments, ref total, depth: 1);
        if (destination.Length < total)
        {
            bytesWritten = 0;
            return false;
        }

        CopySegments(segments, destination);
        bytesWritten = total;
        return true;
    }

    private static void CollectOctetLikeSegments(
        Asn1DecodeCursor parent,
        ReadOnlyMemory<byte> constructedContents,
        Asn1Tag segmentTag,
        List<ReadOnlyMemory<byte>> segments,
        ref int totalLength,
        int depth)
    {
        var nested = parent.CreateNested(constructedContents);
        while (!nested.Eof)
        {
            var tlv = nested.ReadTlv();
            EnsureExpectedTag(tlv.Tag, segmentTag);
            if (tlv.Tag.Constructed)
            {
                EnsureRecursionDepth(parent.Options, depth);
                CollectOctetLikeSegments(
                    parent,
                    tlv.Contents,
                    segmentTag,
                    segments,
                    ref totalLength,
                    depth + 1);
                continue;
            }

            AddMaterializedLength(parent.Options, ref totalLength, tlv.Contents.Length);
            segments.Add(tlv.Contents);
        }
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
        var segments = new List<Asn1BitString>();
        var total = 0;
        CollectBitStringSegments(
            parent,
            constructedContents,
            rejectTrailingBits,
            segments,
            ref total,
            depth: 1);

        var concatenated = total == 0 ? Array.Empty<byte>() : new byte[total];
        var offset = 0;
        foreach (var segment in segments)
        {
            segment.Span.CopyTo(concatenated.AsSpan(offset));
            offset += segment.Span.Length;
        }

        var value = new Asn1BitString(concatenated, segments[^1].UnusedBits);
        if (rejectTrailingBits)
        {
            Asn1TextCodec.EnsureTrailingBitsZero(value.Span, value.UnusedBits);
        }

        return value;
    }

    private static void CollectBitStringSegments(
        Asn1DecodeCursor parent,
        ReadOnlyMemory<byte> constructedContents,
        bool rejectTrailingBits,
        List<Asn1BitString> segments,
        ref int totalLength,
        int depth)
    {
        var initialCount = segments.Count;
        var nested = parent.CreateNested(constructedContents);
        while (!nested.Eof)
        {
            var tlv = nested.ReadTlv();
            EnsureExpectedTag(tlv.Tag, Asn1Tag.BitString);
            if (tlv.Tag.Constructed)
            {
                EnsureRecursionDepth(parent.Options, depth);
                CollectBitStringSegments(
                    parent,
                    tlv.Contents,
                    rejectTrailingBits,
                    segments,
                    ref totalLength,
                    depth + 1);
                continue;
            }

            if (segments.Count > 0 && segments[^1].UnusedBits != 0)
            {
                throw new Asn1Exception("Only the last BIT STRING segment may have unused bits.");
            }

            var segment = Asn1BitString.ParsePrimitive(tlv.Contents, rejectTrailingBits);
            AddMaterializedLength(parent.Options, ref totalLength, segment.Span.Length);
            segments.Add(segment);
        }

        if (segments.Count == initialCount)
        {
            throw new Asn1Exception("Constructed BIT STRING has no segments.");
        }
    }

    private static void EnsureRecursionDepth(Asn1ReaderOptions options, int depth)
    {
        if (depth >= options.MaxRecursionDepth)
        {
            throw new Asn1Exception($"ASN.1 recursion depth exceeds limit {options.MaxRecursionDepth}.");
        }
    }

    private static void AddMaterializedLength(Asn1ReaderOptions options, ref int total, int length)
    {
        if (length > options.MaxMaterializedConstructedValueBytes - total)
        {
            throw new Asn1Exception(
                $"Constructed value exceeds materialization limit {options.MaxMaterializedConstructedValueBytes} bytes.");
        }

        total += length;
    }

    private static void EnsureExpectedTag(Asn1Tag actual, Asn1Tag expected)
    {
        if (!actual.MatchesIgnoreConstructed(expected))
        {
            throw new Asn1Exception($"Expected tag {expected}, found {actual}.");
        }
    }
}
