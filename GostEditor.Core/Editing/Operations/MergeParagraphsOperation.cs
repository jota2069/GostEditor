using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class MergeParagraphsOperation : IEditOperation
{
    private readonly DocumentNodeId _firstParagraphId;
    private readonly DocumentNodeId _secondParagraphId;
    private List<InlineNode>? _firstBefore;
    private ParagraphBlock? _secondParagraph;
    private DocumentNodeId _sectionId;
    private int _secondIndex;

    public MergeParagraphsOperation(
        DocumentNodeId firstParagraphId,
        DocumentNodeId secondParagraphId)
    {
        _firstParagraphId = firstParagraphId;
        _secondParagraphId = secondParagraphId;
    }

    public string Description => "Объединение абзацев";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _firstParagraphId, _secondParagraphId };

    public void Apply(DocumentEditingContext context)
    {
        ParagraphBlock first = context.GetRequiredParagraph(_firstParagraphId);
        ParagraphBlock second = context.GetRequiredParagraph(_secondParagraphId);
        (DocumentSection firstSection, int firstIndex) =
            context.GetRequiredBlockLocation(_firstParagraphId);
        (DocumentSection secondSection, int secondIndex) =
            context.GetRequiredBlockLocation(_secondParagraphId);

        if (firstSection.Id != secondSection.Id || secondIndex != firstIndex + 1)
        {
            throw new InvalidOperationException(
                "Объединяемые абзацы должны быть соседними и находиться " +
                "в одной секции.");
        }

        _firstBefore ??= ParagraphTextBuffer.CloneInlines(first.Inlines);
        _secondParagraph ??= second;
        _sectionId = firstSection.Id;
        _secondIndex = secondIndex;

        List<InlineNode> merged = ParagraphTextBuffer.CloneInlines(
            first.Inlines);
        merged.AddRange(ParagraphTextBuffer.CloneInlines(second.Inlines));
        ParagraphTextBuffer.ReplaceAll(first, merged);
        firstSection.Blocks.RemoveAt(secondIndex);

        context.InvalidationTracker.InvalidateStructure(firstIndex);
    }

    public void Revert(DocumentEditingContext context)
    {
        ParagraphBlock first = context.GetRequiredParagraph(_firstParagraphId);
        DocumentSection section = context.Document.Sections.FirstOrDefault(
            item => item.Id == _sectionId)
            ?? throw new InvalidOperationException(
                $"Секция {_sectionId} не найдена.");

        ParagraphTextBuffer.Restore(
            first,
            _firstBefore ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена."));

        ParagraphBlock second = _secondParagraph
            ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена.");
        section.Blocks.Insert(
            Math.Clamp(_secondIndex, 0, section.Blocks.Count),
            second);

        context.InvalidationTracker.InvalidateStructure(
            Math.Max(0, _secondIndex - 1));
    }
}
