using GostEditor.Core.TextEngine.DOM;
using GostEditor.Core.TextEngine.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GostEditor.Core.TextEngine;

public class DocumentEditor
{
    public GostDocument Document { get; }
    public DocumentPosition CaretPosition { get; set; }
    public DocumentPosition? SelectionAnchor { get; set; }
    public CommandManager History { get; } = new CommandManager();

    public bool HasSelection => SelectionAnchor.HasValue && SelectionAnchor.Value.CompareTo(CaretPosition) != 0;

    public DocumentEditor(GostDocument document)
    {
        Document = document;
        CaretPosition = new DocumentPosition(0, 0);
    }

    public void SelectAll()
    {
        int paragraphCount = Document.Paragraphs.Count;
        if (paragraphCount == 0) return;

        SelectionAnchor = new DocumentPosition(0, 0);

        int lastParagraphIndex = paragraphCount - 1;
        Paragraph lastParagraph = Document.Paragraphs[lastParagraphIndex];
        int lastOffset = lastParagraph.GetPlainText().Length;

        CaretPosition = new DocumentPosition(lastParagraphIndex, lastOffset);
    }

    public (DocumentPosition Start, DocumentPosition End) GetNormalizedSelection()
    {
        if (!HasSelection) return (CaretPosition, CaretPosition);

        return SelectionAnchor!.Value.CompareTo(CaretPosition) < 0
            ? (SelectionAnchor.Value, CaretPosition)
            : (CaretPosition, SelectionAnchor.Value);
    }

    public void ClearSelection()
    {
        SelectionAnchor = null;
    }

    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (HasSelection)
        {
            DeleteSelection();
        }

        ClearSelection();

        InsertTextCommand command = new InsertTextCommand(this, text, CaretPosition);
        History.ExecuteCommand(command);
    }

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

    private void SplitAt(int paragraphIndex, int offset)
    {
        Paragraph p = Document.Paragraphs[paragraphIndex];
        int currentOffset = 0;

        for (int i = 0; i < p.Runs.Count; i++)
        {
            TextRun run = p.Runs[i];

            if (offset > currentOffset && offset < currentOffset + run.Text.Length)
            {
                int splitIdx = offset - currentOffset;
                TextRun nextRun = new TextRun(run.Text.Substring(splitIdx), run.IsBold, run.IsItalic);
                run.Text = run.Text.Substring(0, splitIdx);
                p.Runs.Insert(i + 1, nextRun);
                return;
            }
            currentOffset += run.Text.Length;
        }
    }

    public void ToggleBold()
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
            foreach (TextRun run in p.Runs)
            {
                int runStart = currentOffset;
                int runEnd = currentOffset + run.Text.Length;

                if (runStart >= pStartOffset && runEnd <= pEndOffset && run.Text != "")
                {
                    run.IsBold = !run.IsBold;
                }
                currentOffset += run.Text.Length;
            }
        }
    }

    public void SetFontSize(double fontSize)
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
            foreach (TextRun run in p.Runs)
            {
                int runStart = currentOffset;
                int runEnd = currentOffset + run.Text.Length;

                if (runStart >= pStartOffset && runEnd <= pEndOffset && run.Text != "")
                {
                    // Устанавливаем новый размер
                    run.FontSize = fontSize;
                }
                currentOffset += run.Text.Length;
            }
        }
    }

    public void DeleteSelection()
    {
        if (!HasSelection) return;

        (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();

        DeleteRangeInternal(start, end);

        CaretPosition = start;
        ClearSelection();
    }

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
            foreach (TextRun run in p.Runs)
            {
                int runStart = currentOffset;
                int runEnd = currentOffset + run.Text.Length;

                if (runStart >= pStartOffset && runEnd <= pEndOffset && run.Text != "")
                {
                    run.IsItalic = !run.IsItalic;
                }
                currentOffset += run.Text.Length;
            }
        }
    }

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

    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (HasSelection)
        {
            DeleteSelection();
        }

        string normalizedText = text.Replace("\r", "");
        string[] lines = normalizedText.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (!string.IsNullOrEmpty(line))
            {
                InsertText(line);
            }

            if (i < lines.Length - 1)
            {
                InsertNewLine();
            }
        }
    }

    public void SetAlignment(GostAlignment alignment)
    {
        (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();

        for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
        {
            Paragraph p = Document.Paragraphs[pIdx];
            p.Alignment = alignment;
        }
    }

    internal DocumentPosition InsertTextInternal(DocumentPosition position, string text)
    {
        Paragraph currentParagraph = Document.Paragraphs[position.ParagraphIndex];

        if (currentParagraph.Runs.Count == 0)
        {
            currentParagraph.Runs.Add(new TextRun(text));
            return new DocumentPosition(position.ParagraphIndex, text.Length);
        }

        int currentOffset = 0;
        TextRun? targetRun = null;

        foreach (TextRun run in currentParagraph.Runs)
        {
            if (position.Offset <= currentOffset + run.Text.Length)
            {
                targetRun = run;
                break;
            }
            currentOffset += run.Text.Length;
        }

        if (targetRun != null)
        {
            int insertIndexInRun = position.Offset - currentOffset;
            targetRun.Text = targetRun.Text.Insert(insertIndexInRun, text);
            return new DocumentPosition(position.ParagraphIndex, position.Offset + text.Length);
        }

        return position;
    }

    internal void DeleteRangeInternal(DocumentPosition start, DocumentPosition end)
    {
        SplitAt(end.ParagraphIndex, end.Offset);
        SplitAt(start.ParagraphIndex, start.Offset);

        if (start.ParagraphIndex == end.ParagraphIndex)
        {
            Paragraph paragraph = Document.Paragraphs[start.ParagraphIndex];
            int currentOffset = 0;

            for (int i = 0; i < paragraph.Runs.Count; i++)
            {
                TextRun run = paragraph.Runs[i];
                int runStart = currentOffset;
                int runEnd = currentOffset + run.Text.Length;

                currentOffset += run.Text.Length;

                if (runStart >= start.Offset && runEnd <= end.Offset)
                {
                    paragraph.Runs.RemoveAt(i);
                    i--;
                }
            }
        }
        else
        {
            Paragraph startP = Document.Paragraphs[start.ParagraphIndex];
            Paragraph endP = Document.Paragraphs[end.ParagraphIndex];

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

            startP.Runs.AddRange(endP.Runs);

            int paragraphsToRemove = end.ParagraphIndex - start.ParagraphIndex;
            for (int i = 0; i < paragraphsToRemove; i++)
            {
                Document.Paragraphs.RemoveAt(start.ParagraphIndex + 1);
            }
        }
    }

    /// <summary>
    /// Массово добавляет абзацы в конец документа через систему команд.
    /// </summary>
    public void AppendParagraphs(List<Paragraph> paragraphs)
    {
        if (paragraphs == null || paragraphs.Count == 0) return;

        AppendParagraphsCommand command = new AppendParagraphsCommand(this, paragraphs);
        History.ExecuteCommand(command);
    }

    /// <summary>
    /// Применяет выбранный стиль ко всем выделенным абзацам (или к текущему, если выделения нет).
    /// </summary>
    public void SetParagraphStyle(ParagraphStyle style)
    {
        (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();

        ChangeParagraphStyleCommand command = new ChangeParagraphStyleCommand(
            this,
            style,
            start.ParagraphIndex,
            end.ParagraphIndex);

        History.ExecuteCommand(command);
    }

}
