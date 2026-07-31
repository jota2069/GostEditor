using System;
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

    public Guid? ImageId { get; internal set; }
    public double ImageWidth { get; internal set; }
    public double ImageHeight { get; internal set; }

    public bool IsImage => ImageId.HasValue;

    [Obsolete("Use ImageId and IImageService. Binary image data belongs to GostDocument.Images.")]
    public byte[]? ImageData { get; set; }

    public ParagraphStyle Style { get; set; } = ParagraphStyle.Normal;

    // НОВОЕ СВОЙСТВО: Принудительный разрыв страницы (для начала новых Глав)
    public bool PageBreakBefore { get; set; } = false;
}
