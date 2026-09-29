using System.Runtime.ExceptionServices;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Tests.Infrastructure;
using GostEditor.UI.Services;

namespace GostEditor.Tests.Services;

public sealed class AutoSaveServiceTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new();
    private readonly DocumentSessionState _session;
    private readonly RecoveryStorageService _recoveryStorage;
    private readonly AutoSaveService _autoSave;

    public AutoSaveServiceTests()
    {
        _session = new DocumentSessionState();

        _recoveryStorage = new RecoveryStorageService(
            new ArchiveService(),
            _temporaryDirectory.DirectoryPath);

        _autoSave = new AutoSaveService(
            _recoveryStorage,
            _session,
            new PersistenceIoCoordinator(),
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
    public async Task SaveIfNeededAsync_WhileSaveIsInProgress_SecondCallReturnsFalse()
    {
        ManualAsyncGate saveGate = new();
        BlockingArchiveService archiveService = new(saveGate);
        RecoveryStorageService recoveryStorage = new(
            archiveService,
            _temporaryDirectory.GetPath("overlap"));
        DocumentSessionState session = new();
        using AutoSaveService autoSave = new(
            recoveryStorage,
            session,
            new PersistenceIoCoordinator(),
            TimeSpan.FromMinutes(1));
        using CancellationTokenSource timeout =
            new(TimeSpan.FromSeconds(5));
        session.MarkDirty();

        Task<bool> firstSave = autoSave.SaveIfNeededAsync(
            () => CreateDocument("Первая копия"));
        ExceptionDispatchInfo? primaryFailure = null;
        ExceptionDispatchInfo? operationFailure = null;

        try
        {
            await saveGate.WaitUntilReachedAsync(timeout.Token);

            bool secondSaved = await autoSave.SaveIfNeededAsync(
                    () => CreateDocument("Вторая копия"))
                .WaitAsync(timeout.Token);

            Assert.False(secondSaved);
            Assert.False(firstSave.IsCompleted);
            Assert.Equal(1, archiveService.SaveCalls);
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            operationFailure = await ReleaseAndObserveAsync(
                firstSave,
                saveGate);
        }

        primaryFailure?.Throw();
        operationFailure?.Throw();

        Assert.True(await firstSave);
        Assert.Equal(session.ChangeVersion, autoSave.LastSavedChangeVersion);
        Assert.True(recoveryStorage.HasRecovery);
    }

    [Fact]
    public async Task SaveIfNeededAsync_WhenGateWaitIsCancelledBeforeLateSave_ReleasesAndObservesSave()
    {
        ManualAsyncGate beforeSaveGate = new();
        ManualAsyncGate saveGate = new();
        BlockingArchiveService archiveService = new(
            saveGate,
            beforeSaveGate);
        RecoveryStorageService recoveryStorage = new(
            archiveService,
            _temporaryDirectory.GetPath("late-overlap"));
        DocumentSessionState session = new();
        using AutoSaveService autoSave = new(
            recoveryStorage,
            session,
            new PersistenceIoCoordinator(),
            TimeSpan.FromMinutes(1));
        using CancellationTokenSource timeout =
            new(TimeSpan.FromSeconds(5));
        session.MarkDirty();

        Task<bool> firstSave = autoSave.SaveIfNeededAsync(
            () => CreateDocument("Поздняя копия"));
        ExceptionDispatchInfo? primaryFailure = null;
        ExceptionDispatchInfo? operationFailure = null;

        try
        {
            await beforeSaveGate.WaitUntilReachedAsync(timeout.Token);
            using CancellationTokenSource cancelledWait = new();
            cancelledWait.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                saveGate.WaitUntilReachedAsync(cancelledWait.Token));
            Assert.False(firstSave.IsCompleted);
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            operationFailure = await ReleaseAndObserveAsync(
                firstSave,
                beforeSaveGate,
                saveGate);
        }

        primaryFailure?.Throw();
        operationFailure?.Throw();

        Assert.True(await firstSave);
        Assert.Equal(1, archiveService.SaveCalls);
        Assert.Equal(session.ChangeVersion, autoSave.LastSavedChangeVersion);
        Assert.True(recoveryStorage.HasRecovery);
    }

    [Fact]
    public void Constructor_WithInvalidInterval_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AutoSaveService(
                _recoveryStorage,
                _session,
                new PersistenceIoCoordinator(),
                TimeSpan.Zero));
    }

    public void Dispose()
    {
        _autoSave.Dispose();
        _temporaryDirectory.Dispose();
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

    private static async Task<ExceptionDispatchInfo?> ReleaseAndObserveAsync(
        Task operation,
        params ManualAsyncGate[] gates)
    {
        foreach (ManualAsyncGate gate in gates)
        {
            gate.Release();
        }

        using CancellationTokenSource cleanupTimeout =
            new(TimeSpan.FromSeconds(5));

        try
        {
            await operation.WaitAsync(cleanupTimeout.Token);
            return null;
        }
        catch (Exception exception)
        {
            return ExceptionDispatchInfo.Capture(exception);
        }
    }

    private sealed class BlockingArchiveService : IArchiveService
    {
        private readonly ManualAsyncGate? _beforeSaveGate;
        private readonly ManualAsyncGate _saveGate;
        private int _saveCalls;

        public BlockingArchiveService(
            ManualAsyncGate saveGate,
            ManualAsyncGate? beforeSaveGate = null)
        {
            _saveGate = saveGate;
            _beforeSaveGate = beforeSaveGate;
        }

        public int SaveCalls => Volatile.Read(ref _saveCalls);

        public GostDocument CreateNew() => throw new NotSupportedException();

        public Task<GostDocument> LoadAsync(string filePath) =>
            throw new NotSupportedException();

        public Task<GostDocument> LoadAsync(Stream stream) =>
            throw new NotSupportedException();

        public async Task SaveAsync(
            GostDocument document,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _saveCalls);
            if (_beforeSaveGate is not null)
            {
                await _beforeSaveGate.SignalAndWaitAsync();
            }

            await File.WriteAllBytesAsync(
                filePath,
                "blocked package"u8.ToArray(),
                cancellationToken);
            await _saveGate.SignalAndWaitAsync(cancellationToken);
        }

        public Task SaveAsync(
            GostDocument document,
            Stream stream,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            DocumentPersistenceSnapshot snapshot,
            string filePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            DocumentPersistenceSnapshot snapshot,
            Stream stream,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
