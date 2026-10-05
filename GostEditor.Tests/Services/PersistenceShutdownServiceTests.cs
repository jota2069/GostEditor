using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Tests.Infrastructure;
using GostEditor.UI.Services;
using GostEditor.UI.Views;

namespace GostEditor.Tests.Services;

public sealed class PersistenceShutdownServiceTests : IDisposable
{
    private static readonly DateTimeOffset SavedAt = new(
        2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

    private readonly TestTemporaryDirectory _temporaryDirectory = new();

    [Fact]
    public async Task SuspendAndDrain_CancelsActiveSaveAndWaitsForLeaseRelease()
    {
        ManualAsyncGate write = new();
        ManualAsyncGate cancellationCleanup = new();
        ControlledArchiveService archive = new(
            snapshotOperation: token =>
                WaitForCancellationThenCleanupAsync(
                    write,
                    cancellationCleanup,
                    token));
        DocumentSessionState session = CreateDirtySession(out GostDocument document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(
            archive,
            session,
            coordinator);
        document.Paragraphs[0].Runs[0].Text = "unsaved";

        Task<DocumentSaveResult> save = saves.SaveAsync(
            document,
            session.CurrentFilePath!);
        await write.WaitUntilReachedAsync();

        Task drain = coordinator.SuspendAndDrainAsync();
        await cancellationCleanup.WaitUntilReachedAsync();

        Assert.False(drain.IsCompleted);
        Assert.True(coordinator.IsBusy);
        Assert.True(coordinator.IsSuspended);

        cancellationCleanup.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        await drain;

        Assert.False(coordinator.IsBusy);
        Assert.True(session.IsDirty);
        Assert.NotEqual(session.ChangeVersion, session.SavedRevision);
    }

    [Fact]
    public async Task SuspendAndDrain_CancelsActiveLeaseBeforeReturning()
    {
        PersistenceIoCoordinator coordinator = new();
        PersistenceIoCoordinator.PersistenceIoLease lease =
            await coordinator.AcquireAsync(PersistenceIoOperation.Open);

        Task drain = coordinator.SuspendAndDrainAsync();

        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.False(drain.IsCompleted);

        lease.Dispose();
        await drain;
    }

    [Fact]
    public async Task SuspendAndDrain_WaitsForLateSuccessfulSaveAndKeepsCommittedResult()
    {
        ManualAsyncGate commit = new();
        CancellationToken writerToken = default;
        TaskCompletionSource<bool> cancellationObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ControlledArchiveService archive = new(
            snapshotOperation: async token =>
            {
                writerToken = token;
                using CancellationTokenRegistration registration =
                    token.Register(
                        () => cancellationObserved.TrySetResult(true));
                await commit.SignalAndWaitAsync();
            });
        DocumentSessionState session = CreateDirtySession(out GostDocument document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(
            archive,
            session,
            coordinator);
        document.Paragraphs[0].Runs[0].Text = "committed";
        string newPath = _temporaryDirectory.GetPath("committed-as.gost");

        Task<DocumentSaveResult> save = saves.SaveAsync(
            document,
            newPath);
        await commit.WaitUntilReachedAsync();

        Task drain = coordinator.SuspendAndDrainAsync();

        await cancellationObserved.Task;
        Assert.True(writerToken.IsCancellationRequested);
        Assert.False(drain.IsCompleted);

        commit.Release();

        DocumentSaveResult result = await save;
        await drain;

        Assert.True(result.IsCurrentRevision);
        Assert.Equal(session.ChangeVersion, session.SavedRevision);
        Assert.Equal(Path.GetFullPath(newPath), session.CurrentFilePath);
        Assert.False(session.IsDirty);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task SuspendAndDrain_LateSaveDoesNotCleanNewerEdit()
    {
        ManualAsyncGate commit = new();
        ControlledArchiveService archive = new(
            snapshotOperation: _ => commit.SignalAndWaitAsync());
        DocumentSessionState session = CreateDirtySession(out GostDocument document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(
            archive,
            session,
            coordinator);
        document.Paragraphs[0].Runs[0].Text = "captured";
        long capturedRevision = session.ChangeVersion;

        Task<DocumentSaveResult> save = saves.SaveAsync(
            document,
            session.CurrentFilePath!);
        await commit.WaitUntilReachedAsync();

        document.Paragraphs[0].Runs[0].Text = "newer edit";
        session.RecordMutation();
        Task drain = coordinator.SuspendAndDrainAsync();
        commit.Release();

        DocumentSaveResult result = await save;
        await drain;

        Assert.Equal(capturedRevision, result.SavedRevision);
        Assert.False(result.IsCurrentRevision);
        Assert.Equal(capturedRevision, session.SavedRevision);
        Assert.Equal(capturedRevision + 1, session.ChangeVersion);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task SuspendAndDrain_CancelsQueuedSaveBeforeSnapshotCapture()
    {
        ControlledArchiveService archive = new();
        DocumentSessionState session = CreateDirtySession(out GostDocument document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(
            archive,
            session,
            coordinator);
        PersistenceIoCoordinator.PersistenceIoLease firstOwnership =
            await coordinator.AcquireAsync(PersistenceIoOperation.Export);

        Task<DocumentSaveResult> queuedSave = saves.SaveAsync(
            document,
            session.CurrentFilePath!);
        Task drain = coordinator.SuspendAndDrainAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => queuedSave);
        Assert.Equal(0, archive.SnapshotSaveCalls);
        Assert.False(drain.IsCompleted);

        firstOwnership.Dispose();
        await drain;

        Assert.False(coordinator.IsBusy);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task Resume_AfterDrain_AllowsFollowingSave()
    {
        ControlledArchiveService archive = new();
        DocumentSessionState session = CreateDirtySession(out GostDocument document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(
            archive,
            session,
            coordinator);

        await coordinator.SuspendAndDrainAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            saves.SaveAsync(
                document,
                session.CurrentFilePath!));

        coordinator.Resume();
        DocumentSaveResult result = await saves.SaveAsync(
            document,
            session.CurrentFilePath!);

        Assert.True(result.IsCurrentRevision);
        Assert.Equal(1, archive.SnapshotSaveCalls);
        Assert.False(coordinator.IsSuspended);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task SuspendAndDrain_RepeatedCallSharesDrainAndEarlyResumeIsRejected()
    {
        PersistenceIoCoordinator coordinator = new();
        PersistenceIoCoordinator.PersistenceIoLease ownership =
            await coordinator.AcquireAsync(PersistenceIoOperation.ManualSave);

        Task firstDrain = coordinator.SuspendAndDrainAsync();
        Task secondDrain = coordinator.SuspendAndDrainAsync();

        Assert.Same(firstDrain, secondDrain);
        Assert.False(firstDrain.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => coordinator.Resume());

        ownership.Dispose();
        await Task.WhenAll(firstDrain, secondDrain);

        coordinator.Resume();
        Assert.False(coordinator.IsSuspended);
    }

    [Fact]
    public void CanCloseAfterSave_RequiresSnapshotToRemainCurrentAndClean()
    {
        DocumentSessionState session = CreateDirtySession(out _);
        long savedRevision = session.ChangeVersion;
        session.MarkSaved(
            session.CurrentFilePath!,
            SavedAt,
            savedRevision);
        DocumentSaveResult currentSave = new(
            savedRevision,
            IsCurrentRevision: true,
            SavedAt);

        Assert.True(MainWindow.CanCloseAfterSave(currentSave, session));

        session.RecordMutation();
        DocumentSaveResult staleSave = new(
            savedRevision,
            IsCurrentRevision: false,
            SavedAt);

        Assert.False(MainWindow.CanCloseAfterSave(staleSave, session));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task SuspendAndDrain_CancelsActiveExportAndAllowsResume()
    {
        ManualAsyncGate export = new();
        ManualAsyncGate cancellationCleanup = new();
        ControlledExportService exporter = new(
            token => WaitForCancellationThenCleanupAsync(
                export,
                cancellationCleanup,
                token));
        DocumentSessionState session = CreateDirtySession(out GostDocument document);
        PersistenceIoCoordinator coordinator = new();
        DocumentExportService exports = new(
            exporter,
            session,
            coordinator);

        Task<DocumentExportResult> operation = exports.ExportToDocxAsync(
            document,
            _temporaryDirectory.GetPath("document.docx"));
        await export.WaitUntilReachedAsync();

        Task drain = coordinator.SuspendAndDrainAsync();
        await cancellationCleanup.WaitUntilReachedAsync();
        Assert.False(drain.IsCompleted);

        cancellationCleanup.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation);
        await drain;

        coordinator.Resume();
        Assert.False(coordinator.IsSuspended);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task SuspendAndDrain_WaitsForOpenAndPreventsLatePublication()
    {
        ManualAsyncGate load = new();
        ControlledArchiveService archive = new(
            loadOperation: async () =>
            {
                await load.SignalAndWaitAsync();
                return CreateDocument("loaded");
            });
        PersistenceIoCoordinator coordinator = new();
        bool published = false;
        ManualAsyncGate streamDisposal = new();
        ControlledDisposeStream stream = new(streamDisposal);

        Task open = MainWindow.LoadDocumentForOpenAsync(
            archive,
            () => Task.FromResult<Stream>(stream),
            coordinator,
            _ => published = true);
        await load.WaitUntilReachedAsync();

        Task drain = coordinator.SuspendAndDrainAsync();
        Assert.False(drain.IsCompleted);

        load.Release();
        await streamDisposal.WaitUntilReachedAsync();

        Assert.False(drain.IsCompleted);
        streamDisposal.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => open);
        await drain;

        Assert.False(published);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task ShutdownService_WaitsForTrackedAutosaveCallback()
    {
        ManualAsyncGate recoveryWrite = new();
        ManualAsyncGate cancellationCleanup = new();
        ControlledArchiveService archive = new(
            documentOperation: token =>
                WaitForCancellationThenCleanupAsync(
                    recoveryWrite,
                    cancellationCleanup,
                    token));
        DocumentSessionState session = CreateDirtySession(out GostDocument document);
        PersistenceIoCoordinator coordinator = new();
        using AutoSaveService autoSave = CreateAutoSave(
            archive,
            session,
            coordinator);
        PersistenceShutdownService shutdown = new(coordinator, autoSave);
        autoSave.Start(() => document);

        Task scheduledSave = autoSave.RunScheduledSaveAsync();
        await recoveryWrite.WaitUntilReachedAsync();

        Task drain = shutdown.SuspendAndDrainAsync();
        await cancellationCleanup.WaitUntilReachedAsync();
        Assert.False(drain.IsCompleted);

        cancellationCleanup.Release();

        await scheduledSave;
        await drain;

        Assert.True(shutdown.IsSuspended);
        Assert.False(autoSave.IsRunning);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task ScheduledAutosave_ObservesFailureWhenFailureSubscriberThrows()
    {
        InvalidOperationException writeFailure = new("recovery failed");
        ControlledArchiveService archive = new(
            documentOperation: _ => Task.FromException(writeFailure));
        DocumentSessionState session = CreateDirtySession(out GostDocument document);
        PersistenceIoCoordinator coordinator = new();
        using AutoSaveService autoSave = CreateAutoSave(
            archive,
            session,
            coordinator);
        Exception? reportedFailure = null;
        autoSave.Failed += exception =>
        {
            reportedFailure = exception;
            throw new InvalidOperationException("subscriber failed");
        };
        autoSave.Start(() => document);

        await autoSave.RunScheduledSaveAsync();
        await autoSave.WaitForScheduledOperationsAsync();

        Assert.Same(writeFailure, reportedFailure);
        Assert.False(coordinator.IsBusy);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task ShutdownService_WaitsForAutosaveFailureNotificationAfterLeaseRelease()
    {
        ControlledArchiveService archive = new(
            documentOperation: _ => Task.FromException(
                new InvalidOperationException("recovery failed")));
        DocumentSessionState session = CreateDirtySession(out GostDocument document);
        PersistenceIoCoordinator coordinator = new();
        using AutoSaveService autoSave = CreateAutoSave(
            archive,
            session,
            coordinator);
        PersistenceShutdownService shutdown = new(coordinator, autoSave);
        TaskCompletionSource<bool> notificationReached = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim releaseNotification = new();
        autoSave.Failed += _ =>
        {
            notificationReached.TrySetResult(true);
            releaseNotification.Wait();
        };
        autoSave.Start(() => document);

        Task scheduledSave = Task.Run(autoSave.RunScheduledSaveAsync);

        try
        {
            await notificationReached.Task;
            Assert.False(coordinator.IsBusy);

            Task drain = shutdown.SuspendAndDrainAsync();
            Assert.False(drain.IsCompleted);

            releaseNotification.Set();
            await Task.WhenAll(scheduledSave, drain);

            Assert.True(shutdown.IsSuspended);
        }
        finally
        {
            releaseNotification.Set();
            await scheduledSave;
        }
    }

    [Fact]
    public async Task Stop_PreventsLateScheduledAutosaveFromStarting()
    {
        ControlledArchiveService archive = new();
        DocumentSessionState session = CreateDirtySession(out GostDocument document);
        PersistenceIoCoordinator coordinator = new();
        using AutoSaveService autoSave = CreateAutoSave(
            archive,
            session,
            coordinator);
        autoSave.Start(() => document);

        autoSave.Stop();
        await autoSave.RunScheduledSaveAsync();

        Assert.Equal(0, archive.DocumentSaveCalls);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task Autosave_WhileCoordinatorIsSuspended_SkipsWithoutCapture()
    {
        ControlledArchiveService archive = new();
        DocumentSessionState session = CreateDirtySession(out GostDocument document);
        PersistenceIoCoordinator coordinator = new();
        using AutoSaveService autoSave = CreateAutoSave(
            archive,
            session,
            coordinator);
        int providerCalls = 0;
        await coordinator.SuspendAndDrainAsync();

        bool saved = await autoSave.SaveIfNeededAsync(() =>
        {
            providerCalls++;
            return document;
        });

        Assert.False(saved);
        Assert.Equal(0, providerCalls);
        Assert.Equal(0, archive.DocumentSaveCalls);
        Assert.True(session.IsDirty);
    }

    public void Dispose()
    {
        _temporaryDirectory.Dispose();
    }

    private DocumentSessionState CreateDirtySession(
        out GostDocument document)
    {
        document = CreateDocument("dirty");
        DocumentSessionState session = new();
        session.MarkOpened(
            document,
            _temporaryDirectory.GetPath("original.gost"));
        session.RecordMutation();
        return session;
    }

    private static GostDocument CreateDocument(string text) => new()
    {
        Paragraphs =
        {
            new Paragraph
            {
                Runs = { new TextRun(text) }
            }
        }
    };

    private static DocumentSaveService CreateSaveService(
        IArchiveService archive,
        DocumentSessionState session,
        PersistenceIoCoordinator coordinator) =>
        new(
            archive,
            session,
            coordinator,
            new ManualUtcTimeProvider(SavedAt));

    private AutoSaveService CreateAutoSave(
        IArchiveService archive,
        DocumentSessionState session,
        PersistenceIoCoordinator coordinator) =>
        new(
            new RecoveryStorageService(
                archive,
                _temporaryDirectory.GetPath("recovery")),
            session,
            coordinator,
            TimeSpan.FromHours(1));

    private static async Task WaitForCancellationThenCleanupAsync(
        ManualAsyncGate operation,
        ManualAsyncGate cancellationCleanup,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation.SignalAndWaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await cancellationCleanup.SignalAndWaitAsync();
            throw;
        }
    }

    private sealed class ControlledArchiveService : IArchiveService
    {
        private readonly Func<CancellationToken, Task>? _snapshotOperation;
        private readonly Func<CancellationToken, Task>? _documentOperation;
        private readonly Func<Task<GostDocument>>? _loadOperation;
        private int _snapshotSaveCalls;
        private int _documentSaveCalls;

        internal ControlledArchiveService(
            Func<CancellationToken, Task>? snapshotOperation = null,
            Func<CancellationToken, Task>? documentOperation = null,
            Func<Task<GostDocument>>? loadOperation = null)
        {
            _snapshotOperation = snapshotOperation;
            _documentOperation = documentOperation;
            _loadOperation = loadOperation;
        }

        internal int SnapshotSaveCalls => Volatile.Read(ref _snapshotSaveCalls);

        internal int DocumentSaveCalls => Volatile.Read(ref _documentSaveCalls);

        public GostDocument CreateNew() => throw new NotSupportedException();

        public Task<GostDocument> LoadAsync(string filePath) =>
            throw new NotSupportedException();

        public Task<GostDocument> LoadAsync(Stream stream) =>
            _loadOperation?.Invoke()
            ?? throw new NotSupportedException();

        public async Task SaveAsync(
            GostDocument document,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _documentSaveCalls);
            if (_documentOperation is not null)
            {
                await _documentOperation(cancellationToken);
            }
        }

        public Task SaveAsync(
            GostDocument document,
            Stream stream,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task SaveAsync(
            DocumentPersistenceSnapshot snapshot,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _snapshotSaveCalls);
            if (_snapshotOperation is not null)
            {
                await _snapshotOperation(cancellationToken);
            }
        }

        public Task SaveAsync(
            DocumentPersistenceSnapshot snapshot,
            Stream stream,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ControlledExportService : IExportService
    {
        private readonly Func<CancellationToken, Task> _operation;

        internal ControlledExportService(
            Func<CancellationToken, Task> operation)
        {
            _operation = operation;
        }

        public Task ExportToDocxAsync(
            GostDocument document,
            string outputPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ExportToDocxAsync(
            DocumentPersistenceSnapshot snapshot,
            string outputPath,
            CancellationToken cancellationToken = default) =>
            _operation(cancellationToken);
    }

    private sealed class ControlledDisposeStream : MemoryStream
    {
        private readonly ManualAsyncGate _disposeGate;

        internal ControlledDisposeStream(ManualAsyncGate disposeGate)
        {
            _disposeGate = disposeGate;
        }

        public override async ValueTask DisposeAsync()
        {
            await _disposeGate.SignalAndWaitAsync();
            await base.DisposeAsync();
        }
    }
}
