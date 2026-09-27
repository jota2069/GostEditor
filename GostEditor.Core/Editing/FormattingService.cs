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

    public void SetFontSize(DocumentRange range, double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        ApplyTextStyle(range, style => style.FontSize = value);
    }

    public void SetFontFamily(DocumentRange range, string? value) =>
        ApplyTextStyle(
            range,
            style => style.FontFamily = string.IsNullOrWhiteSpace(value)
                ? null
                : value);

    public void SetTextColor(DocumentRange range, uint value) =>
        ApplyTextStyle(range, style => style.Color = value);

    public void SetParagraphProperties(
        DocumentNodeId paragraphId,
        ParagraphProperties properties) =>
        _session.Execute(new ChangeParagraphPropertiesOperation(
            paragraphId,
            properties));

    public void SetParagraphProperties(
        DocumentRange range,
        Func<ParagraphProperties, ParagraphProperties> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        DocumentRange normalized = NormalizeRange(range);
        int startIndex = _session.Document.GetBlockIndex(
            normalized.Start.BlockId);
        int endIndex = _session.Document.GetBlockIndex(
            normalized.End.BlockId);

        EditTransaction transaction = new("Форматирование абзацев");
        IReadOnlyList<BlockNode> blocks = _session.Document
            .EnumerateBlocks()
            .ToList();
        for (int index = startIndex; index <= endIndex; index++)
        {
            if (blocks[index] is not ParagraphBlock paragraph)
            {
                continue;
            }

            ParagraphProperties changed = change(
                paragraph.Properties.Clone())
                ?? throw new InvalidOperationException(
                    "Функция форматирования вернула null.");
            transaction.Add(new ChangeParagraphPropertiesOperation(
                paragraph.Id,
                changed));
        }

        _session.Execute(transaction);
    }

    private void ApplyTextStyle(
        DocumentRange range,
        Action<TextStyle> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        DocumentRange normalized = NormalizeRange(range);
        if (normalized.IsCollapsed)
        {
            return;
        }

        int startIndex = _session.Document.GetBlockIndex(
            normalized.Start.BlockId);
        int endIndex = _session.Document.GetBlockIndex(
            normalized.End.BlockId);
        IReadOnlyList<BlockNode> blocks = _session.Document
            .EnumerateBlocks()
            .ToList();

        EditTransaction transaction = new("Форматирование текста");
        for (int index = startIndex; index <= endIndex; index++)
        {
            if (blocks[index] is not ParagraphBlock paragraph)
            {
                continue;
            }

            int start = index == startIndex
                ? normalized.Start.Offset
                : 0;
            int end = index == endIndex
                ? normalized.End.Offset
                : paragraph.TextLength;
            int length = Math.Max(0, end - start);
            if (length == 0)
            {
                continue;
            }

            transaction.Add(new ChangeTextStyleOperation(
                paragraph.Id,
                start,
                length,
                change));
        }

        _session.Execute(transaction);
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
