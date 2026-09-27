using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class ChangeParagraphPropertiesOperation : IEditOperation
{
    private readonly DocumentNodeId _paragraphId;
    private readonly ParagraphProperties _newProperties;
    private ParagraphProperties? _oldProperties;

    public ChangeParagraphPropertiesOperation(
        DocumentNodeId paragraphId,
        ParagraphProperties newProperties)
    {
        _paragraphId = paragraphId;
        _newProperties = newProperties?.Clone()
            ?? throw new ArgumentNullException(nameof(newProperties));
    }

    public string Description => "Форматирование абзаца";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        new[] { _paragraphId };

    public void Apply(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        _oldProperties ??= paragraph.Properties.Clone();
        paragraph.Properties = _newProperties.Clone();
        context.InvalidationTracker.Invalidate(
            _paragraphId,
            LayoutInvalidationKind.Metrics | LayoutInvalidationKind.Paint);
    }

    public void Revert(DocumentEditingContext context)
    {
        ParagraphBlock paragraph = context.GetRequiredParagraph(_paragraphId);
        paragraph.Properties = (_oldProperties
            ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена.")).Clone();
        context.InvalidationTracker.Invalidate(
            _paragraphId,
            LayoutInvalidationKind.Metrics | LayoutInvalidationKind.Paint);
    }
}
