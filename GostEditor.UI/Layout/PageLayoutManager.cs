using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using System.Collections.Generic;
using System.Linq;

namespace GostEditor.UI.Layout;

public class PageLayoutManager
{
    public List<RenderedPage> BuildLayout(GostDocument document, DocumentEditor editor, Typeface typeface)
    {
        List<RenderedPage> pages = new List<RenderedPage>();
        RenderedPage currentPage = new RenderedPage { PageNumber = 1 };

        double currentY = document.MarginTop;
        double maxBottom = document.PageHeight - document.MarginBottom;
        double contentWidth = document.ContentWidth;

        (DocumentPosition selStart, DocumentPosition selEnd) = editor.GetNormalizedSelection();

        for (int pIndex = 0; pIndex < document.Paragraphs.Count; pIndex++)
        {
            Paragraph paragraph = document.Paragraphs[pIndex];

            string plainText = paragraph.GetPlainText();
            if (string.IsNullOrEmpty(plainText)) plainText = "\u200B";

            double baseFontSize = 18.67;
            FontWeight baseWeight = FontWeight.Normal;
            FontFamily baseFontFamily = typeface.FontFamily;

            if (paragraph.Style == ParagraphStyle.Heading1)
            {
                baseFontSize = 21.33;
                baseWeight = FontWeight.Bold;
            }
            else if (paragraph.Style == ParagraphStyle.Heading2)
            {
                baseFontSize = 18.67;
                baseWeight = FontWeight.Normal;
            }
            else if (paragraph.Style == ParagraphStyle.Code)
            {
                baseFontSize = 16.0;
                baseFontFamily = new FontFamily("Consolas");
            }

            Typeface baseTypeface = new Typeface(baseFontFamily, FontStyle.Normal, baseWeight);

            bool needsIndentHack = paragraph.Alignment != GostAlignment.Center &&
                                   paragraph.Alignment != GostAlignment.Right &&
                                   paragraph.FirstLineIndent > 0;

            int indentCharsCount = 0;

            if (needsIndentHack)
            {
                string indentString = "\u2003\u2002";
                indentCharsCount = indentString.Length;
                plainText = indentString + plainText;
            }

            List<Avalonia.Utilities.ValueSpan<TextRunProperties>> styleOverrides = new List<Avalonia.Utilities.ValueSpan<TextRunProperties>>();

            if (needsIndentHack)
            {
                TextRunProperties indentProps = new GenericTextRunProperties(baseTypeface, baseFontSize * 1.3333333333333333, null, Brushes.Transparent);
                styleOverrides.Add(new Avalonia.Utilities.ValueSpan<TextRunProperties>(0, indentCharsCount, indentProps));
            }

            int currentPos = indentCharsCount;

            // ИСПРАВЛЕНИЕ: Явно указываем, что используем твой TextRun, а не Avalonia
            foreach (GostEditor.Core.TextEngine.DOM.TextRun run in paragraph.Runs)
            {
                if (string.IsNullOrEmpty(run.Text)) continue;

                double runFontSizePx = run.FontSize * 1.3333333333333333;

                FontWeight effectiveWeight = run.IsBold || baseWeight == FontWeight.Bold ? FontWeight.Bold : FontWeight.Normal;
                FontStyle effectiveStyle = run.IsItalic ? FontStyle.Italic : FontStyle.Normal;

                if (effectiveWeight != baseWeight || effectiveStyle != FontStyle.Normal || System.Math.Abs(runFontSizePx - baseFontSize) > 0.1)
                {
                    Typeface runTypeface = new Typeface(baseFontFamily, effectiveStyle, effectiveWeight);
                    TextRunProperties props = new GenericTextRunProperties(runTypeface, runFontSizePx, null, Brushes.Black);

                    styleOverrides.Add(new Avalonia.Utilities.ValueSpan<TextRunProperties>(currentPos, run.Text.Length, props));
                }

                currentPos += run.Text.Length;
            }

            TextAlignment avaloniaAlignment = paragraph.Alignment switch
            {
                GostAlignment.Center => TextAlignment.Center,
                GostAlignment.Right => TextAlignment.Right,
                GostAlignment.Justify => TextAlignment.Justify,
                _ => TextAlignment.Left
            };

            // ИСПРАВЛЕНИЕ: Используем именованные аргументы, как было в твоем оригинальном коде
            TextLayout layout = new TextLayout(
                plainText,
                baseTypeface,
                baseFontSize,
                Brushes.Black,
                avaloniaAlignment,
                TextWrapping.Wrap,
                maxWidth: contentWidth,
                textStyleOverrides: styleOverrides);

            double paragraphInternalY = 0;

            List<Rect> selectionRects = new List<Rect>();
            if (editor.HasSelection && pIndex >= selStart.ParagraphIndex && pIndex <= selEnd.ParagraphIndex)
            {
                int start = pIndex == selStart.ParagraphIndex ? selStart.Offset : 0;
                int end = pIndex == selEnd.ParagraphIndex ? selEnd.Offset : paragraph.GetPlainText().Length;

                if (end > start)
                {
                    int adjustedStart = start + (pIndex == selStart.ParagraphIndex ? indentCharsCount : 0);
                    int adjustedLength = end - start;
                    selectionRects = layout.HitTestTextRange(adjustedStart, adjustedLength).ToList();
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
                Point location = new Point(currentX, currentY);
                currentPage.Lines.Add(new TextLinePlacement(textLine, location, pIndex, paragraphInternalY, layout));

                foreach (Rect rect in selectionRects)
                {
                    if (rect.Y >= paragraphInternalY - 0.1 && rect.Y < paragraphInternalY + textLine.Height - 0.1)
                    {
                        double selX = document.MarginLeft + rect.X;
                        currentPage.SelectionBounds.Add(new Rect(selX, currentY, rect.Width, rect.Height));
                    }
                }

                if (pIndex == editor.CaretPosition.ParagraphIndex)
                {
                    int adjustedCaretOffset = editor.CaretPosition.Offset + indentCharsCount;
                    Rect charRect = layout.HitTestTextPosition(adjustedCaretOffset);

                    if (charRect.Y >= paragraphInternalY - 0.1 && charRect.Y < paragraphInternalY + textLine.Height - 0.1)
                    {
                        double caretX = document.MarginLeft + charRect.X;
                        currentPage.CaretBounds = new Rect(caretX, currentY, 1.5, textLine.Height);
                    }
                }

                paragraphInternalY += textLine.Height;
                currentY += textLine.Height * paragraph.LineSpacing;
            }
        }

        if (currentPage.Lines.Count > 0 || pages.Count == 0) pages.Add(currentPage);
        return pages;
    }

    public DocumentPosition? GetPositionFromPoint(RenderedPage page, Point clickPoint)
    {
        foreach (TextLinePlacement line in page.Lines)
        {
            if (clickPoint.Y >= line.Location.Y && clickPoint.Y <= line.Location.Y + line.Line.Height)
            {
                double layoutX = clickPoint.X - line.Location.X;
                double layoutY = line.InternalY + (clickPoint.Y - line.Location.Y);

                if (layoutX > line.Line.Width) layoutX = line.Line.Width;
                if (layoutX < 0) layoutX = 0;

                TextHitTestResult hitTest = line.ParentLayout.HitTestPoint(new Point(layoutX, layoutY));

                int paragraphIndex = line.ParagraphIndex;
                int clickedOffset = hitTest.TextPosition;

                if (clickedOffset <= 2 && line.InternalY == 0)
                {
                    clickedOffset = 0;
                }
                else if (line.InternalY == 0)
                {
                    clickedOffset = System.Math.Max(0, clickedOffset - 2);
                }

                return new DocumentPosition(paragraphIndex, clickedOffset);
            }
        }
        return null;
    }
}
