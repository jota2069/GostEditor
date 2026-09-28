using System.Text.Json;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Tests.Infrastructure;
using GostEditor.UI.Services;

namespace GostEditor.Tests.Services;

public sealed class RecoveryStorageServiceTests : IDisposable
{
    private static readonly DateTimeOffset SavedAtUtc = new(
        2026,
        9,
        28,
        12,
        0,
        0,
        TimeSpan.Zero);

    private readonly TestTemporaryDirectory _temporaryDirectory = new();
    private readonly RecoveryStorageService _service;

    public RecoveryStorageServiceTests()
    {
        _service = new RecoveryStorageService(
            new ArchiveService(),
            _temporaryDirectory.DirectoryPath,
            new ManualUtcTimeProvider(SavedAtUtc));
    }

    [Fact]
    public async Task SaveAsync_CreatesRecoveryPackageAndMetadata()
    {
        GostDocument document = CreateDocument("Черновик");
        string originalPath = Path.Combine(
            _temporaryDirectory.DirectoryPath,
            "..",
            "original.gost");

        RecoveryMetadata metadata = await _service.SaveAsync(
            document,
            originalPath);

        Assert.True(File.Exists(_service.RecoveryFilePath));
        Assert.True(File.Exists(_service.MetadataFilePath));
        Assert.Equal(RecoveryMetadata.CurrentVersion, metadata.Version);
        Assert.NotEqual(Guid.Empty, metadata.SessionId);
        Assert.Equal(
            Path.GetFullPath(originalPath),
            metadata.OriginalFilePath);
        Assert.Equal(SavedAtUtc, metadata.SavedAtUtc);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripPreservesDocument()
    {
        GostDocument expected = CreateDocument("Текст восстановления");

        await _service.SaveAsync(expected, null);

        GostDocument actual = await _service.LoadDocumentAsync();

        Paragraph paragraph = Assert.Single(actual.Paragraphs);
        Assert.Equal("Текст восстановления", paragraph.GetPlainText());
        Assert.Equal(
            expected.TitlePage.WorkTitle,
            actual.TitlePage.WorkTitle);
        Assert.Equal(
            expected.Modules.ContentStartPage,
            actual.Modules.ContentStartPage);
    }

    [Fact]
    public async Task LoadMetadataAsync_ReturnsSavedMetadata()
    {
        string originalPath =
            _temporaryDirectory.GetPath("document.gost");

        RecoveryMetadata saved = await _service.SaveAsync(
            CreateDocument("Метаданные"),
            originalPath);

        RecoveryMetadata? loaded = await _service.LoadMetadataAsync();

        Assert.NotNull(loaded);
        Assert.Equal(saved.Version, loaded.Version);
        Assert.Equal(saved.SessionId, loaded.SessionId);
        Assert.Equal(saved.OriginalFilePath, loaded.OriginalFilePath);
        Assert.Equal(saved.SavedAtUtc, loaded.SavedAtUtc);
    }

    [Fact]
    public async Task SaveAsync_WithoutOriginalPath_WritesNullPath()
    {
        await _service.SaveAsync(
            CreateDocument("Новый документ"),
            null);

        RecoveryMetadata? metadata =
            await _service.LoadMetadataAsync();

        Assert.NotNull(metadata);
        Assert.Null(metadata.OriginalFilePath);
    }

    [Fact]
    public async Task SaveAsync_OverwritesPreviousRecovery()
    {
        await _service.SaveAsync(
            CreateDocument("Первая версия"),
            null);

        RecoveryMetadata firstMetadata =
            Assert.IsType<RecoveryMetadata>(
                await _service.LoadMetadataAsync());

        await _service.SaveAsync(
            CreateDocument("Вторая версия"),
            null);

        GostDocument loaded = await _service.LoadDocumentAsync();
        RecoveryMetadata secondMetadata =
            Assert.IsType<RecoveryMetadata>(
                await _service.LoadMetadataAsync());

        Assert.Equal(
            "Вторая версия",
            Assert.Single(loaded.Paragraphs).GetPlainText());

        Assert.NotEqual(
            firstMetadata.SessionId,
            secondMetadata.SessionId);
    }

    [Fact]
    public async Task LoadMetadataAsync_WhenMetadataIsMissing_ReturnsNull()
    {
        RecoveryMetadata? metadata =
            await _service.LoadMetadataAsync();

        Assert.Null(metadata);
    }

    [Fact]
    public async Task LoadDocumentAsync_WhenRecoveryIsMissing_Throws()
    {
        FileNotFoundException exception =
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => _service.LoadDocumentAsync());

        Assert.Equal(
            _service.RecoveryFilePath,
            exception.FileName);
    }

    [Fact]
    public async Task LoadMetadataAsync_WhenMetadataIsCorrupted_ThrowsJsonException()
    {
        await File.WriteAllTextAsync(
            _service.MetadataFilePath,
            "{ invalid json");

        await Assert.ThrowsAsync<JsonException>(
            () => _service.LoadMetadataAsync());
    }

    [Fact]
    public async Task LoadDocumentAsync_WhenRecoveryIsCorrupted_ThrowsInvalidDataException()
    {
        await File.WriteAllTextAsync(
            _service.RecoveryFilePath,
            "this is not a gost archive");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.LoadDocumentAsync());
    }

    [Fact]
    public async Task DeleteRecovery_RemovesPackageMetadataAndTemporaryFiles()
    {
        await _service.SaveAsync(
            CreateDocument("Удаление"),
            null);

        string temporaryFile =
            _temporaryDirectory.GetPath("orphan.tmp");

        await File.WriteAllTextAsync(
            temporaryFile,
            "temporary");

        _service.DeleteRecovery();

        Assert.False(File.Exists(_service.RecoveryFilePath));
        Assert.False(File.Exists(_service.MetadataFilePath));
        Assert.False(File.Exists(temporaryFile));
        Assert.False(_service.HasRecovery);
    }

    public void Dispose()
    {
        _temporaryDirectory.Dispose();
    }

    private static GostDocument CreateDocument(string text)
    {
        return new GostDocument
        {
            TitlePage =
            {
                WorkTitle = "Тестовый документ"
            },
            Modules =
            {
                ContentStartPage = 5
            },
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new TextRun(text)
                    }
                }
            }
        };
    }
}
