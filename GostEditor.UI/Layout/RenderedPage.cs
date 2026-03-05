using System.Collections.Generic;
using Avalonia;
using Avalonia.Media.TextFormatting;
using GostEditor.Core.TextEngine.DOM; // Добавили для DocumentPosition

namespace GostEditor.UI.Layout;

// НОВЫЙ КЛАСС: Описывает, куда именно кликнул пользователь (в текст или в картинку)
public class DocumentHitResult
{
    public bool IsImageHit { get; }
    public DocumentPosition? TextPosition { get; }
    public int? ImageParagraphIndex { get; }

    // Конструктор для клика по тексту
    public DocumentHitResult(DocumentPosition textPosition)
    {
        IsImageHit = false;
        TextPosition = textPosition;
    }

    // Конструктор для клика по картинке
    public DocumentHitResult(int imageParagraphIndex)
    {
        IsImageHit = true;
        ImageParagraphIndex = imageParagraphIndex;
    }
}

// Класс для хранения данных о расположении картинки на листе
public class ImagePlacement
{
    public byte[] ImageData { get; set; }
    public Rect Bounds { get; set; }
    public int ParagraphIndex { get; set; } // ИСПРАВЛЕНИЕ: Добавили индекс абзаца

    public ImagePlacement(byte[] data, Rect bounds, int paragraphIndex)
    {
        ImageData = data;
        Bounds = bounds;
        ParagraphIndex = paragraphIndex;
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

    // Список картинок на этой странице
    public List<ImagePlacement> Images { get; set; } = new List<ImagePlacement>();
}
