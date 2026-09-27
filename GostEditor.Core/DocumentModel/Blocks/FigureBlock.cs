using System;

namespace GostEditor.Core.DocumentModel.Blocks;

public sealed class FigureBlock : BlockNode
{
    public FigureBlock(Guid imageId, double width, double height)
    {
        if (imageId == Guid.Empty)
        {
            throw new ArgumentException(
                "Идентификатор изображения не может быть пустым.",
                nameof(imageId));
        }

        ImageId = imageId;
        Width = width;
        Height = height;
    }

    public FigureBlock(
        DocumentNodeId id,
        Guid imageId,
        double width,
        double height)
        : base(id)
    {
        if (imageId == Guid.Empty)
        {
            throw new ArgumentException(
                "Идентификатор изображения не может быть пустым.",
                nameof(imageId));
        }

        ImageId = imageId;
        Width = width;
        Height = height;
    }

    public Guid ImageId { get; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string Caption { get; set; } = string.Empty;
}
