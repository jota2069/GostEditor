using System.Text;

namespace GostEditor.Core.DocumentModel.Inlines;

public sealed class LineBreakInline : InlineNode
{
    public LineBreakInline()
    {
    }

    public LineBreakInline(DocumentNodeId id)
        : base(id)
    {
    }

    public override int TextLength => 1;

    public override void AppendPlainText(StringBuilder builder) =>
        builder.Append('\n');
}
