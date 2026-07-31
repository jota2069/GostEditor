using System.IO.Compression;
using System.Xml.Linq;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.Services;

public class ExportServiceTests
{
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    [Fact]
    public async Task ExportToDocx_ResolvesImagesAndWritesCaptionsNumberingAndDipSizes()
    {
        string outputPath = CreateOutputPath();

        try
        {
            GostDocument document = CreateDocumentWithNonImageContent();
            ImageService imageService = new ImageService();

            ImageResult<ImagePlacementInfo> firstImage = imageService.InsertPlacement(
                document,
                document.Paragraphs.Count,
                new CreateImageRequest(
                    GetPngBytes(),
                    new ImageSize(96, 48),
                    "diagram.png",
                    "Схема "));
            AssertSuccess(firstImage);
            Paragraph firstImageParagraph =
                document.Paragraphs[firstImage.Value!.ParagraphIndex];
            firstImageParagraph.Runs[0].IsBold = true;
            firstImageParagraph.Runs[0].Color = 0xFF123456;
            firstImageParagraph.Runs.Add(new TextRun("данных", isItalic: true));

            ImageResult<ImagePlacementInfo> secondImage = imageService.InsertPlacement(
                document,
                document.Paragraphs.Count,
                new CreateImageRequest(
                    GetPngBytes(),
                    new ImageSize(98, 50),
                    "empty-caption.png",
                    "   "));
            AssertSuccess(secondImage);

            ExportService service = new ExportService(imageService);
            await service.ExportToDocxAsync(document, outputPath);

            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);

            using ZipArchive package = ZipFile.OpenRead(outputPath);
            Assert.NotNull(package.GetEntry("[Content_Types].xml"));
            Assert.NotNull(package.GetEntry("_rels/.rels"));
            ZipArchiveEntry documentEntry =
                Assert.IsType<ZipArchiveEntry>(package.GetEntry("word/document.xml"));
            ZipArchiveEntry relationshipsEntry =
                Assert.IsType<ZipArchiveEntry>(package.GetEntry("word/_rels/document.xml.rels"));

            XDocument documentXml = await LoadXmlAsync(documentEntry);
            XDocument relationshipsXml = await LoadXmlAsync(relationshipsEntry);

            List<XElement> imageRelationships = relationshipsXml.Descendants()
                .Where(element =>
                    element.Name.LocalName == "Relationship" &&
                    element.Attribute("Type")?.Value.EndsWith("/image") == true)
                .ToList();
            Assert.NotEmpty(imageRelationships);
            foreach (XElement imageRelationship in imageRelationships)
            {
                AssertPackageContainsRelationshipTarget(
                    package,
                    imageRelationship,
                    "word/document.xml");
            }
            HashSet<string> imageRelationshipIds = imageRelationships
                .Select(relationship =>
                    Assert.IsType<XAttribute>(
                        relationship.Attribute("Id")).Value)
                .ToHashSet(StringComparer.Ordinal);
            List<string> embeddedImageIds = documentXml.Descendants()
                .Where(element => element.Name.LocalName == "blip")
                .Select(element => element.Attributes()
                    .Single(attribute => attribute.Name.LocalName == "embed")
                    .Value)
                .ToList();
            Assert.Equal(2, embeddedImageIds.Count);
            Assert.All(
                embeddedImageIds,
                id => Assert.Contains(id, imageRelationshipIds));

            List<string> paragraphTexts = documentXml.Descendants()
                .Where(element => element.Name.LocalName == "p")
                .Select(element => string.Concat(
                    element.Descendants()
                        .Where(descendant => descendant.Name.LocalName == "t")
                        .Select(descendant => descendant.Value)))
                .ToList();
            string exportedText = string.Concat(paragraphTexts);

            Assert.Contains("Тестовый университет", exportedText);
            Assert.Contains("ТЕСТОВАЯ ГЛАВА", exportedText);
            Assert.Contains("СПИСОК ИСПОЛЬЗОВАННЫХ ИСТОЧНИКОВ", exportedText);
            Assert.Contains("Источник для интеграционного теста", exportedText);
            Assert.Contains("ПРИЛОЖЕНИЕ А", exportedText);
            Assert.Contains("Console.WriteLine", exportedText);
            Assert.Contains("Рисунок 1 — Схема данных", paragraphTexts);
            Assert.Contains("Рисунок 2", paragraphTexts);
            Assert.DoesNotContain(
                paragraphTexts,
                text => text.StartsWith(
                    "Рисунок 2 —",
                    StringComparison.Ordinal));

            XElement firstCaptionParagraph = Assert.Single(
                documentXml.Descendants()
                    .Where(element =>
                        element.Name.LocalName == "p" &&
                        string.Concat(
                            element.Descendants()
                                .Where(descendant =>
                                    descendant.Name.LocalName == "t")
                                .Select(descendant => descendant.Value))
                            == "Рисунок 1 — Схема данных"));
            XElement boldCaptionRun = Assert.Single(
                firstCaptionParagraph.Descendants()
                    .Where(element =>
                        element.Name.LocalName == "r" &&
                        string.Concat(
                            element.Descendants()
                                .Where(descendant =>
                                    descendant.Name.LocalName == "t")
                                .Select(descendant => descendant.Value))
                            == "Схема "));
            Assert.Contains(
                boldCaptionRun.Descendants(),
                element => element.Name.LocalName == "b");
            Assert.Contains(
                boldCaptionRun.Descendants(),
                element =>
                    element.Name.LocalName == "color" &&
                    element.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "val" &&
                        attribute.Value.Equals(
                            "123456",
                            StringComparison.OrdinalIgnoreCase)));
            XElement italicCaptionRun = Assert.Single(
                firstCaptionParagraph.Descendants()
                    .Where(element =>
                        element.Name.LocalName == "r" &&
                        string.Concat(
                            element.Descendants()
                                .Where(descendant =>
                                    descendant.Name.LocalName == "t")
                                .Select(descendant => descendant.Value))
                            == "данных"));
            Assert.Contains(
                italicCaptionRun.Descendants(),
                element => element.Name.LocalName == "i");

