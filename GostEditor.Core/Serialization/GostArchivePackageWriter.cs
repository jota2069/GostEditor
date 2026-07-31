using System.IO.Compression;
using System.Text;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization.Format;
using GostEditor.Core.TextEngine.DOM;
using Newtonsoft.Json;

namespace GostEditor.Core.Serialization;

internal sealed class GostArchivePackageWriter
{
    public async Task WriteAsync(
        GostDocument document,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);

        document.ModifiedAt = DateTime.UtcNow;
        WriteProjection projection = CreateProjection(document);

        using ZipArchive archive = new(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true);

        foreach ((Guid imageId, ReadOnlyMemory<byte> data) in projection.Media)
        {
            string mediaPath = GostFormatPaths.GetImagePath(imageId);
            ZipArchiveEntry mediaEntry = archive.CreateEntry(
                mediaPath,
                CompressionLevel.Optimal);
            await using Stream mediaStream = mediaEntry.Open();
            await mediaStream.WriteAsync(data, cancellationToken);
        }

        string json = JsonConvert.SerializeObject(
            projection.Manifest,
            Formatting.Indented);
        ZipArchiveEntry manifestEntry = archive.CreateEntry(
            GostFormatPaths.Manifest,
            CompressionLevel.Optimal);
        await using Stream manifestStream = manifestEntry.Open();
        await using StreamWriter writer = new(
            manifestStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(json.AsMemory(), cancellationToken);
    }

    private static WriteProjection CreateProjection(GostDocument document)
    {
        document.Counters.ImagesCount = document.Paragraphs.Count(
            paragraph => paragraph.ImageId.HasValue);

        GostDocumentV2Dto manifest = new()
        {
            FormatVersion = GostFormatVersions.Current,
            TitlePage = document.TitlePage,
            CodeListings = document.CodeListings,
            BibliographySources = document.BibliographySources,
            Modules = document.Modules,
            Counters = document.Counters,
            CreatedAt = document.CreatedAt,
            ModifiedAt = document.ModifiedAt,
            PageWidth = document.PageWidth,
            PageHeight = document.PageHeight,
            MarginLeft = document.MarginLeft,
            MarginRight = document.MarginRight,
            MarginTop = document.MarginTop,
            MarginBottom = document.MarginBottom
        };
        List<(Guid ImageId, ReadOnlyMemory<byte> Data)> media = new();
        HashSet<Guid> imageIds = new();
        Dictionary<Guid, ReadOnlyMemory<byte>> imageDataById = new();

        foreach (ImageAttachment image in document.Images)
        {
            if (image.Id == Guid.Empty)
            {
                throw new InvalidDataException(
                    "Невозможно сохранить изображение с пустым Id.");
            }

            if (!imageIds.Add(image.Id))
            {
                throw new InvalidDataException(
                    $"Невозможно сохранить повторный Id изображения {image.Id}.");
            }

            manifest.Images.Add(new GostImageV2Dto
            {
                Id = image.Id,
                FileName = image.FileName,
                MediaType = image.MediaType,
                Caption = image.Caption,
                Order = image.Order
            });
            media.Add((image.Id, image.Data));
            imageDataById.Add(image.Id, image.Data);
        }

        for (int paragraphIndex = 0;
             paragraphIndex < document.Paragraphs.Count;
             paragraphIndex++)
        {
            Paragraph paragraph = document.Paragraphs[paragraphIndex];

            if (paragraph.ImageId is Guid imageId)
            {
                if (imageId == Guid.Empty || !imageIds.Contains(imageId))
                {
                    throw new InvalidDataException(
                        $"Параграф {paragraphIndex} ссылается на отсутствующее изображение {imageId}.");
                }

                if (imageDataById[imageId].IsEmpty)
                {
                    throw new InvalidDataException(
                        $"Изображение {imageId}, используемое параграфом {paragraphIndex}, не содержит данных.");
                }

                ValidatePlacementDimension(
                    paragraph.ImageWidth,
                    paragraphIndex,
                    nameof(paragraph.ImageWidth));
                ValidatePlacementDimension(
                    paragraph.ImageHeight,
                    paragraphIndex,
                    nameof(paragraph.ImageHeight));
            }

            GostParagraphV2Dto paragraphDto = new()
            {
                Alignment = (int)paragraph.Alignment,
                Style = (int)paragraph.Style,
                FirstLineIndent = paragraph.FirstLineIndent,
                LineSpacing = paragraph.LineSpacing,
                PageBreakBefore = paragraph.PageBreakBefore,
                ImageId = paragraph.ImageId,
                ImageWidth = paragraph.ImageId.HasValue
                    ? paragraph.ImageWidth
                    : 0,
                ImageHeight = paragraph.ImageId.HasValue
                    ? paragraph.ImageHeight
                    : 0
            };

            foreach (TextRun run in paragraph.Runs)
            {
                paragraphDto.Runs.Add(new GostRunDto
                {
                    Text = run.Text,
                    IsBold = run.IsBold,
                    IsItalic = run.IsItalic,
                    FontSize = run.FontSize,
                    Color = run.Color
                });
            }

            manifest.Paragraphs.Add(paragraphDto);
        }

        return new WriteProjection(manifest, media);
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

    private sealed record WriteProjection(
        GostDocumentV2Dto Manifest,
        IReadOnlyList<(Guid ImageId, ReadOnlyMemory<byte> Data)> Media);
}
