namespace Asn1Kit.Runtime;

/// <summary>
/// Allocation-free nested decode scope returned by <see cref="Asn1Reader.EnterSequence"/>,
/// <see cref="Asn1Reader.EnterSet"/>, and <see cref="Asn1Reader.EnterExplicit"/>.
/// </summary>
public ref struct Asn1ReaderScope
{
    private Asn1Reader? _reader;
    private Asn1DecodeCursor _savedCursor;
    private Asn1ReaderScopeToken _expectedScopeToken;
    private bool _active;

    private Asn1ReaderScope(
        Asn1Reader reader,
        Asn1DecodeCursor savedCursor,
        Asn1ReaderScopeToken expectedScopeToken)
    {
        _reader = reader;
        _savedCursor = savedCursor;
        _expectedScopeToken = expectedScopeToken;
        _active = true;
    }

    internal static Asn1ReaderScope Create(
        Asn1Reader reader,
        Asn1DecodeCursor savedCursor,
        Asn1ReaderScopeToken expectedScopeToken) =>
        new(reader, savedCursor, expectedScopeToken);

    /// <summary>Provides the <c>Dispose</c> operation.</summary>
    public void Dispose()
    {
        if (!_active)
        {
            return;
        }

        _reader!.PopContentsWindow(_savedCursor, _expectedScopeToken);
        _active = false;
        _reader = null;
    }
}
