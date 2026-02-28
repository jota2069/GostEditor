using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Utilities;

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

            // 1. ОПРЕДЕЛЯЕМ БАЗОВЫЕ НАСТРОЙКИ АБЗАЦА ПО ЕГО СТИЛЮ
            double baseFontSize = 18.67; // 14pt (стандарт ГОСТ)
            FontWeight baseWeight = FontWeight.Normal;
            FontFamily baseFontFamily = typeface.FontFamily;

            if (paragraph.Style == ParagraphStyle.Heading1)
            {
                baseFontSize = 21.33; // 16pt
                baseWeight = FontWeight.Bold;
            }
            else if (paragraph.Style == ParagraphStyle.Heading2)
            {
                baseFontSize = 18.67; // 14pt
                baseWeight = FontWeight.Normal;
            }
            else if (paragraph.Style == ParagraphStyle.Code)
            {
                baseFontSize = 16.0; // 12pt для кода
                baseFontFamily = new FontFamily("Consolas");
            }

            Typeface baseTypeface = new Typeface(baseFontFamily, FontStyle.Normal, baseWeight);

            // 2. ПРИМЕНЯЕМ ВНУТРЕННИЕ СТИЛИ (ЖИРНЫЙ/КУРСИВ/РАЗМЕР) ТОЛЬКО ТАМ, ГДЕ ОНИ ОТЛИЧАЮТСЯ
            List<ValueSpan<TextRunProperties>> styleOverrides = new List<ValueSpan<TextRunProperties>>();
            int currentPos = 0;

            foreach (GostEditor.Core.TextEngine.DOM.TextRun run in paragraph.Runs)
            {
                if (string.IsNullOrEmpty(run.Text)) continue;

                // Перевод пунктов (pt) в пиксели Avalonia (1 pt = 1.3333 px)
                double runFontSizePx = run.FontSize * 1.3333333333333333;

                FontWeight effectiveWeight = run.IsBold || baseWeight == FontWeight.Bold ? FontWeight.Bold : FontWeight.Normal;
                FontStyle effectiveStyle = run.IsItalic ? FontStyle.Italic : FontStyle.Normal;

                // Если стиль отличается от базового ИЛИ размер шрифта отличается от базового
                if (effectiveWeight != baseWeight || effectiveStyle != FontStyle.Normal || System.Math.Abs(runFontSizePx - baseFontSize) > 0.1)
                {
                    Typeface runTypeface = new Typeface(baseFontFamily, effectiveStyle, effectiveWeight);
                    TextRunProperties props = new GenericTextRunProperties(runTypeface, runFontSizePx, null, Brushes.Black);

                    styleOverrides.Add(new ValueSpan<TextRunProperties>(currentPos, run.Text.Length, props));
                }

                currentPos += run.Text.Length;
            }

            // 3. ПРИМЕНЯЕМ ВЫРАВНИВАНИЕ
            Avalonia.Media.TextAlignment avaloniaAlignment = paragraph.Alignment switch
            {
                GostEditor.Core.TextEngine.DOM.GostAlignment.Center => Avalonia.Media.TextAlignment.Center,
                GostEditor.Core.TextEngine.DOM.GostAlignment.Right => Avalonia.Media.TextAlignment.Right,
                GostEditor.Core.TextEngine.DOM.GostAlignment.Justify => Avalonia.Media.TextAlignment.Justify,
                _ => Avalonia.Media.TextAlignment.Left
            };

            // 4. ФОРМИРУЕМ LAYOUT АБЗАЦА
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
            bool isFirstLineOfParagraph = true;

            // 5. РАСЧЕТ ВЫДЕЛЕНИЯ
            List<Rect> selectionRects = new List<Rect>();
            if (editor.HasSelection && pIndex >= selStart.ParagraphIndex && pIndex <= selEnd.ParagraphIndex)
            {
                int start = pIndex == selStart.ParagraphIndex ? selStart.Offset : 0;
                int end = pIndex == selEnd.ParagraphIndex ? selEnd.Offset : plainText.Length;

                if (end > start)
                {
                    selectionRects = layout.HitTestTextRange(start, end - start).ToList();
                }
            }

            // 6. ОТРИСОВКА СТРОК
            foreach (TextLine textLine in layout.TextLines)
            {
                if (currentY + textLine.Height > maxBottom)
                {
                    pages.Add(currentPage);
                    currentPage = new RenderedPage { PageNumber = pages.Count + 1 };
                    currentY = document.MarginTop;
                }

                double currentX = document.MarginLeft;

                // ГОСТ: У заголовков по центру обычно нет красной строки, чтобы они стояли строго по центру страницы
                if (isFirstLineOfParagraph && paragraph.Alignment != GostEditor.Core.TextEngine.DOM.GostAlignment.Center)
                {
                    currentX += paragraph.FirstLineIndent;
                }

                Point location = new Point(currentX, currentY);
                currentPage.Lines.Add(new TextLinePlacement(textLine, location, pIndex, paragraphInternalY, layout));

                foreach (Rect rect in selectionRects)
                {
                    if (rect.Y >= paragraphInternalY - 0.1 && rect.Y < paragraphInternalY + textLine.Height - 0.1)
                    {
                        double selX = document.MarginLeft + rect.X;
                        if (isFirstLineOfParagraph && paragraph.Alignment != GostEditor.Core.TextEngine.DOM.GostAlignment.Center)
                            selX += paragraph.FirstLineIndent;

                        currentPage.SelectionBounds.Add(new Rect(selX, currentY, rect.Width, rect.Height));
                    }
                }

                if (pIndex == editor.CaretPosition.ParagraphIndex)
                {
                    Rect charRect = layout.HitTestTextPosition(editor.CaretPosition.Offset);
                    if (charRect.Y >= paragraphInternalY - 0.1 && charRect.Y < paragraphInternalY + textLine.Height - 0.1)
                    {
                        double caretX = document.MarginLeft + charRect.X;
                        if (isFirstLineOfParagraph && paragraph.Alignment != GostEditor.Core.TextEngine.DOM.GostAlignment.Center)
                            caretX += paragraph.FirstLineIndent;

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
        foreach (TextLinePlacement line in page.Lines)
        {
            if (clickPoint.Y >= line.Location.Y && clickPoint.Y <= line.Location.Y + line.Line.Height)
            {
                double layoutX = clickPoint.X - line.Location.X;
                double layoutY = line.InternalY + (clickPoint.Y - line.Location.Y);

                if (layoutX > line.Line.Width) layoutX = line.Line.Width;
                if (layoutX < 0) layoutX = 0;

                TextHitTestResult hitTest = line.ParentLayout.HitTestPoint(new Point(layoutX, layoutY));
                return new DocumentPosition(line.ParagraphIndex, hitTest.TextPosition);
            }
        }
        return null;
    }
}
