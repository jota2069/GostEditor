using GostEditor.Core.Serialization.Format;
using GostEditor.Core.Serialization.Migrations;
using GostEditor.Core.Models;

namespace GostEditor.Core.Serialization;

internal static class GostArchiveInputValidator
{
    public static void ValidateForWrite(GostDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateCollectionCounts(
            document.Paragraphs.Count,
            document.Images.Count,
            document.CodeListings.Count,
            document.BibliographySources.Count);
        ValidateDocumentGeometry(
            document.PageWidth,
            document.PageHeight,
            document.MarginLeft,
            document.MarginRight,
            document.MarginTop,
            document.MarginBottom);

        long totalImageBytes = 0;
        int totalRuns = 0;
        long totalStringCharacters = 0;
        HashSet<Guid> referencedImages = document.Paragraphs
            .Where(paragraph => paragraph.ImageId.HasValue)
            .Select(paragraph => paragraph.ImageId!.Value)
            .ToHashSet();

        foreach (ImageAttachment image in document.Images)
        {
            AddImageBytes(ref totalImageBytes, image.Data.Length);
            AddString(ref totalStringCharacters, image.FileName);
            AddString(ref totalStringCharacters, image.MediaType);
            AddString(ref totalStringCharacters, image.Caption);
            GostImageInputValidator.ValidateDimensionsIfDecodable(
                image.Data,
                $"Изображение {image.Id}");
            if (referencedImages.Contains(image.Id))
            {
                GostImageInputValidator.ValidateDecodableImage(
                    image.Data,
                    $"Изображение {image.Id}");
            }
        }

        foreach (var paragraph in document.Paragraphs)
        {
            if (paragraph.Runs.Count > GostArchiveLimits.MaxRuns - totalRuns)
            {
                throw new InvalidDataException(
                    $"Документ содержит больше {GostArchiveLimits.MaxRuns} " +
                    "текстовых фрагментов.");
            }

            totalRuns += paragraph.Runs.Count;
            ValidateParagraph(
                paragraph.FirstLineIndent,
                paragraph.LineSpacing,
                paragraph.ImageWidth,
                paragraph.ImageHeight,
                paragraph.ImageId.HasValue,
                allowLegacyImageNormalization: false);
            foreach (var run in paragraph.Runs)
            {
                ValidateFontSize(run.FontSize);
                AddString(ref totalStringCharacters, run.Text);
            }
        }

        foreach (CodeListing listing in document.CodeListings)
        {
            AddString(ref totalStringCharacters, listing.FileName);
            AddString(ref totalStringCharacters, listing.RelativePath);
            AddString(ref totalStringCharacters, listing.Language);
            AddString(ref totalStringCharacters, listing.Content);
        }

        foreach (BibliographySource source in document.BibliographySources)
        {
            AddString(ref totalStringCharacters, source.Description);
        }

        AddTitlePageStrings(document.TitlePage, ref totalStringCharacters);
    }
    public static void Validate(
        GostDocumentV1Dto document,
        GostArchiveEntryIndex entries)
    {
        ValidateCollectionCounts(
            document.Paragraphs.Count,
            document.Images?.Count ?? 0,
            document.CodeListings?.Count ?? 0,
            document.BibliographySources?.Count ?? 0);
        ValidateDocumentGeometry(
            document.PageWidth,
            document.PageHeight,
            document.MarginLeft,
            document.MarginRight,
            document.MarginTop,
            document.MarginBottom);
        ValidateObjectCollections(
            document.CodeListings,
            document.BibliographySources);

        long totalImageBytes = 0;
        int totalRuns = 0;
        int materializedImageCount = document.Images?.Count ?? 0;
        foreach (GostImageV1Dto? image in document.Images ?? [])
        {
            EnsureNotNull(image, "Images");
            AddImageBytes(ref totalImageBytes, image!.Data?.LongLength ?? 0);
        }

        foreach (GostParagraphV1Dto? paragraph in document.Paragraphs)
        {
            EnsureNotNull(paragraph, "Paragraphs");
            AddRuns(ref totalRuns, paragraph!.Runs);
            ValidateParagraph(
                paragraph.FirstLineIndent,
                paragraph.LineSpacing,
                paragraph.ImageWidth,
                paragraph.ImageHeight,
                !string.IsNullOrWhiteSpace(paragraph.ImageFileName),
                allowLegacyImageNormalization: true);
            if (!string.IsNullOrWhiteSpace(paragraph.ImageFileName) &&
                entries.TryGet(paragraph.ImageFileName, out var imageEntry))
            {
                AddImageBytes(ref totalImageBytes, imageEntry.Length);
                if (imageEntry.Length > 0 &&
                    ++materializedImageCount > GostArchiveLimits.MaxImages)
                {
                    throw new InvalidDataException(
                        "После legacy-миграции документ содержит больше " +
                        $"{GostArchiveLimits.MaxImages} изображений.");
                }
            }
        }
    }

