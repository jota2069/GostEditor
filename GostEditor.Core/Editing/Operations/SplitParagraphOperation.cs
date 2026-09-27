using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class SplitParagraphOperation : IEditOperation
{
    private readonly DocumentNodeId _paragraphId;
    private readonly int _offset;
    private readonly DocumentNodeId _newParagraphId;
    private List<InlineNode>? _originalInlines;
    private ParagraphProperties? _originalProperties;

    public SplitParagraphOperation(
        DocumentNodeId paragraphId,
        int offset,
        DocumentNodeId? newParagraphId = null)
    {
        _paragraphId = paragraphId;
        _offset = offset;
        _newParagraphId = newParagraphId ?? DocumentNodeId.New();
    }

    public string Description => "Разделение абзаца";

    public DocumentNodeId NewParagraphId => _newParagraphId;

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _paragraphId, _newParagraphId };

    public void Apply(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        (DocumentSection section, int index) =
            context.GetRequiredBlockLocation(_paragraphId);

        if (context.Document.FindBlock(_newParagraphId) is not null)
        {
            throw new InvalidOperationException(
                $"Блок {_newParagraphId} уже существует.");
        }

        _originalInlines ??= ParagraphTextBuffer.CloneInlines(
            paragraph.Inlines);
        _originalProperties ??= paragraph.Properties.Clone();

        int offset = Math.Clamp(_offset, 0, paragraph.TextLength);
        List<InlineNode> left = ParagraphTextBuffer.Slice(
            paragraph,
            0,
            offset);
        List<InlineNode> right = ParagraphTextBuffer.Slice(
            paragraph,
            offset,
            paragraph.TextLength - offset);

        ParagraphTextBuffer.ReplaceAll(paragraph, left);

        ParagraphBlock newParagraph = new(_newParagraphId)
        {
            Properties = paragraph.Properties.Clone()
        };
        ParagraphTextBuffer.ReplaceAll(newParagraph, right);
        section.Blocks.Insert(index + 1, newParagraph);

        context.InvalidationTracker.InvalidateStructure(index);
    }

    public void Revert(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        (DocumentSection section, int newIndex) =
            context.GetRequiredBlockLocation(_newParagraphId);

        section.Blocks.RemoveAt(newIndex);
        paragraph.Properties = (_originalProperties
            ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена.")).Clone();
        ParagraphTextBuffer.Restore(
            paragraph,
            _originalInlines ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена."));

        context.InvalidationTracker.InvalidateStructure(
            Math.Max(0, newIndex - 1));
    }
}
