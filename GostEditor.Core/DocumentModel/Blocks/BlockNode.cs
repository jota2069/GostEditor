namespace GostEditor.Core.DocumentModel.Blocks;

public abstract class BlockNode : DocumentNode
{
    protected BlockNode()
    {
    }

    protected BlockNode(DocumentNodeId id)
        : base(id)
    {
    }
}
