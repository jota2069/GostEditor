using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using System;
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

        int figureCounter = 1;

        for (int pIndex = 0; pIndex < document.Paragraphs.Count; pIndex++)
        {
            Paragraph paragraph = document.Paragraphs[pIndex];

            string plainText = paragraph.GetPlainText();
            if (string.IsNullOrEmpty(plainText))
            {
                plainText = "\u200B";
            }

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

            int prefixCharsCount = 0;
            string prefixString = string.Empty;
            IBrush prefixBrush = Brushes.Black;

            if (paragraph.ImageData != null)
            {
                prefixString = $"Рисунок {figureCounter} - ";
                figureCounter++;
                prefixCharsCount = prefixString.Length;
            }
            else if (paragraph.Alignment != GostAlignment.Center &&
                     paragraph.Alignment != GostAlignment.Right &&
                     paragraph.FirstLineIndent > 0)
            {
                prefixString = "\u2003\u2002";
                prefixCharsCount = prefixString.Length;
                prefixBrush = Brushes.Transparent;
            }

            if (prefixCharsCount > 0)
            {
                plainText = prefixString + plainText;
            }

            List<Avalonia.Utilities.ValueSpan<TextRunProperties>> styleOverrides = new List<Avalonia.Utilities.ValueSpan<TextRunProperties>>();

            if (prefixCharsCount > 0)
            {
                TextRunProperties prefixProps = new GenericTextRunProperties(baseTypeface, baseFontSize, null, prefixBrush);
                styleOverrides.Add(new Avalonia.Utilities.ValueSpan<TextRunProperties>(0, prefixCharsCount, prefixProps));
            }

            int currentPos = prefixCharsCount;

            foreach (GostEditor.Core.TextEngine.DOM.TextRun run in paragraph.Runs)
            {
                if (string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                double runFontSizePx = run.FontSize * 1.3333333333333333;

                FontWeight effectiveWeight = run.IsBold || baseWeight == FontWeight.Bold ? FontWeight.Bold : FontWeight.Normal;
                FontStyle effectiveStyle = run.IsItalic ? FontStyle.Italic : FontStyle.Normal;

                if (effectiveWeight != baseWeight || effectiveStyle != FontStyle.Normal || Math.Abs(runFontSizePx - baseFontSize) > 0.1)
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

            TextLayout layout = new TextLayout(
                plainText,
                baseTypeface,
                baseFontSize,
                Brushes.Black,
                avaloniaAlignment,
                TextWrapping.Wrap,
                maxWidth: contentWidth,
                textStyleOverrides: styleOverrides);

            if (paragraph.ImageData != null)
            {
                double imgWidth = paragraph.ImageWidth;
                double imgHeight = paragraph.ImageHeight;

                if (imgWidth > contentWidth)
                {
                    double scale = contentWidth / imgWidth;
                    imgWidth = contentWidth;
                    imgHeight *= scale;
                }

                if (currentY + imgHeight > maxBottom)
                {
                    pages.Add(currentPage);
                    currentPage = new RenderedPage { PageNumber = pages.Count + 1 };
                    currentY = document.MarginTop;
                }

                double imgX = document.MarginLeft + (contentWidth - imgWidth) / 2;

                currentPage.Images.Add(new ImagePlacement(paragraph.ImageData, new Rect(imgX, currentY, imgWidth, imgHeight), pIndex));

                currentY += imgHeight + 10;
            }

            List<Rect> selectionRects = new List<Rect>();
            if (editor.HasSelection && pIndex >= selStart.ParagraphIndex && pIndex <= selEnd.ParagraphIndex)
            {
                int start = pIndex == selStart.ParagraphIndex ? selStart.Offset : 0;
                int end = pIndex == selEnd.ParagraphIndex ? selEnd.Offset : paragraph.GetPlainText().Length;

                if (end > start)
                {
                    int adjustedStart = start + (pIndex == selStart.ParagraphIndex ? prefixCharsCount : 0);
                    int adjustedLength = end - start;
                    selectionRects = layout.HitTestTextRange(adjustedStart, adjustedLength).ToList();
                }
            }

            double textLayoutInternalY = 0;

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

                currentPage.Lines.Add(new TextLinePlacement(textLine, location, pIndex, textLayoutInternalY, layout, prefixCharsCount));

                foreach (Rect rect in selectionRects)
                {
                    if (rect.Y >= textLayoutInternalY - 0.1 && rect.Y < textLayoutInternalY + textLine.Height - 0.1)
                    {
                        double selX = document.MarginLeft + rect.X;
                        currentPage.SelectionBounds.Add(new Rect(selX, currentY, rect.Width, rect.Height));
                    }
                }

                if (pIndex == editor.CaretPosition.ParagraphIndex)
                {
                    int adjustedCaretOffset = editor.CaretPosition.Offset + prefixCharsCount;
                    Rect charRect = layout.HitTestTextPosition(adjustedCaretOffset);

                    if (charRect.Y >= textLayoutInternalY - 0.1 && charRect.Y < textLayoutInternalY + textLine.Height - 0.1)
                    {
                        double caretX = document.MarginLeft + charRect.X;
                        currentPage.CaretBounds = new Rect(caretX, currentY, 1.5, textLine.Height);
                    }
                }

                textLayoutInternalY += textLine.Height;
                currentY += textLine.Height * paragraph.LineSpacing;
            }
        }

        if (currentPage.Lines.Count > 0 || pages.Count == 0 || currentPage.Images.Count > 0)
        {
            pages.Add(currentPage);
        }

        return pages;
    }

    public DocumentHitResult? GetPositionFromPoint(RenderedPage page, Point clickPoint)
    {
        foreach (ImagePlacement image in page.Images)
        {
            if (image.Bounds.Contains(clickPoint))
            {
                return new DocumentHitResult(image.ParagraphIndex);
            }
        }

        foreach (TextLinePlacement line in page.Lines)
        {
            if (clickPoint.Y >= line.Location.Y && clickPoint.Y <= line.Location.Y + line.Line.Height)
            {
                double layoutX = clickPoint.X - line.Location.X;
                double layoutY = line.InternalY + (clickPoint.Y - line.Location.Y);

                if (layoutX > line.Line.Width)
                {
                    layoutX = line.Line.Width;
                }

                if (layoutX < 0)
                {
                    layoutX = 0;
                }

                TextHitTestResult hitTest = line.ParentLayout.HitTestPoint(new Point(layoutX, layoutY));

                int paragraphIndex = line.ParagraphIndex;
                int clickedOffset = hitTest.TextPosition;

                clickedOffset = Math.Max(0, clickedOffset - line.PrefixLength);

                return new DocumentHitResult(new DocumentPosition(paragraphIndex, clickedOffset));
            }
        }

        return null;
    }
}
