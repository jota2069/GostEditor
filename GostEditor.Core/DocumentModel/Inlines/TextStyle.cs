namespace GostEditor.Core.DocumentModel.Inlines;

public sealed class TextStyle : IEquatable<TextStyle>
{
    public bool IsBold { get; set; }

    public bool IsItalic { get; set; }

    public uint Color { get; set; } = 0xFF000000;

    public double FontSize { get; set; } = 14.0;

    public string? FontFamily { get; set; }

    public TextStyle Clone() => new()
    {
        IsBold = IsBold,
        IsItalic = IsItalic,
        Color = Color,
        FontSize = FontSize,
        FontFamily = FontFamily
    };

    public bool Equals(TextStyle? other)
    {
        if (other is null)
        {
            return false;
        }

        return IsBold == other.IsBold &&
            IsItalic == other.IsItalic &&
            Color == other.Color &&
            FontSize.Equals(other.FontSize) &&
            string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) =>
        obj is TextStyle other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(IsBold, IsItalic, Color, FontSize, FontFamily);
}
