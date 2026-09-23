using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class RuntimeTests
{
    [Fact]
    public void Der_RoundTripsPersonShape()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSequence(Asn1Tag.Sequence, inner =>
        {
            Asn1Integer.Encode(inner, 42, new Asn1Tag(Asn1TagClass.ContextSpecific, 0));
            Asn1OctetString.Encode(inner, Encoding.UTF8.GetBytes("Ann"), new Asn1Tag(Asn1TagClass.ContextSpecific, 1));
        });
        var bytes = writer.Encode();
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        reader.ReadSequence(Asn1Tag.Sequence, inner =>
        {
            Assert.Equal(42, Asn1Integer.Decode(inner, new Asn1Tag(Asn1TagClass.ContextSpecific, 0)).GetInt32());
            Assert.Equal("Ann", Encoding.UTF8.GetString(Asn1OctetString.Decode(inner, new Asn1Tag(Asn1TagClass.ContextSpecific, 1)).Span));
            Assert.True(inner.Eof);
        });
    }

    [Fact]
    public void WriteSequence_Empty_EmitsShortFormLengthZero()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSequence(Asn1Tag.Sequence, _ => { });
        Assert.Equal(new byte[] { 0x30, 0x00 }, writer.Encode());
    }

    [Fact]
    public void WriteSequence_ContentsLength128_UsesLongFormLength()
    {
        // 128-byte OCTET STRING: 04 81 80 + 128 payload = 131 content octets → outer length long-form.
        var payload = new byte[128];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)i;
        }

        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSequence(Asn1Tag.Sequence, inner =>
        {
            inner.WriteOctetString(Asn1Tag.OctetString, payload);
        });
        var bytes = writer.Encode();

        Assert.Equal(0x30, bytes[0]);
        Assert.Equal(0x81, bytes[1]);
        Assert.Equal(131, bytes[2]);
        Assert.Equal(0x04, bytes[3]);
        Assert.Equal(0x81, bytes[4]);
        Assert.Equal(0x80, bytes[5]);
        Assert.Equal(payload, bytes.AsSpan(6).ToArray());

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        reader.ReadSequence(Asn1Tag.Sequence, inner =>
        {
            Assert.Equal(payload, inner.ReadOctetString(Asn1Tag.OctetString).ToArray());
            Assert.True(inner.Eof);
        });
    }

    [Fact]
    public void WriteSequence_DeepNesting_RoundTrips()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSequence(Asn1Tag.Sequence, level1 =>
        {
            level1.WriteSequence(Asn1Tag.Sequence, level2 =>
            {
                level2.WriteSequence(Asn1Tag.Sequence, level3 =>
                {
                    Asn1Integer.Encode(level3, 7);
                    Asn1Integer.Encode(level3, 9);
                });
            });
        });
        var bytes = writer.Encode();

        // 30 0A 30 08 30 06 02 01 07 02 01 09
        Assert.Equal(
            new byte[] { 0x30, 0x0A, 0x30, 0x08, 0x30, 0x06, 0x02, 0x01, 0x07, 0x02, 0x01, 0x09 },
            bytes);

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        reader.ReadSequence(Asn1Tag.Sequence, level1 =>
        {
            level1.ReadSequence(Asn1Tag.Sequence, level2 =>
            {
                level2.ReadSequence(Asn1Tag.Sequence, level3 =>
                {
                    Assert.Equal(7, Asn1Integer.Decode(level3).GetInt32());
                    Assert.Equal(9, Asn1Integer.Decode(level3).GetInt32());
                    Assert.True(level3.Eof);
                });
                Assert.True(level2.Eof);
            });
            Assert.True(level1.Eof);
        });
    }

    [Fact]
    public void Ber_ReadsIndefiniteLengthOctetString()
    {
        var ber = new byte[] { 0x24, 0x80, 0x04, 0x03, 0x41, 0x6E, 0x6E, 0x00, 0x00 };
        var reader = new Asn1Reader(ber, Asn1Encoding.Ber);
        var value = reader.ReadOctetString(Asn1Tag.OctetString);
        Assert.Equal("Ann", Encoding.UTF8.GetString(value.Span));
    }

    [Fact]
    public void Der_RejectsIndefiniteLength()
    {
        var ber = new byte[] { 0x30, 0x80, 0x00, 0x00 };
        var reader = new Asn1Reader(ber, Asn1Encoding.Der);
        Assert.Throws<Asn1Exception>(() => reader.ReadSequence(Asn1Tag.Sequence, _ => { }));
    }

    [Fact]
    public void Set_TagIsUniversal17()
    {
        Assert.Equal(Asn1TagClass.Universal, Asn1Tag.Set.TagClass);
        Assert.Equal(17, Asn1Tag.Set.Number);
        Assert.True(Asn1Tag.Set.Constructed);
    }

    [Fact]
    public void Reader_OffsetLengthCtor_ReadsSliceWithoutOwningPrefix()
    {
        // NULL then INTEGER 42 вЂ” reader starts at the INTEGER TLV.
        var data = new byte[] { 0x05, 0x00, 0x02, 0x01, 0x2A };
        var reader = new Asn1Reader(data, offset: 2, length: 3, Asn1Encoding.Der);
        Assert.Equal(42, reader.ReadInteger(Asn1Tag.Integer));
        Assert.True(reader.Eof);
    }

    [Fact]
    public void Reader_ReadOnlyMemoryCtor_ReadsArrayBackedSlice()
    {
        var data = new byte[] { 0x05, 0x00, 0x02, 0x01, 0x2A };
        var reader = new Asn1Reader(data.AsMemory(2, 3), Asn1Encoding.Der);
        Assert.Equal(42, reader.ReadInteger(Asn1Tag.Integer));
        Assert.True(reader.Eof);
    }

    [Fact]
    public void Reader_DecodedValues_AliasSourceBuffer()
    {
        var data = new byte[]
        {
            0x04, 0x03, 0x41, 0x6E, 0x6E,
            0x02, 0x01, 0x2A,
            0x03, 0x02, 0x00, 0xA0,
            0x05, 0x00
        };
        var reader = new Asn1Reader(data, Asn1Encoding.Der);

        var octet = reader.ReadOctetString(Asn1Tag.OctetString);
        AssertSameArray(data, octet, expectedOffset: 2);

        var integer = reader.ReadIntegerValue(Asn1Tag.Integer);
        AssertSameArray(data, integer.Memory, expectedOffset: 7);

        var bits = reader.ReadBitString(Asn1Tag.BitString);
        AssertSameArray(data, bits.Memory, expectedOffset: 11);

        var any = reader.ReadAny();
        AssertSameArray(data, any.EncodedMemory, expectedOffset: 12);
        AssertSameArray(data, any.ContentsMemory, expectedOffset: 14);
        Assert.True(reader.Eof);
        Assert.True(MemoryMarshal.TryGetArray(reader.Source, out ArraySegment<byte> source));
        Assert.Same(data, source.Array);
    }

    [Fact]
    public void Reader_ConstructedBerOctet_DoesNotAliasSource()
    {
        var ber = new byte[] { 0x24, 0x80, 0x04, 0x03, 0x41, 0x6E, 0x6E, 0x00, 0x00 };
        var reader = new Asn1Reader(ber, Asn1Encoding.Ber);
        var value = reader.ReadOctetString(Asn1Tag.OctetString);
        Assert.Equal(new byte[] { 0x41, 0x6E, 0x6E }, value.ToArray());
        Assert.True(MemoryMarshal.TryGetArray(value, out ArraySegment<byte> segment));
        Assert.NotSame(ber, segment.Array);
    }

    private static void AssertSameArray(byte[] source, ReadOnlyMemory<byte> view, int expectedOffset)
    {
        Assert.True(MemoryMarshal.TryGetArray(view, out ArraySegment<byte> segment));
        Assert.Same(source, segment.Array);
        Assert.Equal(expectedOffset, segment.Offset);
        Assert.Equal(view.Length, segment.Count);
    }

    [Fact]
    public void Reader_OffsetLengthCtor_RejectsOutOfRange()
    {
        var data = new byte[] { 0x02, 0x01, 0x01 };
        Assert.Throws<ArgumentOutOfRangeException>(() => new Asn1Reader(data, 1, 3, Asn1Encoding.Der));
        Assert.Throws<ArgumentNullException>(() => new Asn1Reader((byte[])null!, Asn1Encoding.Der));
    }

    [Fact]
    public void TryEncode_CopiesIntoCallerBuffer()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1Integer.Encode(writer, 42);
        var expected = writer.Encode();
        Assert.Equal(expected.Length, writer.EncodedLength);

        Span<byte> exact = stackalloc byte[expected.Length];
        Assert.True(writer.TryEncode(exact, out var written));
        Assert.Equal(expected.Length, written);
        Assert.True(expected.AsSpan().SequenceEqual(exact));

        Span<byte> tooSmall = stackalloc byte[expected.Length - 1];
        Assert.False(writer.TryEncode(tooSmall, out written));
        Assert.Equal(0, written);

        var memory = new byte[expected.Length];
        Assert.True(writer.TryEncode(memory.AsSpan(), out written));
        Assert.Equal(expected, memory);
    }

    [Fact]
    public void TryReadOctetString_CopiesAndRejectsShortDestination()
    {
        var payload = new byte[] { 0x41, 0x6E, 0x6E };
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteOctetString(Asn1Tag.OctetString, payload.AsMemory().Span);
        var bytes = writer.Encode();

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        Span<byte> dest = stackalloc byte[3];
        Assert.True(reader.TryReadOctetString(Asn1Tag.OctetString, dest, out var written));
        Assert.Equal(3, written);
        Assert.True(payload.AsSpan().SequenceEqual(dest));

        var shortReader = new Asn1Reader(bytes, Asn1Encoding.Der);
        Span<byte> tooSmall = stackalloc byte[2];
        Assert.False(shortReader.TryReadOctetString(Asn1Tag.OctetString, tooSmall, out written));
        Assert.Equal(0, written);
        Assert.True(shortReader.Eof);

        var ber = new byte[] { 0x24, 0x80, 0x04, 0x03, 0x41, 0x6E, 0x6E, 0x00, 0x00 };
        var berReader = new Asn1Reader(ber, Asn1Encoding.Ber);
        var berDest = new byte[3];
        Assert.True(berReader.TryReadOctetString(Asn1Tag.OctetString, berDest.AsSpan(), out written));
        Assert.Equal(3, written);
        Assert.Equal(payload, berDest);
    }

    [Fact]
    public void TryReadValue_CopiesPrimitiveContents()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1Integer.Encode(writer, 1);
        var bytes = writer.Encode();
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        Span<byte> dest = stackalloc byte[1];
        Assert.True(reader.TryReadValue(Asn1Tag.Integer, allowConstructed: false, dest, out var written));
        Assert.Equal(1, written);
        Assert.Equal(0x01, dest[0]);
    }

    [Fact]
    public void WriteOctetString_And_Any_AcceptReadOnlyMemoryViaSpan()
    {
        ReadOnlyMemory<byte> payload = new byte[] { 0x01, 0x02 };
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteOctetString(Asn1Tag.OctetString, payload.Span);
        var any = Asn1Any.FromTagAndContents(Asn1Tag.OctetString, payload.Span);
        Assert.Equal(payload.ToArray(), any.ContentsMemory.ToArray());
        Assert.Equal(new byte[] { 0x04, 0x02, 0x01, 0x02 }, any.ToArray());

        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        rewrite.WriteAny(any);
        Assert.Equal(writer.Encode(), rewrite.Encode());
    }

    [Fact]
    public void WriteSetOf_DerSortsElementEncodings()
    {
        // INTEGER 2 then INTEGER 1 вЂ” DER must emit 1 then 2 (X.690 В§11.6).
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSetOf(Asn1Tag.Set, inner =>
        {
            Asn1Integer.Encode(inner, 2);
            Asn1Integer.Encode(inner, 1);
        });
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x31, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02 }, bytes);

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        reader.ReadSet(Asn1Tag.Set, inner =>
        {
            Assert.Equal(1, Asn1Integer.Decode(inner).GetInt32());
            Assert.Equal(2, Asn1Integer.Decode(inner).GetInt32());
            Assert.True(inner.Eof);
        });
    }

    [Fact]
    public void WriteSetOf_BerPreservesElementOrder()
    {
        var writer = new Asn1Writer(Asn1Encoding.Ber);
        writer.WriteSetOf(Asn1Tag.Set, inner =>
        {
            Asn1Integer.Encode(inner, 2);
            Asn1Integer.Encode(inner, 1);
        });
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x31, 0x06, 0x02, 0x01, 0x02, 0x02, 0x01, 0x01 }, bytes);
    }

    [Fact]
    public void WriteSequenceOf_RoundTripsItems()
    {
        var items = new List<Asn1Integer>
        {
            Asn1Integer.FromInt32(1),
            Asn1Integer.FromInt32(2),
        };
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSequenceOf(Asn1Tag.Sequence, items, static (w, item) => Asn1Integer.Encode(w, item));
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x30, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02 }, bytes);

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var decoded = reader.ReadSequenceOf(Asn1Tag.Sequence, static inner => Asn1Integer.Decode(inner));
        Assert.Equal(2, decoded.Count);
        Assert.Equal(1, decoded[0].GetInt32());
        Assert.Equal(2, decoded[1].GetInt32());
        Assert.True(reader.Eof);
    }

    [Fact]
    public void WriteSetOfItems_DerSortsElementEncodings()
    {
        var items = new List<Asn1Integer>
        {
            Asn1Integer.FromInt32(2),
            Asn1Integer.FromInt32(1),
        };
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSetOf(Asn1Tag.Set, items, static (w, item) => Asn1Integer.Encode(w, item));
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x31, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02 }, bytes);

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var decoded = reader.ReadSetOf(Asn1Tag.Set, static inner => Asn1Integer.Decode(inner));
        Assert.Equal(2, decoded.Count);
        Assert.Equal(1, decoded[0].GetInt32());
        Assert.Equal(2, decoded[1].GetInt32());
    }

    [Fact]
    public void Any_DerRoundTripsTagAndContents()
    {
        var value = Asn1Any.FromTagAndContents(Asn1Tag.Integer, new byte[] { 0x05 });
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteAny(value);
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x02, 0x01, 0x05 }, bytes);

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var decoded = reader.ReadAny();
        Assert.Equal(Asn1Tag.Integer, decoded.Tag);
        Assert.Equal(new byte[] { 0x02, 0x01, 0x05 }, decoded.ToArray());
        Assert.Equal(new byte[] { 0x05 }, decoded.ContentsMemory.ToArray());
        Assert.True(reader.Eof);

        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        rewrite.WriteAny(decoded);
        Assert.Equal(bytes, rewrite.Encode());
    }

    [Fact]
    public void Any_ReadAnyExpectedTag_RejectsMismatch()
    {
        var bytes = new byte[] { 0x02, 0x01, 0x05 };
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var ex = Assert.Throws<Asn1Exception>(() => reader.ReadAny(Asn1Tag.Null));
        Assert.Contains("Expected tag", ex.Message);
    }

    [Fact]
    public void Any_ImplicitTag_RoundTrips()
    {
        var value = Asn1Any.FromTagAndContents(Asn1Tag.Null, Array.Empty<byte>());
        var context = new Asn1Tag(Asn1TagClass.ContextSpecific, 0);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteAny(context, value);
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x80, 0x00 }, bytes);

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var decoded = reader.ReadAny(context);
        Assert.Equal(context, decoded.Tag);
        Assert.Equal(new byte[] { 0x80, 0x00 }, decoded.ToArray());
        Assert.Equal(0, decoded.ContentsMemory.Length);
    }

    [Fact]
    public void Any_BerReadsIndefiniteLength()
    {
        // SEQUENCE { INTEGER 1 } with indefinite length as ANY
        var ber = new byte[] { 0x30, 0x80, 0x02, 0x01, 0x01, 0x00, 0x00 };
        var reader = new Asn1Reader(ber, Asn1Encoding.Ber);
        var decoded = reader.ReadAny();
        Assert.Equal(Asn1Tag.Sequence, decoded.Tag);
        Assert.Equal(ber, decoded.ToArray());
        Assert.Equal(new byte[] { 0x02, 0x01, 0x01 }, decoded.ContentsMemory.ToArray());
        Assert.True(reader.Eof);

        var rewrite = new Asn1Writer(Asn1Encoding.Ber);
        rewrite.WriteAny(decoded);
        Assert.Equal(ber, rewrite.Encode());
    }

    [Fact]
    public void Any_PreservesNonMinimalLengthOnRewrite()
    {
        var encoded = new byte[] { 0x02, 0x81, 0x01, 0x05 };
        var reader = new Asn1Reader(encoded, Asn1Encoding.Ber, Asn1ReaderOptions.AllowNonMinimalLength);
        var decoded = reader.ReadAny();
        Assert.Equal(encoded, decoded.ToArray());

        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        rewrite.WriteAny(decoded);
        Assert.Equal(encoded, rewrite.Encode());
    }

    [Fact]
    public void Any_Ctor_RejectsTrailingOctets()
    {
        var bytes = new byte[] { 0x05, 0x00, 0x05, 0x00 };
        var ex = Assert.Throws<Asn1Exception>(() => new Asn1Any(bytes));
        Assert.Contains("single complete TLV", ex.Message);
    }

    [Fact]
    public void ObjectIdentifier_RoundTripsDottedString()
    {
        const string oid = "1.2.840.113549";
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1Oid.Encode(writer, oid);
        var reader = new Asn1Reader(writer.Encode(), Asn1Encoding.Der);
        Assert.Equal(oid, Asn1Oid.DecodeString(reader));
    }

    [Fact]
    public void ObjectIdentifier_ParseArcsAndEncodeContents()
    {
        const string oid = "1.2.840.113549";
        Assert.Equal(new[] { 1, 2, 840, 113549 }, Asn1Oid.ParseArcs(oid));
        Assert.Equal(new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d }, Asn1Oid.EncodeContents(oid));
        Assert.Equal(oid, Asn1Oid.Parse(oid).ToString());
    }

    [Fact]
    public void ObjectIdentifier_RejectsInvalidDottedStrings()
    {
        Assert.Throws<Asn1Exception>(() => Asn1Oid.ParseArcs(""));
        Assert.Throws<Asn1Exception>(() => Asn1Oid.ParseArcs("1"));
        Assert.Throws<Asn1Exception>(() => Asn1Oid.ParseArcs("1.2.x"));
        Assert.Throws<Asn1Exception>(() => Asn1Oid.ParseArcs("1.-2"));
        Assert.Throws<Asn1Exception>(() => Asn1Oid.EncodeContents("1"));
    }

    [Fact]
    public void BitString_DerRoundTripsAndMatchesVector()
    {
        var value = Asn1BitString.FromBits(new[] { true, false, true });
        Assert.Equal(3, value.BitLength);
        Assert.Equal(5, value.UnusedBits);
        Assert.True(value[0]);
        Assert.False(value[1]);
        Assert.True(value[2]);

        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1BitString.Encode(writer, value);
        var bytes = writer.Encode();
        Assert.Equal(new byte[] { 0x03, 0x02, 0x05, 0xA0 }, bytes);

        var decoded = Asn1BitString.Decode(new Asn1Reader(bytes, Asn1Encoding.Der));
        Assert.Equal(value, decoded);

        var emptyWriter = new Asn1Writer(Asn1Encoding.Der);
        Asn1BitString.Encode(emptyWriter, default);
        Assert.Equal(new byte[] { 0x03, 0x01, 0x00 }, emptyWriter.Encode());
    }

    [Fact]
    public void BitString_BerReadsConstructedIndefinite()
    {
        // constructed indefinite: segment "10" (unused=6, 0x80) + segment "1" (unused=7, 0x80)
        var ber = new byte[]
        {
            0x23, 0x80,
            0x03, 0x02, 0x00, 0xA0,
            0x03, 0x02, 0x05, 0x00,
            0x00, 0x00
        };
        var reader = new Asn1Reader(ber, Asn1Encoding.Ber);
        var value = reader.ReadBitString(Asn1Tag.BitString);
        Assert.Equal(new byte[] { 0xA0, 0x00 }, value.Span.ToArray());
        Assert.Equal(5, value.UnusedBits);
    }

    [Fact]
    public void BitString_RejectsInvalidForms()
    {
        Assert.Throws<Asn1Exception>(() => new Asn1BitString(new byte[] { 0xFF }, 8));
        Assert.Throws<Asn1Exception>(() => Asn1BitString.CopyFrom(ReadOnlySpan<byte>.Empty, 1));

        var emptyContents = new byte[] { 0x03, 0x00 };
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(emptyContents, Asn1Encoding.Der).ReadBitString(Asn1Tag.BitString));

        var badUnused = new byte[] { 0x03, 0x02, 0x08, 0x00 };
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(badUnused, Asn1Encoding.Der).ReadBitString(Asn1Tag.BitString));

        var trailingBits = new Asn1BitString(new byte[] { 0xA1 }, 5); // low 5 bits not zero
        Assert.Throws<Asn1Exception>(() =>
        {
            var writer = new Asn1Writer(Asn1Encoding.Der);
            Asn1BitString.Encode(writer, trailingBits);
        });

        var softTrailing = new byte[] { 0x03, 0x02, 0x05, 0xA1 };
        var softDecoded = new Asn1Reader(softTrailing, Asn1Encoding.Der).ReadBitString(Asn1Tag.BitString);
        Assert.Equal(5, softDecoded.UnusedBits);
        Assert.Equal(new byte[] { 0xA1 }, softDecoded.Span.ToArray());

        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(softTrailing, Asn1Encoding.Der, Asn1ReaderOptions.Strict)
                .ReadBitString(Asn1Tag.BitString));
    }

    [Fact]
    public void String_DerRoundTripsForms()
    {
        RoundTripString(Asn1StringForm.Utf8, "РџСЂРёРІРµС‚", Asn1Tag.Utf8String);
        RoundTripString(Asn1StringForm.Printable, "Ann-1", Asn1Tag.PrintableString);
        RoundTripString(Asn1StringForm.Ia5, "user@host", Asn1Tag.Ia5String);
        RoundTripString(Asn1StringForm.Numeric, "12 34", Asn1Tag.NumericString);
        RoundTripString(Asn1StringForm.Visible, "Hello!", Asn1Tag.VisibleString);
        RoundTripString(Asn1StringForm.Bmp, "Hi", Asn1Tag.BmpString);
        RoundTripString(Asn1StringForm.Universal, "A", Asn1Tag.UniversalString);
        RoundTripString(Asn1StringForm.Teletex, "caf\u00e9", Asn1Tag.TeletexString);

        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1String.Encode(writer, "Ann", Asn1StringForm.Printable);
        Assert.Equal(new byte[] { 0x13, 0x03, 0x41, 0x6E, 0x6E }, writer.Encode());
    }

    [Fact]
    public void String_BerReadsConstructedUtf8()
    {
        var ber = new byte[]
        {
            0x2C, 0x80,
            0x0C, 0x02, 0x41, 0x6E,
            0x0C, 0x01, 0x6E,
            0x00, 0x00
        };
        var reader = new Asn1Reader(ber, Asn1Encoding.Ber);
        Assert.Equal("Ann", reader.ReadString(Asn1Tag.Utf8String, Asn1StringForm.Utf8));
    }

    [Fact]
    public void String_RejectsInvalidCharactersAndLengths()
    {
        Assert.Throws<Asn1Exception>(() =>
        {
            var writer = new Asn1Writer(Asn1Encoding.Der);
            Asn1String.Encode(writer, "Ann@", Asn1StringForm.Printable);
        });
        Assert.Throws<Asn1Exception>(() =>
        {
            var writer = new Asn1Writer(Asn1Encoding.Der);
            Asn1String.Encode(writer, "12a", Asn1StringForm.Numeric);
        });
        Assert.Throws<Asn1Exception>(() =>
        {
            var writer = new Asn1Writer(Asn1Encoding.Der);
            Asn1String.Encode(writer, "\u0080", Asn1StringForm.Ia5);
        });

        var oddBmp = new byte[] { 0x1E, 0x01, 0x00 };
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(oddBmp, Asn1Encoding.Der).ReadString(Asn1Tag.BmpString, Asn1StringForm.Bmp));

        var badPrintable = new byte[] { 0x13, 0x01, (byte)'@' };
        Assert.Throws<Asn1Exception>(() =>
            new Asn1Reader(badPrintable, Asn1Encoding.Der).ReadString(Asn1Tag.PrintableString, Asn1StringForm.Printable));
    }

    [Fact]
    public void Time_DerRoundTripsUtcAndGeneralized()
    {
        var utc = new DateTimeOffset(2017, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1Time.Encode(writer, utc, Asn1TimeForm.Utc);
        var bytes = writer.Encode();
        Assert.Equal(
            new byte[]
            {
                0x17, 0x0D,
                0x31, 0x37, 0x30, 0x31, 0x30, 0x32, 0x30, 0x33, 0x30, 0x34, 0x30, 0x35, 0x5A
            },
            bytes);
        Assert.Equal(utc, Asn1Time.Decode(new Asn1Reader(bytes, Asn1Encoding.Der), Asn1TimeForm.Utc));

        // Default fractionDigits = 3: 120 ms в†’ ".12Z" after trim.
        var withFraction = new DateTimeOffset(2017, 1, 2, 3, 4, 5, 120, TimeSpan.Zero);
        var gWriter = new Asn1Writer(Asn1Encoding.Der);
        Asn1Time.Encode(gWriter, withFraction, Asn1TimeForm.Generalized);
        var gBytes = gWriter.Encode();
        Assert.Equal(
            Encoding.ASCII.GetBytes("20170102030405.12Z"),
            gBytes.AsSpan(2).ToArray());
        Assert.Equal(withFraction, Asn1Time.Decode(new Asn1Reader(gBytes, Asn1Encoding.Der), Asn1TimeForm.Generalized));
    }

    [Fact]
    public void Time_GeneralizedFraction_ReadsOneToSevenDigits_WritesDer()
    {
        // Read: 1..7 fractional digits (BER and DER).
        Assert.Equal(1_000_000, FractionTicksOf("20170102030405.1Z"));
        Assert.Equal(1_200_000, FractionTicksOf("20170102030405.12Z"));
        Assert.Equal(1_230_000, FractionTicksOf("20170102030405.123Z"));
        Assert.Equal(1_234_000, FractionTicksOf("20170102030405.1234Z"));
        Assert.Equal(1_234_500, FractionTicksOf("20170102030405.12345Z"));
        Assert.Equal(1_234_560, FractionTicksOf("20170102030405.123456Z"));
        Assert.Equal(1_234_567, FractionTicksOf("20170102030405.1234567Z"));

        Assert.Throws<Asn1Exception>(() => FractionTicksOf("20170102030405.Z"));
        Assert.Throws<Asn1Exception>(() => FractionTicksOf("20170102030405.12345678Z"));

        // DER read accepts trailing zeros (soft profile).
        Assert.Equal(
            1_200_000,
            FractionTicksOf("20170102030405.120Z", Asn1Encoding.Der));

        // Write default (=3): round to ms, then DER-trim trailing zeros.
        AssertDerFraction(0, "20170102030405Z");
        AssertDerFraction(1_000_000, "20170102030405.1Z");
        AssertDerFraction(1_200_000, "20170102030405.12Z");
        AssertDerFraction(1_230_000, "20170102030405.123Z");
        AssertDerFraction(1_234_567, "20170102030405.123Z"); // rounds 0.1234567 в†’ 0.123

        // Write with fractionDigits = 0: drop subseconds.
        AssertDerFraction(1_234_567, "20170102030405Z", fractionDigits: 0);
        AssertDerFraction(6_000_000, "20170102030406Z", fractionDigits: 0); // 0.6s в†’ round up

        // Write with fractionDigits = 7: full tick precision, trim zeros.
        AssertDerFraction(0, "20170102030405Z", fractionDigits: 7);
        AssertDerFraction(1_000_000, "20170102030405.1Z", fractionDigits: 7);
        AssertDerFraction(1_200_000, "20170102030405.12Z", fractionDigits: 7);
        AssertDerFraction(1_234_567, "20170102030405.1234567Z", fractionDigits: 7);
    }

    private static int FractionTicksOf(string generalized, Asn1Encoding encoding = Asn1Encoding.Ber)
    {
        var bytes = WrapTime(Asn1Tag.GeneralizedTime, generalized);
        var value = Asn1Time.Decode(new Asn1Reader(bytes, encoding), Asn1TimeForm.Generalized);
        return (int)(value.Ticks % TimeSpan.TicksPerSecond);
    }

    private static void AssertDerFraction(int fractionTicks, string expectedText, int fractionDigits = 3)
    {
        var value = new DateTimeOffset(2017, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(fractionTicks);
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1Time.Encode(writer, value, Asn1TimeForm.Generalized, fractionDigits: fractionDigits);
        Assert.Equal(Encoding.ASCII.GetBytes(expectedText), writer.Encode().AsSpan(2).ToArray());
    }

    [Fact]
    public void Time_YearPivotAndBerOffset()
    {
        var y49 = Asn1Time.Decode(
            new Asn1Reader(WrapTime(Asn1Tag.UtcTime, "490102030405Z"), Asn1Encoding.Der),
            Asn1TimeForm.Utc);
        Assert.Equal(2049, y49.Year);

        var y50 = Asn1Time.Decode(
            new Asn1Reader(WrapTime(Asn1Tag.UtcTime, "500102030405Z"), Asn1Encoding.Der),
            Asn1TimeForm.Utc);
        Assert.Equal(1950, y50.Year);

        var berOffset = WrapTime(Asn1Tag.UtcTime, "1701020304+0500");
        var decoded = Asn1Time.Decode(new Asn1Reader(berOffset, Asn1Encoding.Ber), Asn1TimeForm.Utc);
        Assert.Equal(new DateTimeOffset(2017, 1, 1, 22, 4, 0, TimeSpan.Zero), decoded);

        Assert.Throws<Asn1Exception>(() =>
            Asn1Time.Decode(new Asn1Reader(berOffset, Asn1Encoding.Der), Asn1TimeForm.Utc));

        var noSeconds = WrapTime(Asn1Tag.GeneralizedTime, "201701020304Z");
        Assert.Throws<Asn1Exception>(() =>
            Asn1Time.Decode(new Asn1Reader(noSeconds, Asn1Encoding.Der), Asn1TimeForm.Generalized));
        Assert.Equal(
            new DateTimeOffset(2017, 1, 2, 3, 4, 0, TimeSpan.Zero),
            Asn1Time.Decode(new Asn1Reader(noSeconds, Asn1Encoding.Ber), Asn1TimeForm.Generalized));
    }

    private static void RoundTripString(Asn1StringForm form, string value, Asn1Tag tag)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1String.Encode(writer, value, form, tag);
        var bytes = writer.Encode();
        var again = new Asn1Writer(Asn1Encoding.Der);
        Asn1String.Encode(again, Asn1String.Decode(new Asn1Reader(bytes, Asn1Encoding.Der), form, tag), form, tag);
        Assert.Equal(bytes, again.Encode());
        Assert.Equal(value, Asn1String.Decode(new Asn1Reader(bytes, Asn1Encoding.Der), form, tag));
    }

    private static byte[] WrapTime(Asn1Tag tag, string text)
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteString(tag, text, Asn1StringForm.Visible);
        return writer.Encode();
    }

    [Fact]
    public void ReadLazy_DefersDecode_UntilValueAccess()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSequence(Asn1Tag.Sequence, inner =>
        {
            Asn1Integer.Encode(inner, 7);
            Asn1Integer.Encode(inner, 9);
        });
        var bytes = writer.Encode();

        var decodedCalls = 0;
        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var lazy = reader.ReadLazy(r =>
        {
            decodedCalls++;
            return r.ReadSequence(Asn1Tag.Sequence, inner =>
            {
                var a = Asn1Integer.Decode(inner).GetInt32();
                var b = Asn1Integer.Decode(inner).GetInt32();
                return (a, b);
            });
        });

        Assert.True(reader.Eof);
        Assert.Equal(0, decodedCalls);
        Assert.False(lazy.IsMaterialized);
        Assert.True(lazy.HasEncoded);
        AssertSameArray(bytes, lazy.EncodedMemory, expectedOffset: 0);

        Assert.Equal((7, 9), lazy.Value);
        Assert.Equal(1, decodedCalls);
        Assert.True(lazy.IsMaterialized);
        Assert.Equal((7, 9), lazy.Value);
        Assert.Equal(1, decodedCalls);
    }

    [Fact]
    public void ReadLazy_CorruptTlv_FailsOnlyOnValue()
    {
        // SEQUENCE with truncated INTEGER contents after a valid outer TLV header is hard;
        // use a complete TLV whose decoder rejects the payload.
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteOctetString(Asn1Tag.OctetString, new byte[] { 0x01 });
        var bytes = writer.Encode();

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var lazy = reader.ReadLazy(r =>
        {
            _ = r.ReadBoolean(Asn1Tag.Boolean);
            return true;
        });

        Assert.True(reader.Eof);
        Assert.Throws<Asn1Exception>(() => _ = lazy.Value);
    }

    [Fact]
    public void Lazy_WriteTo_PreservesEncodedBytes()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1Integer.Encode(writer, 42);
        var bytes = writer.Encode();

        var lazy = new Asn1Reader(bytes, Asn1Encoding.Der).ReadLazy(r => Asn1Integer.Decode(r).GetInt32());
        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        lazy.WriteTo(rewrite);
        Assert.Equal(bytes, rewrite.Encode());
        Assert.Equal(42, lazy.Value);
    }

    [Fact]
    public void Lazy_FromValue_HasNoEncoded_WriteToThrows()
    {
        var lazy = Asn1Lazy<int>.FromValue(3);
        Assert.True(lazy.IsMaterialized);
        Assert.False(lazy.HasEncoded);
        Assert.Equal(3, lazy.Value);
        Assert.Throws<InvalidOperationException>(() => lazy.WriteTo(new Asn1Writer(Asn1Encoding.Der)));
        Assert.Throws<InvalidOperationException>(() => _ = lazy.EncodedMemory);
    }

    [Fact]
    public void Lazy_Clone_DetachesFromSourceBuffer()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1Integer.Encode(writer, 5);
        var bytes = writer.Encode();
        var lazy = new Asn1Reader(bytes, Asn1Encoding.Der).ReadLazy(r => Asn1Integer.Decode(r).GetInt32());
        var clone = lazy.Clone();
        Assert.Equal(bytes, clone.EncodedMemory.ToArray());
        Assert.True(MemoryMarshal.TryGetArray(clone.EncodedMemory, out ArraySegment<byte> segment));
        Assert.NotSame(bytes, segment.Array);
        Assert.Equal(5, clone.Value);
    }

    [Fact]
    public void Retained_Decode_KeepsEncoded_UntilValueReplaced()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        Asn1Integer.Encode(writer, 42);
        var bytes = writer.Encode();

        var retained = new Asn1Reader(bytes, Asn1Encoding.Der).ReadRetained(r => Asn1Integer.Decode(r));
        Assert.True(retained.HasEncoded);
        Assert.Equal(42, retained.Value.GetInt32());

        var rewrite = new Asn1Writer(Asn1Encoding.Der);
        retained.WriteTo(rewrite);
        Assert.Equal(bytes, rewrite.Encode());

        retained.Value = Asn1Integer.FromInt32(7);
        Assert.False(retained.HasEncoded);
        Assert.Throws<InvalidOperationException>(() => retained.WriteTo(new Asn1Writer(Asn1Encoding.Der)));
    }

    [Fact]
    public void Writer_EncodeCallback_SeesInternalBuffer_WithoutCopy()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteInteger(Asn1Tag.Integer, 1);
        var viaCallback = writer.Encode(span => span.ToArray());
        Assert.Equal(writer.Encode(), viaCallback);
    }

    [Fact]
    public void Writer_EnsureCapacity_GrowsBuffer()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.EnsureCapacity(4096);
        writer.WriteInteger(Asn1Tag.Integer, 1);
        Assert.True(writer.EncodedLength > 0);
    }

    [Fact]
    public void Oid_RoundTripsContents_WithoutStringOnHotPath()
    {
        var oid = Asn1Oid.Parse("1.2.840.113549.1.1.11");
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteObjectIdentifier(Asn1Tag.ObjectIdentifier, oid);
        var bytes = writer.Encode();

        var decoded = new Asn1Reader(bytes, Asn1Encoding.Der).ReadOid(Asn1Tag.ObjectIdentifier);
        Assert.Equal(oid, decoded);
        Assert.Equal("1.2.840.113549.1.1.11", decoded.ToString());
        Assert.Equal("1.2.840.113549.1.1.11", new Asn1Reader(bytes, Asn1Encoding.Der).ReadObjectIdentifier(Asn1Tag.ObjectIdentifier));
    }

    [Fact]
    public void Reader_NestedSequence_DoesNotRequireSeparateBufferOwner()
    {
        var writer = new Asn1Writer(Asn1Encoding.Der);
        writer.WriteSequence(Asn1Tag.Sequence, inner =>
        {
            inner.WriteInteger(Asn1Tag.Integer, 1);
            inner.WriteSequence(Asn1Tag.Sequence, nested => nested.WriteInteger(Asn1Tag.Integer, 2));
        });
        var bytes = writer.Encode();

        var reader = new Asn1Reader(bytes, Asn1Encoding.Der);
        var sum = reader.ReadSequence(Asn1Tag.Sequence, outer =>
        {
            var a = outer.ReadInt32(Asn1Tag.Integer);
            var b = outer.ReadSequence(Asn1Tag.Sequence, nested => nested.ReadInt32(Asn1Tag.Integer));
            return a + b;
        });
        Assert.Equal(3, sum);
        Assert.True(reader.Eof);
    }
}
