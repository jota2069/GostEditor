using System;
using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.Commands;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine;

public partial class DocumentEditor
{
    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        TextBoundaries.EnsureValidUtf16(text, nameof(text));

        (DocumentPosition mutationStart, DocumentPosition mutationEnd) =
            HasSelection
                ? GetNormalizedSelection()
                : (CaretPosition, CaretPosition);
        int mutationStartParagraph = mutationStart.ParagraphIndex;
        int mutationBeforeCount =
            mutationEnd.ParagraphIndex - mutationStartParagraph + 1;
        bool captureImages = SelectionContainsImage();

        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
        {
            if (HasSelection) DeleteSelection();
            ClearSelection();
            CaretPosition = InsertTextInternal(CaretPosition, text);
        },
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint,
            captureImages: captureImages
        );
    }

    public void InsertNewLine()
    {
        (DocumentPosition mutationStart, DocumentPosition mutationEnd) =
            HasSelection
                ? GetNormalizedSelection()
                : (CaretPosition, CaretPosition);
        int mutationStartParagraph = mutationStart.ParagraphIndex;
        int mutationBeforeCount =
            mutationEnd.ParagraphIndex - mutationStartParagraph + 1;
        bool captureImages = SelectionContainsImage();

        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
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
        },
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint,
            captureImages: captureImages
        );
    }

    public void Backspace()
    {
        int mutationStartParagraph;
        int mutationBeforeCount;
        if (HasSelection)
        {
            (DocumentPosition start, DocumentPosition end) =
                GetNormalizedSelection();
            mutationStartParagraph = start.ParagraphIndex;
            mutationBeforeCount =
                end.ParagraphIndex - start.ParagraphIndex + 1;
        }
        else if (CaretPosition.Offset == 0 &&
                 CaretPosition.ParagraphIndex > 0)
        {
            mutationStartParagraph = CaretPosition.ParagraphIndex - 1;
            mutationBeforeCount = 2;
        }
        else
        {
            mutationStartParagraph = CaretPosition.ParagraphIndex;
            mutationBeforeCount = 1;
        }

        bool captureImages = SelectionContainsImage();

        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
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

                    if (prevP.IsImage || currP.IsImage)
                    {
                        return;
                    }

                    int newOffset = prevP.GetPlainText().Length;
                    prevP.Runs.AddRange(currP.Runs);
                    Document.Paragraphs.RemoveAt(CaretPosition.ParagraphIndex);

                    CaretPosition = new DocumentPosition(prevIndex, newOffset);
                }
                return;
            }

            string text = Document.Paragraphs[
                CaretPosition.ParagraphIndex].GetPlainText();
            DocumentPosition end = CaretPosition;
            DocumentPosition start = new(
                end.ParagraphIndex,
                TextBoundaries.Previous(text, end.Offset));
            DeleteRangeInternal(start, end);
            CaretPosition = start;
        },
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint,
            captureImages: captureImages
        );
    }

    public void DeleteForward()
    {
        int mutationStartParagraph;
        int mutationBeforeCount;
        if (HasSelection)
        {
            (DocumentPosition start, DocumentPosition end) =
                GetNormalizedSelection();
            mutationStartParagraph = start.ParagraphIndex;
            mutationBeforeCount =
                end.ParagraphIndex - start.ParagraphIndex + 1;
        }
        else
        {
            mutationStartParagraph = CaretPosition.ParagraphIndex;
            int currentLength = Document.Paragraphs[
                CaretPosition.ParagraphIndex].GetPlainText().Length;
            mutationBeforeCount =
                CaretPosition.Offset == currentLength &&
                CaretPosition.ParagraphIndex < Document.Paragraphs.Count - 1
                    ? 2
                    : 1;
        }

        bool captureImages = SelectionContainsImage();
        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
            {
                if (HasSelection)
                {
                    DeleteSelection();
                    return;
                }

                int paragraphIndex = CaretPosition.ParagraphIndex;
                Paragraph paragraph = Document.Paragraphs[paragraphIndex];
                string text = paragraph.GetPlainText();
                if (CaretPosition.Offset < text.Length)
                {
                    DocumentPosition start = CaretPosition;
                    DocumentPosition end = new(
                        paragraphIndex,
                        TextBoundaries.Next(text, start.Offset));
                    DeleteRangeInternal(start, end);
                    return;
                }

                if (paragraphIndex >= Document.Paragraphs.Count - 1)
                {
                    return;
                }

                Paragraph next = Document.Paragraphs[paragraphIndex + 1];
                if (paragraph.IsImage || next.IsImage)
                {
                    return;
                }

                paragraph.Runs.AddRange(next.Runs);
                Document.Paragraphs.RemoveAt(paragraphIndex + 1);
            },
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint,
            captureImages: captureImages);
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

    public void DeleteSelection()
    {
        if (!HasSelection) return;

        (DocumentPosition mutationStart, DocumentPosition mutationEnd) =
            GetNormalizedSelection();
        int mutationStartParagraph = mutationStart.ParagraphIndex;
        int mutationBeforeCount =
            mutationEnd.ParagraphIndex - mutationStartParagraph + 1;
        bool captureImages = SelectionContainsImage();

        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
        {
            (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();
            DeleteRangeInternal(start, end);
            int paragraphIndex = Math.Clamp(
                start.ParagraphIndex,
                0,
                Document.Paragraphs.Count - 1);
            int offset = Math.Clamp(
                start.Offset,
                0,
                Document.Paragraphs[paragraphIndex].GetPlainText().Length);
            CaretPosition = new DocumentPosition(paragraphIndex, offset);
            ClearSelection();
        },
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint,
            captureImages: captureImages
        );
    }

    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        TextBoundaries.EnsureValidUtf16(text, nameof(text));

        (DocumentPosition mutationStart, DocumentPosition mutationEnd) =
            HasSelection
                ? GetNormalizedSelection()
                : (CaretPosition, CaretPosition);
        int mutationStartParagraph = mutationStart.ParagraphIndex;
        int mutationBeforeCount =
            mutationEnd.ParagraphIndex - mutationStartParagraph + 1;
        bool captureImages = SelectionContainsImage();

        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
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
        },
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint,
            captureImages: captureImages
        );
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
        start = NormalizePosition(start, TextBoundaryAffinity.Backward);
        end = NormalizePosition(end, TextBoundaryAffinity.Forward);
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
            int startTextLength = startP.GetPlainText().Length;
            int endTextLength = endP.GetPlainText().Length;
            bool removeStartImage = startP.IsImage && start.Offset == 0;
            bool removeEndImage = endP.IsImage && end.Offset >= endTextLength;

            if (!removeStartImage)
            {
                RemoveRunsFromOffset(startP, Math.Min(start.Offset, startTextLength));
            }

            if (!removeEndImage)
            {
                RemoveRunsThroughOffset(endP, Math.Min(end.Offset, endTextLength));
            }

            bool mergeTextEndpoints = !removeStartImage
                && !removeEndImage
                && !startP.IsImage
                && !endP.IsImage;
            if (mergeTextEndpoints)
            {
                startP.Runs.AddRange(endP.Runs);
            }

            List<Paragraph> rebuilt = new List<Paragraph>(Document.Paragraphs.Count);
            for (int index = 0; index < start.ParagraphIndex; index++)
            {
                rebuilt.Add(Document.Paragraphs[index]);
            }

            if (!removeStartImage)
            {
                EnsureEditableRun(startP);
                rebuilt.Add(startP);
            }

            if (!mergeTextEndpoints && !removeEndImage)
            {
                EnsureEditableRun(endP);
                rebuilt.Add(endP);
            }

            for (int index = end.ParagraphIndex + 1;
                 index < Document.Paragraphs.Count;
                 index++)
            {
                rebuilt.Add(Document.Paragraphs[index]);
            }

            if (rebuilt.Count == 0)
            {
                rebuilt.Add(new Paragraph());
            }

            Document.Paragraphs.Clear();
            Document.Paragraphs.AddRange(rebuilt);
        }
    }

    private static void RemoveRunsFromOffset(Paragraph paragraph, int offset)
    {
        int currentOffset = 0;
        for (int index = 0; index < paragraph.Runs.Count; index++)
        {
            int runStart = currentOffset;
            currentOffset += paragraph.Runs[index].Text.Length;
            if (runStart >= offset)
            {
                paragraph.Runs.RemoveAt(index);
                index--;
            }
        }
    }

    private static void RemoveRunsThroughOffset(Paragraph paragraph, int offset)
    {
        int currentOffset = 0;
        for (int index = 0; index < paragraph.Runs.Count; index++)
        {
            int runEnd = currentOffset + paragraph.Runs[index].Text.Length;
            currentOffset += paragraph.Runs[index].Text.Length;
            if (runEnd <= offset)
            {
                paragraph.Runs.RemoveAt(index);
                index--;
            }
        }
    }

    private static void EnsureEditableRun(Paragraph paragraph)
    {
        if (paragraph.Runs.Count == 0)
        {
            paragraph.Runs.Add(new TextRun(string.Empty));
        }
    }
}
