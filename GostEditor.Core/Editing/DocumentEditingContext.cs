using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing;

public sealed class DocumentEditingContext
{
    public DocumentEditingContext(
        DocumentRoot document,
        LayoutInvalidationTracker invalidationTracker)
    {
        Document = document
            ?? throw new ArgumentNullException(nameof(document));
        InvalidationTracker = invalidationTracker
            ?? throw new ArgumentNullException(nameof(invalidationTracker));
    }

    public DocumentRoot Document { get; }

    public LayoutInvalidationTracker InvalidationTracker { get; }

    public BlockNode GetRequiredBlock(DocumentNodeId id) =>
        Document.FindBlock(id)
        ?? throw new InvalidOperationException(
            $"Блок документа {id} не найден.");

    public ParagraphBlock GetRequiredParagraph(DocumentNodeId id) =>
        GetRequiredBlock(id) as ParagraphBlock
        ?? throw new InvalidOperationException(
            $"Блок {id} не является абзацем.");

    public (DocumentSection Section, int Index) GetRequiredBlockLocation(
        DocumentNodeId id)
    {
        foreach (DocumentSection section in Document.Sections)
        {
            int index = section.Blocks.FindIndex(block => block.Id == id);
            if (index >= 0)
            {
                return (section, index);
            }
        }

        throw new InvalidOperationException(
            $"Блок документа {id} не найден.");
    }
}
