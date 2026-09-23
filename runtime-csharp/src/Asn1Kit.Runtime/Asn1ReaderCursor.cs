namespace Asn1Kit.Runtime;

/// <summary>
/// Nested decode window over an <see cref="Asn1Reader"/> buffer without allocating a nested reader.
/// Prefer <see cref="Asn1Reader.EnterSequence"/> / <see cref="Asn1Reader.EnterExplicit"/> which use the same push/pop.
/// </summary>
public ref struct Asn1ReaderCursor
{
    private Asn1Reader _reader;
    private int _savedStart;
    private int _savedOffset;
    private int _savedEnd;
    private bool _active;

    private Asn1ReaderCursor(Asn1Reader reader, int savedStart, int savedOffset, int savedEnd)
    {
        _reader = reader;
        _savedStart = savedStart;
        _savedOffset = savedOffset;
        _savedEnd = savedEnd;
        _active = true;
    }

    /// <summary>
    /// Restricts <paramref name="reader"/> to <paramref name="contents"/> when it aliases the reader buffer.
    /// Caller must <see cref="Dispose"/> (or use <c>using</c>) to restore the previous window.
    /// </summary>
    public static Asn1ReaderCursor Push(Asn1Reader reader, ReadOnlyMemory<byte> contents)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        return reader.PushContentsWindow(contents);
    }

    internal static Asn1ReaderCursor Create(Asn1Reader reader, int savedStart, int savedOffset, int savedEnd) =>
        new(reader, savedStart, savedOffset, savedEnd);

    public Asn1Reader Reader => _reader;

    public void Dispose()
    {
        if (!_active)
        {
            return;
        }

        _reader.PopContentsWindow(_savedStart, _savedOffset, _savedEnd);
        _active = false;
    }
}
