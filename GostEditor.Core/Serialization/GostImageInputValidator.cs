using GostEditor.Core.Models;
using GostEditor.Core.Services;

namespace GostEditor.Core.Serialization;

internal static class GostImageInputValidator
{
    public static void ValidateDimensionsIfDecodable(
        ReadOnlyMemory<byte> data,
        string sourceDescription)
    {
        ImageResult<ImageContentMetadata> inspected =
            new ImageContentProbe().Inspect(data);
        if (inspected.IsSuccess)
        {
            ValidateDimensions(inspected.Value, sourceDescription);
        }
    }

    public static void ValidateDecodableImage(
        ReadOnlyMemory<byte> data,
        string sourceDescription)
    {
        ImageResult<ImageContentMetadata> inspected =
            new ImageContentProbe().Inspect(data);
        if (!inspected.IsSuccess)
        {
            throw new InvalidDataException(
                $"{sourceDescription} не является поддерживаемым изображением.");
        }

        ValidateDimensions(inspected.Value, sourceDescription);
    }

    private static void ValidateDimensions(
        ImageContentMetadata metadata,
        string sourceDescription)
    {
        long pixels = (long)metadata.PixelWidth * metadata.PixelHeight;
        if (metadata.PixelWidth > GostArchiveLimits.MaxImagePixelDimension ||
            metadata.PixelHeight > GostArchiveLimits.MaxImagePixelDimension ||
            pixels > GostArchiveLimits.MaxImagePixels)
        {
            throw new InvalidDataException(
                $"{sourceDescription} имеет небезопасные размеры " +
                $"{metadata.PixelWidth}×{metadata.PixelHeight} пикселей.");
        }
    }
}
