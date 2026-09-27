namespace GostEditor.Core.DocumentModel.Blocks;

public sealed class CodeBlock : BlockNode
{
    public CodeBlock()
    {
    }

    public CodeBlock(DocumentNodeId id)
        : base(id)
    {
    }

    public string Language { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;
}
