using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.Serialization;

public class ArchiveServiceTests
{
    [Fact]
    public async Task Save_WritesCurrentFormatVersionToDocumentJson()
    {
        ArchiveService service = new ArchiveService();
        await using MemoryStream archiveStream = new MemoryStream();

        await service.SaveAsync(CreateDocument(), archiveStream);
        archiveStream.Position = 0;
        using ZipArchive archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        ZipArchiveEntry jsonEntry =
            Assert.IsType<ZipArchiveEntry>(archive.GetEntry("document.json"));
        await using Stream jsonStream = jsonEntry.Open();
        using JsonDocument jsonDocument = await JsonDocument.ParseAsync(jsonStream);

        Assert.Equal(
            1,
            jsonDocument.RootElement.GetProperty("FormatVersion").GetInt32());
    }

    [Fact]
    public async Task Load_DocumentWithoutFormatVersion_TreatsItAsLegacyDocument()
    {
        ArchiveService service = new ArchiveService();
        await using MemoryStream archiveStream = await CreateArchiveAsync(
            """
            {
              "Paragraphs": [
                {
                  "Runs": [
                    { "Text": "Legacy document", "FontSize": 14 }
                  ]
                }
              ]
            }
            """);

        GostDocument document = await service.LoadAsync(archiveStream);

        Paragraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal("Legacy document", paragraph.GetPlainText());
    }

