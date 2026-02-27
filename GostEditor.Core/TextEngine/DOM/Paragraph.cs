using System.Collections.Generic;

namespace GostEditor.Core.TextEngine.DOM;

public enum GostAlignment
{
    Left,
    Center,
    Right,
    Justify
}

public class Paragraph
{
    public List<TextRun> Runs { get; set; } = new List<TextRun>();

    public double FirstLineIndent { get; set; } = 47.0;

    public double LineSpacing { get; set; } = 1.5;

    // ИСПРАВЛЕНО: Теперь мы используем наш GostAlignment.
    // Никаких 'Avalonia' здесь быть не должно!
    public GostAlignment Alignment { get; set; } = GostAlignment.Left;

    public string GetPlainText()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (TextRun run in Runs)
        {
            sb.Append(run.Text);
        }
        return sb.ToString();
    }
}
