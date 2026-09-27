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
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        DocumentLocation normalized =
            _session.Selection.NormalizeLocation(location);
        InsertTextOperation operation = new(
            normalized.BlockId,
            normalized.Offset,
            text,
            style);
        _session.ExecuteWithSelection(
            operation,
            () => _session.Selection.MoveCaret(new DocumentLocation(
                normalized.BlockId,
                normalized.Offset + text.Length)));
    }

    public void DeleteText(
        DocumentLocation location,
        int length)
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

        DeleteTextOperation operation = new(
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
}
