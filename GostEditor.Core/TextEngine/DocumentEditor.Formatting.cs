using System;
using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.Commands;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine;

public partial class DocumentEditor
{
    public void ApplyBold() => ToggleBold();

    public void ApplyItalic() => ToggleItalic();

    public void ApplyFontSize(double size) => SetFontSize(size);

    public void AlignLeft() => SetAlignment(GostAlignment.Left);

    public void AlignCenter() => SetAlignment(GostAlignment.Center);

    public void AlignRight() => SetAlignment(GostAlignment.Right);

    public void AlignJustify() => SetAlignment(GostAlignment.Justify);

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
        (DocumentPosition mutationStart, DocumentPosition mutationEnd) =
            HasSelection
                ? GetNormalizedSelection()
                : (CaretPosition, CaretPosition);
        int mutationStartParagraph = mutationStart.ParagraphIndex;
        int mutationBeforeCount =
            mutationEnd.ParagraphIndex - mutationStartParagraph + 1;

        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
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
        },
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint
        );
    }

    public void ToggleItalic()
    {
        (DocumentPosition mutationStart, DocumentPosition mutationEnd) =
            HasSelection
                ? GetNormalizedSelection()
                : (CaretPosition, CaretPosition);
        int mutationStartParagraph = mutationStart.ParagraphIndex;
        int mutationBeforeCount =
            mutationEnd.ParagraphIndex - mutationStartParagraph + 1;

        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
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
        },
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint
        );
    }

    public void SetFontSize(double fontSize)
    {
        (DocumentPosition mutationStart, DocumentPosition mutationEnd) =
            HasSelection
                ? GetNormalizedSelection()
                : (CaretPosition, CaretPosition);
        int mutationStartParagraph = mutationStart.ParagraphIndex;
        int mutationBeforeCount =
            mutationEnd.ParagraphIndex - mutationStartParagraph + 1;

        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
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
        },
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint
        );
    }

    public void ClearFormatting()
    {
        (DocumentPosition mutationStart, DocumentPosition mutationEnd) =
            HasSelection
                ? GetNormalizedSelection()
                : (CaretPosition, CaretPosition);
        int mutationStartParagraph = mutationStart.ParagraphIndex;
        int mutationBeforeCount =
            mutationEnd.ParagraphIndex - mutationStartParagraph + 1;

        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
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
        },
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint
        );
    }

    public void SetAlignment(GostAlignment alignment)
    {
        (DocumentPosition mutationStart, DocumentPosition mutationEnd) =
            HasSelection
                ? GetNormalizedSelection()
                : (CaretPosition, CaretPosition);
        int mutationStartParagraph = mutationStart.ParagraphIndex;
        int mutationBeforeCount =
            mutationEnd.ParagraphIndex - mutationStartParagraph + 1;

        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
        {
            (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();
            for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
            {
                Document.Paragraphs[pIdx].Alignment = alignment;
            }
        },
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint
        );
    }

    public void SetParagraphStyle(ParagraphStyle style)
    {
        (DocumentPosition mutationStart, DocumentPosition mutationEnd) =
            HasSelection
                ? GetNormalizedSelection()
                : (CaretPosition, CaretPosition);
        int mutationStartParagraph = mutationStart.ParagraphIndex;
        int mutationBeforeCount =
            mutationEnd.ParagraphIndex - mutationStartParagraph + 1;

        ExecuteParagraphMutation(
            mutationStartParagraph,
            mutationBeforeCount,
            () =>
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
        },
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint
        );
    }
}