            Assert.Equal(
                2,
                documentXml.Descendants()
                    .Count(element => element.Name.LocalName == "drawing"));
            Assert.Contains(
                documentXml.Descendants(),
                element =>
                    element.Name.LocalName == "extent" &&
                    element.Attribute("cx")?.Value == "914400" &&
                    element.Attribute("cy")?.Value == "457200");
            Assert.Contains(
                documentXml.Descendants(),
                element =>
                    element.Name.LocalName == "extent" &&
                    element.Attribute("cx")?.Value == "939800" &&
                    element.Attribute("cy")?.Value == "482600");
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public async Task ExportToDocx_WhenPlacementCannotBeResolved_ThrowsDetailedError()
    {
        string outputPath = CreateOutputPath();

        try
        {
            GostDocument document = CreateImageOnlyDocument(out ImagePlacementInfo placement);
            StubImageService imageService = new StubImageService(
                (_, paragraphIndex) =>
                    ImageResult<ResolvedImagePlacement>.Failure(
                        ImageErrorCode.ImageNotFound,
                        "Attachment отсутствует.",
                        placement.ImageId,
                        paragraphIndex));
            ExportService service = new ExportService(imageService);

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => service.ExportToDocxAsync(document, outputPath));

            Assert.Contains("абзаца 0", exception.Message);
            Assert.Contains(placement.ImageId.ToString(), exception.Message);
            Assert.Contains(nameof(ImageErrorCode.ImageNotFound), exception.Message);
            Assert.Contains("Attachment отсутствует.", exception.Message);
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public async Task ExportToDocx_WhenResolvedDataIsInvalid_ThrowsDetailedError()
    {
        string outputPath = CreateOutputPath();

        try
        {
            GostDocument document = CreateImageOnlyDocument(out ImagePlacementInfo placement);
            ResolvedImagePlacement invalidImage = new ResolvedImagePlacement(
                placement.ParagraphIndex,
                placement.ImageId,
                placement.Size,
                new ImageContentView(
                    placement.ImageId,
                    new byte[] { 1, 2, 3, 4 },
                    "broken.png",
                    "image/png"));
            StubImageService imageService = new StubImageService(
                (_, _) => ImageResult<ResolvedImagePlacement>.Success(invalidImage));
            ExportService service = new ExportService(imageService);

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => service.ExportToDocxAsync(document, outputPath));

            Assert.Contains("рисунок 1", exception.Message);
            Assert.Contains("абзаца 0", exception.Message);
            Assert.Contains(placement.ImageId.ToString(), exception.Message);
            Assert.Contains("повреждены", exception.Message);
            Assert.NotNull(exception.InnerException);
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public void Constructor_WithNullImageService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ExportService(null!));
    }

