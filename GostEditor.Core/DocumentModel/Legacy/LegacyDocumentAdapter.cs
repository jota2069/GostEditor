using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.DocumentModel.Legacy;

/// <summary>
/// Converts between the current runtime model used by .gost v2 and the new
/// structured model. No v2 serialization contract is changed by this adapter.
/// </summary>
public sealed class LegacyDocumentAdapter
{
    public DocumentRoot ToDocumentModel(GostDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        DocumentRoot target = new()
        {
            Metadata = CopyMetadataFromLegacy(source)
        };

        foreach (ImageAttachment image in source.Images)
        {
            target.Resources.AddImage(new ImageResource(
                image.Id,
                image.FileName,
                image.MediaType,
                image.Data,
                image.Caption,
                image.Order));
        }

        DocumentSection section = new()
        {
            PageSettings = new DocumentPageSettings
            {
                Width = source.PageWidth,
                Height = source.PageHeight,
                MarginLeft = source.MarginLeft,
                MarginRight = source.MarginRight,
                MarginTop = source.MarginTop,
                MarginBottom = source.MarginBottom
            }
        };

        foreach (Paragraph paragraph in source.Paragraphs)
        {
            if (paragraph.IsImage && paragraph.ImageId is Guid imageId)
            {
                FigureBlock figure = new(
                    imageId,
                    paragraph.ImageWidth,
                    paragraph.ImageHeight)
                {
                    Caption = paragraph.GetPlainText()
                };
                section.Blocks.Add(figure);
                continue;
            }

            ParagraphBlock block = new()
            {
                Properties = new ParagraphProperties
                {
                    Alignment = paragraph.Alignment,
                    FirstLineIndent = paragraph.FirstLineIndent,
                    LineSpacing = paragraph.LineSpacing,
                    Style = paragraph.Style,
                    PageBreakBefore = paragraph.PageBreakBefore
                }
            };

            foreach (TextRun run in paragraph.Runs)
            {
                block.Inlines.Add(new TextInline(run.Text)
                {
                    Style = new TextStyle
                    {
                        IsBold = run.IsBold,
                        IsItalic = run.IsItalic,
                        Color = run.Color,
                        FontSize = run.FontSize
                    }
                });
            }

            block.EnsureEditableInline();
            section.Blocks.Add(block);
        }

        target.Sections.Add(section);
        target.EnsureEditableStructure();
        return target;
    }

