using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Editing;

public interface IEditOperation
{
    string Description { get; }

    IReadOnlyCollection<DocumentNodeId> AffectedBlocks { get; }

    void Apply(DocumentEditingContext context);

    void Revert(DocumentEditingContext context);
}
