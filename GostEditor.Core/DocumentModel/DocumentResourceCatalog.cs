using System;

namespace GostEditor.Core.DocumentModel;

public sealed class DocumentResourceCatalog
{
    private readonly Dictionary<Guid, ImageResource> _images = new();

    public IReadOnlyDictionary<Guid, ImageResource> Images => _images;

    public void AddImage(ImageResource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (!_images.TryAdd(image.Id, image))
        {
            throw new InvalidOperationException(
                $"Ресурс изображения {image.Id} уже существует.");
        }
    }

    public bool TryGetImage(Guid id, out ImageResource? image) =>
        _images.TryGetValue(id, out image);

    public bool RemoveImage(Guid id) => _images.Remove(id);

    public void ClearImages() => _images.Clear();
}

public sealed class ImageResource
{
    private readonly byte[] _data;

    public ImageResource(
        Guid id,
        string fileName,
        string mediaType,
        ReadOnlyMemory<byte> data,
        string caption = "",
        int order = 0)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Идентификатор изображения не может быть пустым.",
                nameof(id));
        }

        Id = id;
        FileName = fileName ?? string.Empty;
        MediaType = string.IsNullOrWhiteSpace(mediaType)
            ? "application/octet-stream"
            : mediaType;
        _data = data.ToArray();
        Caption = caption ?? string.Empty;
        Order = order;
    }

    public Guid Id { get; }

    public string FileName { get; }

    public string MediaType { get; }

    public ReadOnlyMemory<byte> Data => _data;

    public string Caption { get; }

    public int Order { get; }
}
