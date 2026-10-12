using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;

namespace GostEditor.Core.Editing;

internal static class HistoryPayloadSizer
{
    private const long ObjectAllowance = 256;

    public static long Strings(params string?[] values) =>
        values.Sum(value => value is null ? 0 : 2L * value.Length + 32L);

    public static long Inlines(IEnumerable<InlineNode>? inlines)
    {
        if (inlines is null)
        {
            return 0;
        }

        long size = 64;
        foreach (InlineNode inline in inlines)
        {
            size += ObjectAllowance + inline switch
            {
                TextInline text => Strings(
                    text.Text,
                    text.Style.FontFamily),
                LinkInline link => Strings(
                    link.Text,
                    link.Target,
                    link.Style.FontFamily),
                _ => 0
            };
        }

        return size;
    }

    public static long Block(BlockNode? block) => block switch
    {
        null => 0,
        ParagraphBlock paragraph =>
            ObjectAllowance + Inlines(paragraph.Inlines),
        CodeBlock code =>
            ObjectAllowance + Strings(code.Language, code.Code, code.Caption),
        FigureBlock figure => ObjectAllowance + Strings(figure.Caption),
        TableBlock table => ObjectAllowance + table.Rows.Sum(row =>
            ObjectAllowance + row.Cells.Sum(cell =>
                ObjectAllowance + cell.Blocks.Sum(Block))),
        _ => ObjectAllowance
    };

    public static long Operation(IEditOperation operation) =>
        operation is IHistoryPayloadSize sized
            ? sized.EstimatedPayloadBytes
            : 4_096L;
}