    public static void Validate(
        GostDocumentV2Dto document,
        GostArchiveEntryIndex entries)
    {
        ValidateCollectionCounts(
            document.Paragraphs.Count,
            document.Images.Count,
            document.CodeListings?.Count ?? 0,
            document.BibliographySources?.Count ?? 0);
        ValidateDocumentGeometry(
            document.PageWidth,
            document.PageHeight,
            document.MarginLeft,
            document.MarginRight,
            document.MarginTop,
            document.MarginBottom);
        ValidateObjectCollections(
            document.CodeListings,
            document.BibliographySources);

        long totalImageBytes = 0;
        int totalRuns = 0;
        foreach (GostImageV2Dto? image in document.Images)
        {
            EnsureNotNull(image, "Images");
            if (entries.TryGet(
                    GostFormatPaths.GetImagePath(image!.Id),
                    out var imageEntry))
            {
                AddImageBytes(ref totalImageBytes, imageEntry.Length);
            }
        }

        foreach (GostParagraphV2Dto? paragraph in document.Paragraphs)
        {
            EnsureNotNull(paragraph, "Paragraphs");
            AddRuns(ref totalRuns, paragraph!.Runs);
            ValidateParagraph(
                paragraph.FirstLineIndent,
                paragraph.LineSpacing,
                paragraph.ImageWidth,
                paragraph.ImageHeight,
                paragraph.ImageId.HasValue,
                allowLegacyImageNormalization: false);
        }
    }

    private static void ValidateCollectionCounts(
        int paragraphCount,
        int imageCount,
        int codeListingCount,
        int bibliographyCount)
    {
        EnsureCount("Paragraphs", paragraphCount, GostArchiveLimits.MaxParagraphs);
        EnsureCount("Images", imageCount, GostArchiveLimits.MaxImages);
        EnsureCount(
            "CodeListings",
            codeListingCount,
            GostArchiveLimits.MaxCodeListings);
        EnsureCount(
            "BibliographySources",
            bibliographyCount,
            GostArchiveLimits.MaxBibliographySources);
    }

    private static void ValidateObjectCollections(
        System.Collections.IEnumerable? codeListings,
        System.Collections.IEnumerable? bibliographySources)
    {
        EnsureNoNullElements(codeListings, "CodeListings");
        EnsureNoNullElements(bibliographySources, "BibliographySources");
    }

    private static void EnsureNoNullElements(
        System.Collections.IEnumerable? values,
        string collectionName)
    {
        if (values is null)
        {
            return;
        }

        foreach (object? value in values)
        {
            EnsureNotNull(value, collectionName);
        }
    }

    private static void AddRuns(ref int totalRuns, List<GostRunDto>? runs)
    {
        if (runs is null)
        {
            return;
        }

        if (runs.Any(run => run is null))
        {
            throw new InvalidDataException(
                "Коллекция Runs содержит null.");
        }

        if (runs.Count > GostArchiveLimits.MaxRuns - totalRuns)
        {
            throw new InvalidDataException(
                $"Документ содержит больше {GostArchiveLimits.MaxRuns} " +
                "текстовых фрагментов.");
        }

        totalRuns += runs.Count;
        foreach (GostRunDto run in runs)
        {
            ValidateFontSize(run.FontSize);
        }
    }

    private static void ValidateFontSize(double value)
    {
        if (!double.IsFinite(value) ||
            value > GostArchiveLimits.MaxFontSize)
        {
            throw new InvalidDataException(
                $"Недопустимый размер шрифта: {value}.");
        }
    }

    private static void AddTitlePageStrings(
        TitlePageInfo titlePage,
        ref long totalCharacters)
    {
        AddString(ref totalCharacters, titlePage.University);
        AddString(ref totalCharacters, titlePage.Department);
        AddString(ref totalCharacters, titlePage.Discipline);
        AddString(ref totalCharacters, titlePage.WorkType);
        AddString(ref totalCharacters, titlePage.WorkTitle);
        AddString(ref totalCharacters, titlePage.StudentName);
        AddString(ref totalCharacters, titlePage.GroupNumber);
        AddString(ref totalCharacters, titlePage.TeacherName);
        AddString(ref totalCharacters, titlePage.City);
    }

    private static void AddString(ref long totalCharacters, string? value)
    {
        int length = value?.Length ?? 0;
        if (length > GostArchiveLimits.MaxJsonStringCharacters)
        {
            throw new InvalidDataException(
                "Документ содержит строку недопустимой длины.");
        }

        if (length > GostArchiveLimits.MaxTotalJsonStringCharacters -
            totalCharacters)
        {
            throw new InvalidDataException(
                "Суммарный размер строк документа превышает допустимый предел.");
        }

        totalCharacters += length;
    }

