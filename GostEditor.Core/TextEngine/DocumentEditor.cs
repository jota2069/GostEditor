using GostEditor.Core.TextEngine.DOM;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GostEditor.Core.TextEngine;

public class DocumentEditor
{
    public GostDocument Document { get; }

    // Позиция мигающего курсора
    public DocumentPosition CaretPosition { get; set; }

    // Точка, где мы нажали мышку (начало выделения)
    public DocumentPosition? SelectionAnchor { get; set; }

    // Проверка, выделено ли что-то в данный момент
    public bool HasSelection => SelectionAnchor.HasValue && SelectionAnchor.Value.CompareTo(CaretPosition) != 0;

    public DocumentEditor(GostDocument document)
    {
        Document = document;
        CaretPosition = new DocumentPosition(0, 0);
    }

    /// <summary>
    /// Возвращает упорядоченные границы выделения (от меньшего к большему).
    /// </summary>
    public (DocumentPosition Start, DocumentPosition End) GetNormalizedSelection()
    {
        if (!HasSelection) return (CaretPosition, CaretPosition);

        return SelectionAnchor!.Value.CompareTo(CaretPosition) < 0
            ? (SelectionAnchor.Value, CaretPosition)
            : (CaretPosition, SelectionAnchor.Value);
    }

    /// <summary>
    /// Сбрасывает выделение, оставляя только курсор.
    /// </summary>
    public void ClearSelection()
    {
        SelectionAnchor = null;
    }

    /// <summary>
    /// Вставляет текст в текущую позицию.
    /// </summary>
    public void InsertText(string text)
    {
        if (HasSelection)
        {
            // Если есть выделение, удаляем его.
            // Каретка сама встанет в начало удаленного куска.
            DeleteSelection();
        }

        ClearSelection();

        Paragraph currentParagraph = Document.Paragraphs[CaretPosition.ParagraphIndex];

        // Если абзац оказался абсолютно пустым (удалили все Runs)
        if (currentParagraph.Runs.Count == 0)
        {
            currentParagraph.Runs.Add(new TextRun(text));
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex, text.Length);
            return;
        }

        int currentOffset = 0;
        TextRun? targetRun = null;

        // ЯВНАЯ ТИПИЗАЦИЯ: TextRun вместо var
        foreach (TextRun run in currentParagraph.Runs)
        {
            // Находим кусок текста, в который попадает каретка
            if (CaretPosition.Offset <= currentOffset + run.Text.Length)
            {
                targetRun = run;
                break;
            }
            currentOffset += run.Text.Length;
        }

