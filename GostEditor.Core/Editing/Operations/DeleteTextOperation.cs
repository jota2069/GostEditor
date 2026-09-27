using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class DeleteTextOperation : IEditOperation
{
    private readonly DocumentNodeId _paragraphId;
    private readonly int _offset;
    private readonly int _length;
    private List<InlineNode>? _before;

    public DeleteTextOperation(
        DocumentNodeId paragraphId,
        int offset,
        int length)
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
    }

    public string Description => "Удаление текста";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _paragraphId };

    public void Apply(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        _before ??= ParagraphTextBuffer.CloneInlines(paragraph.Inlines);
        ParagraphTextBuffer.Delete(paragraph, _offset, _length);
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
