using System.Collections.Generic;

namespace GostEditor.Core.TextEngine.DOM;

/// <summary>
/// Представляет один абзац текста, состоящий из разных фрагментов форматирования
/// </summary>
public class Paragraph
{
    // Список фрагментов текста внутри абзаца
    public List<TextRun> Runs { get; set; } = new();

    // Настройки абзаца (ГОСТ требует 1.25 см)
    public double FirstLineIndent { get; set; } = 47.0; // 1.25 см в пикселях

    public double LineSpacing { get; set; } = 1.5; // Межстрочный интервал

    // Метод для получения чистого текста без форматирования (для поиска или экспорта)
    public string GetPlainText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var run in Runs) sb.Append(run.Text);
        return sb.ToString();
    }
}