    [Fact]
    public async Task Load_DocumentWithFormatVersionOne_OpensDocument()
    {
        ArchiveService service = new ArchiveService();
        await using MemoryStream archiveStream = await CreateArchiveAsync(
            """
            {
              "FormatVersion": 1,
              "Paragraphs": [
                {
                  "Runs": [
                    { "Text": "Version one", "FontSize": 14 }
                  ]
                }
              ]
            }
            """);

        GostDocument document = await service.LoadAsync(archiveStream);

        Paragraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal("Version one", paragraph.GetPlainText());
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesCompleteDocumentModel()
    {
        ArchiveService service = new ArchiveService();
        GostDocument expected = CreateDocument();
        await using MemoryStream archiveStream = new MemoryStream();

        await service.SaveAsync(expected, archiveStream);
        archiveStream.Position = 0;
        GostDocument actual = await service.LoadAsync(archiveStream);

        AssertDocumentEqual(expected, actual);
    }

    [Fact]
    public async Task Save_WritesJsonAndParagraphImagesToArchive()
    {
        ArchiveService service = new ArchiveService();
        GostDocument document = CreateDocument();
        await using MemoryStream archiveStream = new MemoryStream();

        await service.SaveAsync(document, archiveStream);
        archiveStream.Position = 0;
        using ZipArchive archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("document.json"));
        ZipArchiveEntry imageEntry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry("media/img_0.dat"));
        await using Stream imageStream = imageEntry.Open();
        using MemoryStream imageBytes = new MemoryStream();
        await imageStream.CopyToAsync(imageBytes);
        Assert.Equal(document.Paragraphs[1].ImageData, imageBytes.ToArray());
    }

    private static GostDocument CreateDocument()
    {
        Guid listingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid imageId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid sourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        GostDocument document = new GostDocument
        {
            TitlePage = new TitlePageInfo
            {
                University = "Тестовый университет",
                Department = "Кафедра разработки",
                Discipline = "Программная инженерия",
                WorkType = "Курсовая работа",
                WorkTitle = "Редактор документов",
                StudentName = "Иванов И. И.",
                GroupNumber = "ПИ-01",
                TeacherName = "Петров П. П.",
                City = "Москва",
                Year = 2026
            },
            Modules = new DocumentModules
            {
                HasTitlePage = true,
                HasTableOfContents = true,
                HasBibliography = true,
                HasAppendix = true,
                ContentStartPage = 4,
                AutoGenerateTOC = true,
                TOCMaxLevel = 3
            },
            Counters = new DocumentCounters
            {
                ImagesCount = 2,
                TablesCount = 1,
                SourcesCount = 1,
                PagesCount = 12,
                ApplicationsCount = 1
            },
            CreatedAt = new DateTime(2026, 7, 1, 10, 20, 30, DateTimeKind.Utc),
            PageWidth = 800,
            PageHeight = 1100,
            MarginLeft = 120,
            MarginRight = 60,
            MarginTop = 70,
            MarginBottom = 80
        };

        Paragraph textParagraph = new Paragraph
        {
            Alignment = GostAlignment.Justify,
            FirstLineIndent = 42,
            LineSpacing = 1.25,
            Style = ParagraphStyle.Heading2,
            PageBreakBefore = true
        };
        textParagraph.Runs.Add(new TextRun("Цветной ", isBold: true)
        {
            FontSize = 16,
            Color = 0xFF123456
        });
        textParagraph.Runs.Add(new TextRun("текст", isItalic: true)
        {
            FontSize = 12,
            Color = 0xFF654321
        });

        Paragraph imageParagraph = new Paragraph
        {
            Alignment = GostAlignment.Center,
            FirstLineIndent = 0,
            LineSpacing = 1,
            ImageData = [137, 80, 78, 71, 1, 2, 3],
            ImageWidth = 320,
            ImageHeight = 180
        };
        imageParagraph.Runs.Add(new TextRun(string.Empty));

        document.Paragraphs.Add(textParagraph);
        document.Paragraphs.Add(imageParagraph);
        document.CodeListings.Add(new CodeListing
        {
            Id = listingId,
            FileName = "Program.cs",
            RelativePath = "src/Program.cs",
            Language = "csharp",
            Content = "Console.WriteLine(\"test\");",
            IsSelected = true,
            Order = 2,
            ListingNumber = 3
        });
        document.Images.Add(new ImageAttachment
        {
            Id = imageId,
            FileName = "diagram.png",
            Data = [10, 20, 30],
            Caption = "Диаграмма",
            Order = 4
        });
        document.BibliographySources.Add(new BibliographySource
        {
            Id = sourceId,
            Description = "Тестовый источник",
            IsSelected = true,
            Order = 5
        });

        return document;
    }

    private static async Task<MemoryStream> CreateArchiveAsync(string documentJson)
    {
        MemoryStream stream = new MemoryStream();

        using (ZipArchive archive = new ZipArchive(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            ZipArchiveEntry jsonEntry = archive.CreateEntry("document.json");
            await using Stream jsonStream = jsonEntry.Open();
            await using StreamWriter writer = new StreamWriter(
                jsonStream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteAsync(documentJson);
        }

        stream.Position = 0;
        return stream;
    }

    private static void AssertDocumentEqual(GostDocument expected, GostDocument actual)
    {
        Assert.Equal(expected.TitlePage.University, actual.TitlePage.University);
        Assert.Equal(expected.TitlePage.Department, actual.TitlePage.Department);
        Assert.Equal(expected.TitlePage.Discipline, actual.TitlePage.Discipline);
        Assert.Equal(expected.TitlePage.WorkType, actual.TitlePage.WorkType);
        Assert.Equal(expected.TitlePage.WorkTitle, actual.TitlePage.WorkTitle);
        Assert.Equal(expected.TitlePage.StudentName, actual.TitlePage.StudentName);
        Assert.Equal(expected.TitlePage.GroupNumber, actual.TitlePage.GroupNumber);
        Assert.Equal(expected.TitlePage.TeacherName, actual.TitlePage.TeacherName);
        Assert.Equal(expected.TitlePage.City, actual.TitlePage.City);
        Assert.Equal(expected.TitlePage.Year, actual.TitlePage.Year);

        Assert.Equal(expected.Modules.HasTitlePage, actual.Modules.HasTitlePage);
        Assert.Equal(expected.Modules.HasTableOfContents, actual.Modules.HasTableOfContents);
        Assert.Equal(expected.Modules.HasBibliography, actual.Modules.HasBibliography);
        Assert.Equal(expected.Modules.HasAppendix, actual.Modules.HasAppendix);
        Assert.Equal(expected.Modules.ContentStartPage, actual.Modules.ContentStartPage);
        Assert.Equal(expected.Modules.AutoGenerateTOC, actual.Modules.AutoGenerateTOC);
        Assert.Equal(expected.Modules.TOCMaxLevel, actual.Modules.TOCMaxLevel);

        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.ModifiedAt, actual.ModifiedAt);
        Assert.Equal(expected.PageWidth, actual.PageWidth);
        Assert.Equal(expected.PageHeight, actual.PageHeight);
        Assert.Equal(expected.MarginLeft, actual.MarginLeft);
        Assert.Equal(expected.MarginRight, actual.MarginRight);
        Assert.Equal(expected.MarginTop, actual.MarginTop);
        Assert.Equal(expected.MarginBottom, actual.MarginBottom);

        Assert.Equal(expected.Counters.ImagesCount, actual.Counters.ImagesCount);
        Assert.Equal(expected.Counters.TablesCount, actual.Counters.TablesCount);
        Assert.Equal(expected.Counters.SourcesCount, actual.Counters.SourcesCount);
        Assert.Equal(expected.Counters.PagesCount, actual.Counters.PagesCount);
        Assert.Equal(expected.Counters.ApplicationsCount, actual.Counters.ApplicationsCount);

        Assert.Equal(expected.Paragraphs.Count, actual.Paragraphs.Count);
        for (int paragraphIndex = 0; paragraphIndex < expected.Paragraphs.Count; paragraphIndex++)
        {
            Paragraph expectedParagraph = expected.Paragraphs[paragraphIndex];
            Paragraph actualParagraph = actual.Paragraphs[paragraphIndex];
            Assert.Equal(expectedParagraph.Alignment, actualParagraph.Alignment);
            Assert.Equal(expectedParagraph.FirstLineIndent, actualParagraph.FirstLineIndent);
            Assert.Equal(expectedParagraph.LineSpacing, actualParagraph.LineSpacing);
            Assert.Equal(expectedParagraph.Style, actualParagraph.Style);
            Assert.Equal(expectedParagraph.PageBreakBefore, actualParagraph.PageBreakBefore);
            Assert.Equal(expectedParagraph.ImageData, actualParagraph.ImageData);
            Assert.Equal(expectedParagraph.ImageWidth, actualParagraph.ImageWidth);
            Assert.Equal(expectedParagraph.ImageHeight, actualParagraph.ImageHeight);
            Assert.Equal(expectedParagraph.Runs.Count, actualParagraph.Runs.Count);

            for (int runIndex = 0; runIndex < expectedParagraph.Runs.Count; runIndex++)
            {
                TextRun expectedRun = expectedParagraph.Runs[runIndex];
                TextRun actualRun = actualParagraph.Runs[runIndex];
                Assert.Equal(expectedRun.Text, actualRun.Text);
                Assert.Equal(expectedRun.IsBold, actualRun.IsBold);
                Assert.Equal(expectedRun.IsItalic, actualRun.IsItalic);
                Assert.Equal(expectedRun.FontSize, actualRun.FontSize);
                Assert.Equal(expectedRun.Color, actualRun.Color);
            }
        }

        CodeListing expectedListing = Assert.Single(expected.CodeListings);
        CodeListing actualListing = Assert.Single(actual.CodeListings);
        Assert.Equal(expectedListing.Id, actualListing.Id);
        Assert.Equal(expectedListing.FileName, actualListing.FileName);
        Assert.Equal(expectedListing.RelativePath, actualListing.RelativePath);
        Assert.Equal(expectedListing.Language, actualListing.Language);
        Assert.Equal(expectedListing.Content, actualListing.Content);
        Assert.Equal(expectedListing.IsSelected, actualListing.IsSelected);
        Assert.Equal(expectedListing.Order, actualListing.Order);
        Assert.Equal(expectedListing.ListingNumber, actualListing.ListingNumber);

        ImageAttachment expectedImage = Assert.Single(expected.Images);
        ImageAttachment actualImage = Assert.Single(actual.Images);
        Assert.Equal(expectedImage.Id, actualImage.Id);
        Assert.Equal(expectedImage.FileName, actualImage.FileName);
        Assert.Equal(expectedImage.Data, actualImage.Data);
        Assert.Equal(expectedImage.Caption, actualImage.Caption);
        Assert.Equal(expectedImage.Order, actualImage.Order);

        BibliographySource expectedSource = Assert.Single(expected.BibliographySources);
        BibliographySource actualSource = Assert.Single(actual.BibliographySources);
        Assert.Equal(expectedSource.Id, actualSource.Id);
        Assert.Equal(expectedSource.Description, actualSource.Description);
        Assert.Equal(expectedSource.IsSelected, actualSource.IsSelected);
        Assert.Equal(expectedSource.Order, actualSource.Order);
    }
}
