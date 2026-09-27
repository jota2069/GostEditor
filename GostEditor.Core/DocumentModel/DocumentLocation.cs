namespace GostEditor.Core.DocumentModel;

public readonly record struct DocumentLocation(
    DocumentNodeId BlockId,
    int Offset)
{
    public DocumentLocation Clamp(int textLength) =>
        new(BlockId, Math.Clamp(Offset, 0, textLength));
}

public readonly record struct DocumentRange(
    DocumentLocation Start,
    DocumentLocation End)
{
    public bool IsCollapsed => Start == End;
}
