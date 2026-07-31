using GostEditor.Core.Serialization.Format;

namespace GostEditor.Core.Serialization.Migrations;

internal static class GostV2PackageMapper
{
    public static async Task<GostMigratableDocument> ReadAsync(
        GostDocumentV2Dto source,
        GostArchiveEntryIndex entries,
        GostSerializationDiagnostics diagnostics,
        CancellationToken cancellationToken = default)
    {
        GostMigratableDocument document = new()
        {
            TitlePage = source.TitlePage,
            CodeListings = source.CodeListings,
            BibliographySources = source.BibliographySources,
            Modules = source.Modules,
            Counters = source.Counters,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            PageWidth = source.PageWidth,
            PageHeight = source.PageHeight,
            MarginLeft = source.MarginLeft,
            MarginRight = source.MarginRight,
            MarginTop = source.MarginTop,
            MarginBottom = source.MarginBottom
        };

        HashSet<Guid> imageIds = new();
        Dictionary<Guid, byte[]> imageData = new();
        HashSet<string> consumedMediaEntries = new(StringComparer.Ordinal);

        foreach (GostImageV2Dto image in source.Images)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (image.Id == Guid.Empty)
            {
                throw new InvalidDataException(
                    "Формат v2 содержит изображение с пустым Id.");
            }

            if (!imageIds.Add(image.Id))
            {
                throw new InvalidDataException(
                    $"Формат v2 содержит повторный Id изображения {image.Id}.");
            }

            string mediaPath = GostFormatPaths.GetImagePath(image.Id);
            System.IO.Compression.ZipArchiveEntry mediaEntry = entries.GetRequired(
                mediaPath,
                $"Для изображения {image.Id} не найдена запись '{mediaPath}'.");
            byte[] data = await GostArchiveEntryIndex.ReadBytesAsync(
                mediaEntry,
                GostArchiveEntryIndex.MaxImageBytes,
                cancellationToken);
            consumedMediaEntries.Add(mediaPath);

            document.Images.Add(new GostMigratableImage
            {
                Id = image.Id,
                FileName = image.FileName ?? string.Empty,
                MediaType = string.IsNullOrWhiteSpace(image.MediaType)
                    ? GostMediaTypes.Detect(data, image.FileName)
                    : image.MediaType,
                Data = data,
                Caption = image.Caption ?? string.Empty,
                Order = image.Order
            });
            imageData.Add(image.Id, data);
        }

        for (int paragraphIndex = 0;
             paragraphIndex < source.Paragraphs.Count;
             paragraphIndex++)
        {
            GostParagraphV2Dto paragraph = source.Paragraphs[paragraphIndex];

            if (paragraph.ImageId is Guid imageId && !imageIds.Contains(imageId))
            {
                throw new InvalidDataException(
                    $"Параграф {paragraphIndex} ссылается на отсутствующее изображение {imageId}.");
            }

            if (paragraph.ImageId is Guid placementImageId)
            {
                if (imageData[placementImageId].Length == 0)
                {
                    throw new InvalidDataException(
                        $"Изображение {placementImageId}, используемое параграфом {paragraphIndex}, не содержит данных.");
                }

                ValidatePlacementDimension(
                    paragraph.ImageWidth,
                    paragraphIndex,
                    "ImageWidth");
                ValidatePlacementDimension(
                    paragraph.ImageHeight,
                    paragraphIndex,
                    "ImageHeight");
            }

            document.Paragraphs.Add(new GostMigratableParagraph
            {
                Alignment = paragraph.Alignment,
                Style = paragraph.Style,
                FirstLineIndent = paragraph.FirstLineIndent,
                LineSpacing = paragraph.LineSpacing,
                PageBreakBefore = paragraph.PageBreakBefore,
                Runs = paragraph.Runs.Select(run => new GostMigratableRun
                {
                    Text = run.Text,
                    IsBold = run.IsBold,
                    IsItalic = run.IsItalic,
                    FontSize = run.FontSize,
                    Color = run.Color
                }).ToList(),
                ImageId = paragraph.ImageId,
                ImageWidth = paragraph.ImageId.HasValue
                    ? paragraph.ImageWidth
                    : 0,
                ImageHeight = paragraph.ImageId.HasValue
                    ? paragraph.ImageHeight
                    : 0
            });
        }

        foreach (string entryName in entries.Names)
        {
            if (entryName.StartsWith(
                    GostFormatPaths.ImageDirectory,
                    StringComparison.Ordinal) &&
                !consumedMediaEntries.Contains(entryName))
            {
                diagnostics.Warn(
                    "UNREFERENCED_MEDIA_ENTRY",
                    $"Запись '{entryName}' не объявлена в document.json и будет проигнорирована.");
            }
        }

        return document;
    }

    private static void ValidatePlacementDimension(
        double value,
        int paragraphIndex,
        string propertyName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new InvalidDataException(
                $"Параграф {paragraphIndex} содержит недопустимое значение {propertyName}: {value}.");
        }
    }
}
