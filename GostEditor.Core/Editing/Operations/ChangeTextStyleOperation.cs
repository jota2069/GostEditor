using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class ChangeTextStyleOperation : IEditOperation, IHistoryPayloadSize
{
    private readonly DocumentNodeId _paragraphId;
    private readonly int _offset;
    private readonly int _length;
    private readonly Action<TextStyle> _change;
    private readonly Func<DocumentNodeId>? _createId;
    private List<InlineNode>? _before;
    private List<InlineNode>? _after;

    public ChangeTextStyleOperation(
        DocumentNodeId paragraphId,
        int offset,
        int length,
        Action<TextStyle> change)
        : this(paragraphId, offset, length, change, null)
    {
    }

    internal ChangeTextStyleOperation(
        DocumentNodeId paragraphId,
        int offset,
        int length,
        Action<TextStyle> change,
        Func<DocumentNodeId>? createId)
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
        _createId = createId;
    }

    public string Description => "Форматирование текста";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _paragraphId };

    public DocumentIndexUpdateKind IndexUpdateKind =>
        DocumentIndexUpdateKind.AffectedParagraphs;

    public long EstimatedPayloadBytes => 256L +
        HistoryPayloadSizer.Inlines(_before) +
        HistoryPayloadSizer.Inlines(_after);

    public void Apply(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        if (_after is not null)
        {
            ParagraphTextBuffer.Restore(paragraph, _after);
            Invalidate(context);
            return;
        }

        _before ??= ParagraphTextBuffer.CloneInlines(paragraph.Inlines);
        ParagraphTextBuffer.ApplyStyle(
            paragraph,
            _offset,
            _length,
            _change,
            _createId);
        _after = ParagraphTextBuffer.CloneInlines(paragraph.Inlines);
        Invalidate(context);
    }

    public void Revert(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        ParagraphTextBuffer.Restore(
            paragraph,
            _before ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена."));
        Invalidate(context);
    }

    private void Invalidate(DocumentEditingContext context) =>
        context.InvalidationTracker.Invalidate(
            _paragraphId,
            LayoutInvalidationKind.Metrics | LayoutInvalidationKind.Paint);
}
