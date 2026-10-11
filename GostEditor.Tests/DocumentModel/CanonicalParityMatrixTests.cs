using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Legacy;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.DocumentModel;

public sealed class CanonicalParityMatrixTests
{
    private readonly LegacyDocumentAdapter _adapter = new();

    [Fact]
    public void LegacyRoundTrip_PreservesCompleteMeaningfulState()
    {
        DateTime created = new(2024, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        DateTime modified = new(2025, 06, 07, 08, 09, 10, DateTimeKind.Utc);
        GostDocument source = new()
        {
            CreatedAt = created,
            ModifiedAt = modified,
            PageWidth = 901,
            PageHeight = 1201,
            MarginLeft = 41,
            MarginRight = 42,
            MarginTop = 43,
            MarginBottom = 44,
            TitlePage = new TitlePageInfo
            {
                University = "Университет",
                Department = "Кафедра",
                Discipline = "Дисциплина",
                WorkType = "Работа",
                WorkTitle = "Тема 👩🏽‍💻",
                StudentName = "Студент",
                GroupNumber = "Группа",
                TeacherName = "Преподаватель",
                Year = 2030,
                City = "Город"
            },
            Modules = new DocumentModules
            {
                HasTitlePage = false,
                HasTableOfContents = false,
                HasBibliography = true,
                HasAppendix = false,
                ContentStartPage = 7,
                AutoGenerateTOC = false,
                TOCMaxLevel = 3
            },
            Counters = new DocumentCounters
            {
                ImagesCount = 17,
                TablesCount = 18,
                SourcesCount = 19,
                PagesCount = 20,
                ApplicationsCount = 21
            },
            CodeListings =
            {
                new CodeListing
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    FileName = "пример.cs",
                    RelativePath = "src/пример.cs",
                    Language = "csharp",
                    Content = "Console.WriteLine(\"Привет\");",
                    IsSelected = false,
                    Order = 4,
                    ListingNumber = 9
                }
            },
            BibliographySources =
            {
                new BibliographySource
                {
                    Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                    Description = "Источник",
                    IsSelected = false,
                    Order = 8
                }
            },
            Paragraphs =
            {
                new Paragraph
                {
                    FirstLineIndent = 12.5,
                    LineSpacing = 1.15,
                    Alignment = GostAlignment.Right,
                    Style = ParagraphStyle.Heading3,
                    PageBreakBefore = true,
                    Runs =
                    {
                        new TextRun("A😀é", isBold: true)
                        {
                            IsItalic = true,
                            Color = 0xFF010203,
                            FontSize = 17.25
                        },
                        new TextRun(string.Empty)
                    }
                },
                new Paragraph()
            }
        };

        GostDocument result = _adapter.ToLegacyDocument(
            _adapter.ToDocumentModel(source));

        Assert.Equal(created, result.CreatedAt);
        Assert.Equal(modified, result.ModifiedAt);
        Assert.Equal(
            (901, 1201, 41, 42, 43, 44),
            (result.PageWidth, result.PageHeight, result.MarginLeft,
                result.MarginRight, result.MarginTop, result.MarginBottom));
        AssertTitlePageEqual(source.TitlePage, result.TitlePage);
        AssertModulesEqual(source.Modules, result.Modules);
        AssertCountersEqual(source.Counters, result.Counters);
        AssertListingEqual(
            Assert.Single(source.CodeListings),
            Assert.Single(result.CodeListings));
        AssertBibliographyEqual(
            Assert.Single(source.BibliographySources),
            Assert.Single(result.BibliographySources));
        Assert.Equal(2, result.Paragraphs.Count);
        AssertParagraphEqual(source.Paragraphs[0], result.Paragraphs[0]);
        Assert.Equal(string.Empty, result.Paragraphs[1].GetPlainText());
        Assert.Equal(
            new Paragraph().FirstLineIndent,
            result.Paragraphs[1].FirstLineIndent);
        Assert.Single(result.Paragraphs[1].Runs);
        Assert.Equal(string.Empty, result.Paragraphs[1].Runs[0].Text);
    }

    [Fact]
    public void LegacyRoundTrip_PreservesImageOrderAndRichCaptionRuns()
    {
        Guid firstId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        Guid secondId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        GostDocument source = new();
        source.MutableImages.Add(new ImageAttachment(
            firstId,
            "first.png",
            "image/png",
            TestImageData.CreatePng(),
            "legacy resource caption",
            10));
        source.MutableImages.Add(new ImageAttachment(
            secondId,
            "second.png",
            "image/png",
            TestImageData.CreatePng(),
            order: 5));
        source.Paragraphs.Add(CreateFigureParagraph(firstId, "Под", "пись"));
        source.Paragraphs.Add(CreateFigureParagraph(secondId));

        DocumentRoot canonical = _adapter.ToDocumentModel(source);
        GostDocument result = _adapter.ToLegacyDocument(canonical);

        Assert.Equal(new[] { firstId, secondId }, result.Images.Select(x => x.Id));
        Assert.Equal("legacy resource caption", result.Images[0].Caption);
        Assert.Equal(10, result.Images[0].Order);
        Assert.Equal(2, result.Paragraphs[0].Runs.Count);
        Assert.True(result.Paragraphs[0].Runs[0].IsBold);
        Assert.True(result.Paragraphs[0].Runs[1].IsItalic);
        Assert.Equal(0xFF123456u, result.Paragraphs[0].Runs[1].Color);
        Assert.Empty(result.Paragraphs[1].Runs);
    }

