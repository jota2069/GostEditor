using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Tests.Infrastructure;
using SkiaSharp;

namespace GostEditor.Tests.Serialization;

public class ArchiveServiceTests
{
    private readonly ArchiveService _service = new ArchiveService();

    [Fact]
    public async Task Save_WritesFormatVersionTwoAndNoLegacyImagePayloadFields()
    {
        GostDocument document = CreateDocument();
        await using MemoryStream stream = new MemoryStream();

        await _service.SaveAsync(document, stream);

        using JsonDocument manifest = await ReadManifestAsync(stream);
        JsonElement root = manifest.RootElement;
        Assert.Equal(2, root.GetProperty("FormatVersion").GetInt32());

        JsonElement image = root.GetProperty("Images")[0];
        Assert.True(image.TryGetProperty("Id", out _));
        Assert.True(image.TryGetProperty("MediaType", out _));
        Assert.False(image.TryGetProperty("Data", out _));

        foreach (JsonElement paragraph in root.GetProperty("Paragraphs").EnumerateArray())
        {
            Assert.False(paragraph.TryGetProperty("ImageFileName", out _));
        }
    }

    [Fact]
    public async Task Load_DocumentWithoutFormatVersion_TreatsItAsVersionZero()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
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

        GostDocument document = await _service.LoadAsync(stream);

        Paragraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal("Legacy document", paragraph.GetPlainText());
        Assert.False(paragraph.IsImage);
    }

    [Fact]
    public async Task LoadWithDiagnostics_VersionZeroReportsFullMigrationChain()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """{"Paragraphs":[]}""");

        GostArchiveLoadResult result =
            await _service.LoadWithDiagnosticsAsync(stream);

        Assert.Equal(0, result.SourceVersion);
        Assert.Equal(2, result.CurrentVersion);
        Assert.True(result.WasMigrated);
        Assert.Equal(
            new[] { "FORMAT_V0_ASSUMED", "FORMAT_V1_MIGRATED" },
            result.Diagnostics.Select(item => item.Code));
        Assert.All(
            result.Diagnostics,
            item => Assert.Equal(
                GostArchiveDiagnosticSeverity.Information,
                item.Severity));
    }

    [Fact]
    public async Task Load_DocumentWithFormatVersionOne_OpensDocument()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
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

        GostDocument document = await _service.LoadAsync(stream);

        Assert.Equal(
            "Version one",
            Assert.Single(document.Paragraphs).GetPlainText());
    }

    [Fact]
    public async Task LoadWithDiagnostics_VersionOneReportsMigration()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """{"FormatVersion":1,"Paragraphs":[]}""");

        GostArchiveLoadResult result =
            await _service.LoadWithDiagnosticsAsync(stream);

        Assert.Equal(1, result.SourceVersion);
        Assert.True(result.WasMigrated);
        Assert.Contains(
            result.Diagnostics,
            item => item.Code == "FORMAT_V1_MIGRATED");
    }

    [Theory]
    [InlineData("formatVersion")]
    [InlineData("FORMATVERSION")]
    public async Task Load_MisCasedFormatVersion_IsNotSilentlyTreatedAsV0(
        string propertyName)
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            $$"""{"{{propertyName}}":2,"Paragraphs":[]}""");

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains("точный регистр", exception.Message);
    }

    [Fact]
    public async Task SaveAndLoad_VersionTwoRoundTripPreservesCompleteModel()
    {
        GostDocument expected = CreateDocument();
        await using MemoryStream stream = new MemoryStream();

        await _service.SaveAsync(expected, stream);
        stream.Position = 0;
        GostDocument actual = await _service.LoadAsync(stream);

        AssertDocumentEqual(expected, actual);
    }

    [Fact]
    public async Task LoadWithDiagnostics_CurrentWriterOutputNeedsNoMigration()
    {
        await using MemoryStream stream = new();
        await _service.SaveAsync(CreateDocument(), stream);
        stream.Position = 0;

        GostArchiveLoadResult result =
            await _service.LoadWithDiagnosticsAsync(stream);

        Assert.Equal(2, result.SourceVersion);
        Assert.Equal(2, result.CurrentVersion);
        Assert.False(result.WasMigrated);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task Save_WritesEveryAttachmentExactlyOnceUsingDeterministicPath()
    {
        GostDocument document = CreateDocument();
        Guid sharedId = document.Paragraphs[1].ImageId!.Value;
        ImageService imageService = new ImageService();
        AssertSuccess(imageService.InsertExistingPlacement(
            document,
            document.Paragraphs.Count,
            sharedId,
            new ImagePlacementRequest(
                new ImageSize(160, 90),
                "Повторное размещение")));
        await using MemoryStream stream = new MemoryStream();

        await _service.SaveAsync(document, stream);

        stream.Position = 0;
        using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read);
        List<ZipArchiveEntry> media = archive.Entries
            .Where(entry => entry.FullName.StartsWith(
                "media/images/",
                StringComparison.Ordinal))
            .ToList();
        Assert.Equal(document.Images.Count, media.Count);

        foreach (ImageAttachment attachment in document.Images)
        {
            ZipArchiveEntry entry = Assert.IsType<ZipArchiveEntry>(
                archive.GetEntry($"media/images/{attachment.Id:N}.dat"));
            await using Stream entryStream = entry.Open();
            using MemoryStream data = new MemoryStream();
            await entryStream.CopyToAsync(data);
            Assert.Equal(attachment.Data.ToArray(), data.ToArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Load_LegacyParagraphImage_CreatesCanonicalAttachment(
        bool includeVersion)
    {
        string version = includeVersion ? "\"FormatVersion\": 1," : string.Empty;
        await using MemoryStream stream = await CreateArchiveAsync(
            $$"""
            {
              {{version}}
              "Paragraphs": [
                {
                  "Alignment": 1,
                  "Runs": [
                    { "Text": "Подпись", "FontSize": 14 }
                  ],
                  "ImageFileName": "media/img_0.dat",
                  "ImageWidth": 320,
                  "ImageHeight": 180
                }
              ]
            }
            """,
            ("media/img_0.dat", TestImageData.CreatePng()));

        GostDocument document = await _service.LoadAsync(stream);

        Paragraph paragraph = Assert.Single(document.Paragraphs);
        ImageAttachment attachment = Assert.Single(document.Images);
        Assert.Equal(attachment.Id, paragraph.ImageId);
        Assert.Equal(TestImageData.CreatePng(), attachment.Data.ToArray());
        Assert.Equal("img_0.dat", attachment.FileName);
        Assert.Equal("image/png", attachment.MediaType);
        Assert.Equal("Подпись", paragraph.GetPlainText());
        Assert.Equal(320, paragraph.ImageWidth);
        Assert.Equal(180, paragraph.ImageHeight);
        Assert.Equal(1, document.Counters.ImagesCount);
    }

    [Fact]
    public async Task Load_LegacyRootAndParagraphImages_PreservesBothWithoutGuessingLink()
    {
        byte[] sameBytes = TestImageData.CreatePng();
        string inlineData = Convert.ToBase64String(sameBytes);
        Guid legacyRootId = Guid.Parse(
            "22222222-2222-2222-2222-222222222222");
        await using MemoryStream stream = await CreateArchiveAsync(
            $$"""
            {
              "FormatVersion": 1,
              "Images": [
                {
                  "Id": "{{legacyRootId}}",
                  "FileName": "same.png",
                  "Data": "{{inlineData}}",
                  "Caption": "Legacy root metadata",
                  "Order": 7
                }
              ],
              "Paragraphs": [
                {
                  "Runs": [{ "Text": "Placement caption", "FontSize": 14 }],
                  "ImageFileName": "media/same.png",
                  "ImageWidth": 200,
                  "ImageHeight": 100
                }
              ]
            }
            """,
            ("media/same.png", sameBytes));

        GostDocument document = await _service.LoadAsync(stream);

        Assert.Equal(2, document.Images.Count);
        ImageAttachment root = Assert.Single(
            document.Images.Where(image => image.Id == legacyRootId));
        Paragraph paragraph = Assert.Single(document.Paragraphs);
        Assert.NotEqual(root.Id, paragraph.ImageId);
        Assert.Equal("Legacy root metadata", root.Caption);
        Assert.Equal(7, root.Order);
        Assert.Equal(sameBytes, root.Data.ToArray());
        Assert.Equal(
            sameBytes,
            document.Images.Single(image => image.Id == paragraph.ImageId).Data.ToArray());
    }

    [Fact]
    public async Task Load_LegacySharedMediaPath_CreatesIndependentAttachments()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """
            {
              "FormatVersion": 1,
              "Paragraphs": [
                {
                  "Runs": [],
                  "ImageFileName": "media/shared.dat",
                  "ImageWidth": 100,
                  "ImageHeight": 50
                },
                {
                  "Runs": [],
                  "ImageFileName": "media/shared.dat",
                  "ImageWidth": 200,
                  "ImageHeight": 100
                }
              ]
            }
            """,
            ("media/shared.dat", TestImageData.CreatePng()));

        GostDocument document = await _service.LoadAsync(stream);

        Assert.Equal(2, document.Images.Count);
        Assert.Equal(2, document.Paragraphs.Count);
        Assert.NotEqual(
            document.Paragraphs[0].ImageId,
            document.Paragraphs[1].ImageId);
    }

    [Fact]
    public async Task Load_LegacyMissingMedia_PreservesCurrentTolerantBehavior()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """
            {
              "FormatVersion": 1,
              "Paragraphs": [
                {
                  "Runs": [{ "Text": "Caption survives", "FontSize": 14 }],
                  "ImageFileName": "media/missing.dat",
                  "ImageWidth": 100,
                  "ImageHeight": 50
                }
              ]
            }
            """);

        GostDocument document = await _service.LoadAsync(stream);

        Paragraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Null(paragraph.ImageId);
        Assert.Equal("Caption survives", paragraph.GetPlainText());
        Assert.Empty(document.Images);
    }

    [Fact]
    public async Task LoadWithDiagnostics_LegacyRepairIsObservableToCaller()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """
            {
              "FormatVersion": 1,
              "Paragraphs": [
                {
                  "Runs": [],
                  "ImageFileName": "media/missing.dat",
                  "ImageWidth": 100,
                  "ImageHeight": 50
                }
              ]
            }
            """);

        GostArchiveLoadResult result =
            await _service.LoadWithDiagnosticsAsync(stream);

        GostArchiveDiagnostic warning = Assert.Single(
            result.Diagnostics.Where(
                item => item.Code == "LEGACY_IMAGE_MISSING"));
        Assert.Equal(GostArchiveDiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public async Task Load_LegacyEmptyMedia_PreservesCurrentTolerantBehavior()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """
            {
              "FormatVersion": 1,
              "Paragraphs": [
                {
                  "Runs": [{ "Text": "Caption survives", "FontSize": 14 }],
                  "ImageFileName": "media/empty.dat",
                  "ImageWidth": 100,
                  "ImageHeight": 50
                }
              ]
            }
            """,
            ("media/empty.dat", Array.Empty<byte>()));

        GostDocument document = await _service.LoadAsync(stream);

        Paragraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Null(paragraph.ImageId);
        Assert.Equal("Caption survives", paragraph.GetPlainText());
        Assert.Empty(document.Images);
    }

    [Fact]
    public async Task Load_LegacyInvalidImageSize_NormalizesToFallback()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """
            {
              "FormatVersion": 1,
              "Paragraphs": [
                {
                  "Runs": [],
                  "ImageFileName": "media/image.dat",
                  "ImageWidth": 0,
                  "ImageHeight": -1
                }
              ]
            }
            """,
            ("media/image.dat", TestImageData.CreatePng()));

        GostDocument document = await _service.LoadAsync(stream);

        Paragraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(450, paragraph.ImageWidth);
        Assert.Equal(300, paragraph.ImageHeight);
    }

    [Fact]
    public async Task SaveAndLoad_LegacyOrphanAttachmentSurvivesVersionTwo()
    {
        Guid orphanId = Guid.Parse(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        string inlineData = Convert.ToBase64String(new byte[] { 9, 8, 7 });
        await using MemoryStream legacy = await CreateArchiveAsync(
            $$"""
            {
              "FormatVersion": 1,
              "Images": [
                {
                  "Id": "{{orphanId}}",
                  "FileName": "orphan.bin",
                  "Data": "{{inlineData}}",
                  "Caption": "orphan metadata",
                  "Order": 3
                }
              ],
              "Paragraphs": []
            }
            """);
        GostDocument migrated = await _service.LoadAsync(legacy);
        await using MemoryStream current = new MemoryStream();

        await _service.SaveAsync(migrated, current);
        current.Position = 0;
        GostDocument reloaded = await _service.LoadAsync(current);

        ImageAttachment orphan = Assert.Single(reloaded.Images);
        Assert.Equal(orphanId, orphan.Id);
        Assert.Equal(new byte[] { 9, 8, 7 }, orphan.Data.ToArray());
        Assert.Equal("orphan metadata", orphan.Caption);
        Assert.Empty(reloaded.Paragraphs);
    }

    [Fact]
    public async Task LegacyParagraph_MigratesSavesReloadsAndExportsEndToEnd()
    {
        await using MemoryStream legacy = await CreateArchiveAsync(
            """
            {
              "FormatVersion": 1,
              "Modules": {
                "HasTitlePage": false,
                "HasTableOfContents": false,
                "HasBibliography": false,
                "HasAppendix": false
              },
              "Paragraphs": [
                {
                  "Runs": [{ "Text": "Схема", "FontSize": 14 }],
                  "ImageFileName": "media/legacy.png",
                  "ImageWidth": 192,
                  "ImageHeight": 96
                }
              ]
            }
            """,
            ("media/legacy.png", TestImageData.CreatePng()));
        GostDocument migrated = await _service.LoadAsync(legacy);
        await using MemoryStream current = new MemoryStream();

        await _service.SaveAsync(migrated, current);
        using (JsonDocument manifest = await ReadManifestAsync(current))
        {
            Assert.Equal(
                2,
                manifest.RootElement
                    .GetProperty("FormatVersion")
                    .GetInt32());
        }

        current.Position = 0;
        GostDocument reloaded = await _service.LoadAsync(current);
        using TestTemporaryDirectory temporaryDirectory = new();
        string outputPath =
            temporaryDirectory.GetPath("migrated.docx");

        await new ExportService(new ImageService())
            .ExportToDocxAsync(reloaded, outputPath);

        Assert.True(File.Exists(outputPath));
        using WordprocessingDocument docx =
            WordprocessingDocument.Open(outputPath, isEditable: false);
        Assert.Single(docx.MainDocumentPart!.ImageParts);
    }

    [Fact]
    public async Task Load_LegacyEmptyAndDuplicateIds_AreRemappedWithoutDataLoss()
    {
        Guid duplicateId = Guid.Parse(
            "77777777-7777-7777-7777-777777777777");
        string payload = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        await using MemoryStream stream = await CreateArchiveAsync(
            $$"""
            {
              "FormatVersion": 1,
              "Images": [
                { "Id": "{{Guid.Empty}}", "FileName": "empty.bin", "Data": "{{payload}}" },
                { "Id": "{{duplicateId}}", "FileName": "first.bin", "Data": "{{payload}}" },
                { "Id": "{{duplicateId}}", "FileName": "second.bin", "Data": "{{payload}}" }
              ],
              "Paragraphs": []
            }
            """);

        GostDocument document = await _service.LoadAsync(stream);

        Assert.Equal(3, document.Images.Count);
        Assert.All(document.Images, image => Assert.NotEqual(Guid.Empty, image.Id));
        Assert.Equal(3, document.Images.Select(image => image.Id).Distinct().Count());
        Assert.Equal(
            new[] { "empty.bin", "first.bin", "second.bin" },
            document.Images.Select(image => image.FileName));
    }

    [Fact]
    public async Task Load_VersionTwoWithMissingMedia_Throws()
    {
        Guid imageId = Guid.NewGuid();
        await using MemoryStream stream = await CreateArchiveAsync(
            CreateV2Manifest(imageId, includeParagraph: false));

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains(imageId.ToString(), exception.Message);
    }

    [Fact]
    public async Task Load_VersionTwoWithDanglingImageId_Throws()
    {
        Guid imageId = Guid.NewGuid();
        await using MemoryStream stream = await CreateArchiveAsync(
            $$"""
            {
              "FormatVersion": 2,
              "Images": [],
              "Paragraphs": [
                {
                  "Runs": [],
                  "ImageId": "{{imageId}}",
                  "ImageWidth": 100,
                  "ImageHeight": 50
                }
              ]
            }
            """);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains(imageId.ToString(), exception.Message);
    }

    [Fact]
    public async Task Load_VersionTwoWithEmptyReferencedMedia_Throws()
    {
        Guid imageId = Guid.NewGuid();
        await using MemoryStream stream = await CreateArchiveAsync(
            CreateV2Manifest(imageId, includeParagraph: true),
            ($"media/images/{imageId:N}.dat", Array.Empty<byte>()));

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains("не содержит данных", exception.Message);
    }

    [Fact]
    public async Task Load_VersionTwoWithDuplicateImageIds_Throws()
    {
        Guid imageId = Guid.NewGuid();
        await using MemoryStream stream = await CreateArchiveAsync(
            $$"""
            {
              "FormatVersion": 2,
              "Images": [
                { "Id": "{{imageId}}", "FileName": "a.png", "MediaType": "image/png" },
                { "Id": "{{imageId}}", "FileName": "b.png", "MediaType": "image/png" }
              ],
              "Paragraphs": []
            }
            """,
            ($"media/images/{imageId:N}.dat", TestImageData.CreatePng()));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.LoadAsync(stream));
    }

    [Fact]
    public async Task Load_FutureVersion_ThrowsNotSupportedException()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """{ "FormatVersion": 999, "Paragraphs": [] }""");

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.LoadAsync(stream));
    }

    [Fact]
    public async Task Load_DuplicateZipEntry_Throws()
    {
        await using MemoryStream stream = new MemoryStream();
        using (ZipArchive archive = new ZipArchive(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            await WriteEntryAsync(
                archive.CreateEntry("document.json"),
                Encoding.UTF8.GetBytes("""{ "Paragraphs": [] }"""));
            await WriteEntryAsync(
                archive.CreateEntry("document.json"),
                Encoding.UTF8.GetBytes("""{ "Paragraphs": [] }"""));
        }
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.LoadAsync(stream));
    }

    [Fact]
    public async Task Load_TooManyZipEntries_IsRejectedBeforeManifestRead()
    {
        await using MemoryStream stream = new();
        using (ZipArchive archive = new(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            for (int index = 0;
                 index <= GostArchiveLimits.MaxEntryCount;
                 index++)
            {
                archive.CreateEntry($"unused/{index}");
            }
        }
        stream.Position = 0;

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains("Центральный каталог", exception.Message);
    }

    [Fact]
    public async Task Load_ManifestBeyondMaximumDepth_IsRejected()
    {
        StringBuilder json = new();
        for (int index = 0;
             index <= GostArchiveLimits.MaxJsonDepth;
             index++)
        {
            json.Append("{\"a\":");
        }
        json.Append('0');
        for (int index = 0;
             index <= GostArchiveLimits.MaxJsonDepth;
             index++)
        {
            json.Append('}');
        }
        await using MemoryStream stream = await CreateArchiveAsync(
            json.ToString());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.LoadAsync(stream));
    }

    [Fact]
    public async Task Load_HugeNumericLiteral_IsRejectedBeforeNumericParsing()
    {
        string hugeNumber = new('9', 1024 * 1024);
        await using MemoryStream stream = await CreateArchiveAsync(
            $$"""{"FormatVersion":{{hugeNumber}},"Paragraphs":[]}""");

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains("числовой литерал", exception.Message);
    }

    [Fact]
    public async Task Load_TooManyImages_IsRejectedBeforeMediaLookup()
    {
        string images = string.Join(
            ',',
            Enumerable.Range(0, GostArchiveLimits.MaxImages + 1)
                .Select(_ => $$"""{"Id":"{{Guid.NewGuid()}}"}"""));
        await using MemoryStream stream = await CreateArchiveAsync(
            $$"""
            {
              "FormatVersion": 2,
              "Images": [{{images}}],
              "Paragraphs": []
            }
            """);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains("Images", exception.Message);
    }

    [Fact]
    public async Task Load_TooManyParagraphs_IsRejectedBeforeMaterialization()
    {
        string paragraphs = string.Join(
            ',',
            Enumerable.Repeat(
                "{\"Runs\":[]}",
                GostArchiveLimits.MaxParagraphs + 1));
        await using MemoryStream stream = await CreateArchiveAsync(
            $$"""
            {
              "FormatVersion": 2,
              "Images": [],
              "Paragraphs": [{{paragraphs}}]
            }
            """);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains("Paragraphs", exception.Message);
    }

    [Fact]
    public async Task Load_NullParagraph_IsReportedAsInvalidData()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """
            {
              "FormatVersion": 2,
              "Images": [],
              "Paragraphs": [null]
            }
            """);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains("Paragraphs", exception.Message);
    }

    [Fact]
    public async Task Load_NullPersistedStrings_AreNormalizedBeforePublication()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """
            {
              "FormatVersion": 2,
              "TitlePage": { "WorkTitle": null },
              "CodeListings": [{ "Content": null, "FileName": null }],
              "BibliographySources": [{ "Description": null }],
              "Images": [],
              "Paragraphs": [{ "Runs": [{ "Text": null }] }]
            }
            """);

        GostDocument document = await _service.LoadAsync(stream);

        Assert.Equal(string.Empty, document.TitlePage.WorkTitle);
        Assert.Equal(string.Empty, document.CodeListings[0].Content);
        Assert.Equal(string.Empty, document.CodeListings[0].FileName);
        Assert.Equal(string.Empty, document.BibliographySources[0].Description);
        Assert.Equal(string.Empty, document.Paragraphs[0].Runs[0].Text);
    }

    [Fact]
    public async Task Load_NonFiniteOrExtremeLayoutValues_AreRejected()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """
            {
              "FormatVersion": 2,
              "PageWidth": 100001,
              "Images": [],
              "Paragraphs": []
            }
            """);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains("PageWidth", exception.Message);
    }

    [Fact]
    public async Task Load_PageMarginsWithoutContentArea_AreRejected()
    {
        await using MemoryStream stream = await CreateArchiveAsync(
            """
            {
              "FormatVersion": 2,
              "PageWidth": 100,
              "PageHeight": 100,
              "MarginLeft": 60,
              "MarginRight": 60,
              "MarginTop": 60,
              "MarginBottom": 60,
              "Images": [],
              "Paragraphs": []
            }
            """);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains("положительной ширины", exception.Message);
    }

    [Fact]
    public async Task Load_NonSeekableStream_UsesBoundedCoreBuffer()
    {
        await using MemoryStream archive = await CreateArchiveAsync(
            """{"FormatVersion":2,"Images":[],"Paragraphs":[]}""");
        await using NonSeekableReadStream source = new(archive.ToArray());

        GostDocument document = await _service.LoadAsync(source);

        Assert.Empty(document.Paragraphs);
    }

    [Fact]
    public async Task Load_CentralDirectoryCountMismatch_IsRejectedByPreflight()
    {
        await using MemoryStream archive = await CreateArchiveAsync(
            """{"FormatVersion":2,"Images":[],"Paragraphs":[]}""",
            ("unused", new byte[] { 1 }));
        byte[] bytes = archive.ToArray();
        int eocd = FindSignatureFromEnd(bytes, 0x06054B50);
        bytes[eocd + 8] = 1;
        bytes[eocd + 9] = 0;
        bytes[eocd + 10] = 1;
        bytes[eocd + 11] = 0;
        await using MemoryStream corrupted = new(bytes);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(corrupted));

        Assert.Contains("Количество записей", exception.Message);
    }

    [Fact]
    public async Task Load_ReferencedInvalidImagePayload_IsRejected()
    {
        Guid imageId = Guid.NewGuid();
        await using MemoryStream stream = await CreateArchiveAsync(
            CreateV2Manifest(imageId, includeParagraph: true),
            ($"media/images/{imageId:N}.dat", new byte[] { 1, 2, 3 }));

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains("поддерживаемым изображением", exception.Message);
    }

    [Fact]
    public async Task Load_ReferencedImageBeyondPixelDimensionLimit_IsRejected()
    {
        Guid imageId = Guid.NewGuid();
        using SKBitmap bitmap = new(
            GostArchiveLimits.MaxImagePixelDimension + 1,
            1);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        await using MemoryStream stream = await CreateArchiveAsync(
            CreateV2Manifest(imageId, includeParagraph: true),
            ($"media/images/{imageId:N}.dat", encoded.ToArray()));

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _service.LoadAsync(stream));

        Assert.Contains("небезопасные размеры", exception.Message);
    }

    [Fact]
    public async Task Save_DanglingReference_Throws()
    {
        GostDocument document = new GostDocument();
        document.Paragraphs.Add(new Paragraph
        {
            ImageId = Guid.NewGuid(),
            ImageWidth = 100,
            ImageHeight = 50
        });
        await using MemoryStream stream = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.SaveAsync(document, stream));
    }

    [Fact]
    public async Task Save_DocumentBeyondReaderLimits_IsRejectedBeforeWriting()
    {
        GostDocument document = new();
        document.Paragraphs = Enumerable.Range(
                0,
                GostArchiveLimits.MaxParagraphs + 1)
            .Select(_ => new Paragraph())
            .ToList();
        await using MemoryStream stream = new();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.SaveAsync(document, stream));

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task SavePath_WhenSerializationFails_PreservesExistingFile()
    {
        using TestTemporaryDirectory temporaryDirectory = new();
        string filePath = temporaryDirectory.GetPath("document.gost");
        byte[] originalBytes = "existing user document"u8.ToArray();
        await File.WriteAllBytesAsync(filePath, originalBytes);

        GostDocument invalidDocument = new();
        invalidDocument.Paragraphs.Add(new Paragraph
        {
            ImageId = Guid.NewGuid(),
            ImageWidth = 100,
            ImageHeight = 50
        });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.SaveAsync(invalidDocument, filePath));

        Assert.Equal(
            originalBytes,
            await File.ReadAllBytesAsync(filePath));
        Assert.Empty(Directory.GetFiles(
            temporaryDirectory.DirectoryPath,
            "document.gost.*.tmp"));
        Assert.Empty(Directory.GetFiles(
            temporaryDirectory.DirectoryPath,
            "document.gost.*.rollback"));
    }

    [Fact]
    public async Task SavePath_WhenSuccessful_ReplacesExistingFileWithLoadableArchive()
    {
        using TestTemporaryDirectory temporaryDirectory = new();
        string filePath = temporaryDirectory.GetPath("document.gost");
        await File.WriteAllTextAsync(filePath, "old content");
        GostDocument expected = CreateDocument();

        await _service.SaveAsync(expected, filePath);
        GostDocument actual = await _service.LoadAsync(filePath);

        AssertDocumentEqual(expected, actual);
        Assert.Empty(Directory.GetFiles(
            temporaryDirectory.DirectoryPath,
            "document.gost.*.tmp"));
        Assert.Empty(Directory.GetFiles(
            temporaryDirectory.DirectoryPath,
            "document.gost.*.rollback"));
    }

    private static GostDocument CreateDocument()
    {
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
                TablesCount = 1,
                SourcesCount = 1,
                PagesCount = 12,
                ApplicationsCount = 1
            },
            CreatedAt = new DateTime(
                2026,
                7,
                1,
                10,
                20,
                30,
                DateTimeKind.Utc),
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
        document.Paragraphs.Add(textParagraph);

        ImageService imageService = new ImageService();
        AssertSuccess(imageService.InsertPlacement(
            document,
            1,
            new CreateImageRequest(
                TestImageData.CreatePng(),
                new ImageSize(320, 180),
                "diagram.png",
                "Диаграмма")));

        document.CodeListings.Add(new CodeListing
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            FileName = "Program.cs",
            RelativePath = "src/Program.cs",
            Language = "csharp",
            Content = "Console.WriteLine(\"test\");",
            IsSelected = true,
            Order = 2,
            ListingNumber = 3
        });
        document.MutableImages.Add(new ImageAttachment(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "legacy-orphan.bin",
            "application/octet-stream",
            new byte[] { 10, 20, 30 },
            "Legacy metadata",
            4));
        document.BibliographySources.Add(new BibliographySource
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Description = "Тестовый источник",
            IsSelected = true,
            Order = 5
        });

        return document;
    }

    private static string CreateV2Manifest(Guid imageId, bool includeParagraph)
    {
        string paragraphs = includeParagraph
            ? $$"""
              [
                {
                  "Runs": [],
                  "ImageId": "{{imageId}}",
                  "ImageWidth": 100,
                  "ImageHeight": 50
                }
              ]
              """
            : "[]";

        return $$"""
        {
          "FormatVersion": 2,
          "Images": [
            {
              "Id": "{{imageId}}",
              "FileName": "image.png",
              "MediaType": "image/png",
              "Caption": "",
              "Order": 0
            }
          ],
          "Paragraphs": {{paragraphs}}
        }
        """;
    }

    private static async Task<JsonDocument> ReadManifestAsync(
        MemoryStream stream)
    {
        stream.Position = 0;
        using ZipArchive archive = new ZipArchive(
            stream,
            ZipArchiveMode.Read,
            leaveOpen: true);
        ZipArchiveEntry entry = Assert.IsType<ZipArchiveEntry>(
            archive.GetEntry("document.json"));
        await using Stream json = entry.Open();
        return await JsonDocument.ParseAsync(json);
    }

    private static async Task<MemoryStream> CreateArchiveAsync(
        string documentJson,
        params (string Path, byte[] Data)[] entries)
    {
        MemoryStream stream = new MemoryStream();

        using (ZipArchive archive = new ZipArchive(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            await WriteEntryAsync(
                archive.CreateEntry("document.json"),
                Encoding.UTF8.GetBytes(documentJson));
            foreach ((string path, byte[] data) in entries)
            {
                await WriteEntryAsync(archive.CreateEntry(path), data);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static async Task WriteEntryAsync(
        ZipArchiveEntry entry,
        byte[] data)
    {
        await using Stream stream = entry.Open();
        await stream.WriteAsync(data);
    }

    private static int FindSignatureFromEnd(byte[] bytes, uint signature)
    {
        for (int index = bytes.Length - 4; index >= 0; index--)
        {
            if (BitConverter.ToUInt32(bytes, index) == signature)
            {
                return index;
            }
        }

        throw new InvalidOperationException("ZIP signature not found.");
    }

    private sealed class NonSeekableReadStream : MemoryStream
    {
        internal NonSeekableReadStream(byte[] bytes)
            : base(bytes, writable: false)
        {
        }

        public override bool CanSeek => false;
    }

    private static void AssertDocumentEqual(
        GostDocument expected,
        GostDocument actual)
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
        Assert.Equal(
            expected.Modules.HasTableOfContents,
            actual.Modules.HasTableOfContents);
        Assert.Equal(
            expected.Modules.HasBibliography,
            actual.Modules.HasBibliography);
        Assert.Equal(expected.Modules.HasAppendix, actual.Modules.HasAppendix);
        Assert.Equal(
            expected.Modules.ContentStartPage,
            actual.Modules.ContentStartPage);
        Assert.Equal(
            expected.Modules.AutoGenerateTOC,
            actual.Modules.AutoGenerateTOC);
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
        Assert.Equal(
            expected.Counters.ApplicationsCount,
            actual.Counters.ApplicationsCount);

        Assert.Equal(expected.Paragraphs.Count, actual.Paragraphs.Count);
        for (int index = 0; index < expected.Paragraphs.Count; index++)
        {
            Paragraph left = expected.Paragraphs[index];
            Paragraph right = actual.Paragraphs[index];
            Assert.Equal(left.Alignment, right.Alignment);
            Assert.Equal(left.FirstLineIndent, right.FirstLineIndent);
            Assert.Equal(left.LineSpacing, right.LineSpacing);
            Assert.Equal(left.Style, right.Style);
            Assert.Equal(left.PageBreakBefore, right.PageBreakBefore);
            Assert.Equal(left.ImageId, right.ImageId);
            Assert.Equal(left.ImageWidth, right.ImageWidth);
            Assert.Equal(left.ImageHeight, right.ImageHeight);
            Assert.Equal(left.Runs.Count, right.Runs.Count);

            for (int runIndex = 0; runIndex < left.Runs.Count; runIndex++)
            {
                TextRun leftRun = left.Runs[runIndex];
                TextRun rightRun = right.Runs[runIndex];
                Assert.Equal(leftRun.Text, rightRun.Text);
                Assert.Equal(leftRun.IsBold, rightRun.IsBold);
                Assert.Equal(leftRun.IsItalic, rightRun.IsItalic);
                Assert.Equal(leftRun.FontSize, rightRun.FontSize);
                Assert.Equal(leftRun.Color, rightRun.Color);
            }
        }

        Assert.Equal(expected.Images.Count, actual.Images.Count);
        for (int index = 0; index < expected.Images.Count; index++)
        {
            ImageAttachment left = expected.Images[index];
            ImageAttachment right = actual.Images[index];
            Assert.Equal(left.Id, right.Id);
            Assert.Equal(left.FileName, right.FileName);
            Assert.Equal(left.MediaType, right.MediaType);
            Assert.Equal(left.Data.ToArray(), right.Data.ToArray());
            Assert.Equal(left.Caption, right.Caption);
            Assert.Equal(left.Order, right.Order);
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

        BibliographySource expectedSource =
            Assert.Single(expected.BibliographySources);
        BibliographySource actualSource =
            Assert.Single(actual.BibliographySources);
        Assert.Equal(expectedSource.Id, actualSource.Id);
        Assert.Equal(expectedSource.Description, actualSource.Description);
        Assert.Equal(expectedSource.IsSelected, actualSource.IsSelected);
        Assert.Equal(expectedSource.Order, actualSource.Order);
    }

    private static void AssertSuccess<T>(ImageResult<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
    }
}
