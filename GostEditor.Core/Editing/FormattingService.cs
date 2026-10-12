using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Editing.Operations;

namespace GostEditor.Core.Editing;

public sealed class FormattingService
{
    private readonly DocumentEditingSession _session;

    internal FormattingService(DocumentEditingSession session)
    {
        _session = session;
    }

    public void SetBold(DocumentRange range, bool value) =>
        ApplyTextStyle(range, style => style.IsBold = value);

    public void SetItalic(DocumentRange range, bool value) =>
        ApplyTextStyle(range, style => style.IsItalic = value);

    public void ToggleBold(DocumentRange range) =>
        ApplyTextStyle(range, style => style.IsBold = !style.IsBold);

    public void ToggleItalic(DocumentRange range) =>
        ApplyTextStyle(range, style => style.IsItalic = !style.IsItalic);

    public void SetFontSize(DocumentRange range, double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        ApplyTextStyle(range, style => style.FontSize = value);
    }

    public void SetFontFamily(DocumentRange range, string? value)
    {
        if (_session.RequiresLegacyLosslessProjection &&
            !string.IsNullOrWhiteSpace(value))
        {
            throw new NotSupportedException(
                "FontFamily нельзя без потерь сохранить в текущую " +
                "модель .gost v2.");
        }

        ApplyTextStyle(
            range,
            style => style.FontFamily = string.IsNullOrWhiteSpace(value)
                ? null
                : value);
    }

    public void SetTextColor(DocumentRange range, uint value) =>
        ApplyTextStyle(range, style => style.Color = value);

    public void ChangeTypingStyle(
        DocumentLocation location,
        Action<TextStyle> change)
    {
        DocumentLocation normalized = _session.Selection.NormalizeLocation(
            location);
        ParagraphBlock paragraph = (ParagraphBlock)
            _session.Document.FindBlock(normalized.BlockId)!;
        TextStyle before = ChangeTypingStyleOperation.ResolveStyle(
            paragraph,
            normalized.Offset);
        TextStyle after = before.Clone();
        change(after);
        if (before.Equals(after))
        {
            return;
        }

        ChangeTypingStyleOperation operation = new(
            normalized.BlockId,
            normalized.Offset,
            change);
        _session.ExecuteWithSelection(
            operation,
            () => _session.Selection.MoveCaret(normalized));
    }

    public void ClearTextFormatting(DocumentRange range) =>
        ApplyTextStyle(
            range,
            style =>
            {
                style.IsBold = false;
                style.IsItalic = false;
                style.FontSize = 14;
                style.Color = 0xFF000000;
                style.FontFamily = null;
            });

    public void SetParagraphProperties(
        DocumentNodeId paragraphId,
        ParagraphProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ParagraphBlock paragraph = _session.Document.FindBlock(paragraphId)
            as ParagraphBlock
            ?? throw new InvalidOperationException(
                $"Абзац {paragraphId} не найден.");
        if (!PropertiesEqual(paragraph.Properties, properties))
        {
            _session.Execute(new ChangeParagraphPropertiesOperation(
                paragraphId,
                properties));
        }
    }

    public void SetParagraphProperties(
        DocumentRange range,
        Func<ParagraphProperties, ParagraphProperties> change)
    {
        ApplyFormatting(range, null, change);
    }

    private static bool PropertiesEqual(
        ParagraphProperties left,
        ParagraphProperties right) =>
        left.FirstLineIndent.Equals(right.FirstLineIndent) &&
        left.LineSpacing.Equals(right.LineSpacing) &&
        left.Alignment == right.Alignment &&
        left.Style == right.Style &&
        left.PageBreakBefore == right.PageBreakBefore;

    internal void ApplyTextStyle(
        DocumentRange range,
        Action<TextStyle> change) =>
        ApplyFormatting(range, change, null);

    internal void ApplyFormatting(
        DocumentRange range,
        Action<TextStyle>? textChange,
        Func<ParagraphProperties, ParagraphProperties>? paragraphChange,
        bool clearSelection = false)
    {
        if (textChange is null && paragraphChange is null)
        {
            return;
        }

        DocumentRange normalized = NormalizeRange(range);
        int startIndex = _session.Document.GetBlockIndex(
            normalized.Start.BlockId);
        int endIndex = _session.Document.GetBlockIndex(
            normalized.End.BlockId);
        IReadOnlyList<BlockNode> blocks = _session.Document
            .EnumerateBlocks()
            .ToList();

        EditTransaction transaction = new("Форматирование документа");
        for (int index = startIndex; index <= endIndex; index++)
        {
            if (blocks[index] is not ParagraphBlock paragraph)
            {
                continue;
            }

            if (paragraphChange is not null)
            {
                ParagraphProperties changed = paragraphChange(
                    paragraph.Properties.Clone())
                    ?? throw new InvalidOperationException(
                        "Функция форматирования вернула null.");
                if (!PropertiesEqual(paragraph.Properties, changed))
                {
                    transaction.Add(new ChangeParagraphPropertiesOperation(
                        paragraph.Id,
                        changed));
                }
            }

            if (textChange is not null)
            {
                int start = index == startIndex
                    ? normalized.Start.Offset
                    : 0;
                int end = index == endIndex
                    ? normalized.End.Offset
                    : paragraph.TextLength;
                int length = Math.Max(0, end - start);
                if (length > 0 && WouldChangeTextStyle(
                        paragraph,
                        start,
                        length,
                        textChange))
                {
                    transaction.Add(new ChangeTextStyleOperation(
                        paragraph.Id,
                        start,
                        length,
                        textChange));
                }
            }
        }

        if (transaction.IsEmpty)
        {
            if (clearSelection)
            {
                _session.Selection.ClearSelection();
            }
            return;
        }

        if (clearSelection)
        {
            _session.ExecuteWithSelection(
                transaction,
                _session.Selection.ClearSelection);
        }
        else
        {
            _session.Execute(transaction);
        }
    }

    private static bool WouldChangeTextStyle(
        ParagraphBlock paragraph,
        int start,
        int length,
        Action<TextStyle> change)
    {
        int end = checked(start + length);
        int offset = 0;
        foreach (InlineNode inline in paragraph.Inlines)
        {
            int inlineEnd = offset + inline.TextLength;
            if (inlineEnd > start && offset < end)
            {
                TextStyle? style = inline switch
                {
                    TextInline text => text.Style,
                    LinkInline link => link.Style,
                    _ => null
                };
                if (style is not null)
                {
                    TextStyle changed = style.Clone();
                    change(changed);
                    if (!style.Equals(changed))
                    {
                        return true;
                    }
                }
            }

            offset = inlineEnd;
        }

        return false;
    }

    private DocumentRange NormalizeRange(DocumentRange range)
    {
        DocumentLocation start = _session.Selection.NormalizeLocation(
            range.Start);
        DocumentLocation end = _session.Selection.NormalizeLocation(
            range.End);
        int startIndex = _session.Document.GetBlockIndex(start.BlockId);
        int endIndex = _session.Document.GetBlockIndex(end.BlockId);

        if (startIndex < endIndex ||
            startIndex == endIndex && start.Offset <= end.Offset)
        {
            return new DocumentRange(start, end);
        }

        return new DocumentRange(end, start);
    }
}
