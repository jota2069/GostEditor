namespace GostEditor.Core.TextEngine.DOM;

/// <summary>
/// Минимальный кусок текста с одинаковым форматированием
/// </summary>
public class TextRun
{
    // Сам текст (слово, часть слова или пробел)
    public string Text { get; set; } = string.Empty;

    // Свойства стиля
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }

    // Можно добавить цвет или размер в будущем
    public uint Color { get; set; } = 0xFF000000; // Черный по умолчанию

    public double FontSize { get; set; } = 14.0;

    public TextRun() { }

    public TextRun(string text, bool isBold = false, bool isItalic = false)
    {
        Text = text;
        IsBold = isBold;
        IsItalic = isItalic;
    }
}
