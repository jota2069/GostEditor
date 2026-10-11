using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;
using System.Text.Json;

namespace GostEditor.Core.DocumentModel.Legacy;

/// <summary>
/// Converts between the current runtime model used by .gost v2 and the new
/// structured model. No v2 serialization contract is changed by this adapter.
/// </summary>
public sealed class LegacyDocumentAdapter
{
    public const string LegacyCompatibilityExtension =
        "org.gosteditor.compatibility.gost-v2";

    private const string JsonMediaType = "application/json";

    public DocumentRoot ToDocumentModel(GostDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateLegacySource(source);

        LegacyCompatibilityData compatibility = new()
        {
            TitlePage = CloneTitlePage(source.TitlePage),
            Modules = CloneModules(source.Modules),
            Counters = CloneCounters(source.Counters),
            CodeListings = CloneCodeListings(source.CodeListings),
            BibliographySources = CloneBibliography(
                source.BibliographySources)
        };

        DocumentRoot target = new()
        {
            Metadata = new DocumentMetadata
            {
                CreatedAtUtc = source.CreatedAt,
                ModifiedAtUtc = source.ModifiedAt
            }
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
                compatibility.FigureCaptionRuns[figure.Id.ToString()] =
                    CloneRuns(paragraph.Runs);
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
        target.Extensions.Set(
            LegacyCompatibilityExtension,
            schemaVersion: 1,
            JsonMediaType,
            JsonSerializer.SerializeToUtf8Bytes(compatibility));
        target.EnsureEditableStructure();
        return target;
    }

    public GostDocument ToLegacyDocument(DocumentRoot source)
    {
        ArgumentNullException.ThrowIfNull(source);

        LegacyCompatibilityData compatibility =
            ValidateLegacyProjection(source);

        DocumentSection section = source.Sections.FirstOrDefault()
            ?? new DocumentSection();
        DocumentPageSettings pageSettings = section.PageSettings;

        GostDocument target = new()
        {
            TitlePage = CloneTitlePage(compatibility.TitlePage),
            Modules = CloneModules(compatibility.Modules),
            Counters = CloneCounters(compatibility.Counters),
            CodeListings = CloneCodeListings(compatibility.CodeListings),
            BibliographySources = CloneBibliography(
                compatibility.BibliographySources),
            CreatedAt = source.Metadata.CreatedAtUtc,
            ModifiedAt = source.Metadata.ModifiedAtUtc,
            PageWidth = pageSettings.Width,
            PageHeight = pageSettings.Height,
            MarginLeft = pageSettings.MarginLeft,
            MarginRight = pageSettings.MarginRight,
            MarginTop = pageSettings.MarginTop,
            MarginBottom = pageSettings.MarginBottom
        };

        foreach (ImageResource image in source.Resources.ImagesInOrder)
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
                    target.Paragraphs.Add(ToLegacyFigure(
                        figure,
                        compatibility));
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
                    if (!string.IsNullOrEmpty(code.Language) ||
                        !string.IsNullOrEmpty(code.Caption))
                    {
                        throw new NotSupportedException(
                            "Language и caption блока кода нельзя без потерь " +
                            "сохранить в текущую модель .gost v2.");
                    }

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

        return target;
    }

    private static LegacyCompatibilityData ValidateLegacyProjection(
        DocumentRoot source)
    {
        DocumentGraphValidationReport graph = source.Validate();
        if (!graph.IsValid)
        {
            throw new InvalidDocumentGraphException(graph);
        }

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

        string? unsupportedExtension = source.Extensions.Parts.Keys.FirstOrDefault(
            key => !string.Equals(
                key,
                LegacyCompatibilityExtension,
                StringComparison.Ordinal));
        if (unsupportedExtension is not null)
        {
            throw new NotSupportedException(
                $"Extension {unsupportedExtension} нельзя без потерь " +
                "сохранить в текущую модель .gost v2.");
        }

        if (!source.Extensions.TryGet(
                LegacyCompatibilityExtension,
                out DocumentExtensionPart? part))
        {
            return new LegacyCompatibilityData();
        }

        if (part is null || part.SchemaVersion != 1 ||
            !string.Equals(part.MediaType, JsonMediaType, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Legacy compatibility extension имеет неподдерживаемый contract.");
        }

        try
        {
            LegacyCompatibilityData result =
                JsonSerializer.Deserialize<LegacyCompatibilityData>(
                    part.Data.Span)
                ?? throw new InvalidDataException(
                    "Legacy compatibility extension пуст.");
            ValidateCompatibilityData(result);
            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Legacy compatibility extension содержит некорректный JSON.",
                exception);
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
                    if (!string.IsNullOrWhiteSpace(text.Style.FontFamily))
                    {
                        throw new NotSupportedException(
                            "FontFamily нельзя без потерь сохранить в " +
                            "текущую модель .gost v2.");
                    }

                    paragraph.Runs.Add(new TextRun
                    {
                        Text = text.Text,
                        IsBold = text.Style.IsBold,
                        IsItalic = text.Style.IsItalic,
                        Color = text.Style.Color,
                        FontSize = text.Style.FontSize
                    });
                    break;

                case LinkInline:
                    throw new NotSupportedException(
                        "Ссылки нельзя без потерь сохранить в текущую " +
                        "модель .gost v2.");

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

    private static Paragraph ToLegacyFigure(
        FigureBlock source,
        LegacyCompatibilityData compatibility)
    {
        Paragraph paragraph = new()
        {
            ImageId = source.ImageId,
            ImageWidth = source.Width,
            ImageHeight = source.Height,
            FirstLineIndent = 0,
            Alignment = GostAlignment.Center
        };

        if (compatibility.FigureCaptionRuns.TryGetValue(
                source.Id.ToString(),
                out List<LegacyRunCompatibilityData>? runs) &&
            string.Equals(
                source.Caption,
                string.Concat(runs.Select(run => run.Text)),
                StringComparison.Ordinal))
        {
            paragraph.Runs = RestoreRuns(runs);
        }
        else if (!string.IsNullOrEmpty(source.Caption))
        {
            paragraph.Runs.Add(new TextRun(source.Caption));
        }

        return paragraph;
    }

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

    private static List<LegacyRunCompatibilityData> CloneRuns(
        IEnumerable<TextRun> source) =>
        source.Select(run => new LegacyRunCompatibilityData
        {
            Text = run.Text,
            IsBold = run.IsBold,
            IsItalic = run.IsItalic,
            Color = run.Color,
            FontSize = run.FontSize
        }).ToList();

    private static List<TextRun> RestoreRuns(
        IEnumerable<LegacyRunCompatibilityData> source) =>
        source.Select(run => new TextRun
        {
            Text = run.Text,
            IsBold = run.IsBold,
            IsItalic = run.IsItalic,
            Color = run.Color,
            FontSize = run.FontSize
        }).ToList();

    private static void ValidateLegacySource(GostDocument source)
    {
        if (source.TitlePage is null || source.Modules is null ||
            source.Counters is null || source.Paragraphs is null ||
            source.CodeListings is null || source.BibliographySources is null)
        {
            throw new InvalidDataException(
                "Legacy document содержит null в обязательном поле.");
        }

        ValidateLegacyMetadata(
            source.TitlePage,
            source.CodeListings,
            source.BibliographySources);

        for (int paragraphIndex = 0;
             paragraphIndex < source.Paragraphs.Count;
             paragraphIndex++)
        {
            Paragraph? paragraph = source.Paragraphs[paragraphIndex];
            if (paragraph is null || paragraph.Runs is null)
            {
                throw new InvalidDataException(
                    $"Legacy paragraph {paragraphIndex} содержит null.");
            }

            if (paragraph.HasLegacyInlineImageData)
            {
                throw new NotSupportedException(
                    $"Legacy paragraph {paragraphIndex} содержит obsolete " +
                    "inline ImageData; используйте resource catalog.");
            }

            for (int runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
            {
                TextRun? run = paragraph.Runs[runIndex];
                if (run is null || run.Text is null)
                {
                    throw new InvalidDataException(
                        $"Legacy paragraph {paragraphIndex}, run {runIndex} " +
                        "содержит null.");
                }
            }
        }
    }

    private static void ValidateCompatibilityData(LegacyCompatibilityData data)
    {
        if (data.TitlePage is null || data.Modules is null ||
            data.Counters is null || data.CodeListings is null ||
            data.BibliographySources is null ||
            data.FigureCaptionRuns is null)
        {
            throw new InvalidDataException(
                "Legacy compatibility extension содержит null в " +
                "обязательном поле.");
        }

        ValidateLegacyMetadata(
            data.TitlePage,
            data.CodeListings,
            data.BibliographySources);

        foreach ((string nodeId, List<LegacyRunCompatibilityData>? runs) in
                 data.FigureCaptionRuns)
        {
            if (!Guid.TryParse(nodeId, out _) || runs is null ||
                runs.Any(run => run is null || run.Text is null))
            {
                throw new InvalidDataException(
                    "Legacy compatibility extension содержит некорректные " +
                    "caption runs.");
            }
        }
    }

    private static void ValidateLegacyMetadata(
        TitlePageInfo titlePage,
        IEnumerable<CodeListing?> codeListings,
        IEnumerable<BibliographySource?> bibliographySources)
    {
        if (titlePage.University is null || titlePage.Department is null ||
            titlePage.Discipline is null || titlePage.WorkType is null ||
            titlePage.WorkTitle is null || titlePage.StudentName is null ||
            titlePage.GroupNumber is null || titlePage.TeacherName is null ||
            titlePage.City is null || codeListings.Any(item =>
                item is null || item.FileName is null ||
                item.RelativePath is null || item.Language is null ||
                item.Content is null) || bibliographySources.Any(item =>
                item is null || item.Description is null))
        {
            throw new InvalidDataException(
                "Legacy metadata содержит null в обязательном поле.");
        }
    }

    private sealed class LegacyCompatibilityData
    {
        public TitlePageInfo TitlePage { get; set; } = new();

        public DocumentModules Modules { get; set; } = new();

        public List<CodeListing> CodeListings { get; set; } = new();

        public List<BibliographySource> BibliographySources { get; set; } = new();

        public DocumentCounters Counters { get; set; } = new();

        public Dictionary<string, List<LegacyRunCompatibilityData>>
            FigureCaptionRuns
        { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class LegacyRunCompatibilityData
    {
        public string Text { get; set; } = string.Empty;

        public bool IsBold { get; set; }

        public bool IsItalic { get; set; }

        public uint Color { get; set; }

        public double FontSize { get; set; }
    }
}
