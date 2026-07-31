using System.IO.Compression;
using System.Xml.Linq;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.Services;

public class ExportServiceTests
{
    [Fact]
    public async Task ExportToDocx_CreatesReadablePackageWithDocumentContent()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"gosteditor-{Guid.NewGuid():N}.docx");

        try
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
            heading.Runs.Add(new TextRun("ТЕСТОВАЯ ГЛАВА", isBold: true) { FontSize = 16 });
            document.Paragraphs.Add(heading);
            Paragraph imageParagraph = new Paragraph
            {
                Alignment = GostAlignment.Center,
                ImageData = Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
                ImageWidth = 100,
                ImageHeight = 100
            };
            imageParagraph.Runs.Add(new TextRun(string.Empty));
            document.Paragraphs.Add(imageParagraph);
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

            ArchiveService archiveService = new ArchiveService();
            await using MemoryStream gostArchive = new MemoryStream();
            await archiveService.SaveAsync(document, gostArchive);
            gostArchive.Position = 0;
            GostDocument loadedDocument = await archiveService.LoadAsync(gostArchive);

            ExportService service = new ExportService();
            await service.ExportToDocxAsync(loadedDocument, outputPath);

            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);

            using ZipArchive package = ZipFile.OpenRead(outputPath);
            Assert.NotNull(package.GetEntry("[Content_Types].xml"));
            Assert.NotNull(package.GetEntry("_rels/.rels"));
            ZipArchiveEntry documentEntry =
                Assert.IsType<ZipArchiveEntry>(package.GetEntry("word/document.xml"));
            ZipArchiveEntry relationshipsEntry =
                Assert.IsType<ZipArchiveEntry>(package.GetEntry("word/_rels/document.xml.rels"));

            await using Stream documentStream = documentEntry.Open();
            XDocument documentXml = await XDocument.LoadAsync(
                documentStream,
                LoadOptions.None,
                CancellationToken.None);
            await using Stream relationshipsStream = relationshipsEntry.Open();
            XDocument relationshipsXml = await XDocument.LoadAsync(
                relationshipsStream,
                LoadOptions.None,
                CancellationToken.None);

            XElement imageRelationship = Assert.Single(
                relationshipsXml.Descendants()
                    .Where(element =>
                        element.Name.LocalName == "Relationship" &&
                        element.Attribute("Type")?.Value.EndsWith("/image") == true));
            string relationshipId = Assert.IsType<XAttribute>(
                imageRelationship.Attribute("Id")).Value;
            string mediaTarget = Assert.IsType<XAttribute>(
                imageRelationship.Attribute("Target")).Value;
            string mediaEntryPath = mediaTarget.StartsWith('/')
                ? mediaTarget.TrimStart('/')
                : new Uri(
                        new Uri("https://package.local/word/document.xml"),
                        mediaTarget)
                    .AbsolutePath
                    .TrimStart('/');
            Assert.NotNull(package.GetEntry(mediaEntryPath));
            string exportedText = string.Concat(
                documentXml.Descendants()
                    .Where(element => element.Name.LocalName == "t")
                    .Select(element => element.Value));

            Assert.Contains("Тестовый университет", exportedText);
            Assert.Contains("ТЕСТОВАЯ ГЛАВА", exportedText);
            Assert.Contains("СПИСОК ИСПОЛЬЗОВАННЫХ ИСТОЧНИКОВ", exportedText);
            Assert.Contains("Источник для интеграционного теста", exportedText);
            Assert.Contains("ПРИЛОЖЕНИЕ А", exportedText);
            Assert.Contains("Console.WriteLine", exportedText);
            Assert.Contains(
                documentXml.Descendants(),
                element => element.Name.LocalName == "drawing");
            Assert.Contains(
                documentXml.Descendants(),
                element =>
                    element.Name.LocalName == "blip" &&
                    element.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "embed" &&
                        attribute.Value == relationshipId));
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}