        if (targetRun != null)
        {
            int insertIndexInRun = CaretPosition.Offset - currentOffset;
            targetRun.Text = targetRun.Text.Insert(insertIndexInRun, text);
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex, CaretPosition.Offset + text.Length);
        }
    }

    /// <summary>
    /// Перенос строки (Enter).
    /// </summary>
    public void InsertNewLine()
    {
        ClearSelection();

        Paragraph currentParagraph = Document.Paragraphs[CaretPosition.ParagraphIndex];
        string fullText = currentParagraph.GetPlainText();

        string firstPart = fullText.Substring(0, CaretPosition.Offset);
        string secondPart = fullText.Substring(CaretPosition.Offset);

        currentParagraph.Runs.Clear();
        currentParagraph.Runs.Add(new TextRun { Text = firstPart });

        Paragraph newParagraph = new Paragraph();
        newParagraph.Runs.Add(new TextRun { Text = secondPart });

        Document.Paragraphs.Insert(CaretPosition.ParagraphIndex + 1, newParagraph);
        CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex + 1, 0);
    }

    /// <summary>
    /// Удаление символа (Backspace).
    /// </summary>
    public void Backspace()
    {
        if (HasSelection)
        {
            DeleteSelection();
            return;
        }

        if (CaretPosition.Offset == 0)
        {
            if (CaretPosition.ParagraphIndex > 0)
            {
                int prevIndex = CaretPosition.ParagraphIndex - 1;
                Paragraph prevP = Document.Paragraphs[prevIndex];
                Paragraph currP = Document.Paragraphs[CaretPosition.ParagraphIndex];

                int newOffset = prevP.GetPlainText().Length;
                prevP.Runs.AddRange(currP.Runs);
                Document.Paragraphs.RemoveAt(CaretPosition.ParagraphIndex);

                CaretPosition = new DocumentPosition(prevIndex, newOffset);
            }
            return;
        }

        Paragraph p = Document.Paragraphs[CaretPosition.ParagraphIndex];
        int currentOffset = 0;
        foreach (var run in p.Runs)
        {
            if (CaretPosition.Offset <= currentOffset + run.Text.Length)
            {
                int deleteIdx = CaretPosition.Offset - currentOffset - 1;
                if (deleteIdx >= 0)
                {
                    run.Text = run.Text.Remove(deleteIdx, 1);
                    CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex, CaretPosition.Offset - 1);
                }
                break;
            }
            currentOffset += run.Text.Length;
        }
    }

    public void MoveLeft()
    {
        ClearSelection();
        if (CaretPosition.Offset > 0)
        {
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex, CaretPosition.Offset - 1);
        }
        else if (CaretPosition.ParagraphIndex > 0)
        {
            int prevIdx = CaretPosition.ParagraphIndex - 1;
            CaretPosition = new DocumentPosition(prevIdx, Document.Paragraphs[prevIdx].GetPlainText().Length);
        }
    }

    public void MoveRight()
    {
        ClearSelection();
        int currentLength = Document.Paragraphs[CaretPosition.ParagraphIndex].GetPlainText().Length;
        if (CaretPosition.Offset < currentLength)
        {
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex, CaretPosition.Offset + 1);
        }
        else if (CaretPosition.ParagraphIndex < Document.Paragraphs.Count - 1)
        {
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex + 1, 0);
        }
    }


    /// <summary>
    /// Гарантирует, что в указанном смещении (offset) заканчивается один TextRun и начинается другой.
    /// Это нужно, чтобы мы могли менять стиль куска текста, не затрагивая соседей.
    /// </summary>
    private void SplitAt(int paragraphIndex, int offset)
    {
        Paragraph p = Document.Paragraphs[paragraphIndex];
        int currentOffset = 0;

        for (int i = 0; i < p.Runs.Count; i++)
        {
            TextRun run = p.Runs[i];

            // Если смещение попадает прямо внутрь этого куска текста
            if (offset > currentOffset && offset < currentOffset + run.Text.Length)
            {
                int splitIdx = offset - currentOffset;

                // Создаем новый кусок с той же стилистикой
                TextRun nextRun = new TextRun(run.Text.Substring(splitIdx), run.IsBold, run.IsItalic);

                // Обрезаем старый
                run.Text = run.Text.Substring(0, splitIdx);

                // Вставляем новый следом
                p.Runs.Insert(i + 1, nextRun);
                return;
            }
            currentOffset += run.Text.Length;
        }
    }


    /// <summary>
    /// Инвертирует жирность у выделенного текста (применяет или снимает).
    /// </summary>
    public void ToggleBold()
    {
        if (!HasSelection) return;

        (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();

        // Проходим по всем абзацам, которые затронуты выделением
        for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
        {
            Paragraph p = Document.Paragraphs[pIdx];

            // 1. Подготавливаем границы (разрезаем куски на стыках выделения)
            int pStartOffset = (pIdx == start.ParagraphIndex) ? start.Offset : 0;
            int pEndOffset = (pIdx == end.ParagraphIndex) ? end.Offset : p.GetPlainText().Length;

            // Важное правило: сначала режем с конца, потом с начала.
            SplitAt(pIdx, pEndOffset);
            SplitAt(pIdx, pStartOffset);

            // 2. Проходим по всем TextRun этого абзаца и меняем стиль тем, кто ВНУТРИ
            int currentOffset = 0;
            foreach (TextRun run in p.Runs)
            {
                int runStart = currentOffset;
                int runEnd = currentOffset + run.Text.Length;

                // Если этот кусок полностью лежит внутри выделенного диапазона
                if (runStart >= pStartOffset && runEnd <= pEndOffset && run.Text != "")
                {
                    // Инвертируем: если был обычный - станет жирным, и наоборот
                    run.IsBold = !run.IsBold;
                }
                currentOffset += run.Text.Length;
            }
        }
    }

   /// <summary>
    /// Полностью удаляет текст внутри выделенного диапазона, включая объединение абзацев.
    /// </summary>
    public void DeleteSelection()
    {
        if (!HasSelection) return;

        // ЯВНАЯ ТИПИЗАЦИЯ: Явно указываем типы в кортеже
        (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();

        // Разрезаем куски точно по границам выделения
        SplitAt(end.ParagraphIndex, end.Offset);
        SplitAt(start.ParagraphIndex, start.Offset);

        if (start.ParagraphIndex == end.ParagraphIndex)
        {
            // --- ЛОГИКА 1: Удаление внутри ОДНОГО абзаца ---
            Paragraph paragraph = Document.Paragraphs[start.ParagraphIndex];
            int currentOffset = 0;

            for (int i = 0; i < paragraph.Runs.Count; i++)
            {
                // ЯВНАЯ ТИПИЗАЦИЯ
                TextRun run = paragraph.Runs[i];
                int runStart = currentOffset;
                int runEnd = currentOffset + run.Text.Length;

                currentOffset += run.Text.Length;

                // Если кусок полностью внутри выделения - сносим его
                if (runStart >= start.Offset && runEnd <= end.Offset)
                {
                    paragraph.Runs.RemoveAt(i);
                    i--; // Сдвигаем индекс назад, так как список уменьшился
                }
            }
        }
        else
        {
            // --- ЛОГИКА 2: Удаление через НЕСКОЛЬКО абзацев ---
            // ЯВНАЯ ТИПИЗАЦИЯ
            Paragraph startP = Document.Paragraphs[start.ParagraphIndex];
            Paragraph endP = Document.Paragraphs[end.ParagraphIndex];

            // 1. Очищаем первый абзац от всего, что правее старта выделения
            int currentOffset = 0;
            for (int i = 0; i < startP.Runs.Count; i++)
            {
                int runStart = currentOffset;
                currentOffset += startP.Runs[i].Text.Length;

                if (runStart >= start.Offset)
                {
                    startP.Runs.RemoveAt(i);
                    i--;
                }
            }

            // 2. Очищаем последний абзац от всего, что левее конца выделения
            currentOffset = 0;
            for (int i = 0; i < endP.Runs.Count; i++)
            {
                int runEnd = currentOffset + endP.Runs[i].Text.Length;
                currentOffset += endP.Runs[i].Text.Length;

                if (runEnd <= end.Offset)
                {
                    endP.Runs.RemoveAt(i);
                    i--;
                }
            }

            // 3. Приклеиваем "хвост" последнего абзаца к первому
            startP.Runs.AddRange(endP.Runs);

            // 4. Удаляем все промежуточные абзацы и сам последний
            int paragraphsToRemove = end.ParagraphIndex - start.ParagraphIndex;
            for (int i = 0; i < paragraphsToRemove; i++)
            {
                // Всегда удаляем индекс start+1, так как после удаления элементы сдвигаются
                Document.Paragraphs.RemoveAt(start.ParagraphIndex + 1);
            }
        }

        // Ставим каретку в начало удаленного куска и сбрасываем выделение
        CaretPosition = start;
        ClearSelection();
    }

    /// <summary>
    /// Инвертирует курсив у выделенного текста.
    /// </summary>
    public void ToggleItalic()
    {
        if (!HasSelection) return;

        (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();

        for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
        {
            Paragraph p = Document.Paragraphs[pIdx];

            int pStartOffset = (pIdx == start.ParagraphIndex) ? start.Offset : 0;
            int pEndOffset = (pIdx == end.ParagraphIndex) ? end.Offset : p.GetPlainText().Length;

            SplitAt(pIdx, pEndOffset);
            SplitAt(pIdx, pStartOffset);

            int currentOffset = 0;
            foreach (GostEditor.Core.TextEngine.DOM.TextRun run in p.Runs)
            {
                int runStart = currentOffset;
                int runEnd = currentOffset + run.Text.Length;

                if (runStart >= pStartOffset && runEnd <= pEndOffset && run.Text != "")
                {
                    // Инвертируем наклон
                    run.IsItalic = !run.IsItalic;
                }
                currentOffset += run.Text.Length;
            }
        }
    }


    /// <summary>
    /// Возвращает выделенный текст в виде строки для буфера обмена.
    /// </summary>
    public string GetSelectedText()
    {
        if (!HasSelection) return string.Empty;

        (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();
        System.Text.StringBuilder resultBuilder = new System.Text.StringBuilder();

        for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
        {
            Paragraph currentParagraph = Document.Paragraphs[pIdx];
            string plainText = currentParagraph.GetPlainText();

            int startIndex = (pIdx == start.ParagraphIndex) ? start.Offset : 0;
            int endIndex = (pIdx == end.ParagraphIndex) ? end.Offset : plainText.Length;

            resultBuilder.Append(plainText.Substring(startIndex, endIndex - startIndex));

            if (pIdx < end.ParagraphIndex)
            {
                resultBuilder.AppendLine();
            }
        }

        return resultBuilder.ToString();
    }

    /// <summary>
    /// Вставляет многострочный текст из буфера обмена.
    /// </summary>
    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (HasSelection)
        {
            DeleteSelection();
        }

        // Очищаем текст от возврата каретки \r (оставляем только \n)
        string normalizedText = text.Replace("\r", "");

        // ЯВНАЯ ТИПИЗАЦИЯ: Массив строк
        string[] lines = normalizedText.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (!string.IsNullOrEmpty(line))
            {
                InsertText(line); // Вызываем наш надежный метод для одной строки
            }

            // Если это не последняя строка, делаем перенос
            if (i < lines.Length - 1)
            {
                InsertNewLine();
            }
        }
    }

    /// <summary>
    /// Применяет выравнивание ко всем выделенным абзацам (или к текущему, если выделения нет).
    /// </summary>
    public void SetAlignment(GostEditor.Core.TextEngine.DOM.GostAlignment alignment)
    {
        // ЯВНАЯ ТИПИЗАЦИЯ
        (GostEditor.Core.TextEngine.DOM.DocumentPosition start, GostEditor.Core.TextEngine.DOM.DocumentPosition end) = GetNormalizedSelection();

        for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
        {
            GostEditor.Core.TextEngine.DOM.Paragraph p = Document.Paragraphs[pIdx];
            p.Alignment = alignment;
        }
    }

}
