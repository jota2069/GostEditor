using System;
using GostEditor.Core.DocumentModel.Blocks;

namespace GostEditor.Core.DocumentModel;

public sealed class DocumentRoot : DocumentNode
{
    public DocumentRoot()
    {
    }

    public DocumentRoot(DocumentNodeId id)
        : base(id)
    {
    }

    public DocumentMetadata Metadata { get; set; } = new();

    public DocumentResourceCatalog Resources { get; } = new();

    public List<DocumentSection> Sections { get; } = new();

    public IEnumerable<BlockNode> EnumerateBlocks() =>
        Sections.SelectMany(section => section.Blocks);

    public BlockNode? FindBlock(DocumentNodeId id) =>
        EnumerateBlocks().FirstOrDefault(block => block.Id == id);

    public DocumentSection? FindSectionContaining(DocumentNodeId blockId) =>
        Sections.FirstOrDefault(
            section => section.Blocks.Any(block => block.Id == blockId));

    public int GetBlockIndex(DocumentNodeId blockId)
    {
        int index = 0;
        foreach (DocumentSection section in Sections)
        {
            foreach (BlockNode block in section.Blocks)
            {
                if (block.Id == blockId)
                {
                    return index;
                }

                index++;
            }
        }

        return -1;
    }

    public void EnsureEditableStructure()
    {
        if (Sections.Count == 0)
        {
            Sections.Add(new DocumentSection());
        }

        if (Sections.All(section => section.Blocks.Count == 0))
        {
            Sections[0].Blocks.Add(new ParagraphBlock());
        }
    }
}
