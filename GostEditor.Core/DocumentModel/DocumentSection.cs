using GostEditor.Core.DocumentModel.Blocks;

namespace GostEditor.Core.DocumentModel;

public sealed class DocumentSection : DocumentNode
{
    public DocumentSection()
    {
    }

    public DocumentSection(DocumentNodeId id)
        : base(id)
    {
    }

    public DocumentPageSettings PageSettings { get; set; } = new();

    public List<BlockNode> HeaderBlocks { get; } = new();

    public List<BlockNode> FooterBlocks { get; } = new();

    public List<BlockNode> Blocks { get; } = new();
}
