using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class InsertBlockOperation : IEditOperation
{
    private readonly DocumentNodeId _sectionId;
    private readonly int _index;
    private readonly BlockNode _block;

    public InsertBlockOperation(
        DocumentNodeId sectionId,
        int index,
        BlockNode block)
    {
        _sectionId = sectionId;
        _index = index;
        _block = block
            ?? throw new ArgumentNullException(nameof(block));
    }

    public string Description => "Добавление блока";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _block.Id };

    public void Apply(DocumentEditingContext context)
    {
        DocumentSection section = context.Document.Sections.FirstOrDefault(
            item => item.Id == _sectionId)
            ?? throw new InvalidOperationException(
                $"Секция {_sectionId} не найдена.");

        if (section.Blocks.Any(block => block.Id == _block.Id))
        {
            throw new InvalidOperationException(
                $"Блок {_block.Id} уже находится в секции.");
        }

        int index = Math.Clamp(_index, 0, section.Blocks.Count);
        section.Blocks.Insert(index, _block);
        context.InvalidationTracker.InvalidateStructure(index);
    }

    public void Revert(DocumentEditingContext context)
    {
        (DocumentSection section, int index) =
            context.GetRequiredBlockLocation(_block.Id);
        section.Blocks.RemoveAt(index);
        context.InvalidationTracker.InvalidateStructure(index);
    }
}
