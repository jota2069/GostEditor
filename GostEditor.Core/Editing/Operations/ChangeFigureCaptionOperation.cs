using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

internal sealed class ChangeFigureCaptionOperation :
    IEditOperation,
    IHistoryPayloadSize
{
    private readonly DocumentNodeId _figureId;
    private readonly string _caption;
    private string? _previous;

    public ChangeFigureCaptionOperation(DocumentNodeId figureId, string caption)
    {
        _figureId = figureId;
        _caption = caption ?? string.Empty;
    }

    public string Description => "Изменение подписи изображения";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks => [_figureId];

    public DocumentIndexUpdateKind IndexUpdateKind => DocumentIndexUpdateKind.None;

    public long EstimatedPayloadBytes =>
        128L + 2L * (_caption.Length + (_previous?.Length ?? 0));

    public void Apply(DocumentEditingContext context)
    {
        FigureBlock figure = GetFigure(context);
        _previous ??= figure.Caption;
        SetCaption(context, figure, _caption);
    }

    public void Revert(DocumentEditingContext context) =>
        SetCaption(
            context,
            GetFigure(context),
            _previous ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена."));

    private FigureBlock GetFigure(DocumentEditingContext context) =>
        context.GetRequiredBlock(_figureId) as FigureBlock
        ?? throw new InvalidOperationException(
            $"Блок {_figureId} не является изображением.");

    private void SetCaption(
        DocumentEditingContext context,
        FigureBlock figure,
        string caption)
    {
        figure.Caption = caption;
        context.InvalidationTracker.Invalidate(
            _figureId,
            LayoutInvalidationKind.Metrics | LayoutInvalidationKind.Paint);
    }
}
