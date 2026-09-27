using System.Text;
using GostEditor.Core.DocumentModel.Inlines;

namespace GostEditor.Core.DocumentModel.Blocks;

public sealed class ParagraphBlock : BlockNode
{
    public ParagraphBlock()
    {
    }

    public ParagraphBlock(DocumentNodeId id)
        : base(id)
    {
    }

    public ParagraphProperties Properties { get; set; } = new();

    public List<InlineNode> Inlines { get; } = new();

    public int TextLength => Inlines.Sum(inline => inline.TextLength);

    public string GetPlainText()
    {
        StringBuilder builder = new();
        foreach (InlineNode inline in Inlines)
        {
            inline.AppendPlainText(builder);
        }

        return builder.ToString();
    }

    public void EnsureEditableInline()
    {
        if (Inlines.Count == 0)
        {
            Inlines.Add(new TextInline(string.Empty));
        }
    }
}
