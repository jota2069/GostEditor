using System.Text;

namespace GostEditor.Core.DocumentModel.Inlines;

public sealed class TextInline : InlineNode
{
    public TextInline(string text)
    {
        Text = text ?? string.Empty;
    }

    public TextInline(
        DocumentNodeId id,
        string text,
        TextStyle? style = null)
        : base(id)
    {
        Text = text ?? string.Empty;
        Style = style ?? new TextStyle();
    }

    public string Text { get; set; }

    public TextStyle Style { get; set; } = new();

    public override int TextLength => Text.Length;

    public override void AppendPlainText(StringBuilder builder) =>
        builder.Append(Text);
}
