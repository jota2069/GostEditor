using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

/// <summary>
/// Stores the insertion style at a collapsed caret as a zero-length canonical
/// inline marker. The marker is consumed naturally by the next text edit.
/// </summary>
internal sealed class ChangeTypingStyleOperation :
    IEditOperation,
    IHistoryPayloadSize
{
    private readonly DocumentNodeId _paragraphId;
    private readonly int _offset;
    private readonly Action<TextStyle> _change;
    private List<InlineNode>? _before;
    private List<InlineNode>? _after;

    public ChangeTypingStyleOperation(
        DocumentNodeId paragraphId,
        int offset,
        Action<TextStyle> change)
    {
        _paragraphId = paragraphId;
        _offset = offset;
        _change = change ?? throw new ArgumentNullException(nameof(change));
    }

    public string Description => "Стиль ввода";

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
            context.InvalidationTracker.Invalidate(
                _paragraphId,
                LayoutInvalidationKind.Metrics |
                LayoutInvalidationKind.Paint);
            return;
        }

        _before = ParagraphTextBuffer.CloneInlines(paragraph.Inlines);
        int offset = Math.Clamp(_offset, 0, paragraph.TextLength);
        TextStyle style = ResolveStyle(paragraph, offset);
        _change(style);

        List<InlineNode> changed = ParagraphTextBuffer.Slice(
            paragraph,
            0,
            offset);
        changed.Add(new TextInline(DocumentNodeId.New(), string.Empty, style));
        changed.AddRange(ParagraphTextBuffer.Slice(
            paragraph,
            offset,
            paragraph.TextLength - offset));
        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(changed);
        _after = ParagraphTextBuffer.CloneInlines(paragraph.Inlines);
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

    internal static TextStyle ResolveStyle(
        ParagraphBlock paragraph,
        int offset)
    {
        int current = 0;
        TextStyle? previous = null;
        foreach (InlineNode inline in paragraph.Inlines)
        {
            TextStyle? style = inline switch
            {
                TextInline text => text.Style,
                LinkInline link => link.Style,
                _ => null
            };
            int end = current + inline.TextLength;
            if (style is not null &&
                (offset < end || offset == current && inline.TextLength == 0))
            {
                return style.Clone();
            }

            previous = style ?? previous;
            current = end;
        }

        return previous?.Clone() ?? new TextStyle();
    }
}
