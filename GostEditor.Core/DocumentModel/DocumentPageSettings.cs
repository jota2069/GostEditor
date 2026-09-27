namespace GostEditor.Core.DocumentModel;

public sealed class DocumentPageSettings
{
    public double Width { get; set; } = 794.0;

    public double Height { get; set; } = 1123.0;

    public double MarginLeft { get; set; } = 113.0;

    public double MarginRight { get; set; } = 57.0;

    public double MarginTop { get; set; } = 76.0;

    public double MarginBottom { get; set; } = 76.0;

    public double ContentWidth => Width - MarginLeft - MarginRight;

    public double ContentHeight => Height - MarginTop - MarginBottom;

    public DocumentPageSettings Clone() => new()
    {
        Width = Width,
        Height = Height,
        MarginLeft = MarginLeft,
        MarginRight = MarginRight,
        MarginTop = MarginTop,
        MarginBottom = MarginBottom
    };
}
