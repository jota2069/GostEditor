using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Layout;

namespace GostEditor.UI.Services;

/// <summary>
/// Сервис для генерации таблицы содержания (оглавления) на основе заголовков документа
/// </summary>
public class TableOfContentsGenerator : ITableOfContentsService
{
    /// <summary>
    /// Генерирует таблицу содержания, сканируя документ и находя реальные номера страниц
    /// </summary>
    public List<TocEntry> GenerateTableOfContents(
        GostDocument document,
        dynamic renderedPages,
        int contentStartPage)
    {
        List<TocEntry> tocEntries = new List<TocEntry>();

        if (document?.Paragraphs == null || document.Paragraphs.Count == 0)
        {
            Debug.WriteLine("[TOC] Документ пуст или не содержит параграфов");
            return tocEntries;
        }

        if (renderedPages == null)
        {
            Debug.WriteLine("[TOC] Список отрендеренных страниц пуст");
            return tocEntries;
        }

        List<RenderedPage> pages = (List<RenderedPage>)renderedPages;

        if (pages.Count == 0)
        {
            Debug.WriteLine("[TOC] Список отрендеренных страниц пуст");
            return tocEntries;
        }

        Debug.WriteLine($"[TOC] Начало генерации оглавления. Параграфов: {document.Paragraphs.Count}");

        for (int pIndex = 0; pIndex < document.Paragraphs.Count; pIndex++)
        {
            Paragraph paragraph = document.Paragraphs[pIndex];

            // Ищем только заголовки
            if (paragraph.Style != ParagraphStyle.Heading1 && paragraph.Style != ParagraphStyle.Heading2)
            {
                continue;
            }

            // Получаем текст заголовка
            string title = paragraph.GetPlainText();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            // Определяем уровень
            int level = paragraph.Style == ParagraphStyle.Heading1 ? 1 : 2;

            // Находим номер страницы для этого параграфа
            int pageNumber = GetPageNumberForParagraph(pIndex, pages, contentStartPage);

            tocEntries.Add(new TocEntry
            {
                Title = title,
                Level = level,
                PageNumber = pageNumber,
                ParagraphIndex = pIndex
            });

            Debug.WriteLine($"[TOC] Добавлен заголовок (Heading{level}): '{title}' на странице {pageNumber}");
        }

        Debug.WriteLine($"[TOC] Всего записей в оглавлении: {tocEntries.Count}");

        return tocEntries;
    }

    /// <summary>
    /// Находит номер страницы для заданного параграфа, учитывая contentStartPage
    /// </summary>
    private static int GetPageNumberForParagraph(
        int paragraphIndex,
        List<RenderedPage> renderedPages,
        int contentStartPage)
    {
        // Ищем первую строку этого параграфа на какой-либо странице
        foreach (RenderedPage page in renderedPages)
        {
            // Проверяем текстовые строки
            foreach (TextLinePlacement textLine in page.Lines)
            {
                if (textLine.ParagraphIndex == paragraphIndex)
                {
                    // Найдена строка этого параграфа на этой странице
                    // Номер страницы в RenderedPage относительный, нужно добавить contentStartPage
                    int absolutePageNumber = page.PageNumber + contentStartPage - 1;
                    return absolutePageNumber;
                }
            }

            // Проверяем картинки
            foreach (ImagePlacement image in page.Images)
            {
                if (image.ParagraphIndex == paragraphIndex)
                {
                    // Найдена картинка этого параграфа на этой странице
                    int absolutePageNumber = page.PageNumber + contentStartPage - 1;
                    return absolutePageNumber;
                }
            }
        }

        // Если не нашли, возвращаем ContentStartPage как fallback
        Debug.WriteLine($"[TOC] WARNING: Не найдена страница для параграфа {paragraphIndex}");
        return contentStartPage;
    }
}
