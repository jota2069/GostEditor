using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Editing.Operations;

namespace GostEditor.Core.Editing;

public sealed class TextEditingService
{
    private readonly DocumentEditingSession _session;

    internal TextEditingService(DocumentEditingSession session)
    {
        _session = session;
    }

    public void InsertText(
        DocumentLocation location,
        string text,
        TextStyle? style = null)
        => InsertText(location, text, style, TextCoalescingKind.None);

    internal void InsertTyping(
        DocumentLocation location,
        string text,
        TextStyle? style = null)
        => InsertText(location, text, style, TextCoalescingKind.Typing);

    private void InsertText(
        DocumentLocation location,
        string text,
        TextStyle? style,
        TextCoalescingKind kind)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        DocumentLocation normalized =
            _session.Selection.NormalizeLocation(location);
        ParagraphBlock paragraph = (ParagraphBlock)_session.Document.FindBlock(
            normalized.BlockId)!;
        TextStyle effectiveStyle = style?.Clone() ??
            ParagraphTextBuffer.GetStyleAt(paragraph, normalized.Offset);
        IEditOperation operation = (IEditOperation?)
            InlineTextDeltaOperation.TryCreateInsert(
                paragraph,
                normalized.Offset,
                text,
                effectiveStyle,
                kind) ??
            new InsertTextOperation(
                normalized.BlockId,
                normalized.Offset,
                text,
                effectiveStyle);
        _session.ExecuteWithSelection(
            operation,
            () => _session.Selection.MoveCaret(new DocumentLocation(
                normalized.BlockId,
                normalized.Offset + text.Length)));
    }

    public void DeleteText(
        DocumentLocation location,
        int length)
        => DeleteText(location, length, TextCoalescingKind.None);

    internal void DeleteBackward(DocumentLocation location, int length) =>
        DeleteText(location, length, TextCoalescingKind.Backspace);

    internal void DeleteForward(DocumentLocation location, int length) =>
        DeleteText(location, length, TextCoalescingKind.DeleteForward);

    private void DeleteText(
        DocumentLocation location,
        int length,
        TextCoalescingKind kind)
    {
        if (length <= 0)
        {
            return;
        }

        DocumentLocation normalized =
            _session.Selection.NormalizeLocation(location);
        ParagraphBlock paragraph = (ParagraphBlock)_session.Document.FindBlock(
            normalized.BlockId)!;
        int actualLength = Math.Min(
            length,
            paragraph.TextLength - normalized.Offset);
        if (actualLength <= 0)
        {
            return;
        }

        IEditOperation operation = (IEditOperation?)
            InlineTextDeltaOperation.TryCreateDelete(
                paragraph,
                normalized.Offset,
                actualLength,
                kind) ??
            new DeleteTextOperation(
                normalized.BlockId,
                normalized.Offset,
                actualLength);
        _session.ExecuteWithSelection(
            operation,
            () => _session.Selection.MoveCaret(normalized));
    }

    public void ReplaceRange(
        DocumentRange range,
        string replacement,
        TextStyle? style = null)
    {
        ReplaceTextRangeOperation operation = new(
            _session.Document,
            range,
            replacement ?? string.Empty,
            style);
        if (range.IsCollapsed && string.IsNullOrEmpty(replacement))
        {
            return;
        }

        _session.ExecuteWithSelection(
            operation,
            () => _session.Selection.MoveCaret(operation.ResultLocation));
    }

    public void ReplaceSelection(
        string replacement,
        TextStyle? style = null) =>
        ReplaceRange(
            _session.Selection.GetRange(),
            replacement,
            style);

    public void DeleteSelection()
    {
        if (!_session.Selection.HasSelection)
        {
            return;
        }

        ReplaceSelection(string.Empty);
    }

    public void InsertParagraphBreak(DocumentLocation location)
    {
        DocumentLocation normalized =
            _session.Selection.NormalizeLocation(location);
        SplitParagraphOperation operation = new(
            normalized.BlockId,
            normalized.Offset);
        _session.ExecuteWithSelection(
            operation,
            () => _session.Selection.MoveCaret(new DocumentLocation(
                operation.NewParagraphId,
                0)));
    }

    public void InsertParagraphBreakAtCaret()
    {
        if (_session.Selection.HasSelection)
        {
            DeleteSelection();
        }

        InsertParagraphBreak(_session.Selection.Caret);
    }

    public void ReplaceSelectionWithParagraphs(
        IReadOnlyList<string> paragraphs,
        TextStyle? style = null,
        Func<ParagraphProperties, ParagraphProperties>?
            finalParagraphChange = null)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        if (paragraphs.Count == 0)
        {
            return;
        }

        if (paragraphs.Count == 1)
        {
            ReplaceSelection(paragraphs[0], style);
            return;
        }

        DocumentRange range = _session.Selection.GetRange();
        ParagraphProperties sourceProperties = ((ParagraphBlock)
            _session.Document.FindBlock(range.Start.BlockId)!)
            .Properties
            .Clone();
        ReplaceTextRangeOperation replace = new(
            _session.Document,
            range,
            paragraphs[0],
            style);
        EditTransaction transaction = new("Вставка многострочного текста");
        transaction.Add(replace);

        DocumentNodeId currentParagraphId = replace.ResultLocation.BlockId;
        int currentOffset = replace.ResultLocation.Offset;
        for (int index = 1; index < paragraphs.Count; index++)
        {
            DocumentNodeId newParagraphId = DocumentNodeId.New();
            transaction.Add(new SplitParagraphOperation(
                currentParagraphId,
                currentOffset,
                newParagraphId));
            if (paragraphs[index].Length > 0)
            {
                transaction.Add(new InsertTextOperation(
                    newParagraphId,
                    0,
                    paragraphs[index],
                    style));
            }

            currentParagraphId = newParagraphId;
            currentOffset = paragraphs[index].Length;
        }

        if (finalParagraphChange is not null)
        {
            ParagraphProperties changed = finalParagraphChange(
                sourceProperties.Clone())
                ?? throw new InvalidOperationException(
                    "Функция форматирования вернула null.");
            transaction.Add(new ChangeParagraphPropertiesOperation(
                currentParagraphId,
                changed));
        }

        DocumentLocation result = new(currentParagraphId, currentOffset);
        _session.ExecuteWithSelection(
            transaction,
            () => _session.Selection.MoveCaret(result));
    }

    public void MergeParagraphs(
        DocumentNodeId firstParagraphId,
        DocumentNodeId secondParagraphId,
        int resultOffset)
    {
        MergeParagraphsOperation operation = new(
            firstParagraphId,
            secondParagraphId);
        _session.ExecuteWithSelection(
            operation,
            () => _session.Selection.MoveCaret(new DocumentLocation(
                firstParagraphId,
                resultOffset)));
    }
}
