using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class RemoveBlockOperation : IEditOperation
{
    private readonly DocumentNodeId _blockId;
    private BlockNode? _removed;
    private DocumentNodeId _sectionId;
    private int _index;

    public RemoveBlockOperation(DocumentNodeId blockId)
    {
        _blockId = blockId;
    }

    public string Description => "Удаление блока";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _blockId };

    public void Apply(DocumentEditingContext context)
    {
        (DocumentSection section, int index) =
            context.GetRequiredBlockLocation(_blockId);
        _sectionId = section.Id;
        _index = index;
        _removed = section.Blocks[index];
        section.Blocks.RemoveAt(index);
        context.InvalidationTracker.InvalidateStructure(index);
    }

    public void Revert(DocumentEditingContext context)
    {
        DocumentSection section = context.Document.Sections.FirstOrDefault(
            item => item.Id == _sectionId)
            ?? throw new InvalidOperationException(
                $"Секция {_sectionId} не найдена.");

        section.Blocks.Insert(
            Math.Clamp(_index, 0, section.Blocks.Count),
            _removed ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена."));
        context.InvalidationTracker.InvalidateStructure(_index);
    }
}
