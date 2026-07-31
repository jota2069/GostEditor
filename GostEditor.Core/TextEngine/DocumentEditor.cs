using System;
using System.Collections.Generic;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.Commands;
using GostEditor.Core.TextEngine.DOM;
using GostDocument = GostEditor.Core.Models.GostDocument;

namespace GostEditor.Core.TextEngine;

public class DocumentEditor
{
    public GostDocument Document { get; private set; }
    public DocumentPosition CaretPosition { get; set; }
    public DocumentPosition? SelectionAnchor { get; set; }
    public int? SelectedImageParagraphIndex { get; set; }

    public CommandManager History { get; } = new CommandManager();

    public bool HasSelection => SelectionAnchor.HasValue && SelectionAnchor.Value.CompareTo(CaretPosition) != 0;

    private bool _isExecutingCommand = false;

    public DocumentEditor()
    {
        Document = new GostDocument();
        if (Document.Paragraphs.Count == 0)
        {
            Document.Paragraphs.Add(new Paragraph());
        }
        CaretPosition = new DocumentPosition(0, 0);
    }

    public DocumentEditor(GostDocument document)
    {
        Document = document ?? new GostDocument();
        if (Document.Paragraphs.Count == 0)
        {
            Document.Paragraphs.Add(new Paragraph());
        }
        CaretPosition = new DocumentPosition(0, 0);
    }

    public void LoadDocument(GostDocument document)
    {
        if (document is null) return;

        Document = document;
        if (Document.Paragraphs.Count == 0)
        {
            Document.Paragraphs.Add(new Paragraph());
        }
        CaretPosition = new DocumentPosition(0, 0);
        ClearSelection();
        History.Clear();
    }

    public void ApplyBold() => ToggleBold();
    public void ApplyItalic() => ToggleItalic();
    public void ApplyFontSize(double size) => SetFontSize(size);

    public void AlignLeft() => SetAlignment(GostAlignment.Left);
    public void AlignCenter() => SetAlignment(GostAlignment.Center);
    public void AlignRight() => SetAlignment(GostAlignment.Right);
    public void AlignJustify() => SetAlignment(GostAlignment.Justify);

    public void InsertHeading(int level, string text)
    {
        ExecuteWithSnapshot(() =>
        {
            Paragraph heading = new Paragraph();
            heading.Runs.Add(new TextRun(text, isBold: true, isItalic: false)
            {
                FontSize = level == 1 ? 16 : 14
            });

            heading.Style = level == 1 ? ParagraphStyle.Heading1 : ParagraphStyle.Heading2;
            heading.Alignment = GostAlignment.Center;
            heading.FirstLineIndent = 0;
            heading.PageBreakBefore = level == 1;

            Document.Paragraphs.Add(heading);
            CaretPosition = new DocumentPosition(Document.Paragraphs.Count - 1, text.Length);
            ClearSelection();
        });
    }

    public void ScrollToParagraph(int index)
    {
        if (index >= 0 && index < Document.Paragraphs.Count)
        {
            ClearSelection();
            CaretPosition = new DocumentPosition(index, 0);
        }
    }

