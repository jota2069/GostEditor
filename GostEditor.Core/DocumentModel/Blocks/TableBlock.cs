namespace GostEditor.Core.DocumentModel.Blocks;

public sealed class TableBlock : BlockNode
{
    public TableBlock()
    {
    }

    public TableBlock(DocumentNodeId id)
        : base(id)
    {
    }

    public List<TableRow> Rows { get; } = new();
}

public sealed class TableRow : DocumentNode
{
    public List<TableCell> Cells { get; } = new();
}

public sealed class TableCell : DocumentNode
{
    public int RowSpan { get; set; } = 1;

    public int ColumnSpan { get; set; } = 1;

    public List<BlockNode> Blocks { get; } = new();
}
