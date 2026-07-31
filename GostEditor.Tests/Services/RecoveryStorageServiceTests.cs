using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Services;

namespace GostEditor.Tests.Services;

public sealed class RecoveryStorageServiceTests : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly RecoveryStorageService _service;

    public RecoveryStorageServiceTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "GostEditor.Tests",
            Guid.NewGuid().ToString("N"));

        _service = new RecoveryStorageService(
            new ArchiveService(),
            _temporaryDirectory);
    }

    [Fact]
    public async Task SaveAsync_CreatesRecoveryPackageAndMetadata()
    {
        GostDocument document = CreateDocument("Черновик");
        string originalPath = Path.Combine(
            _temporaryDirectory,
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
        Assert.True(metadata.SavedAtUtc <= DateTimeOffset.UtcNow);
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
        string originalPath = Path.Combine(
            _temporaryDirectory,
            "document.gost");

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
        Directory.CreateDirectory(_temporaryDirectory);

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
    public async Task DeleteRecovery_RemovesPackageMetadataAndTemporaryFiles()
    {
        await _service.SaveAsync(
            CreateDocument("Удаление"),
            null);

        string temporaryFile = Path.Combine(
            _temporaryDirectory,
            "orphan.tmp");

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
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(
                _temporaryDirectory,
                recursive: true);
        }
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
