using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Layout.Incremental;

public sealed class LayoutInvalidationTracker
{
    private readonly DocumentRoot _document;
    private int _startBlockIndex = -1;
    private LayoutInvalidationKind _kind;

    public LayoutInvalidationTracker(DocumentRoot document)
    {
        _document = document
            ?? throw new ArgumentNullException(nameof(document));
    }

    public bool HasInvalidation => _startBlockIndex >= 0;

    public LayoutInvalidation Current => HasInvalidation
        ? new LayoutInvalidation(_startBlockIndex, _kind)
        : LayoutInvalidation.Empty;

    public void Invalidate(
        DocumentNodeId blockId,
        LayoutInvalidationKind kind)
    {
        int index = _document.GetBlockIndex(blockId);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Блок документа {blockId} не найден.");
        }

        InvalidateFromIndex(index, kind);
    }

    public void InvalidateStructure(int startBlockIndex) =>
        InvalidateFromIndex(
            Math.Max(0, startBlockIndex),
            LayoutInvalidationKind.Structure |
            LayoutInvalidationKind.Metrics |
            LayoutInvalidationKind.Paint);

    public LayoutInvalidation Consume()
    {
        LayoutInvalidation result = Current;
        Clear();
        return result;
    }

    public void Clear()
    {
        _startBlockIndex = -1;
        _kind = LayoutInvalidationKind.None;
    }

    private void InvalidateFromIndex(
        int index,
        LayoutInvalidationKind kind)
    {
        _startBlockIndex = _startBlockIndex < 0
            ? index
            : Math.Min(_startBlockIndex, index);
        _kind |= kind;
    }
}
