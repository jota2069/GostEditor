using System;
using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.Commands;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine;

public partial class DocumentEditor
{
    public void InsertHeading(int level, string text)
    {
        int mutationInsertIndex = Document.Paragraphs.Count;

        ExecuteParagraphMutation(
            mutationInsertIndex,
            0,
            () =>
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
        },
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint
        );
    }

    public void InsertTextBlock()
    {
        int mutationInsertIndex = Math.Min(
            CaretPosition.ParagraphIndex + 1,
            Document.Paragraphs.Count);

        ExecuteParagraphMutation(
            mutationInsertIndex,
            0,
            () =>
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
        },
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint
        );
    }

    public void InsertTablePlaceholder(int rows = 3, int columns = 3)
    {
        rows = Math.Clamp(rows, 1, 20);
        columns = Math.Clamp(columns, 1, 8);

        int mutationInsertIndex = Math.Min(
            CaretPosition.ParagraphIndex + 1,
            Document.Paragraphs.Count);

        ExecuteParagraphMutation(
            mutationInsertIndex,
            0,
            () =>
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
        },
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint
        );
    }

    public void AppendParagraphs(List<Paragraph> paragraphs)
    {
        if (paragraphs == null || paragraphs.Count == 0)
        {
            return;
        }

        int mutationInsertIndex = Document.Paragraphs.Count;
        ExecuteParagraphMutation(
            mutationInsertIndex,
            0,
            () =>
            {
                foreach (Paragraph paragraph in paragraphs)
                {
                    Document.Paragraphs.Add(paragraph);
                }
            },
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint);
    }
}
