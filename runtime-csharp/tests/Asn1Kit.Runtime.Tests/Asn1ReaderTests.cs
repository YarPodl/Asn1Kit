using System.Buffers;
using Asn1Kit.Runtime;

namespace Asn1Kit.Tests;

public sealed class Asn1ReaderTests
{
    [Theory]
    [InlineData("04800000")] // primitive indefinite length
    [InlineData("1F1E00")] // high-tag form for tag number below 31
    [InlineData("1F801F00")] // leading zero high-tag group
    [InlineData("1F888080800000")] // tag number exceeds Int32
    [InlineData("048480000000")] // length exceeds Int32
    [InlineData("0000")] // EOC outside indefinite contents
    public void MalformedTlv_ThrowsAsn1Exception(string hex)
    {
        var reader = new Asn1Reader(Hex.Parse(hex), Asn1Encoding.Ber);
        Assert.Throws<Asn1Exception>(() => reader.ReadAny());
    }

    [Fact]
    public void TryPeekTag_MalformedTag_DoesNotAdvance()
    {
        var reader = new Asn1Reader(Hex.Parse("1F81"), Asn1Encoding.Ber);
        var remaining = reader.Remaining;

        Assert.Throws<Asn1Exception>(() => reader.TryPeekTag(out _));
        Assert.Equal(remaining, reader.Remaining);
        Assert.Throws<Asn1Exception>(() => reader.TryPeekTag(out _));
        Assert.Equal(remaining, reader.Remaining);
    }

    [Fact]
    public void EnterSequence_RejectsPrimitiveTag()
    {
        var reader = new Asn1Reader(Hex.Parse("1000"), Asn1Encoding.Ber);
        Assert.Throws<Asn1Exception>(() => reader.EnterSequence(Asn1Tag.Sequence).Dispose());
    }

    [Fact]
    public void ReaderScope_RejectsOutOfOrderDispose_AndCanRecover()
    {
        var reader = new Asn1Reader(Hex.Parse("30053003020101"), Asn1Encoding.Der);
        var outer = reader.EnterSequence(Asn1Tag.Sequence);
        var inner = reader.EnterSequence(Asn1Tag.Sequence);

        var threw = false;
        try
        {
            outer.Dispose();
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert.True(threw);
        Assert.Equal(1, reader.ReadInt32(Asn1Tag.Integer));
        inner.Dispose();
        outer.Dispose();
        Assert.True(reader.Eof);
    }

    [Fact]
    public void ReaderScope_CopyCannotRestoreTwice()
    {
        var reader = new Asn1Reader(Hex.Parse("3000"), Asn1Encoding.Der);
        var scope = reader.EnterSequence(Asn1Tag.Sequence);
        var copy = scope;

        scope.Dispose();
        scope.Dispose();

        var threw = false;
        try
        {
            copy.Dispose();
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert.True(threw);
        Assert.True(reader.Eof);
    }

    [Fact]
    public void ReaderScope_StaleCopyCannotCloseNextSibling()
    {
        var reader = new Asn1Reader(Hex.Parse("300430003000"), Asn1Encoding.Der);
        var outer = reader.EnterSequence(Asn1Tag.Sequence);
        var first = reader.EnterSequence(Asn1Tag.Sequence);
        var staleCopy = first;

        first.Dispose();
        var second = reader.EnterSequence(Asn1Tag.Sequence);

        var threw = false;
        try
        {
            staleCopy.Dispose();
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert.True(threw);
        second.Dispose();
        outer.Dispose();
        Assert.True(reader.Eof);
    }

    [Fact]
    public void ReadOnlyMemoryCtor_DoesNotCopyCustomMemory()
    {
        var bytes = Hex.Parse("04012A");
        using var manager = new TestMemoryManager(bytes);
        var reader = new Asn1Reader(manager.Memory, Asn1Encoding.Der);

        var contents = reader.ReadOctetString(Asn1Tag.OctetString);
        manager.GetSpan()[2] = 0x2B;

        Assert.Equal(0x2B, contents.Span[0]);
        Assert.True(reader.Eof);
    }

    [Fact]
    public void OffsetLengthCtor_ReportsInvalidParameter()
    {
        var bytes = Hex.Parse("020101");
        var offset = Assert.Throws<ArgumentOutOfRangeException>(
            () => new Asn1Reader(bytes, -1, 0, Asn1Encoding.Der));
        var length = Assert.Throws<ArgumentOutOfRangeException>(
            () => new Asn1Reader(bytes, 1, 3, Asn1Encoding.Der));

        Assert.Equal("offset", offset.ParamName);
        Assert.Equal("length", length.ParamName);
    }

    private sealed class TestMemoryManager : MemoryManager<byte>
    {
        private readonly byte[] _buffer;

        public TestMemoryManager(byte[] buffer)
        {
            _buffer = buffer;
        }

        public override Span<byte> GetSpan() => _buffer;

        public override MemoryHandle Pin(int elementIndex = 0) =>
            throw new NotSupportedException();

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
