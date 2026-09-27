using System.Text;

namespace GostEditor.Core.DocumentModel.Inlines;

public abstract class InlineNode : DocumentNode
{
    protected InlineNode()
    {
    }

    protected InlineNode(DocumentNodeId id)
        : base(id)
    {
    }

    public abstract int TextLength { get; }

    public abstract void AppendPlainText(StringBuilder builder);
}
