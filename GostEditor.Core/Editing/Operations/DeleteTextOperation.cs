using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class DeleteTextOperation : IEditOperation, IHistoryPayloadSize
{
    private readonly DocumentNodeId _paragraphId;
    private readonly int _offset;
    private readonly int _length;
    private readonly Func<DocumentNodeId>? _createId;
    private List<InlineNode>? _before;
    private List<InlineNode>? _after;

    public DeleteTextOperation(
        DocumentNodeId paragraphId,
        int offset,
        int length)
        : this(paragraphId, offset, length, null)
    {
    }

    internal DeleteTextOperation(
        DocumentNodeId paragraphId,
        int offset,
        int length,
        Func<DocumentNodeId>? createId)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "Длина удаления должна быть положительной.");
        }

        _paragraphId = paragraphId;
        _offset = offset;
        _length = length;
        _createId = createId;
    }

    public string Description => "Удаление текста";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _paragraphId };

    public DocumentIndexUpdateKind IndexUpdateKind =>
        DocumentIndexUpdateKind.AffectedParagraphs;

    public long EstimatedPayloadBytes => 192L +
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
        ParagraphTextBuffer.Delete(
            paragraph,
            _offset,
            _length,
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
