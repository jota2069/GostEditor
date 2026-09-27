using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.DocumentModel.Blocks;

public sealed class ParagraphProperties
{
    public double FirstLineIndent { get; set; } = 47.0;

    public double LineSpacing { get; set; } = 1.5;

    public GostAlignment Alignment { get; set; } = GostAlignment.Left;

    public ParagraphStyle Style { get; set; } = ParagraphStyle.Normal;

    public bool PageBreakBefore { get; set; }

    public ParagraphProperties Clone() => new()
    {
        FirstLineIndent = FirstLineIndent,
        LineSpacing = LineSpacing,
        Alignment = Alignment,
        Style = Style,
        PageBreakBefore = PageBreakBefore
    };
}
