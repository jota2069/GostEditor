using System.Collections.Generic;

namespace GostEditor.Core.TextEngine.DOM;

/// <summary>
/// Главный класс документа. Хранит весь текст и глобальные настройки страниц.
/// </summary>
public class GostDocument
{
    // Весь текст документа, разбитый на абзацы
    public List<Paragraph> Paragraphs { get; set; } = new();

    // Физические размеры страницы А4 (при 96 DPI)
    // 210 мм x 297 мм ≈ 794 x 1123 пикселей
    public double PageWidth { get; set; } = 794.0;
    public double PageHeight { get; set; } = 1123.0;

    // Поля по ГОСТу (в пикселях при 96 DPI):
    // Левое: 3 см (113 px), Правое: 1.5 см (57 px)
    // Верхнее: 2 см (76 px), Нижнее: 2 см (76 px)
    public double MarginLeft { get; set; } = 113.0;
    public double MarginRight { get; set; } = 57.0;
    public double MarginTop { get; set; } = 76.0;
    public double MarginBottom { get; set; } = 76.0;

    // Ширина рабочей области для текста (Ширина листа минус левое и правое поле)
    public double ContentWidth => PageWidth - MarginLeft - MarginRight;

    // Высота рабочей области для текста (Высота листа минус верхнее и нижнее поле)
    public double ContentHeight => PageHeight - MarginTop - MarginBottom;

    // Метод для быстрого получения всего текста разом (пригодится для отладки)
    public string GetAllPlainText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in Paragraphs)
        {
            sb.AppendLine(p.GetPlainText());
        }
        return sb.ToString();
    }
}
