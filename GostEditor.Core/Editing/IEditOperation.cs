using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Editing;

public enum DocumentIndexUpdateKind
{
    None,
    AffectedParagraphs,
    Full
}

public interface IEditOperation
{
    string Description { get; }

    IReadOnlyCollection<DocumentNodeId> AffectedBlocks { get; }

    DocumentIndexUpdateKind IndexUpdateKind => DocumentIndexUpdateKind.Full;

    void Apply(DocumentEditingContext context);

    void Revert(DocumentEditingContext context);
}
