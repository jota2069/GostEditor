using GostEditor.Core.Serialization.Format;

namespace GostEditor.Core.Serialization.Migrations;

internal sealed class GostV1ToV2Migration : IGostFormatMigration
{
    public int FromVersion => GostFormatVersions.Legacy;
    public int ToVersion => GostFormatVersions.Current;

    public async Task ApplyAsync(
        GostMigrationContext context,
        CancellationToken cancellationToken = default)
    {
        GostDocumentV1Dto legacy = context.LegacyDocument
            ?? throw new InvalidOperationException(
                "Для миграции v1→v2 отсутствует legacy-модель документа.");

        GostMigratableDocument document = CopyDocumentProperties(legacy);
        HashSet<Guid> usedIds = new();

        foreach (GostImageV1Dto legacyImage in legacy.Images ?? Enumerable.Empty<GostImageV1Dto>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            Guid imageId = EnsureUniqueLegacyId(
                legacyImage.Id,
                usedIds,
                context.Diagnostics);
            byte[] data = legacyImage.Data ?? Array.Empty<byte>();
            string fileName = legacyImage.FileName ?? string.Empty;

            document.Images.Add(new GostMigratableImage
            {
                Id = imageId,
                FileName = fileName,
                MediaType = GostMediaTypes.Detect(data, fileName),
                Data = data,
                Caption = legacyImage.Caption ?? string.Empty,
                Order = legacyImage.Order
            });
        }

        for (int paragraphIndex = 0;
             paragraphIndex < legacy.Paragraphs.Count;
             paragraphIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GostParagraphV1Dto source = legacy.Paragraphs[paragraphIndex];
            GostMigratableParagraph paragraph = CopyParagraph(source);

            if (!string.IsNullOrWhiteSpace(source.ImageFileName))
            {
                if (!context.Entries.TryGet(
                        source.ImageFileName,
                        out System.IO.Compression.ZipArchiveEntry imageEntry))
                {
                    context.Diagnostics.Warn(
                        "LEGACY_IMAGE_MISSING",
                        $"Параграф {paragraphIndex}: запись '{source.ImageFileName}' отсутствует; абзац загружен без изображения.");
                }
                else
                {
                    byte[] data = await GostArchiveEntryIndex.ReadBytesAsync(
                        imageEntry,
                        GostArchiveEntryIndex.MaxImageBytes,
                        cancellationToken);
                    if (data.Length == 0)
                    {
                        context.Diagnostics.Warn(
                            "LEGACY_IMAGE_EMPTY",
                            $"Параграф {paragraphIndex}: запись " +
                            $"'{source.ImageFileName}' пуста; абзац загружен без изображения.");
                    }
                    else
                    {
                        Guid imageId = CreateUniqueId(usedIds);
                        string fileName = GetLegacyFileName(
                            source.ImageFileName,
                            paragraphIndex);

                        document.Images.Add(new GostMigratableImage
                        {
                            Id = imageId,
                            FileName = fileName,
                            MediaType = GostMediaTypes.Detect(data, fileName),
                            Data = data,
                            Caption = string.Empty,
                            Order = paragraphIndex
                        });
                        paragraph.ImageId = imageId;
                        NormalizeLegacyImageSize(
                            paragraph,
                            paragraphIndex,
                            context.Diagnostics);
                    }
                }
            }

            document.Paragraphs.Add(paragraph);
        }

        context.Document = document;
        context.LegacyDocument = null;
        context.WorkingVersion = ToVersion;
    }

    private static GostMigratableDocument CopyDocumentProperties(
        GostDocumentV1Dto source)
    {
        return new GostMigratableDocument
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
    }

    private static GostMigratableParagraph CopyParagraph(
        GostParagraphV1Dto source)
    {
        return new GostMigratableParagraph
        {
            Alignment = source.Alignment,
            Style = source.Style,
            FirstLineIndent = source.FirstLineIndent,
            LineSpacing = source.LineSpacing,
            PageBreakBefore = source.PageBreakBefore,
            Runs = source.Runs.Select(CopyRun).ToList(),
            ImageWidth = source.ImageWidth,
            ImageHeight = source.ImageHeight
        };
    }

    private static GostMigratableRun CopyRun(GostRunDto source)
    {
        return new GostMigratableRun
        {
            Text = source.Text,
            IsBold = source.IsBold,
            IsItalic = source.IsItalic,
            FontSize = source.FontSize,
            Color = source.Color
        };
    }

    private static Guid EnsureUniqueLegacyId(
        Guid candidate,
        HashSet<Guid> usedIds,
        GostSerializationDiagnostics diagnostics)
    {
        if (candidate != Guid.Empty && usedIds.Add(candidate))
        {
            return candidate;
        }

        Guid replacement = CreateUniqueId(usedIds);
        diagnostics.Warn(
            "LEGACY_IMAGE_ID_REPLACED",
            candidate == Guid.Empty
                ? $"Пустой legacy ID изображения заменён на {replacement}."
                : $"Повторный legacy ID {candidate} заменён на {replacement}.");
        return replacement;
    }

    private static Guid CreateUniqueId(HashSet<Guid> usedIds)
    {
        Guid imageId;
        do
        {
            imageId = Guid.NewGuid();
        }
        while (!usedIds.Add(imageId));

        return imageId;
    }

    private static string GetLegacyFileName(
        string archivePath,
        int paragraphIndex)
    {
        string normalized = archivePath.Replace('\\', '/');
        int separatorIndex = normalized.LastIndexOf('/');
        string fileName = separatorIndex >= 0
            ? normalized[(separatorIndex + 1)..]
            : normalized;

        return string.IsNullOrWhiteSpace(fileName)
            ? $"image-{paragraphIndex + 1}.dat"
            : fileName;
    }

    private static void NormalizeLegacyImageSize(
        GostMigratableParagraph paragraph,
        int paragraphIndex,
        GostSerializationDiagnostics diagnostics)
    {
        bool validWidth = double.IsFinite(paragraph.ImageWidth)
            && paragraph.ImageWidth > 0;
        bool validHeight = double.IsFinite(paragraph.ImageHeight)
            && paragraph.ImageHeight > 0;
        if (validWidth && validHeight)
        {
            return;
        }

        diagnostics.Warn(
            "LEGACY_IMAGE_SIZE_NORMALIZED",
            $"Параграф {paragraphIndex}: недопустимый размер изображения " +
            $"{paragraph.ImageWidth}×{paragraph.ImageHeight} заменён на 450×300.");
        paragraph.ImageWidth = 450;
        paragraph.ImageHeight = 300;
    }
}
