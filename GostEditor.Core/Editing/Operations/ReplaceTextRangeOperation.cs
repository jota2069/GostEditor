using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.DocumentModel.Blocks;

namespace GostEditor.Core.Editing.Operations;

/// <summary>
/// Atomic replacement of a normalized range. A multi-block replacement is
/// represented as one history item composed from reversible primitive
/// operations.
/// </summary>
public sealed class ReplaceTextRangeOperation : IEditOperation
{
    private readonly EditTransaction _transaction;

    public ReplaceTextRangeOperation(
        DocumentRoot document,
        DocumentRange range,
        string replacement,
        TextStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        replacement ??= string.Empty;

        DocumentRange normalized = NormalizeRange(document, range);
        _transaction = BuildTransaction(
            document,
            normalized,
            replacement,
            style);
        ResultLocation = new DocumentLocation(
            normalized.Start.BlockId,
            normalized.Start.Offset + replacement.Length);
    }

    public string Description => "Замена диапазона текста";

    public DocumentLocation ResultLocation { get; }

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        _transaction.AffectedBlocks;

    public void Apply(DocumentEditingContext context) =>
        _transaction.Apply(context);

    public void Revert(DocumentEditingContext context) =>
        _transaction.Revert(context);

    private static EditTransaction BuildTransaction(
        DocumentRoot document,
        DocumentRange range,
        string replacement,
        TextStyle? style)
    {
        EditTransaction transaction = new("Замена диапазона текста");
        if (!range.IsCollapsed)
        {
            AddDeleteOperations(document, range, transaction);
        }

        if (replacement.Length > 0)
        {
            transaction.Add(new InsertTextOperation(
                range.Start.BlockId,
                range.Start.Offset,
                replacement,
                style));
        }

        return transaction;
    }

    private static void AddDeleteOperations(
        DocumentRoot document,
        DocumentRange range,
        EditTransaction transaction)
    {
        if (range.Start.BlockId == range.End.BlockId)
        {
            int length = range.End.Offset - range.Start.Offset;
            if (length > 0)
            {
                transaction.Add(new DeleteTextOperation(
                    range.Start.BlockId,
                    range.Start.Offset,
                    length));
            }

            return;
        }

        ParagraphBlockInfo start = GetParagraphInfo(
            document,
            range.Start.BlockId);
        ParagraphBlockInfo end = GetParagraphInfo(
            document,
            range.End.BlockId);
        if (start.Section.Id != end.Section.Id)
        {
            throw new NotSupportedException(
                "Удаление диапазона через несколько секций пока не " +
                "поддерживается.");
        }

        int startTailLength = start.Paragraph.TextLength - range.Start.Offset;
        if (startTailLength > 0)
        {
            transaction.Add(new DeleteTextOperation(
                start.Paragraph.Id,
                range.Start.Offset,
                startTailLength));
        }

        for (int index = start.Index + 1; index < end.Index; index++)
        {
            transaction.Add(new RemoveBlockOperation(
                start.Section.Blocks[index].Id));
        }

        if (range.End.Offset > 0)
        {
            transaction.Add(new DeleteTextOperation(
                end.Paragraph.Id,
                0,
                range.End.Offset));
        }

        transaction.Add(new MergeParagraphsOperation(
            start.Paragraph.Id,
            end.Paragraph.Id));
    }

    private static DocumentRange NormalizeRange(
        DocumentRoot document,
        DocumentRange range)
    {
        int startIndex = document.GetBlockIndex(range.Start.BlockId);
        int endIndex = document.GetBlockIndex(range.End.BlockId);
        if (startIndex < 0 || endIndex < 0)
        {
            throw new InvalidOperationException(
                "Диапазон ссылается на отсутствующий блок.");
        }

        DocumentLocation start = Clamp(document, range.Start);
        DocumentLocation end = Clamp(document, range.End);
        if (startIndex < endIndex ||
            startIndex == endIndex && start.Offset <= end.Offset)
        {
            return new DocumentRange(start, end);
        }

        return new DocumentRange(end, start);
    }

    private static DocumentLocation Clamp(
        DocumentRoot document,
        DocumentLocation location)
    {
        var paragraph = document.FindBlock(location.BlockId)
            as ParagraphBlock
            ?? throw new InvalidOperationException(
                $"Блок {location.BlockId} не является абзацем.");
        return location.Clamp(paragraph.TextLength);
    }

    private static ParagraphBlockInfo GetParagraphInfo(
        DocumentRoot document,
        DocumentNodeId paragraphId)
    {
        foreach (DocumentSection section in document.Sections)
        {
            int index = section.Blocks.FindIndex(
                block => block.Id == paragraphId);
            if (index >= 0 && section.Blocks[index] is ParagraphBlock paragraph)
            {
                return new ParagraphBlockInfo(section, index, paragraph);
            }
        }

        throw new InvalidOperationException(
            $"Абзац {paragraphId} не найден.");
    }

    private sealed record ParagraphBlockInfo(
        DocumentSection Section,
        int Index,
        ParagraphBlock Paragraph);
}
