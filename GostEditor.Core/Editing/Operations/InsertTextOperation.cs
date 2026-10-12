using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class InsertTextOperation : IEditOperation, IHistoryPayloadSize
{
    private readonly DocumentNodeId _paragraphId;
    private readonly int _offset;
    private readonly string _text;
    private readonly TextStyle? _style;
    private readonly Func<DocumentNodeId>? _createId;
    private List<InlineNode>? _before;
    private List<InlineNode>? _after;

    public InsertTextOperation(
        DocumentNodeId paragraphId,
        int offset,
        string text,
        TextStyle? style = null)
        : this(paragraphId, offset, text, style, null)
    {
    }

    internal InsertTextOperation(
        DocumentNodeId paragraphId,
        int offset,
        string text,
        TextStyle? style,
        Func<DocumentNodeId>? createId)
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
        _createId = createId;
    }

    public string Description => "Вставка текста";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _paragraphId };

    public DocumentIndexUpdateKind IndexUpdateKind =>
        DocumentIndexUpdateKind.AffectedParagraphs;

    public long EstimatedPayloadBytes => 192L +
        HistoryPayloadSizer.Strings(_text, _style?.FontFamily) +
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
        ParagraphTextBuffer.Insert(
            paragraph,
            _offset,
            _text,
            _style,
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
