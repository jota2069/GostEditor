using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class ChangeTextStyleOperation : IEditOperation
{
    private readonly DocumentNodeId _paragraphId;
    private readonly int _offset;
    private readonly int _length;
    private readonly Action<TextStyle> _change;
    private List<InlineNode>? _before;

    public ChangeTextStyleOperation(
        DocumentNodeId paragraphId,
        int offset,
        int length,
        Action<TextStyle> change)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _paragraphId = paragraphId;
        _offset = offset;
        _length = length;
        _change = change
            ?? throw new ArgumentNullException(nameof(change));
    }

    public string Description => "Форматирование текста";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _paragraphId };

    public void Apply(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        _before ??= ParagraphTextBuffer.CloneInlines(paragraph.Inlines);
        ParagraphTextBuffer.ApplyStyle(
            paragraph,
            _offset,
            _length,
            _change);
        context.InvalidationTracker.Invalidate(
            _paragraphId,
            LayoutInvalidationKind.Metrics | LayoutInvalidationKind.Paint);
    }

    public void Revert(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        ParagraphTextBuffer.Restore(
            paragraph,
            _before ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена."));
        context.InvalidationTracker.Invalidate(
            _paragraphId,
            LayoutInvalidationKind.Metrics | LayoutInvalidationKind.Paint);
    }
}
