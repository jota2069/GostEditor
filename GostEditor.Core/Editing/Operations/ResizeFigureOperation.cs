using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing.Operations;

public sealed class ResizeFigureOperation : IEditOperation, IHistoryPayloadSize
{
    private readonly DocumentNodeId _figureId;
    private readonly double _width;
    private readonly double _height;
    private double _oldWidth;
    private double _oldHeight;

    public ResizeFigureOperation(
        DocumentNodeId figureId,
        double width,
        double height)
    {
        if (!double.IsFinite(width) || width <= 0 ||
            !double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Размеры изображения должны быть конечными и положительными.");
        }

        _figureId = figureId;
        _width = width;
        _height = height;
    }

    public string Description => "Изменение размера изображения";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks => [_figureId];

    public DocumentIndexUpdateKind IndexUpdateKind => DocumentIndexUpdateKind.None;

    public long EstimatedPayloadBytes => 128L;

    public void Apply(DocumentEditingContext context)
    {
        FigureBlock figure = GetFigure(context);
        _oldWidth = figure.Width;
        _oldHeight = figure.Height;
        SetSize(context, figure, _width, _height);
    }

    public void Revert(DocumentEditingContext context) =>
        SetSize(context, GetFigure(context), _oldWidth, _oldHeight);

    private FigureBlock GetFigure(DocumentEditingContext context) =>
        context.GetRequiredBlock(_figureId) as FigureBlock
        ?? throw new InvalidOperationException(
            $"Блок {_figureId} не является изображением.");

    private void SetSize(
        DocumentEditingContext context,
        FigureBlock figure,
        double width,
        double height)
    {
        figure.Width = width;
        figure.Height = height;
        context.InvalidationTracker.Invalidate(
            _figureId,
            LayoutInvalidationKind.Metrics | LayoutInvalidationKind.Paint);
    }
}
