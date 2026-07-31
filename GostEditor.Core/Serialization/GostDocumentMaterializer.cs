using GostEditor.Core.Models;
using GostEditor.Core.Serialization.Migrations;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.Serialization;

internal static class GostDocumentMaterializer
{
    public static GostDocument Materialize(GostMigratableDocument source)
    {
        GostDocument document = new()
        {
            TitlePage = source.TitlePage ?? new TitlePageInfo(),
            CodeListings = source.CodeListings ?? new List<CodeListing>(),
            BibliographySources =
                source.BibliographySources ?? new List<BibliographySource>(),
            Modules = source.Modules ?? new DocumentModules(),
            Counters = source.Counters ?? new DocumentCounters(),
            CreatedAt = source.CreatedAt == default
                ? DateTime.UtcNow
                : source.CreatedAt,
            ModifiedAt = source.ModifiedAt == default
                ? DateTime.UtcNow
                : source.ModifiedAt,
            PageWidth = source.PageWidth > 0 ? source.PageWidth : 794.0,
            PageHeight = source.PageHeight > 0 ? source.PageHeight : 1123.0,
            MarginLeft = source.MarginLeft > 0 ? source.MarginLeft : 113.0,
            MarginRight = source.MarginRight > 0 ? source.MarginRight : 57.0,
            MarginTop = source.MarginTop > 0 ? source.MarginTop : 76.0,
            MarginBottom = source.MarginBottom > 0 ? source.MarginBottom : 76.0
        };

        HashSet<Guid> imageIds = new();
        foreach (GostMigratableImage image in source.Images)
        {
            if (image.Id == Guid.Empty || !imageIds.Add(image.Id))
            {
                throw new InvalidDataException(
                    $"Невозможно материализовать изображение с Id {image.Id}.");
            }

            document.MutableImages.Add(new ImageAttachment(
                image.Id,
                image.FileName,
                image.MediaType,
                image.Data,
                image.Caption,
                image.Order));
        }

        for (int paragraphIndex = 0;
             paragraphIndex < source.Paragraphs.Count;
             paragraphIndex++)
        {
            GostMigratableParagraph sourceParagraph =
                source.Paragraphs[paragraphIndex];

            if (sourceParagraph.ImageId is Guid imageId &&
                !imageIds.Contains(imageId))
            {
                throw new InvalidDataException(
                    $"Параграф {paragraphIndex} ссылается на отсутствующее изображение {imageId}.");
            }

            Paragraph paragraph = new()
            {
                Alignment = (GostAlignment)sourceParagraph.Alignment,
                Style = (ParagraphStyle)sourceParagraph.Style,
                FirstLineIndent = sourceParagraph.FirstLineIndent
                    ?? GetDefaultFirstLineIndent(
                        (ParagraphStyle)sourceParagraph.Style),
                LineSpacing = sourceParagraph.LineSpacing
                    ?? new Paragraph().LineSpacing,
                PageBreakBefore = sourceParagraph.PageBreakBefore,
                ImageId = sourceParagraph.ImageId,
                ImageWidth = sourceParagraph.ImageWidth,
                ImageHeight = sourceParagraph.ImageHeight
            };

            foreach (GostMigratableRun sourceRun in sourceParagraph.Runs)
            {
                paragraph.Runs.Add(new TextRun
                {
                    Text = sourceRun.Text,
                    IsBold = sourceRun.IsBold,
                    IsItalic = sourceRun.IsItalic,
                    FontSize = sourceRun.FontSize > 0
                        ? sourceRun.FontSize
                        : 14.0,
                    Color = sourceRun.Color ?? 0xFF000000
                });
            }

            document.Paragraphs.Add(paragraph);
        }

        document.Counters.ImagesCount = document.Paragraphs.Count(
            paragraph => paragraph.ImageId.HasValue);

        return document;
    }

    private static double GetDefaultFirstLineIndent(ParagraphStyle style)
    {
        return style is ParagraphStyle.Heading1
            or ParagraphStyle.Heading2
            or ParagraphStyle.Heading3
            or ParagraphStyle.Code
                ? 0
                : new Paragraph().FirstLineIndent;
    }
}
