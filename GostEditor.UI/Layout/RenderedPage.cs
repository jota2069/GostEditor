using Avalonia;
using Avalonia.Media.TextFormatting;
using System.Collections.Generic;

namespace GostEditor.UI.Layout;

public class RenderedPage
{
    public int PageNumber { get; set; }
    public List<TextLinePlacement> Lines { get; set; } = new List<TextLinePlacement>();
    public Rect? CaretBounds { get; set; }

    // Прямоугольники для отрисовки выделения (Selection)
    public List<Rect> SelectionBounds { get; set; } = new List<Rect>();
}

public class TextLinePlacement
{
    public TextLine Line { get; }
    public Point Location { get; }
    public int ParagraphIndex { get; }
    public double InternalY { get; }
    public TextLayout ParentLayout { get; }

    public TextLinePlacement(TextLine line, Point location, int paragraphIndex, double internalY, TextLayout parentLayout)
    {
        Line = line;
        Location = location;
        ParagraphIndex = paragraphIndex;
        InternalY = internalY;
        ParentLayout = parentLayout;
    }
}
