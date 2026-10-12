using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Editing.Operations;

public sealed class AddImageResourceOperation : IEditOperation, IHistoryPayloadSize
{
    private readonly ImageResource _resource;

    public AddImageResourceOperation(ImageResource resource)
    {
        _resource = resource ?? throw new ArgumentNullException(nameof(resource));
    }

    public string Description => "Добавление ресурса изображения";

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks => [];

    public DocumentIndexUpdateKind IndexUpdateKind => DocumentIndexUpdateKind.None;

    public long EstimatedPayloadBytes => 256L + _resource.Data.Length;

    public void Apply(DocumentEditingContext context) =>
        context.Document.Resources.AddImage(_resource);

    public void Revert(DocumentEditingContext context)
    {
        if (!context.Document.Resources.RemoveImage(_resource.Id))
        {
            throw new InvalidOperationException(
                $"Ресурс изображения {_resource.Id} отсутствует.");
        }
    }
}
