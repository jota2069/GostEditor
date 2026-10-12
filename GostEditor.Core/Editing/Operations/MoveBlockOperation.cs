using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;

namespace GostEditor.Core.Editing.Operations;

public sealed class MoveBlockOperation : IEditOperation, IHistoryPayloadSize
{
    private readonly DocumentNodeId _blockId;
    private readonly DocumentNodeId _destinationSectionId;
    private readonly int _destinationIndex;
    private DocumentNodeId _sourceSectionId;
    private int _sourceIndex;
    private int _appliedDestinationIndex;

    public MoveBlockOperation(
        DocumentNodeId blockId,
        DocumentNodeId destinationSectionId,
        int destinationIndex)
    {
        _blockId = blockId;
        _destinationSectionId = destinationSectionId;
        _destinationIndex = destinationIndex;
    }

    public string Description => "Перемещение блока";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _blockId };

    public long EstimatedPayloadBytes => 256L;

    public void Apply(DocumentEditingContext context)
    {
        (DocumentSection source, int sourceIndex) =
            context.GetRequiredBlockLocation(_blockId);
        DocumentSection destination = context.Document.FindNode(
                _destinationSectionId) as DocumentSection
            ?? throw new InvalidOperationException(
                $"Секция {_destinationSectionId} не найдена.");
        BlockNode block = source.Blocks[sourceIndex];

        _sourceSectionId = source.Id;
        _sourceIndex = sourceIndex;
        source.Blocks.RemoveAt(sourceIndex);
        _appliedDestinationIndex = Math.Clamp(
            _destinationIndex,
            0,
            destination.Blocks.Count);
        destination.Blocks.Insert(_appliedDestinationIndex, block);
        context.InvalidationTracker.InvalidateStructure(
            Math.Min(sourceIndex, _appliedDestinationIndex));
    }

    public void Revert(DocumentEditingContext context)
    {
        (DocumentSection destination, int destinationIndex) =
            context.GetRequiredBlockLocation(_blockId);
        DocumentSection source = context.Document.FindNode(
                _sourceSectionId) as DocumentSection
            ?? throw new InvalidOperationException(
                $"Секция {_sourceSectionId} не найдена.");
        BlockNode block = destination.Blocks[destinationIndex];

        destination.Blocks.RemoveAt(destinationIndex);
        source.Blocks.Insert(
            Math.Clamp(_sourceIndex, 0, source.Blocks.Count),
            block);
        context.InvalidationTracker.InvalidateStructure(
            Math.Min(_sourceIndex, _appliedDestinationIndex));
    }
}