    private static GostDocument CreateDocumentWithNonImageContent()
    {
        GostDocument document = new GostDocument
        {
            TitlePage = new TitlePageInfo
            {
                University = "Тестовый университет",
                WorkTitle = "Проверка экспорта",
                Year = 2026,
                City = "Москва"
            },
            Modules = new DocumentModules
            {
                HasTitlePage = true,
                HasTableOfContents = false,
                HasBibliography = true,
                HasAppendix = true
            }
        };

        Paragraph heading = new Paragraph
        {
            Style = ParagraphStyle.Heading1,
            Alignment = GostAlignment.Center,
            FirstLineIndent = 0
        };
        heading.Runs.Add(
            new TextRun("ТЕСТОВАЯ ГЛАВА", isBold: true)
            {
                FontSize = 16
            });
        document.Paragraphs.Add(heading);
        document.BibliographySources.Add(new BibliographySource
        {
            Description = "Источник для интеграционного теста",
            IsSelected = true
        });
        document.CodeListings.Add(new CodeListing
        {
            FileName = "Program.cs",
            RelativePath = "Program.cs",
            Content = "Console.WriteLine(\"GostEditor\");",
            IsSelected = true
        });

        return document;
    }

    private static GostDocument CreateImageOnlyDocument(
        out ImagePlacementInfo placement)
    {
        GostDocument document = new GostDocument
        {
            Modules = new DocumentModules
            {
                HasTitlePage = false,
                HasTableOfContents = false,
                HasBibliography = false,
                HasAppendix = false
            }
        };
        ImageService imageService = new ImageService();
        ImageResult<ImagePlacementInfo> inserted = imageService.InsertPlacement(
            document,
            0,
            new CreateImageRequest(
                GetPngBytes(),
                new ImageSize(96, 48),
                "diagram.png",
                "Схема"));
        AssertSuccess(inserted);
        placement = inserted.Value!;
        return document;
    }

    private static void AssertSuccess<T>(ImageResult<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    private static async Task<XDocument> LoadXmlAsync(ZipArchiveEntry entry)
    {
        await using Stream stream = entry.Open();
        return await XDocument.LoadAsync(
            stream,
            LoadOptions.None,
            CancellationToken.None);
    }

    private static void AssertPackageContainsRelationshipTarget(
        ZipArchive package,
        XElement relationship,
        string sourcePath)
    {
        string mediaTarget = Assert.IsType<XAttribute>(
            relationship.Attribute("Target")).Value;
        string mediaEntryPath = mediaTarget.StartsWith('/')
            ? mediaTarget.TrimStart('/')
            : new Uri(
                    new Uri($"https://package.local/{sourcePath}"),
                    mediaTarget)
                .AbsolutePath
                .TrimStart('/');
        Assert.NotNull(package.GetEntry(mediaEntryPath));
    }

    private static byte[] GetPngBytes()
    {
        return Convert.FromBase64String(OnePixelPngBase64);
    }

    private static string CreateOutputPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"gosteditor-{Guid.NewGuid():N}.docx");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class StubImageService : IImageService
    {
        private readonly Func<
            GostDocument,
            int,
            ImageResult<ResolvedImagePlacement>> _resolvePlacement;

        public StubImageService(
            Func<
                GostDocument,
                int,
                ImageResult<ResolvedImagePlacement>> resolvePlacement)
        {
            _resolvePlacement = resolvePlacement;
        }

        public ImageResult<ImagePlacementInfo> InsertPlacement(
            GostDocument document,
            int insertionIndex,
            CreateImageRequest request)
        {
            throw new NotSupportedException();
        }

        public ImageResult<ImagePlacementInfo> InsertExistingPlacement(
            GostDocument document,
            int insertionIndex,
            Guid imageId,
            ImagePlacementRequest request)
        {
            throw new NotSupportedException();
        }

        public ImageResult<ImagePlacementInfo> ReplacePlacementContent(
            GostDocument document,
            int paragraphIndex,
            ReplaceImageRequest request)
        {
            throw new NotSupportedException();
        }

        public ImageResult<ImagePlacementInfo> ResizePlacement(
            GostDocument document,
            int paragraphIndex,
            ImageSize size)
        {
            throw new NotSupportedException();
        }

        public ImageResult<ImageRemovalInfo> RemovePlacement(
            GostDocument document,
            int paragraphIndex)
        {
            throw new NotSupportedException();
        }

        public ImageResult<ResolvedImagePlacement> ResolvePlacement(
            GostDocument document,
            int paragraphIndex)
        {
            return _resolvePlacement(document, paragraphIndex);
        }

        public ImageResult<ImageContentView> ResolveContent(
            GostDocument document,
            Guid imageId)
        {
            throw new NotSupportedException();
        }

        public ImageIntegrityReport Inspect(GostDocument document)
        {
            throw new NotSupportedException();
        }

        public ImageResult<OrphanCleanupResult> RemoveOrphans(
            GostDocument document,
            IReadOnlyCollection<Guid> confirmedImageIds)
        {
            throw new NotSupportedException();
        }
    }
}
