using System;
using GostEditor.Core.Models;
using SkiaSharp;

namespace GostEditor.Core.Services;

internal interface IImageContentProbe
{
    ImageResult<ImageContentMetadata> Inspect(ReadOnlyMemory<byte> data);
}

internal sealed class ImageContentProbe : IImageContentProbe
{
    public ImageResult<ImageContentMetadata> Inspect(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            return ImageResult<ImageContentMetadata>.Failure(
                ImageErrorCode.EmptyData,
                "Изображение не содержит данных.");
        }

        try
        {
            using SKData skData = SKData.CreateCopy(data.Span);
            using SKCodec? codec = SKCodec.Create(skData);

            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                return ImageResult<ImageContentMetadata>.Failure(
                    ImageErrorCode.UnsupportedOrInvalidImage,
                    "Файл не является поддерживаемым изображением.");
            }

            string mediaType = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Png => "image/png",
                SKEncodedImageFormat.Jpeg => "image/jpeg",
                SKEncodedImageFormat.Gif => "image/gif",
                SKEncodedImageFormat.Bmp => "image/bmp",
                SKEncodedImageFormat.Webp => "image/webp",
                SKEncodedImageFormat.Ico => "image/x-icon",
                SKEncodedImageFormat.Wbmp => "image/vnd.wap.wbmp",
                SKEncodedImageFormat.Pkm => "image/pkm",
                SKEncodedImageFormat.Ktx => "image/ktx",
                SKEncodedImageFormat.Astc => "image/astc",
                SKEncodedImageFormat.Dng => "image/x-adobe-dng",
                SKEncodedImageFormat.Heif => "image/heif",
                SKEncodedImageFormat.Avif => "image/avif",
                _ => "application/octet-stream"
            };

            return ImageResult<ImageContentMetadata>.Success(
                new ImageContentMetadata(mediaType, codec.Info.Width, codec.Info.Height));
        }
        catch (Exception)
        {
            return ImageResult<ImageContentMetadata>.Failure(
                ImageErrorCode.UnsupportedOrInvalidImage,
                "Не удалось декодировать изображение.");
        }
    }
}
