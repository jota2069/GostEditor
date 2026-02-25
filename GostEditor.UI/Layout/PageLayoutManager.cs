using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using System.Collections.Generic;
using System.Linq; // Важно для .ToList()

namespace GostEditor.UI.Layout;

public class PageLayoutManager
{
    public List<RenderedPage> BuildLayout(GostDocument document, DocumentEditor editor)
    {
        List<RenderedPage> pages = new List<RenderedPage>();
        RenderedPage currentPage = new RenderedPage { PageNumber = 1 };

        double currentY = document.MarginTop;
        double maxBottom = document.PageHeight - document.MarginBottom;
        double contentWidth = document.ContentWidth;

        Typeface typeface = new Typeface("Times New Roman");
        double fontSize = 18.67;

        var (selStart, selEnd) = editor.GetNormalizedSelection();

        for (int pIndex = 0; pIndex < document.Paragraphs.Count; pIndex++)
        {
            Paragraph paragraph = document.Paragraphs[pIndex];
            string plainText = paragraph.GetPlainText();
            if (string.IsNullOrEmpty(plainText)) plainText = "\u200B";

            TextLayout layout = new TextLayout(
                plainText, typeface, fontSize, Brushes.Black,
                TextAlignment.Left, TextWrapping.Wrap, maxWidth: contentWidth);

            double paragraphInternalY = 0;
            bool isFirstLineOfParagraph = true;

            // Расчет прямоугольников выделения для всего абзаца сразу
            List<Rect> selectionRects = new List<Rect>();
            if (editor.HasSelection && pIndex >= selStart.ParagraphIndex && pIndex <= selEnd.ParagraphIndex)
            {
                int start = pIndex == selStart.ParagraphIndex ? selStart.Offset : 0;
                int end = pIndex == selEnd.ParagraphIndex ? selEnd.Offset : plainText.Length;

                if (end > start)
                {
                    // Исправлено: добавляем .ToList() для преобразования типов
                    selectionRects = layout.HitTestTextRange(start, end - start).ToList();
                }
            }

            foreach (TextLine textLine in layout.TextLines)
            {
                if (currentY + textLine.Height > maxBottom)
                {
                    pages.Add(currentPage);
                    currentPage = new RenderedPage { PageNumber = pages.Count + 1 };
                    currentY = document.MarginTop;
                }

                double currentX = document.MarginLeft;
                if (isFirstLineOfParagraph) currentX += paragraph.FirstLineIndent;

                Point location = new Point(currentX, currentY);
                currentPage.Lines.Add(new TextLinePlacement(textLine, location, pIndex, paragraphInternalY, layout));

                // Привязываем прямоугольники выделения к конкретной строке на странице
                foreach (Rect rect in selectionRects)
                {
                    if (rect.Y >= paragraphInternalY - 0.1 && rect.Y < paragraphInternalY + textLine.Height - 0.1)
                    {
                        double selX = document.MarginLeft + rect.X;
                        if (isFirstLineOfParagraph) selX += paragraph.FirstLineIndent;
                        currentPage.SelectionBounds.Add(new Rect(selX, currentY, rect.Width, rect.Height));
                    }
                }

                // Каретка
                if (pIndex == editor.CaretPosition.ParagraphIndex)
                {
                    Rect charRect = layout.HitTestTextPosition(editor.CaretPosition.Offset);
                    if (charRect.Y >= paragraphInternalY - 0.1 && charRect.Y < paragraphInternalY + textLine.Height - 0.1)
                    {
                        double caretX = document.MarginLeft + charRect.X;
                        if (isFirstLineOfParagraph) caretX += paragraph.FirstLineIndent;
                        currentPage.CaretBounds = new Rect(caretX, currentY, 1.5, textLine.Height);
                    }
                }

                paragraphInternalY += textLine.Height;
                currentY += textLine.Height * paragraph.LineSpacing;
                isFirstLineOfParagraph = false;
            }
        }

        if (currentPage.Lines.Count > 0 || pages.Count == 0) pages.Add(currentPage);
        return pages;
    }

    public DocumentPosition? GetPositionFromPoint(RenderedPage page, Point clickPoint)
    {
        foreach (var line in page.Lines)
        {
            if (clickPoint.Y >= line.Location.Y && clickPoint.Y <= line.Location.Y + line.Line.Height)
            {
                double layoutX = clickPoint.X - line.Location.X;
                double layoutY = line.InternalY + (clickPoint.Y - line.Location.Y);
                if (layoutX > line.Line.Width) layoutX = line.Line.Width;
                if (layoutX < 0) layoutX = 0;

                var hitTest = line.ParentLayout.HitTestPoint(new Point(layoutX, layoutY));
                return new DocumentPosition(line.ParagraphIndex, hitTest.TextPosition);
            }
        }
        return null;
    }
}
