using GostEditor.Core.TextEngine.DOM;
using GostEditor.Core.TextEngine.Commands;
using System;
using System.Collections.Generic;

namespace GostEditor.Core.TextEngine;

public class DocumentEditor
{
    public GostDocument Document { get; }
    public DocumentPosition CaretPosition { get; set; }
    public DocumentPosition? SelectionAnchor { get; set; }
    public int? SelectedImageParagraphIndex { get; set; }

    // Менеджер истории остается на месте
    public CommandManager History { get; } = new CommandManager();

    public bool HasSelection => SelectionAnchor.HasValue && SelectionAnchor.Value.CompareTo(CaretPosition) != 0;

    // Флаг для группировки сложных действий в один шаг истории
    private bool _isExecutingCommand = false;

    public DocumentEditor(GostDocument document)
    {
        Document = document;
        CaretPosition = new DocumentPosition(0, 0);
    }

    // ==========================================
    // СЕРДЦЕ НОВОЙ СИСТЕМЫ ИСТОРИИ (UNDO/REDO)
    // ==========================================
    public void ExecuteWithSnapshot(Action action)
    {
        // Если мы УЖЕ внутри команды (например, Paste вызывает InsertText),
        // просто выполняем код, чтобы не плодить 100 снимков.
        if (_isExecutingCommand)
        {
            action();
            return;
        }

        _isExecutingCommand = true;
        try
        {
            SnapshotCommand command = new SnapshotCommand(this, action);
            History.ExecuteCommand(command);
        }
        finally
        {
            _isExecutingCommand = false;
        }
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
        SelectedImageParagraphIndex = null;
    }

