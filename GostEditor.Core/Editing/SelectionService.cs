using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;

namespace GostEditor.Core.Editing;

public sealed class SelectionService
{
    private readonly DocumentRoot _document;
    private readonly Action? _selectionChanged;

    public SelectionService(
        DocumentRoot document,
        Action? selectionChanged = null)
    {
        _document = document
            ?? throw new ArgumentNullException(nameof(document));
        _selectionChanged = selectionChanged;

        _document.EnsureEditableStructure();
        ParagraphBlock? firstParagraph = _document.EnumerateBlocks()
            .OfType<ParagraphBlock>()
            .FirstOrDefault();
        if (firstParagraph is null)
        {
            firstParagraph = new ParagraphBlock();
            _document.Sections[0].Blocks.Add(firstParagraph);
        }
        Caret = new DocumentLocation(firstParagraph.Id, 0);
    }

    public DocumentLocation Caret { get; private set; }

    public DocumentLocation? Anchor { get; private set; }

    public bool HasSelection => Anchor.HasValue && Anchor.Value != Caret;

    public void MoveCaret(DocumentLocation location, bool extendSelection = false)
    {
        DocumentLocation normalized = NormalizeLocation(location);
        DocumentLocation previousCaret = Caret;
        DocumentLocation? previousAnchor = Anchor;

        if (extendSelection)
        {
            Anchor ??= Caret;
        }
        else
        {
            Anchor = null;
        }

        Caret = normalized;
        NotifyIfChanged(previousCaret, previousAnchor);
    }

    public void SetSelection(DocumentLocation anchor, DocumentLocation caret)
    {
        DocumentLocation previousCaret = Caret;
        DocumentLocation? previousAnchor = Anchor;
        Anchor = NormalizeLocation(anchor);
        Caret = NormalizeLocation(caret);
        NotifyIfChanged(previousCaret, previousAnchor);
    }

    public void SelectAll()
    {
        DocumentLocation previousCaret = Caret;
        DocumentLocation? previousAnchor = Anchor;
        ParagraphBlock first = _document.EnumerateBlocks()
            .OfType<ParagraphBlock>()
            .First();
        ParagraphBlock last = _document.EnumerateBlocks()
            .OfType<ParagraphBlock>()
            .Last();
        Anchor = new DocumentLocation(first.Id, 0);
        Caret = new DocumentLocation(last.Id, last.TextLength);
        NotifyIfChanged(previousCaret, previousAnchor);
    }

    public void ClearSelection()
    {
        if (Anchor.HasValue)
        {
            Anchor = null;
            _selectionChanged?.Invoke();
        }
    }

    internal SelectionState CaptureState() => new(Caret, Anchor);

    internal void RestoreState(SelectionState state)
    {
        if (state.Anchor.HasValue)
        {
            SetSelection(state.Anchor.Value, state.Caret);
            return;
        }

        MoveCaret(state.Caret);
    }

    public DocumentRange GetRange()
    {
        if (!Anchor.HasValue)
        {
            return new DocumentRange(Caret, Caret);
        }

        int anchorIndex = _document.GetBlockIndex(Anchor.Value.BlockId);
        int caretIndex = _document.GetBlockIndex(Caret.BlockId);

        if (anchorIndex < caretIndex ||
            anchorIndex == caretIndex && Anchor.Value.Offset <= Caret.Offset)
        {
            return new DocumentRange(Anchor.Value, Caret);
        }

        return new DocumentRange(Caret, Anchor.Value);
    }

    public DocumentLocation NormalizeLocation(DocumentLocation location)
    {
        ParagraphBlock paragraph = _document.FindBlock(location.BlockId)
            as ParagraphBlock
            ?? throw new InvalidOperationException(
                $"Позиция должна ссылаться на абзац {location.BlockId}.");

        return location.Clamp(paragraph.TextLength);
    }

    private void NotifyIfChanged(
        DocumentLocation previousCaret,
        DocumentLocation? previousAnchor)
    {
        if (Caret != previousCaret || Anchor != previousAnchor)
        {
            _selectionChanged?.Invoke();
        }
    }
}

internal readonly record struct SelectionState(
    DocumentLocation Caret,
    DocumentLocation? Anchor);