    [Fact]
    public void LegacyCompatibilityState_IsBoundedNamespacedAndDeepCopied()
    {
        GostDocument source = new();
        source.TitlePage.University = "Исходное значение";

        DocumentRoot canonical = _adapter.ToDocumentModel(source);
        source.TitlePage.University = "Изменённое значение";

        DocumentExtensionPart extension = Assert.Single(
            canonical.Extensions.Parts).Value;
        Assert.Equal(
            LegacyDocumentAdapter.LegacyCompatibilityExtension,
            extension.Namespace);
        Assert.Equal(1, extension.SchemaVersion);
        Assert.Equal("application/json", extension.MediaType);
        Assert.InRange(
            extension.Data.Length,
            1,
            DocumentExtensionCatalog.MaxPartBytes);
        Assert.Equal(
            "Исходное значение",
            _adapter.ToLegacyDocument(canonical).TitlePage.University);
        Assert.DoesNotContain(
            typeof(DocumentMetadata).GetProperties(),
            property => property.PropertyType == typeof(TitlePageInfo) ||
                property.PropertyType == typeof(DocumentModules));
    }

    [Fact]
    public void LegacyProjection_WhenFigureCaptionChanges_DiscardsStaleRunFormatting()
    {
        Guid imageId = Guid.NewGuid();
        GostDocument source = new();
        source.MutableImages.Add(new ImageAttachment(
            imageId,
            "image.png",
            "image/png",
            TestImageData.CreatePng()));
        source.Paragraphs.Add(CreateFigureParagraph(imageId, "Old"));
        DocumentRoot canonical = _adapter.ToDocumentModel(source);
        FigureBlock figure = Assert.IsType<FigureBlock>(
            canonical.Sections[0].Blocks[0]);
        figure.Caption = "New";

        Paragraph projected = Assert.Single(
            _adapter.ToLegacyDocument(canonical).Paragraphs);

        TextRun run = Assert.Single(projected.Runs);
        Assert.Equal("New", run.Text);
        Assert.False(run.IsBold);
    }