    public GostDocument ToLegacyDocument(DocumentRoot source)
    {
        ArgumentNullException.ThrowIfNull(source);

        ValidateLegacyProjection(source);

        DocumentSection section = source.Sections.FirstOrDefault()
            ?? new DocumentSection();
        DocumentPageSettings pageSettings = section.PageSettings;

        GostDocument target = new()
        {
            TitlePage = CloneTitlePage(source.Metadata.TitlePage),
            Modules = CloneModules(source.Metadata.Modules),
            Counters = CloneCounters(source.Metadata.Counters),
            CodeListings = CloneCodeListings(source.Metadata.CodeListings),
            BibliographySources = CloneBibliography(
                source.Metadata.BibliographySources),
            CreatedAt = source.Metadata.CreatedAtUtc,
            ModifiedAt = source.Metadata.ModifiedAtUtc,
            PageWidth = pageSettings.Width,
            PageHeight = pageSettings.Height,
            MarginLeft = pageSettings.MarginLeft,
            MarginRight = pageSettings.MarginRight,
            MarginTop = pageSettings.MarginTop,
            MarginBottom = pageSettings.MarginBottom
        };

        foreach (ImageResource image in source.Resources.Images.Values)
        {
            target.MutableImages.Add(new ImageAttachment(
                image.Id,
                image.FileName,
                image.MediaType,
                image.Data,
                image.Caption,
                image.Order));
        }

        foreach (BlockNode block in source.EnumerateBlocks())
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    target.Paragraphs.Add(ToLegacyParagraph(paragraph));
                    break;

                case FigureBlock figure:
                    target.Paragraphs.Add(ToLegacyFigure(figure));
                    break;

                case PageBreakBlock:
                    target.Paragraphs.Add(new Paragraph
                    {
                        PageBreakBefore = true,
                        Runs = new List<TextRun>
                        {
                            new(string.Empty)
                        }
                    });
                    break;

                case CodeBlock code:
                    target.Paragraphs.Add(new Paragraph
                    {
                        Style = ParagraphStyle.Code,
                        FirstLineIndent = 0,
                        Runs = new List<TextRun>
                        {
                            new(code.Code)
                        }
                    });
                    break;

                default:
                    throw new NotSupportedException(
                        $"Блок {block.GetType().Name} нельзя без потерь " +
                        "сохранить в текущую модель .gost v2.");
            }
        }

        if (target.Paragraphs.Count == 0)
        {
            target.Paragraphs.Add(new Paragraph());
        }

        target.Counters.ImagesCount = target.Paragraphs.Count(
            paragraph => paragraph.IsImage);

        return target;
    }

    private static void ValidateLegacyProjection(DocumentRoot source)
    {
        if (source.Sections.Count > 1)
        {
            throw new NotSupportedException(
                "Текущая модель .gost v2 не поддерживает несколько секций.");
        }

        foreach (DocumentSection section in source.Sections)
        {
            if (section.HeaderBlocks.Count > 0 ||
                section.FooterBlocks.Count > 0)
            {
                throw new NotSupportedException(
                    "Текущая модель .gost v2 не поддерживает колонтитулы.");
            }
        }
    }

    private static Paragraph ToLegacyParagraph(ParagraphBlock source)
    {
        Paragraph paragraph = new()
        {
            Alignment = source.Properties.Alignment,
            FirstLineIndent = source.Properties.FirstLineIndent,
            LineSpacing = source.Properties.LineSpacing,
            Style = source.Properties.Style,
            PageBreakBefore = source.Properties.PageBreakBefore
        };

        foreach (InlineNode inline in source.Inlines)
        {
            switch (inline)
            {
                case TextInline text:
                    paragraph.Runs.Add(new TextRun
                    {
                        Text = text.Text,
                        IsBold = text.Style.IsBold,
                        IsItalic = text.Style.IsItalic,
                        Color = text.Style.Color,
                        FontSize = text.Style.FontSize
                    });
                    break;

                case LinkInline link:
                    paragraph.Runs.Add(new TextRun
                    {
                        Text = link.Text,
                        IsBold = link.Style.IsBold,
                        IsItalic = link.Style.IsItalic,
                        Color = link.Style.Color,
                        FontSize = link.Style.FontSize
                    });
                    break;

                case LineBreakInline:
                    paragraph.Runs.Add(new TextRun("\n"));
                    break;

                default:
                    throw new NotSupportedException(
                        $"Inline-узел {inline.GetType().Name} нельзя без " +
                        "потерь сохранить в текущую модель .gost v2.");
            }
        }

        if (paragraph.Runs.Count == 0)
        {
            paragraph.Runs.Add(new TextRun(string.Empty));
        }

        return paragraph;
    }

    private static Paragraph ToLegacyFigure(FigureBlock source)
    {
        Paragraph paragraph = new()
        {
            ImageId = source.ImageId,
            ImageWidth = source.Width,
            ImageHeight = source.Height,
            FirstLineIndent = 0,
            Alignment = GostAlignment.Center
        };

        if (!string.IsNullOrEmpty(source.Caption))
        {
            paragraph.Runs.Add(new TextRun(source.Caption));
        }

        return paragraph;
    }

    private static DocumentMetadata CopyMetadataFromLegacy(GostDocument source) =>
        new()
        {
            TitlePage = CloneTitlePage(source.TitlePage),
            Modules = CloneModules(source.Modules),
            Counters = CloneCounters(source.Counters),
            CodeListings = CloneCodeListings(source.CodeListings),
            BibliographySources = CloneBibliography(
                source.BibliographySources),
            CreatedAtUtc = source.CreatedAt,
            ModifiedAtUtc = source.ModifiedAt
        };

    private static TitlePageInfo CloneTitlePage(TitlePageInfo source) => new()
    {
        University = source.University,
        Department = source.Department,
        Discipline = source.Discipline,
        WorkType = source.WorkType,
        WorkTitle = source.WorkTitle,
        StudentName = source.StudentName,
        GroupNumber = source.GroupNumber,
        TeacherName = source.TeacherName,
        Year = source.Year,
        City = source.City
    };

    private static DocumentModules CloneModules(DocumentModules source) => new()
    {
        HasTitlePage = source.HasTitlePage,
        HasTableOfContents = source.HasTableOfContents,
        HasBibliography = source.HasBibliography,
        HasAppendix = source.HasAppendix,
        ContentStartPage = source.ContentStartPage,
        AutoGenerateTOC = source.AutoGenerateTOC,
        TOCMaxLevel = source.TOCMaxLevel
    };

    private static DocumentCounters CloneCounters(DocumentCounters source) =>
        new()
        {
            ImagesCount = source.ImagesCount,
            TablesCount = source.TablesCount,
            SourcesCount = source.SourcesCount,
            PagesCount = source.PagesCount,
            ApplicationsCount = source.ApplicationsCount
        };

    private static List<CodeListing> CloneCodeListings(
        IEnumerable<CodeListing> source) =>
        source.Select(item => new CodeListing
        {
            Id = item.Id,
            FileName = item.FileName,
            RelativePath = item.RelativePath,
            Language = item.Language,
            Content = item.Content,
            IsSelected = item.IsSelected,
            Order = item.Order,
            ListingNumber = item.ListingNumber
        }).ToList();

    private static List<BibliographySource> CloneBibliography(
        IEnumerable<BibliographySource> source) =>
        source.Select(item => new BibliographySource
        {
            Id = item.Id,
            Description = item.Description,
            IsSelected = item.IsSelected,
            Order = item.Order
        }).ToList();
}
