using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class SplitParagraphOperation : IEditOperation, IHistoryPayloadSize
{
    private readonly DocumentNodeId _paragraphId;
    private readonly int _offset;
    private readonly DocumentNodeId _newParagraphId;
    private readonly Func<DocumentNodeId>? _createId;
    private List<InlineNode>? _originalInlines;
    private ParagraphProperties? _originalProperties;
    private List<InlineNode>? _leftAfter;
    private List<InlineNode>? _rightAfter;
    private ParagraphBlock? _newParagraph;

    public SplitParagraphOperation(
        DocumentNodeId paragraphId,
        int offset,
        DocumentNodeId? newParagraphId = null)
        : this(paragraphId, offset, newParagraphId, null)
    {
    }

    internal SplitParagraphOperation(
        DocumentNodeId paragraphId,
        int offset,
        DocumentNodeId? newParagraphId,
        Func<DocumentNodeId>? createId)
    {
        _paragraphId = paragraphId;
        _offset = offset;
        _newParagraphId = newParagraphId ?? DocumentNodeId.New();
        _createId = createId;
    }

    public string Description => "Разделение абзаца";

    public DocumentNodeId NewParagraphId => _newParagraphId;

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _paragraphId, _newParagraphId };

    public long EstimatedPayloadBytes => 384L +
        HistoryPayloadSizer.Inlines(_originalInlines) +
        HistoryPayloadSizer.Inlines(_leftAfter) +
        HistoryPayloadSizer.Inlines(_rightAfter) +
        HistoryPayloadSizer.Block(_newParagraph);

    public void Apply(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        (DocumentSection section, int index) =
            context.GetRequiredBlockLocation(_paragraphId);

        if (context.Document.FindBlock(_newParagraphId) is not null)
        {
            throw new InvalidOperationException(
                $"Блок {_newParagraphId} уже существует.");
        }

        _originalInlines ??= ParagraphTextBuffer.CloneInlines(
            paragraph.Inlines);
        _originalProperties ??= paragraph.Properties.Clone();

        if (_newParagraph is not null)
        {
            ParagraphTextBuffer.Restore(
                paragraph,
                _leftAfter ?? throw new InvalidOperationException(
                    "Операция не сохранила left snapshot."));
            _newParagraph.Properties = paragraph.Properties.Clone();
            ParagraphTextBuffer.Restore(
                _newParagraph,
                _rightAfter ?? throw new InvalidOperationException(
                    "Операция не сохранила right snapshot."));
            section.Blocks.Insert(index + 1, _newParagraph);
            context.InvalidationTracker.InvalidateStructure(index);
            return;
        }

        int offset = Math.Clamp(_offset, 0, paragraph.TextLength);
        List<InlineNode> left = ParagraphTextBuffer.Slice(
            paragraph,
            0,
            offset,
            _createId);
        List<InlineNode> right = ParagraphTextBuffer.Slice(
            paragraph,
            offset,
            paragraph.TextLength - offset,
            _createId);

        ParagraphTextBuffer.ReplaceAll(paragraph, left, _createId);

        _newParagraph = new ParagraphBlock(_newParagraphId)
        {
            Properties = paragraph.Properties.Clone()
        };
        ParagraphTextBuffer.ReplaceAll(_newParagraph, right, _createId);
        _leftAfter = ParagraphTextBuffer.CloneInlines(paragraph.Inlines);
        _rightAfter = ParagraphTextBuffer.CloneInlines(_newParagraph.Inlines);
        section.Blocks.Insert(index + 1, _newParagraph);

        context.InvalidationTracker.InvalidateStructure(index);
    }

    public void Revert(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        (DocumentSection section, int newIndex) =
            context.GetRequiredBlockLocation(_newParagraphId);

        section.Blocks.RemoveAt(newIndex);
        paragraph.Properties = (_originalProperties
            ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена.")).Clone();
        ParagraphTextBuffer.Restore(
            paragraph,
            _originalInlines ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена."));

        context.InvalidationTracker.InvalidateStructure(
            Math.Max(0, newIndex - 1));
    }
}
