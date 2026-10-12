using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;

namespace GostEditor.Core.Editing.Operations;

public sealed class RemoveImageResourceOperation : IEditOperation, IHistoryPayloadSize
{
    private readonly Guid _resourceId;
    private readonly ImageResource? _compatibilityResource;
    private ImageResource? _removed;

    public RemoveImageResourceOperation(Guid resourceId)
    {
        if (resourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Идентификатор изображения не может быть пустым.",
                nameof(resourceId));
        }

        _resourceId = resourceId;
    }

    internal RemoveImageResourceOperation(ImageResource compatibilityResource)
        : this(compatibilityResource?.Id
            ?? throw new ArgumentNullException(nameof(compatibilityResource)))
    {
        _compatibilityResource = compatibilityResource;
    }

    public string Description => "Удаление ресурса изображения";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks => [];

    public DocumentIndexUpdateKind IndexUpdateKind => DocumentIndexUpdateKind.None;

    public long EstimatedPayloadBytes => 256L + (_removed?.Data.Length ?? 0);

    public void Apply(DocumentEditingContext context)
    {
        if (context.Document.EnumerateBlocks()
            .OfType<FigureBlock>()
            .Any(figure => figure.ImageId == _resourceId))
        {
            throw new InvalidOperationException(
                $"Ресурс изображения {_resourceId} всё ещё используется.");
        }

        if (!context.Document.Resources.TryGetImage(_resourceId, out _removed))
        {
            _removed = _compatibilityResource;
            if (_removed is not null)
            {
                return;
            }
        }

        if (_removed is null || !context.Document.Resources.RemoveImage(_resourceId))
        {
            throw new InvalidOperationException(
                $"Ресурс изображения {_resourceId} отсутствует.");
        }
    }

    public void Revert(DocumentEditingContext context) =>
        context.Document.Resources.AddImage(_removed
            ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена."));
}