    private static void ValidateDocumentGeometry(
        double pageWidth,
        double pageHeight,
        double marginLeft,
        double marginRight,
        double marginTop,
        double marginBottom)
    {
        ValidateOptionalPositiveDimension(pageWidth, "PageWidth");
        ValidateOptionalPositiveDimension(pageHeight, "PageHeight");
        ValidateOptionalNonNegativeDimension(marginLeft, "MarginLeft");
        ValidateOptionalNonNegativeDimension(marginRight, "MarginRight");
        ValidateOptionalNonNegativeDimension(marginTop, "MarginTop");
        ValidateOptionalNonNegativeDimension(marginBottom, "MarginBottom");

        double effectivePageWidth = pageWidth > 0 ? pageWidth : 794.0;
        double effectivePageHeight = pageHeight > 0 ? pageHeight : 1123.0;
        double effectiveMarginLeft = marginLeft > 0 ? marginLeft : 113.0;
        double effectiveMarginRight = marginRight > 0 ? marginRight : 57.0;
        double effectiveMarginTop = marginTop > 0 ? marginTop : 76.0;
        double effectiveMarginBottom = marginBottom > 0 ? marginBottom : 76.0;
        if (effectivePageWidth <= effectiveMarginLeft + effectiveMarginRight)
        {
            throw new InvalidDataException(
                "Поля страницы не оставляют положительной ширины содержимого.");
        }

        if (effectivePageHeight <= effectiveMarginTop + effectiveMarginBottom)
        {
            throw new InvalidDataException(
                "Поля страницы не оставляют положительной высоты содержимого.");
        }
    }

    private static void ValidateParagraph(
        double? firstLineIndent,
        double? lineSpacing,
        double imageWidth,
        double imageHeight,
        bool hasImage,
        bool allowLegacyImageNormalization)
    {
        if (firstLineIndent.HasValue &&
            (!double.IsFinite(firstLineIndent.Value) ||
             Math.Abs(firstLineIndent.Value) >
             GostArchiveLimits.MaxLayoutDimension))
        {
            throw new InvalidDataException(
                $"Недопустимый отступ абзаца: {firstLineIndent}.");
        }

        if (lineSpacing.HasValue &&
            (!double.IsFinite(lineSpacing.Value) ||
             lineSpacing.Value <= 0 ||
             lineSpacing.Value > GostArchiveLimits.MaxLayoutDimension))
        {
            throw new InvalidDataException(
                $"Недопустимый межстрочный интервал: {lineSpacing}.");
        }

        if (hasImage)
        {
            if (allowLegacyImageNormalization &&
                (imageWidth <= 0 || imageHeight <= 0))
            {
                return;
            }

            ValidateRequiredPositiveDimension(imageWidth, "ImageWidth");
            ValidateRequiredPositiveDimension(imageHeight, "ImageHeight");
        }
    }

    private static void ValidateOptionalPositiveDimension(
        double value,
        string name)
    {
        if (!double.IsFinite(value) ||
            value > GostArchiveLimits.MaxLayoutDimension)
        {
            throw new InvalidDataException(
                $"Недопустимое значение {name}: {value}.");
        }
    }

    private static void ValidateOptionalNonNegativeDimension(
        double value,
        string name)
    {
        if (!double.IsFinite(value) ||
            value > GostArchiveLimits.MaxLayoutDimension)
        {
            throw new InvalidDataException(
                $"Недопустимое значение {name}: {value}.");
        }
    }

    private static void ValidateRequiredPositiveDimension(
        double value,
        string name)
    {
        if (!double.IsFinite(value) ||
            value <= 0 ||
            value > GostArchiveLimits.MaxLayoutDimension)
        {
            throw new InvalidDataException(
                $"Недопустимое значение {name}: {value}.");
        }
    }

    private static void AddImageBytes(ref long totalBytes, long imageBytes)
    {
        if (imageBytes > GostArchiveLimits.MaxImageBytes)
        {
            throw new InvalidDataException(
                "Размер одного изображения превышает допустимый предел " +
                $"{GostArchiveLimits.MaxImageBytes} байт.");
        }

        if (imageBytes > GostArchiveLimits.MaxTotalImageBytes - totalBytes)
        {
            throw new InvalidDataException(
                "Суммарный размер изображений превышает допустимый предел " +
                $"{GostArchiveLimits.MaxTotalImageBytes} байт.");
        }

        totalBytes += imageBytes;
    }

    private static void EnsureCount(string name, int count, int maximum)
    {
        if (count > maximum)
        {
            throw new InvalidDataException(
                $"Коллекция {name} содержит {count} элементов; " +
                $"допустимо не более {maximum}.");
        }
    }

    private static void EnsureNotNull(object? value, string collectionName)
    {
        if (value is null)
        {
            throw new InvalidDataException(
                $"Коллекция {collectionName} содержит null.");
        }
    }
}
