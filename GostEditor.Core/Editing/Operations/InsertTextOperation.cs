using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class InsertTextOperation : IEditOperation
{
    private readonly DocumentNodeId _paragraphId;
    private readonly int _offset;
    private readonly string _text;
    private readonly TextStyle? _style;
    private List<InlineNode>? _before;

    public InsertTextOperation(
        DocumentNodeId paragraphId,
        int offset,
        string text,
        TextStyle? style = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException(
                "Вставляемый текст не может быть пустым.",
                nameof(text));
        }

        _paragraphId = paragraphId;
        _offset = offset;
        _text = text;
        _style = style?.Clone();
    }

    public string Description => "Вставка текста";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _paragraphId };

    public void Apply(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        _before ??= ParagraphTextBuffer.CloneInlines(paragraph.Inlines);
        ParagraphTextBuffer.Insert(paragraph, _offset, _text, _style);
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