    public bool FindNext(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText) || Document.Paragraphs.Count == 0)
        {
            return false;
        }

        int startParagraph = Math.Clamp(CaretPosition.ParagraphIndex, 0, Document.Paragraphs.Count - 1);
        int startOffset = Math.Clamp(CaretPosition.Offset, 0, Document.Paragraphs[startParagraph].GetPlainText().Length);

        if (HasSelection)
        {
            (_, DocumentPosition end) = GetNormalizedSelection();
            startParagraph = end.ParagraphIndex;
            startOffset = end.Offset;
        }

        for (int pass = 0; pass < Document.Paragraphs.Count; pass++)
        {
            int pIdx = (startParagraph + pass) % Document.Paragraphs.Count;
            string text = Document.Paragraphs[pIdx].GetPlainText();
            int searchStart = pIdx == startParagraph ? Math.Min(startOffset, text.Length) : 0;
            int index = text.IndexOf(searchText, searchStart, StringComparison.CurrentCultureIgnoreCase);

            if (index >= 0)
            {
                SelectionAnchor = new DocumentPosition(pIdx, index);
                CaretPosition = new DocumentPosition(pIdx, index + searchText.Length);
                SelectedImageParagraphIndex = null;
                return true;
            }
        }

        string startText = Document.Paragraphs[startParagraph].GetPlainText();
        int wrapIndex = startText.IndexOf(searchText, 0, StringComparison.CurrentCultureIgnoreCase);
        if (wrapIndex >= 0 && wrapIndex <= startOffset)
        {
            SelectionAnchor = new DocumentPosition(startParagraph, wrapIndex);
            CaretPosition = new DocumentPosition(startParagraph, wrapIndex + searchText.Length);
            SelectedImageParagraphIndex = null;
            return true;
        }

        return false;
    }

    public void ExecuteWithSnapshot(Action action)
    {
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

    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        ExecuteWithSnapshot(() =>
        {
            if (HasSelection) DeleteSelection();
            ClearSelection();
            CaretPosition = InsertTextInternal(CaretPosition, text);
        });
    }

    public void InsertNewLine()
    {
        ExecuteWithSnapshot(() =>
        {
            if (HasSelection) DeleteSelection();
            ClearSelection();

            Paragraph currentParagraph = Document.Paragraphs[CaretPosition.ParagraphIndex];

            Paragraph newParagraph = new Paragraph
            {
                Alignment = currentParagraph.Alignment,
                FirstLineIndent = currentParagraph.FirstLineIndent,
                LineSpacing = currentParagraph.LineSpacing,
                Style = currentParagraph.Style,
                PageBreakBefore = false
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
                TextRun emptyRun = new TextRun("", firstRight.IsBold, firstRight.IsItalic)
                {
                    FontSize = firstRight.FontSize,
                    Color = firstRight.Color
                };
                currentParagraph.Runs.Add(emptyRun);
            }

            if (rightRuns.Count == 0 && lastLeftRun != null)
            {
                TextRun emptyRun = new TextRun("", lastLeftRun.IsBold, lastLeftRun.IsItalic)
                {
                    FontSize = lastLeftRun.FontSize,
                    Color = lastLeftRun.Color
                };
                newParagraph.Runs.Add(emptyRun);
            }

            if ((currentParagraph.Style == ParagraphStyle.Heading1 || currentParagraph.Style == ParagraphStyle.Heading2) && newParagraph.GetPlainText().Length == 0)
            {
                newParagraph.Style = ParagraphStyle.Normal;
                newParagraph.Alignment = GostAlignment.Justify;
                newParagraph.FirstLineIndent = 47.0;
                newParagraph.Runs.Clear();
                newParagraph.Runs.Add(new TextRun("", false, false) { FontSize = 14 });
            }

            Document.Paragraphs.Insert(CaretPosition.ParagraphIndex + 1, newParagraph);
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex + 1, 0);
        });
    }

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
                TextRun nextRun = new TextRun(run.Text.Substring(splitIdx), run.IsBold, run.IsItalic)
                {
                    FontSize = run.FontSize,
                    Color = run.Color
                };
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
        uint color = baseRun?.Color ?? 0xFF000000;

        TextRun emptyRun = new TextRun("", isBold, isItalic)
        {
            FontSize = fontSize,
            Color = color
        };
        styleAction(emptyRun);
        p.Runs.Insert(insertIndex, emptyRun);
    }

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
            if (pIdx < end.ParagraphIndex) resultBuilder.AppendLine();
        }

        return resultBuilder.ToString();
    }

    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        ExecuteWithSnapshot(() =>
        {
            if (HasSelection) DeleteSelection();

            string normalizedText = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalizedText.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!string.IsNullOrEmpty(line)) InsertText(line);
                if (i < lines.Length - 1) InsertNewLine();
            }
        });
    }

    public void ClearFormatting()
    {
        ExecuteWithSnapshot(() =>
        {
            (DocumentPosition start, DocumentPosition end) = HasSelection
                ? GetNormalizedSelection()
                : (new DocumentPosition(CaretPosition.ParagraphIndex, 0),
                    new DocumentPosition(CaretPosition.ParagraphIndex, Document.Paragraphs[CaretPosition.ParagraphIndex].GetPlainText().Length));

            for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
            {
                Paragraph paragraph = Document.Paragraphs[pIdx];
                int pStartOffset = pIdx == start.ParagraphIndex ? start.Offset : 0;
                int pEndOffset = pIdx == end.ParagraphIndex ? end.Offset : paragraph.GetPlainText().Length;

                paragraph.Style = ParagraphStyle.Normal;
                paragraph.Alignment = GostAlignment.Justify;
                paragraph.FirstLineIndent = 47.0;
                paragraph.PageBreakBefore = false;

                SplitAt(pIdx, pEndOffset);
                SplitAt(pIdx, pStartOffset);

                int currentOffset = 0;
                foreach (TextRun run in paragraph.Runs)
                {
                    int runStart = currentOffset;
                    int runEnd = currentOffset + run.Text.Length;
                    if (runStart >= pStartOffset && runEnd <= pEndOffset)
                    {
                        run.IsBold = false;
                        run.IsItalic = false;
                        run.FontSize = 14;
                        run.Color = 0xFF000000;
                    }

                    currentOffset += run.Text.Length;
                }
            }

            ClearSelection();
        });
    }

    public void InsertTextBlock()
    {
        ExecuteWithSnapshot(() =>
        {
            Paragraph paragraph = new Paragraph
            {
                Alignment = GostAlignment.Justify,
                FirstLineIndent = 47.0,
                LineSpacing = 1.5,
                Style = ParagraphStyle.Normal
            };
            paragraph.Runs.Add(new TextRun(string.Empty, false, false) { FontSize = 14 });

            int insertIndex = Math.Min(CaretPosition.ParagraphIndex + 1, Document.Paragraphs.Count);
            Document.Paragraphs.Insert(insertIndex, paragraph);
            CaretPosition = new DocumentPosition(insertIndex, 0);
            ClearSelection();
        });
    }

    public void InsertTablePlaceholder(int rows = 3, int columns = 3)
    {
        rows = Math.Clamp(rows, 1, 20);
        columns = Math.Clamp(columns, 1, 8);

        ExecuteWithSnapshot(() =>
        {
            List<Paragraph> tableParagraphs = new List<Paragraph>();

            Paragraph title = new Paragraph
            {
                Alignment = GostAlignment.Right,
                FirstLineIndent = 0,
                Style = ParagraphStyle.Normal
            };
            title.Runs.Add(new TextRun("Таблица 1 - Название таблицы") { FontSize = 14 });
            tableParagraphs.Add(title);

            for (int row = 0; row < rows; row++)
            {
                Paragraph line = new Paragraph
                {
                    Alignment = GostAlignment.Left,
                    FirstLineIndent = 0,
                    Style = ParagraphStyle.Code
                };

                string[] cells = new string[columns];
                for (int column = 0; column < columns; column++)
                {
                    cells[column] = row == 0 ? $"Заголовок {column + 1}" : "Данные";
                }

                line.Runs.Add(new TextRun(string.Join(" | ", cells)) { FontSize = 12 });
                tableParagraphs.Add(line);
            }

            int insertIndex = Math.Min(CaretPosition.ParagraphIndex + 1, Document.Paragraphs.Count);
            Document.Paragraphs.InsertRange(insertIndex, tableParagraphs);
            CaretPosition = new DocumentPosition(insertIndex, 0);
            ClearSelection();
        });
    }

    public void SetAlignment(GostAlignment alignment)
    {
        ExecuteWithSnapshot(() =>
        {
            (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();
            for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
            {
                Document.Paragraphs[pIdx].Alignment = alignment;
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
            if (runLength == 0 && position.Offset == currentOffset) { targetRun = run; runStartOffset = currentOffset; break; }
            if (position.Offset > currentOffset && position.Offset < currentOffset + runLength) { targetRun = run; runStartOffset = currentOffset; break; }
            if (position.Offset == currentOffset + runLength) { targetRun = run; runStartOffset = currentOffset; }
            if (position.Offset == currentOffset && targetRun == null) { targetRun = run; runStartOffset = currentOffset; }
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
                if (runStart >= start.Offset) { startP.Runs.RemoveAt(i); i--; }
            }

            currentOffset = 0;
            for (int i = 0; i < endP.Runs.Count; i++)
            {
                int runEnd = currentOffset + endP.Runs[i].Text.Length;
                currentOffset += endP.Runs[i].Text.Length;
                if (runEnd <= end.Offset) { endP.Runs.RemoveAt(i); i--; }
            }

            startP.Runs.AddRange(endP.Runs);

            int paragraphsToRemove = end.ParagraphIndex - start.ParagraphIndex;
            for (int i = 0; i < paragraphsToRemove; i++)
            {
                Document.Paragraphs.RemoveAt(start.ParagraphIndex + 1);
            }
        }
    }

    public void AppendParagraphs(List<Paragraph> paragraphs)
    {
        if (paragraphs == null || paragraphs.Count == 0) return;
        ExecuteWithSnapshot(() => { foreach (Paragraph p in paragraphs) Document.Paragraphs.Add(p); });
    }

    public void SetParagraphStyle(ParagraphStyle style)
    {
        ExecuteWithSnapshot(() =>
        {
            (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();

            for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
            {
                Document.Paragraphs[pIdx].Style = style;

                if (style == ParagraphStyle.Heading1)
                {
                    Document.Paragraphs[pIdx].Alignment = GostAlignment.Center;
                    Document.Paragraphs[pIdx].FirstLineIndent = 0;
                    Document.Paragraphs[pIdx].PageBreakBefore = true;

                    foreach (TextRun run in Document.Paragraphs[pIdx].Runs)
                    {
                        run.IsBold = true;
                        run.FontSize = 16;
                    }
                }
                else if (style == ParagraphStyle.Heading2)
                {
                    Document.Paragraphs[pIdx].Alignment = GostAlignment.Center;
                    Document.Paragraphs[pIdx].FirstLineIndent = 0;
                    Document.Paragraphs[pIdx].PageBreakBefore = false;

                    foreach (TextRun run in Document.Paragraphs[pIdx].Runs)
                    {
                        run.IsBold = true;
                        run.FontSize = 14;
                    }
                }
                else if (style == ParagraphStyle.Normal)
                {
                    Document.Paragraphs[pIdx].Alignment = GostAlignment.Justify;
                    Document.Paragraphs[pIdx].FirstLineIndent = 47.0;
                    Document.Paragraphs[pIdx].PageBreakBefore = false;

                    foreach (TextRun run in Document.Paragraphs[pIdx].Runs)
                    {
                        run.IsBold = false;
                        run.FontSize = 14;
                    }
                }
            }
        });
    }

    public void InsertImage(byte[] imageBytes, double width, double height)
    {
        ExecuteWithSnapshot(() =>
        {
            Paragraph imgPara = new Paragraph
            {
                Alignment = GostAlignment.Center,
                ImageData = imageBytes,
                ImageWidth = width,
                ImageHeight = height
            };

            imgPara.Runs.Add(new TextRun("", false, false) { FontSize = 14 });

            Document.Paragraphs.Insert(CaretPosition.ParagraphIndex + 1, imgPara);

            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex + 1, 0);
            ClearSelection();
        });
    }
}
