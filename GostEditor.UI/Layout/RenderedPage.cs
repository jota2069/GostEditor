using System.Collections.Generic;
using Avalonia;
using Avalonia.Media.TextFormatting;

namespace GostEditor.UI.Layout;

// Класс для хранения данных о расположении картинки на листе
public class ImagePlacement
{
    public byte[] ImageData { get; set; }
    public Rect Bounds { get; set; }

    public ImagePlacement(byte[] data, Rect bounds)
    {
        ImageData = data;
        Bounds = bounds;
    }
}

public class TextLinePlacement
{
    public TextLine Line { get; }
    public Point Location { get; }
    public int ParagraphIndex { get; }
    public double InternalY { get; }
    public TextLayout ParentLayout { get; }
    public int PrefixLength { get; }

    public TextLinePlacement(TextLine line, Point location, int paragraphIndex, double internalY, TextLayout parentLayout, int prefixLength)
    {
        Line = line;
        Location = location;
        ParagraphIndex = paragraphIndex;
        InternalY = internalY;
        ParentLayout = parentLayout;
        PrefixLength = prefixLength;
    }
}

// Обновленный класс страницы
public class RenderedPage
{
    public int PageNumber { get; set; }
    public List<TextLinePlacement> Lines { get; set; } = new List<TextLinePlacement>();
    public List<Rect> SelectionBounds { get; set; } = new List<Rect>();
    public Rect? CaretBounds { get; set; }

    // НОВОЕ СВОЙСТВО: Список картинок на этой странице
    public List<ImagePlacement> Images { get; set; } = new List<ImagePlacement>();
}
