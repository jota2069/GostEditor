using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Services;

namespace GostEditor.Tests.Services;

public sealed class AutoSaveServiceTests : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly DocumentSessionState _session;
    private readonly RecoveryStorageService _recoveryStorage;
    private readonly AutoSaveService _autoSave;

    public AutoSaveServiceTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "GostEditor.Tests",
            Guid.NewGuid().ToString("N"));

        _session = new DocumentSessionState();

        _recoveryStorage = new RecoveryStorageService(
            new ArchiveService(),
            _temporaryDirectory);

        _autoSave = new AutoSaveService(
            _recoveryStorage,
            _session,
            TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SaveIfNeededAsync_WhenSessionIsClean_DoesNothing()
    {
        bool saved = await _autoSave.SaveIfNeededAsync(
            () => CreateDocument("Чистый документ"));

        Assert.False(saved);
        Assert.False(_recoveryStorage.HasRecovery);
        Assert.Equal(-1, _autoSave.LastSavedChangeVersion);
    }

    [Fact]
    public async Task SaveIfNeededAsync_WhenDocumentIsDirty_CreatesRecovery()
    {
        _session.MarkDirty();

        bool saved = await _autoSave.SaveIfNeededAsync(
            () => CreateDocument("Черновик"));

        Assert.True(saved);
        Assert.True(_recoveryStorage.HasRecovery);
        Assert.True(_session.IsDirty);
        Assert.Equal(
            _session.ChangeVersion,
            _autoSave.LastSavedChangeVersion);

        GostDocument recovered =
            await _recoveryStorage.LoadDocumentAsync();

        Assert.Equal(
            "Черновик",
            Assert.Single(recovered.Paragraphs).GetPlainText());
    }

    [Fact]
    public async Task SaveIfNeededAsync_WhenVersionWasAlreadySaved_DoesNotRewriteRecovery()
    {
        _session.MarkDirty();

        Assert.True(await _autoSave.SaveIfNeededAsync(
            () => CreateDocument("Первая копия")));

        RecoveryMetadata firstMetadata =
            Assert.IsType<RecoveryMetadata>(
                await _recoveryStorage.LoadMetadataAsync());

        bool savedAgain = await _autoSave.SaveIfNeededAsync(
            () => CreateDocument("Не должна сохраниться"));

        RecoveryMetadata secondMetadata =
            Assert.IsType<RecoveryMetadata>(
                await _recoveryStorage.LoadMetadataAsync());

        Assert.False(savedAgain);
        Assert.Equal(
            firstMetadata.SessionId,
            secondMetadata.SessionId);

        GostDocument recovered =
            await _recoveryStorage.LoadDocumentAsync();

        Assert.Equal(
            "Первая копия",
            Assert.Single(recovered.Paragraphs).GetPlainText());
    }

    [Fact]
    public async Task SaveIfNeededAsync_AfterNewChange_WritesNewRecoveryVersion()
    {
        _session.MarkDirty();

        Assert.True(await _autoSave.SaveIfNeededAsync(
            () => CreateDocument("Первая версия")));

        RecoveryMetadata firstMetadata =
            Assert.IsType<RecoveryMetadata>(
                await _recoveryStorage.LoadMetadataAsync());

        _session.MarkDirty();

        Assert.True(await _autoSave.SaveIfNeededAsync(
            () => CreateDocument("Вторая версия")));

        RecoveryMetadata secondMetadata =
            Assert.IsType<RecoveryMetadata>(
                await _recoveryStorage.LoadMetadataAsync());

        Assert.NotEqual(
            firstMetadata.SessionId,
            secondMetadata.SessionId);

        Assert.Equal(
            2,
            _autoSave.LastSavedChangeVersion);

        GostDocument recovered =
            await _recoveryStorage.LoadDocumentAsync();

        Assert.Equal(
            "Вторая версия",
            Assert.Single(recovered.Paragraphs).GetPlainText());
    }

    [Fact]
    public async Task ClearRecoveryAsync_RemovesRecoveryAndKeepsCurrentVersion()
    {
        _session.MarkDirty();

        Assert.True(await _autoSave.SaveIfNeededAsync(
            () => CreateDocument("Для удаления")));

        await _autoSave.ClearRecoveryAsync();

        Assert.False(_recoveryStorage.HasRecovery);
        Assert.False(File.Exists(_recoveryStorage.MetadataFilePath));
        Assert.Equal(
            _session.ChangeVersion,
            _autoSave.LastSavedChangeVersion);
    }

    [Fact]
    public async Task ResetAsync_RemovesRecoveryAndResetsSavedVersion()
    {
        _session.MarkDirty();

        Assert.True(await _autoSave.SaveIfNeededAsync(
            () => CreateDocument("Для сброса")));

        await _autoSave.ResetAsync();

        Assert.False(_recoveryStorage.HasRecovery);
        Assert.False(File.Exists(_recoveryStorage.MetadataFilePath));
        Assert.Equal(-1, _autoSave.LastSavedChangeVersion);
    }

    [Fact]
    public async Task SaveIfNeededAsync_WithoutStartedProvider_ReturnsFalse()
    {
        _session.MarkDirty();

        bool saved = await _autoSave.SaveIfNeededAsync();

        Assert.False(saved);
        Assert.False(_recoveryStorage.HasRecovery);
    }

    [Fact]
    public void Constructor_WithInvalidInterval_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AutoSaveService(
                _recoveryStorage,
                _session,
                TimeSpan.Zero));
    }

    public void Dispose()
    {
        _autoSave.Dispose();

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
