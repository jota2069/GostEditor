using System.Text;

namespace GostEditor.Core.DocumentModel.Inlines;

public sealed class LinkInline : InlineNode
{
    public LinkInline()
    {
    }

    public LinkInline(DocumentNodeId id)
        : base(id)
    {
    }

    public string Text { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public TextStyle Style { get; set; } = new();

    public override int TextLength => Text.Length;

    public override void AppendPlainText(StringBuilder builder) =>
        builder.Append(Text);
}
