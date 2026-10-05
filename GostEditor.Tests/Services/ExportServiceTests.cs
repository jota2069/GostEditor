using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using GostEditor.Core.Interfaces;
using GostEditor.Core.IO;
using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Tests.Infrastructure;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace GostEditor.Tests.Services;

public class ExportServiceTests
{
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    [Fact]
    public async Task ExportToDocx_CompleteDocument_IsOpenXmlValidAndHasResolvableRelationships()
    {
        string outputPath = CreateOutputPath();

        try
        {
            GostDocument document = CreateCompleteDocument();
            AddImage(document, new ImageSize(96, 48), "diagram.png", "Схема данных");

            await new ExportService().ExportToDocxAsync(document, outputPath);

            using WordprocessingDocument package =
                WordprocessingDocument.Open(outputPath, isEditable: false);
            List<ValidationErrorInfo> validationErrors =
                new OpenXmlValidator(FileFormatVersions.Office2019)
                    .Validate(package)
                    .ToList();
            Assert.True(
                validationErrors.Count == 0,
                string.Join(
                    Environment.NewLine,
                    validationErrors.Select(error =>
                        $"{error.Path?.XPath}: {error.Description}")));

            MainDocumentPart mainPart = Assert.IsType<MainDocumentPart>(
                package.MainDocumentPart);
            Assert.NotNull(mainPart.StyleDefinitionsPart);
            Assert.NotNull(mainPart.DocumentSettingsPart);
            Assert.Single(mainPart.FooterParts);
            Assert.Single(mainPart.ImageParts);

            W.Document wordDocument = Assert.IsType<W.Document>(mainPart.Document);
            HashSet<string> embeddedRelationshipIds = wordDocument
                .Descendants<A.Blip>()
                .Select(blip => blip.Embed?.Value)
                .Where(id => id is not null)
                .Select(id => id!)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Single(embeddedRelationshipIds);
            Assert.All(
                embeddedRelationshipIds,
                id => Assert.IsType<ImagePart>(mainPart.GetPartById(id)));
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public async Task ExportToDocx_WritesPageSettingsPageNumberAndTocFields()
    {
        string outputPath = CreateOutputPath();

        try
        {
            GostDocument document = CreateCompleteDocument();
            document.PageWidth = 800;
            document.PageHeight = 1100;
            document.MarginLeft = 120;
            document.MarginRight = 60;
            document.MarginTop = 80;
            document.MarginBottom = 70;
            document.Modules.TOCMaxLevel = 3;

            await new ExportService().ExportToDocxAsync(document, outputPath);

            using WordprocessingDocument package =
                WordprocessingDocument.Open(outputPath, isEditable: false);
            MainDocumentPart mainPart = package.MainDocumentPart!;
            W.Document wordDocument = Assert.IsType<W.Document>(mainPart.Document);
            W.Body body = Assert.IsType<W.Body>(wordDocument.Body);
            W.SectionProperties section = Assert.Single(
                body.Elements<W.SectionProperties>());
            W.PageSize pageSize = Assert.IsType<W.PageSize>(section.GetFirstChild<W.PageSize>());
            Assert.Equal(12000U, pageSize.Width!.Value);
            Assert.Equal(16500U, pageSize.Height!.Value);
            W.PageMargin margins = Assert.IsType<W.PageMargin>(
                section.GetFirstChild<W.PageMargin>());
            Assert.Equal(1800U, margins.Left!.Value);
            Assert.Equal(900U, margins.Right!.Value);
            Assert.Equal(1200, margins.Top!.Value);
            Assert.Equal(1050, margins.Bottom!.Value);

            Assert.Contains(
                wordDocument.Descendants<W.FieldCode>(),
                field => field.Text.Contains(
                    "TOC \\o \"1-3\" \\h \\z \\u",
                    StringComparison.Ordinal));
            FooterPart footer = Assert.Single(mainPart.FooterParts);
            Assert.Contains(
                footer.Footer!.Descendants<W.FieldCode>(),
                field => field.Text.Trim() == "PAGE");
            Assert.True(
                mainPart.DocumentSettingsPart!.Settings!
                    .GetFirstChild<W.UpdateFieldsOnOpen>()!
                    .Val!.Value);
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public async Task ExportToDocx_PreservesStylesFormattingBibliographyAndListingFidelity()
    {
        string outputPath = CreateOutputPath();

        try
        {
            GostDocument document = CreateCompleteDocument();

            await new ExportService().ExportToDocxAsync(document, outputPath);

            using WordprocessingDocument package =
                WordprocessingDocument.Open(outputPath, isEditable: false);
            MainDocumentPart mainPart = package.MainDocumentPart!;
            W.Document wordDocument = Assert.IsType<W.Document>(mainPart.Document);
            W.Body body = Assert.IsType<W.Body>(wordDocument.Body);

            W.Paragraph heading = FindParagraph(body, "ГЛАВА 1");
            Assert.Equal(
                "Heading1",
                heading.ParagraphProperties!.ParagraphStyleId!.Val!.Value);
            Assert.NotNull(heading.ParagraphProperties.PageBreakBefore);
            Assert.Equal(
                "Heading2",
                FindParagraph(body, "Подраздел 1.1")
                    .ParagraphProperties!.ParagraphStyleId!.Val!.Value);
            Assert.Equal(
                "Heading3",
                FindParagraph(body, "Подраздел 1.1.1")
                    .ParagraphProperties!.ParagraphStyleId!.Val!.Value);

            W.Paragraph formatted = FindParagraph(body, "Обычный жирный курсив");
            Assert.Equal(
                W.JustificationValues.Both,
                formatted.ParagraphProperties!.Justification!.Val!.Value);
            Assert.Equal("480", formatted.ParagraphProperties.SpacingBetweenLines!.Line!.Value);
            Assert.Equal("709", formatted.ParagraphProperties.Indentation!.FirstLine!.Value);
            List<W.Run> runs = formatted.Elements<W.Run>().ToList();
            Assert.Equal(3, runs.Count);
            W.RunProperties boldFormatting = Assert.IsType<W.RunProperties>(
                runs[1].RunProperties);
            Assert.NotNull(boldFormatting.Bold);
            Assert.Equal("112233", boldFormatting.Color!.Val!.Value);
            W.RunProperties italicFormatting = Assert.IsType<W.RunProperties>(
                runs[2].RunProperties);
            Assert.NotNull(italicFormatting.Italic);
            Assert.Equal("32", italicFormatting.FontSize!.Val!.Value);

            List<string> texts = body.Elements<W.Paragraph>()
                .Select(GetParagraphText)
                .ToList();
            int firstSource = texts.IndexOf("1. Первый источник");
            int secondSource = texts.IndexOf("2. Второй источник");
            Assert.True(firstSource >= 0 && secondSource > firstSource);
            Assert.DoesNotContain("Скрытый источник", texts);

            W.Paragraph listingTitle = FindParagraph(
                body,
                "Листинг 1 — файл src/Program.cs");
            Assert.Equal(
                W.JustificationValues.Left,
                listingTitle.ParagraphProperties!.Justification!.Val!.Value);
            W.RunProperties titleFormatting =
                Assert.Single(listingTitle.Elements<W.Run>()).RunProperties!;
            Assert.NotNull(titleFormatting.Italic);
            Assert.Equal("24", titleFormatting.FontSize!.Val!.Value);
            Assert.Equal(
                "Times New Roman",
                titleFormatting.RunFonts!.Ascii!.Value);

            W.Paragraph code = FindParagraph(body, "    Console.WriteLine(\"GostEditor\");");
            Assert.Equal(
                "Code",
                code.ParagraphProperties!.ParagraphStyleId!.Val!.Value);
            W.RunProperties codeFormatting = Assert.Single(code.Elements<W.Run>()).RunProperties!;
            Assert.Equal("Consolas", codeFormatting.RunFonts!.Ascii!.Value);
            Assert.Equal("20", codeFormatting.FontSize!.Val!.Value);

            W.Styles styles = mainPart.StyleDefinitionsPart!.Styles!;
            Assert.All(
                new[] { "Normal", "Heading1", "Heading2", "Heading3", "Code" },
                styleId => Assert.Contains(
                    styles.Elements<W.Style>(),
                    style => style.StyleId?.Value == styleId));
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

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
    public async Task ExportToDocx_TranscodesSupportedNonWordRasterFormatToPngPart()
    {
        string outputPath = CreateOutputPath();

        try
        {
            GostDocument document = CreateDocumentWithNonImageContent();
            byte[] webp = CreateOnePixelImage(SKEncodedImageFormat.Webp);
            ImageResult<ImagePlacementInfo> inserted = new ImageService().InsertPlacement(
                document,
                document.Paragraphs.Count,
                new CreateImageRequest(
                    webp,
                    new ImageSize(32, 32),
                    "pixel.webp",
                    "WebP"));
            AssertSuccess(inserted);

            await new ExportService().ExportToDocxAsync(document, outputPath);

            using WordprocessingDocument package =
                WordprocessingDocument.Open(outputPath, isEditable: false);
            ImagePart imagePart = Assert.Single(package.MainDocumentPart!.ImageParts);
            Assert.Equal("image/png", imagePart.ContentType);
            using Stream imageStream = imagePart.GetStream();
            using SKData imageData = SKData.Create(imageStream);
            using SKCodec? codec = SKCodec.Create(imageData);
            Assert.NotNull(codec);
            Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);
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
    public async Task ExportToDocx_WhenExportFails_PreservesExistingDestination()
    {
        string outputPath = CreateOutputPath();
        byte[] original = "existing docx"u8.ToArray();
        await File.WriteAllBytesAsync(outputPath, original);

        try
        {
            GostDocument document = CreateImageOnlyDocument(
                out ImagePlacementInfo placement);
            StubImageService imageService = new(
                (_, paragraphIndex) =>
                    ImageResult<ResolvedImagePlacement>.Failure(
                        ImageErrorCode.ImageNotFound,
                        "Injected export failure.",
                        placement.ImageId,
                        paragraphIndex));

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new ExportService(imageService)
                    .ExportToDocxAsync(document, outputPath));

            Assert.Equal(original, await File.ReadAllBytesAsync(outputPath));
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(outputPath)!,
                Path.GetFileName(outputPath) + ".*.tmp"));
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public async Task ExportToDocx_WhenExportSucceeds_AtomicallyReplacesDestination()
    {
        string outputPath = CreateOutputPath();
        byte[] original = "existing docx"u8.ToArray();
        await File.WriteAllBytesAsync(outputPath, original);

        try
        {
            await new ExportService().ExportToDocxAsync(
                CreateDocumentWithNonImageContent(),
                outputPath);

            Assert.NotEqual(original, await File.ReadAllBytesAsync(outputPath));
            using ZipArchive package = ZipFile.OpenRead(outputPath);
            Assert.NotNull(package.GetEntry("word/document.xml"));
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(outputPath)!,
                Path.GetFileName(outputPath) + ".*.tmp"));
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public async Task ExportToDocx_WhenCancelledAfterDurableWrite_PreservesExistingDestination()
    {
        string outputPath = CreateOutputPath();
        byte[] original = "existing docx"u8.ToArray();
        await File.WriteAllBytesAsync(outputPath, original);
        using CancellationTokenSource cancellation = new();
        FaultInjectingAtomicFileSystem fileSystem = new(
            AtomicFileFailurePoint.CancelAfterFlush,
            cancellation.Cancel);
        ExportService service = new(
            new ImageService(),
            new AtomicFileCommitter(fileSystem));

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.ExportToDocxAsync(
                    CreateDocumentWithNonImageContent(),
                    outputPath,
                    cancellation.Token));

            Assert.Equal(original, await File.ReadAllBytesAsync(outputPath));
            Assert.Equal(0, fileSystem.CommitAttempts);
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(outputPath)!,
                Path.GetFileName(outputPath) + ".*.tmp"));
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

    private static GostDocument CreateCompleteDocument()
    {
        GostDocument document = new()
        {
            TitlePage = new TitlePageInfo
            {
                University = "Тестовый университет",
                Department = "Кафедра тестирования",
                Discipline = "Качество ПО",
                WorkType = "Курсовая работа",
                WorkTitle = "Проверка Open XML экспорта",
                StudentName = "Студент И.И.",
                GroupNumber = "ИВТ-01",
                TeacherName = "Преподаватель П.П.",
                City = "Москва",
                Year = 2026
            },
            Modules = new DocumentModules
            {
                HasTitlePage = true,
                HasTableOfContents = true,
                HasBibliography = true,
                HasAppendix = true,
                TOCMaxLevel = 3
            }
        };

        Paragraph heading = new()
        {
            Style = ParagraphStyle.Heading1,
            Alignment = GostAlignment.Center,
            FirstLineIndent = 0,
            PageBreakBefore = true
        };
        heading.Runs.Add(new TextRun("ГЛАВА 1", isBold: true));
        document.Paragraphs.Add(heading);

        Paragraph formatted = new()
        {
            Alignment = GostAlignment.Justify,
            FirstLineIndent = 47,
            LineSpacing = 2
        };
        formatted.Runs.Add(new TextRun("Обычный "));
        formatted.Runs.Add(new TextRun("жирный ", isBold: true)
        {
            Color = 0xFF112233
        });
        formatted.Runs.Add(new TextRun("курсив", isItalic: true)
        {
            FontSize = 16
        });
        document.Paragraphs.Add(formatted);

        Paragraph secondHeading = new()
        {
            Style = ParagraphStyle.Heading2,
            FirstLineIndent = 0
        };
        secondHeading.Runs.Add(new TextRun("Подраздел 1.1"));
        document.Paragraphs.Add(secondHeading);

        Paragraph thirdHeading = new()
        {
            Style = ParagraphStyle.Heading3,
            FirstLineIndent = 0
        };
        thirdHeading.Runs.Add(new TextRun("Подраздел 1.1.1"));
        document.Paragraphs.Add(thirdHeading);

        document.BibliographySources.AddRange(
        [
            new BibliographySource
            {
                Description = "Второй источник",
                IsSelected = true,
                Order = 2
            },
            new BibliographySource
            {
                Description = "Первый источник",
                IsSelected = true,
                Order = 1
            },
            new BibliographySource
            {
                Description = "Скрытый источник",
                IsSelected = false,
                Order = 0
            }
        ]);
        document.CodeListings.Add(new CodeListing
        {
            FileName = "Program.cs",
            RelativePath = "src/Program.cs",
            Content = "\tConsole.WriteLine(\"GostEditor\");\n",
            IsSelected = true
        });
        return document;
    }

    private static ImagePlacementInfo AddImage(
        GostDocument document,
        ImageSize size,
        string fileName,
        string caption)
    {
        ImageResult<ImagePlacementInfo> result = new ImageService().InsertPlacement(
            document,
            document.Paragraphs.Count,
            new CreateImageRequest(GetPngBytes(), size, fileName, caption));
        AssertSuccess(result);
        return result.Value!;
    }

    private static W.Paragraph FindParagraph(W.Body body, string expectedText)
    {
        return Assert.Single(
            body.Elements<W.Paragraph>(),
            paragraph => GetParagraphText(paragraph) == expectedText);
    }

    private static string GetParagraphText(W.Paragraph paragraph)
    {
        return string.Concat(
            paragraph.Descendants<W.Text>().Select(text => text.Text));
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

    private static byte[] CreateOnePixelImage(SKEncodedImageFormat format)
    {
        using SKBitmap bitmap = new(1, 1);
        bitmap.SetPixel(0, 0, SKColors.Red);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(format, 100);
        return encoded.ToArray();
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
