namespace Asn1Kit.Runtime;

/// <summary>
/// Allocation-free constructed-value scope returned by <see cref="Asn1Writer.EnterSequence"/>,
/// <see cref="Asn1Writer.EnterSet"/>, <see cref="Asn1Writer.EnterSequenceOf"/>,
/// <see cref="Asn1Writer.EnterSetOf"/>, and <see cref="Asn1Writer.EnterExplicit"/>.
/// </summary>
public ref struct Asn1WriterScope
{
    private Asn1Writer? _writer;
    private Asn1EncodeFrame _frame;
    private long _scopeToken;
    private long _parentScopeToken;
    private bool _sortDerSetOf;
    private bool _active;

    private Asn1WriterScope(
        Asn1Writer writer,
        Asn1EncodeFrame frame,
        long scopeToken,
        long parentScopeToken,
        bool sortDerSetOf)
    {
        _writer = writer;
        _frame = frame;
        _scopeToken = scopeToken;
        _parentScopeToken = parentScopeToken;
        _sortDerSetOf = sortDerSetOf;
        _active = true;
    }

    internal static Asn1WriterScope Create(
        Asn1Writer writer,
        Asn1EncodeFrame frame,
        long scopeToken,
        long parentScopeToken,
        bool sortDerSetOf) =>
        new(writer, frame, scopeToken, parentScopeToken, sortDerSetOf);

    /// <summary>Finalizes the constructed value.</summary>
    public void Dispose()
    {
        if (!_active)
        {
            throw new InvalidOperationException("ASN.1 writer scopes must be disposed once in LIFO order.");
        }

        _writer!.EndScope(_frame, _scopeToken, _parentScopeToken, _sortDerSetOf);
        _active = false;
        _writer = null;
    }
}
