using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Editing;
using GostEditor.Core.Editing.Operations;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine;

public partial class DocumentEditor
{
    public void InsertHeading(int level, string text)
    {
        ParagraphBlock heading = new()
        {
            Properties = new ParagraphProperties
            {
                Style = level == 1
                    ? ParagraphStyle.Heading1
                    : ParagraphStyle.Heading2,
                Alignment = GostAlignment.Center,
                FirstLineIndent = 0,
                LineSpacing = 1.5,
                PageBreakBefore = level == 1
            },
            Inlines =
            {
                new TextInline(text ?? string.Empty)
                {
                    Style = new TextStyle
                    {
                        IsBold = true,
                        FontSize = level == 1 ? 16 : 14
                    }
                }
            }
        };
        InsertCanonicalBlockAfterCaret(heading);
    }

    public void InsertTextBlock()
    {
        ParagraphBlock paragraph = new()
        {
            Properties = new ParagraphProperties
            {
                Alignment = GostAlignment.Justify,
                FirstLineIndent = 47.0,
                LineSpacing = 1.5,
                Style = ParagraphStyle.Normal
            },
            Inlines =
            {
                new TextInline(string.Empty)
                {
                    Style = new TextStyle { FontSize = 14 }
                }
            }
        };
        InsertCanonicalBlockAfterCaret(paragraph);
    }

    public void InsertPageBreak() =>
        InsertCanonicalBlockAfterCaret(new PageBreakBlock());

    public void InsertTablePlaceholder(int rows = 3, int columns = 3)
    {
        rows = Math.Clamp(rows, 1, 20);
        columns = Math.Clamp(columns, 1, 8);
        List<ParagraphBlock> paragraphs = new();
        paragraphs.Add(CreateCanonicalParagraph(new Paragraph
        {
            Alignment = GostAlignment.Right,
            FirstLineIndent = 0,
            Style = ParagraphStyle.Normal,
            Runs = { new TextRun("Таблица 1 - Название таблицы") { FontSize = 14 } }
        }));

        for (int row = 0; row < rows; row++)
        {
            string[] cells = Enumerable.Range(0, columns)
                .Select(column => row == 0
                    ? $"Заголовок {column + 1}"
                    : "Данные")
                .ToArray();
            paragraphs.Add(CreateCanonicalParagraph(new Paragraph
            {
                Alignment = GostAlignment.Left,
                FirstLineIndent = 0,
                Style = ParagraphStyle.Code,
                Runs = { new TextRun(string.Join(" | ", cells)) { FontSize = 12 } }
            }));
        }

        InsertCanonicalBlocksAfterCaret(paragraphs);
    }

    public void AppendParagraphs(List<Paragraph> paragraphs)
    {
        if (paragraphs is null || paragraphs.Count == 0)
        {
            return;
        }

        DocumentSection section = _editingSession.Document.Sections[^1];
        List<ParagraphBlock> blocks = paragraphs
            .Select(CreateCanonicalParagraph)
            .ToList();
        ExecuteCanonicalStructuralEdit(() =>
        {
            EditTransaction transaction = new("Добавление абзацев");
            int index = section.Blocks.Count;
            foreach (ParagraphBlock block in blocks)
            {
                transaction.Add(new InsertBlockOperation(section.Id, index++, block));
            }

            _editingSession.Execute(transaction);
        });
    }

    private void InsertCanonicalBlockAfterCaret(BlockNode block) =>
        InsertCanonicalBlocksAfterCaret([block]);

    private void InsertCanonicalBlocksAfterCaret(IReadOnlyList<BlockNode> blocks)
    {
        IReadOnlyList<BlockNode> ordered = _editingSession.Document
            .EnumerateBlocks()
            .ToList();
        int currentIndex = Math.Clamp(CaretPosition.ParagraphIndex, 0, ordered.Count - 1);
        BlockNode current = ordered[currentIndex];
        DocumentSection section = _editingSession.Document.FindSectionContaining(current.Id)
            ?? throw new InvalidOperationException("Секция caret не найдена.");
        int sectionIndex = section.Blocks.FindIndex(item => item.Id == current.Id) + 1;

        ExecuteCanonicalStructuralEdit(() =>
        {
            EditTransaction transaction = new("Добавление блоков");
            for (int index = 0; index < blocks.Count; index++)
            {
                transaction.Add(new InsertBlockOperation(
                    section.Id,
                    sectionIndex + index,
                    blocks[index]));
            }

            BlockNode first = blocks[0];
            DocumentLocation location = first is ParagraphBlock paragraph
                ? new DocumentLocation(paragraph.Id, paragraph.TextLength)
                : GetNearestCanonicalTextLocation(new DocumentPosition(
                    currentIndex + blocks.Count,
                    0));
            _editingSession.ExecuteWithSelection(
                transaction,
                () => _editingSession.Selection.MoveCaret(location));
        });
    }

    private static ParagraphBlock CreateCanonicalParagraph(Paragraph source)
    {
        ParagraphBlock block = new()
        {
            Properties = new ParagraphProperties
            {
                Alignment = source.Alignment,
                FirstLineIndent = source.FirstLineIndent,
                LineSpacing = source.LineSpacing,
                Style = source.Style,
                PageBreakBefore = source.PageBreakBefore
            }
        };
        foreach (TextRun run in source.Runs)
        {
            block.Inlines.Add(new TextInline(run.Text)
            {
                Style = new TextStyle
                {
                    IsBold = run.IsBold,
                    IsItalic = run.IsItalic,
                    Color = run.Color,
                    FontSize = run.FontSize
                }
            });
        }

        block.EnsureEditableInline();
        return block;
    }
}
