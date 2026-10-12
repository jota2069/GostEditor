using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

internal sealed class InlineTextDeltaOperation :
    IEditOperation,
    IHistoryCoalescingOperation,
    IHistoryPayloadSize
{
    private readonly DocumentNodeId _paragraphId;
    private readonly DocumentNodeId _inlineId;
    private readonly int _inlineOffset;
    private readonly string _removedText;
    private readonly string _insertedText;

    private InlineTextDeltaOperation(
        DocumentNodeId paragraphId,
        DocumentNodeId inlineId,
        int paragraphOffset,
        int inlineOffset,
        string removedText,
        string insertedText,
        TextCoalescingKind kind,
        int styleHash)
    {
        _paragraphId = paragraphId;
        _inlineId = inlineId;
        _inlineOffset = inlineOffset;
        _removedText = removedText;
        _insertedText = insertedText;
        CoalescingDescriptor = new HistoryCoalescingDescriptor(
            kind,
            paragraphId,
            paragraphOffset,
            Math.Max(removedText.Length, insertedText.Length),
            styleHash);
    }

    public string Description => _removedText.Length == 0
        ? "Вставка текста"
        : "Удаление текста";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _paragraphId };

    public DocumentIndexUpdateKind IndexUpdateKind =>
        DocumentIndexUpdateKind.AffectedParagraphs;

    public HistoryCoalescingDescriptor CoalescingDescriptor { get; }

    public long EstimatedPayloadBytes =>
        160L + 2L * (_removedText.Length + _insertedText.Length);

    public static InlineTextDeltaOperation? TryCreateInsert(
        ParagraphBlock paragraph,
        int offset,
        string text,
        TextStyle style,
        TextCoalescingKind kind)
    {
        if (!TryFindTextInline(paragraph, offset, 0, out TextInline? inline,
                out int inlineOffset) || inline is null ||
            !inline.Style.Equals(style))
        {
            return null;
        }

        return new InlineTextDeltaOperation(
            paragraph.Id,
            inline.Id,
            offset,
            inlineOffset,
            string.Empty,
            text,
            kind,
            style.GetHashCode());
    }

    public static InlineTextDeltaOperation? TryCreateDelete(
        ParagraphBlock paragraph,
        int offset,
        int length,
        TextCoalescingKind kind)
    {
        if (!TryFindTextInline(paragraph, offset, length,
                out TextInline? inline, out int inlineOffset) ||
            inline is null)
        {
            return null;
        }

        string removed = inline.Text.Substring(inlineOffset, length);
        return new InlineTextDeltaOperation(
            paragraph.Id,
            inline.Id,
            offset,
            inlineOffset,
            removed,
            string.Empty,
            kind,
            inline.Style.GetHashCode());
    }

    public void Apply(DocumentEditingContext context)
    {
        TextInline inline = GetRequiredInline(context);
        ReplaceExact(inline, _removedText, _insertedText);
        Invalidate(context);
    }

    public void Revert(DocumentEditingContext context)
    {
        TextInline inline = GetRequiredInline(context);
        ReplaceExact(inline, _insertedText, _removedText);
        Invalidate(context);
    }

    private TextInline GetRequiredInline(DocumentEditingContext context)
    {
        _ = context.GetRequiredParagraph(_paragraphId);
        return context.Document.FindNode(_inlineId) as TextInline
            ?? throw new InvalidOperationException(
                $"Text inline {_inlineId} не найден в абзаце {_paragraphId}.");
    }

    private void ReplaceExact(
        TextInline inline,
        string expected,
        string replacement)
    {
        if (_inlineOffset < 0 ||
            _inlineOffset + expected.Length > inline.Text.Length ||
            !inline.Text.AsSpan(_inlineOffset, expected.Length)
                .SequenceEqual(expected.AsSpan()))
        {
            throw new InvalidOperationException(
                "Text delta больше не соответствует canonical inline.");
        }

        inline.Text = string.Concat(
            inline.Text.AsSpan(0, _inlineOffset),
            replacement,
            inline.Text.AsSpan(_inlineOffset + expected.Length));
    }

    private void Invalidate(DocumentEditingContext context) =>
        context.InvalidationTracker.Invalidate(
            _paragraphId,
            LayoutInvalidationKind.Metrics | LayoutInvalidationKind.Paint);

    private static bool TryFindTextInline(
        ParagraphBlock paragraph,
        int offset,
        int length,
        out TextInline? result,
        out int inlineOffset)
    {
        result = null;
        inlineOffset = 0;
        if (offset < 0 || length < 0 || offset + length > paragraph.TextLength)
        {
            return false;
        }

        int current = 0;
        foreach (InlineNode node in paragraph.Inlines)
        {
            int end = current + node.TextLength;
            if (node is TextInline text && offset >= current &&
                offset + length <= end &&
                (length > 0 || offset < end || text.Text.Length == 0 ||
                 end == paragraph.TextLength))
            {
                result = text;
                inlineOffset = offset - current;
                return true;
            }

            current = end;
        }

        return false;
    }
}