    // Обернуто в Snapshot
    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        ExecuteWithSnapshot(() =>
        {
            if (HasSelection)
            {
                DeleteSelection();
            }

            ClearSelection();
            CaretPosition = InsertTextInternal(CaretPosition, text);
        });
    }

    // Обернуто в Snapshot
    public void InsertNewLine()
    {
        ExecuteWithSnapshot(() =>
        {
            if (HasSelection)
            {
                DeleteSelection();
            }

            ClearSelection();

            Paragraph currentParagraph = Document.Paragraphs[CaretPosition.ParagraphIndex];

            Paragraph newParagraph = new Paragraph
            {
                Alignment = currentParagraph.Alignment,
                FirstLineIndent = currentParagraph.FirstLineIndent,
                LineSpacing = currentParagraph.LineSpacing,
                Style = currentParagraph.Style
            };

            SplitAt(CaretPosition.ParagraphIndex, CaretPosition.Offset);

            int currentLength = 0;
            List<TextRun> leftRuns = new List<TextRun>();
            List<TextRun> rightRuns = new List<TextRun>();
            TextRun? lastLeftRun = null;

            foreach (TextRun run in currentParagraph.Runs)
            {
                if (currentLength < CaretPosition.Offset)
                {
                    leftRuns.Add(run);
                    lastLeftRun = run;
                }
                else
                {
                    rightRuns.Add(run);
                }
                currentLength += run.Text.Length;
            }

            currentParagraph.Runs = leftRuns;
            newParagraph.Runs = rightRuns;

            if (leftRuns.Count == 0 && rightRuns.Count > 0)
            {
                TextRun firstRight = rightRuns[0];
                TextRun emptyRun = new TextRun("", firstRight.IsBold, firstRight.IsItalic);
                emptyRun.FontSize = firstRight.FontSize;
                currentParagraph.Runs.Add(emptyRun);
            }

            if (rightRuns.Count == 0 && lastLeftRun != null)
            {
                TextRun emptyRun = new TextRun("", lastLeftRun.IsBold, lastLeftRun.IsItalic);
                emptyRun.FontSize = lastLeftRun.FontSize;
                newParagraph.Runs.Add(emptyRun);
            }

            Document.Paragraphs.Insert(CaretPosition.ParagraphIndex + 1, newParagraph);
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex + 1, 0);
        });
    }

    // Обернуто в Snapshot
    public void Backspace()
    {
        ExecuteWithSnapshot(() =>
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

            foreach (TextRun run in p.Runs)
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
        });
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

    private void ApplyStyleToCaret(Action<TextRun> styleAction)
    {
        int pIdx = CaretPosition.ParagraphIndex;
        int offset = CaretPosition.Offset;

        SplitAt(pIdx, offset);

        Paragraph p = Document.Paragraphs[pIdx];

        int currentOffset = 0;
        for (int i = 0; i < p.Runs.Count; i++)
        {
            if (currentOffset == offset && p.Runs[i].Text.Length == 0)
            {
                styleAction(p.Runs[i]);
                return;
            }
            currentOffset += p.Runs[i].Text.Length;
        }

        currentOffset = 0;
        int insertIndex = p.Runs.Count;
        TextRun? baseRun = null;

        for (int i = 0; i < p.Runs.Count; i++)
        {
            if (currentOffset == offset)
            {
                insertIndex = i;
                baseRun = i > 0 ? p.Runs[i - 1] : p.Runs[i];
                break;
            }
            currentOffset += p.Runs[i].Text.Length;
            if (currentOffset == offset)
            {
                insertIndex = i + 1;
                baseRun = p.Runs[i];
                break;
            }
        }

        bool isBold = baseRun?.IsBold ?? false;
        bool isItalic = baseRun?.IsItalic ?? false;
        double fontSize = baseRun?.FontSize ?? 14.0;

        TextRun emptyRun = new TextRun("", isBold, isItalic) { FontSize = fontSize };
        styleAction(emptyRun);
        p.Runs.Insert(insertIndex, emptyRun);
    }

    // Обернуто в Snapshot
    public void ToggleBold()
    {
        ExecuteWithSnapshot(() =>
        {
            if (!HasSelection)
            {
                ApplyStyleToCaret(run => run.IsBold = !run.IsBold);
                return;
            }

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
        });
    }

    // Обернуто в Snapshot
    public void ToggleItalic()
    {
        ExecuteWithSnapshot(() =>
        {
            if (!HasSelection)
            {
                ApplyStyleToCaret(run => run.IsItalic = !run.IsItalic);
                return;
            }

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
        });
    }

    // Обернуто в Snapshot
    public void SetFontSize(double fontSize)
    {
        ExecuteWithSnapshot(() =>
        {
            if (!HasSelection)
            {
                ApplyStyleToCaret(run => run.FontSize = fontSize);
                return;
            }

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
                        run.FontSize = fontSize;
                    }
                    currentOffset += run.Text.Length;
                }
            }
        });
    }

    // Обернуто в Snapshot
    public void DeleteSelection()
    {
        if (!HasSelection) return;

        ExecuteWithSnapshot(() =>
        {
            (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();

            DeleteRangeInternal(start, end);

            CaretPosition = start;
            ClearSelection();
        });
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

    // Обернуто в Snapshot
    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        ExecuteWithSnapshot(() =>
        {
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
        });
    }

    // Обернуто в Snapshot
    public void SetAlignment(GostAlignment alignment)
    {
        ExecuteWithSnapshot(() =>
        {
            (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();

            for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
            {
                Paragraph p = Document.Paragraphs[pIdx];
                p.Alignment = alignment;
            }
        });
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
        int runStartOffset = 0;

        foreach (TextRun run in currentParagraph.Runs)
        {
            int runLength = run.Text.Length;

            if (runLength == 0 && position.Offset == currentOffset)
            {
                targetRun = run;
                runStartOffset = currentOffset;
                break;
            }

            if (position.Offset > currentOffset && position.Offset < currentOffset + runLength)
            {
                targetRun = run;
                runStartOffset = currentOffset;
                break;
            }

            if (position.Offset == currentOffset + runLength)
            {
                targetRun = run;
                runStartOffset = currentOffset;
            }

            if (position.Offset == currentOffset && targetRun == null)
            {
                targetRun = run;
                runStartOffset = currentOffset;
            }

            currentOffset += runLength;
        }

        if (targetRun != null)
        {
            int insertIndexInRun = position.Offset - runStartOffset;
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

    // Обернуто в Snapshot
    public void AppendParagraphs(List<Paragraph> paragraphs)
    {
        if (paragraphs == null || paragraphs.Count == 0) return;

        ExecuteWithSnapshot(() =>
        {
            foreach (Paragraph p in paragraphs)
            {
                Document.Paragraphs.Add(p);
            }
        });
    }

    // Обернуто в Snapshot
    public void SetParagraphStyle(ParagraphStyle style)
    {
        ExecuteWithSnapshot(() =>
        {
            (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();

            for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
            {
                Document.Paragraphs[pIdx].Style = style;
            }
        });
    }

    // Обернуто в Snapshot
    public void InsertImage(byte[] imageBytes, double width, double height)
    {
        ExecuteWithSnapshot(() =>
        {
            Paragraph imgPara = new Paragraph
            {
                Alignment = GostAlignment.Center, // По ГОСТу рисунки по центру
                ImageData = imageBytes,
                ImageWidth = width,
                ImageHeight = height
            };

            // Просто создаем пустое место (шрифт 14), чтобы пользователь мог написать название после тире
            imgPara.Runs.Add(new TextRun("", false, false) { FontSize = 14 });

            Document.Paragraphs.Insert(CaretPosition.ParagraphIndex + 1, imgPara);

            // Ставим каретку в самое начало (offset 0), потому что префикс генерируется движком
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex + 1, 0);
            ClearSelection();
        });
    }
}