    [Fact]
    public void ToDocumentModel_WhenRequiredLegacyFieldIsNull_FailsWithDiagnostic()
    {
        GostDocument source = new()
        {
            TitlePage = null!
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => _adapter.ToDocumentModel(source));

        Assert.Contains("null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToLegacyDocument_WhenGenericExtensionWouldBeLost_FailsExplicitly()
    {
        DocumentRoot source = CreateTextDocument("text");
        source.Extensions.Set(
            "org.example.feature",
            1,
            "application/octet-stream",
            new byte[] { 1 });

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => _adapter.ToLegacyDocument(source));

        Assert.Contains("org.example.feature", exception.Message);
    }

    [Fact]
    public void ToLegacyDocument_WhenCompatibilityPayloadIsMalformed_FailsExplicitly()
    {
        DocumentRoot source = CreateTextDocument("text");
        source.Extensions.Set(
            LegacyDocumentAdapter.LegacyCompatibilityExtension,
            1,
            "application/json",
            "{"u8.ToArray());

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => _adapter.ToLegacyDocument(source));

        Assert.Contains("JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToLegacyDocument_WhenFontFamilyWouldBeLost_FailsExplicitly()
    {
        DocumentRoot source = CreateTextDocument("text");
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(
            source.Sections[0].Blocks[0]);
        Assert.IsType<TextInline>(paragraph.Inlines[0]).Style.FontFamily =
            "Custom Font";

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => _adapter.ToLegacyDocument(source));

        Assert.Contains("FontFamily", exception.Message);
    }

    [Theory]
    [InlineData("csharp", "")]
    [InlineData("", "caption")]
    public void ToLegacyDocument_WhenCodeMetadataWouldBeLost_FailsExplicitly(
        string language,
        string caption)
    {
        DocumentRoot source = new();
        DocumentSection section = new();
        section.Blocks.Add(new CodeBlock
        {
            Code = "code",
            Language = language,
            Caption = caption
        });
        source.Sections.Add(section);
        source.RebuildIndex();

        Assert.Throws<NotSupportedException>(
            () => _adapter.ToLegacyDocument(source));
    }

    [Fact]
    public void ExtensionCatalog_EnforcesLimitsAndOwnsPayload()
    {
        DocumentExtensionCatalog catalog = new();
        byte[] payload = { 1, 2, 3 };
        catalog.Set(
            "org.example.payload",
            1,
            "application/octet-stream",
            payload);
        payload[0] = 9;

        Assert.Equal(
            new byte[] { 1, 2, 3 },
            catalog.Parts["org.example.payload"].Data.ToArray());
        Assert.Throws<ArgumentException>(() => catalog.Set(
            "not-namespaced",
            1,
            "application/octet-stream",
            ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() => catalog.Set(
            "org.example.oversized",
            1,
            "application/octet-stream",
            new byte[DocumentExtensionCatalog.MaxPartBytes + 1]));
    }

    private static DocumentRoot CreateTextDocument(string text)
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        ParagraphBlock paragraph = new();
        paragraph.Inlines.Add(new TextInline(text));
        section.Blocks.Add(paragraph);
        document.Sections.Add(section);
        document.RebuildIndex();
        return document;
    }

    private static Paragraph CreateFigureParagraph(
        Guid imageId,
        params string[] texts)
    {
        Paragraph paragraph = new()
        {
            ImageId = imageId,
            ImageWidth = 100,
            ImageHeight = 50
        };
        for (int index = 0; index < texts.Length; index++)
        {
            paragraph.Runs.Add(new TextRun(texts[index])
            {
                IsBold = index == 0,
                IsItalic = index == 1,
                Color = index == 1 ? 0xFF123456 : 0xFF000000,
                FontSize = 14 + index
            });
        }

        return paragraph;
    }

    private static void AssertParagraphEqual(Paragraph expected, Paragraph actual)
    {
        Assert.Equal(expected.FirstLineIndent, actual.FirstLineIndent);
        Assert.Equal(expected.LineSpacing, actual.LineSpacing);
        Assert.Equal(expected.Alignment, actual.Alignment);
        Assert.Equal(expected.Style, actual.Style);
        Assert.Equal(expected.PageBreakBefore, actual.PageBreakBefore);
        Assert.Equal(expected.Runs.Count, actual.Runs.Count);
        for (int index = 0; index < expected.Runs.Count; index++)
        {
            TextRun expectedRun = expected.Runs[index];
            TextRun actualRun = actual.Runs[index];
            Assert.Equal(expectedRun.Text, actualRun.Text);
            Assert.Equal(expectedRun.IsBold, actualRun.IsBold);
            Assert.Equal(expectedRun.IsItalic, actualRun.IsItalic);
            Assert.Equal(expectedRun.Color, actualRun.Color);
            Assert.Equal(expectedRun.FontSize, actualRun.FontSize);
        }
    }

    private static void AssertTitlePageEqual(
        TitlePageInfo expected,
        TitlePageInfo actual)
    {
        Assert.Equal(expected.University, actual.University);
        Assert.Equal(expected.Department, actual.Department);
        Assert.Equal(expected.Discipline, actual.Discipline);
        Assert.Equal(expected.WorkType, actual.WorkType);
        Assert.Equal(expected.WorkTitle, actual.WorkTitle);
        Assert.Equal(expected.StudentName, actual.StudentName);
        Assert.Equal(expected.GroupNumber, actual.GroupNumber);
        Assert.Equal(expected.TeacherName, actual.TeacherName);
        Assert.Equal(expected.Year, actual.Year);
        Assert.Equal(expected.City, actual.City);
    }

    private static void AssertModulesEqual(
        DocumentModules expected,
        DocumentModules actual)
    {
        Assert.Equal(expected.HasTitlePage, actual.HasTitlePage);
        Assert.Equal(expected.HasTableOfContents, actual.HasTableOfContents);
        Assert.Equal(expected.HasBibliography, actual.HasBibliography);
        Assert.Equal(expected.HasAppendix, actual.HasAppendix);
        Assert.Equal(expected.ContentStartPage, actual.ContentStartPage);
        Assert.Equal(expected.AutoGenerateTOC, actual.AutoGenerateTOC);
        Assert.Equal(expected.TOCMaxLevel, actual.TOCMaxLevel);
    }

    private static void AssertCountersEqual(
        DocumentCounters expected,
        DocumentCounters actual)
    {
        Assert.Equal(expected.ImagesCount, actual.ImagesCount);
        Assert.Equal(expected.TablesCount, actual.TablesCount);
        Assert.Equal(expected.SourcesCount, actual.SourcesCount);
        Assert.Equal(expected.PagesCount, actual.PagesCount);
        Assert.Equal(expected.ApplicationsCount, actual.ApplicationsCount);
    }

    private static void AssertListingEqual(CodeListing expected, CodeListing actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.FileName, actual.FileName);
        Assert.Equal(expected.RelativePath, actual.RelativePath);
        Assert.Equal(expected.Language, actual.Language);
        Assert.Equal(expected.Content, actual.Content);
        Assert.Equal(expected.IsSelected, actual.IsSelected);
        Assert.Equal(expected.Order, actual.Order);
        Assert.Equal(expected.ListingNumber, actual.ListingNumber);
    }

    private static void AssertBibliographyEqual(
        BibliographySource expected,
        BibliographySource actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.IsSelected, actual.IsSelected);
        Assert.Equal(expected.Order, actual.Order);
    }
}
