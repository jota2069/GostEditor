using System.Collections.Concurrent;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Tests.Infrastructure;
using GostEditor.UI.Services;

namespace GostEditor.Tests.Services;

public sealed class PersistenceIoCoordinationTests : IDisposable
{
    private static readonly DateTimeOffset SavedAt = new(
        2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly TestTemporaryDirectory _temporaryDirectory = new();

    [Fact]
    public async Task ManualSave_ThenManualSave_SerializesAndCapturesLatestRevision()
    {
        ManualAsyncGate firstWrite = new();
        ControlledArchiveService archive = new(snapshotGate: firstWrite);
        GostDocument document = CreateDocument("revision one");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);
        string path = session.CurrentFilePath!;

        Task<DocumentSaveResult> first = saves.SaveAsync(document, path);
        await firstWrite.WaitUntilReachedAsync();

        document.Paragraphs[0].Runs[0].Text = "revision two";
        session.RecordMutation();
        Task<DocumentSaveResult> second = saves.SaveAsync(document, path);

        try
        {
            Assert.Equal(1, archive.SnapshotSaveCalls);
            Assert.False(second.IsCompleted);
            Assert.Equal(PersistenceIoOperation.ManualSave, coordinator.ActiveOperation);
        }
        finally
        {
            firstWrite.Release();
        }

        DocumentSaveResult[] results = await Task.WhenAll(first, second);

        Assert.Equal([2L, 3L], archive.SnapshotRevisions);
        Assert.Equal(["revision one", "revision two"], archive.SnapshotTexts);
        Assert.Equal(3, session.ChangeVersion);
        Assert.Equal(3, session.SavedRevision);
        Assert.False(session.IsDirty);
        Assert.True(results[1].IsCurrentRevision);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task Save_ThenSaveAs_PublishesNewPathFromSecondOwnedSnapshot()
    {
        ManualAsyncGate firstWrite = new();
        ControlledArchiveService archive = new(snapshotGate: firstWrite);
        GostDocument document = CreateDocument("old path");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);
        string oldPath = session.CurrentFilePath!;
        string newPath = _temporaryDirectory.GetPath("save-as.gost");

        Task<DocumentSaveResult> save = saves.SaveAsync(document, oldPath);
        await firstWrite.WaitUntilReachedAsync();

        document.Paragraphs[0].Runs[0].Text = "new path";
        session.RecordMutation();
        Task<DocumentSaveResult> saveAs = saves.SaveAsync(document, newPath);

        try
        {
            Assert.False(saveAs.IsCompleted);
            Assert.Equal(oldPath, session.CurrentFilePath);
        }
        finally
        {
            firstWrite.Release();
        }

        await Task.WhenAll(save, saveAs);

        Assert.Equal(
            [Path.GetFullPath(oldPath), Path.GetFullPath(newPath)],
            archive.SnapshotPaths);
        Assert.Equal([2L, 3L], archive.SnapshotRevisions);
        Assert.Equal(Path.GetFullPath(newPath), session.CurrentFilePath);
        Assert.Equal(session.ChangeVersion, session.SavedRevision);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task ManualSave_InProgress_AutoSaveSkipsWithoutCapturingDocument()
    {
        ManualAsyncGate manualWrite = new();
        ControlledArchiveService archive = new(snapshotGate: manualWrite);
        GostDocument document = CreateDocument("manual");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);
        using AutoSaveService autoSave = CreateAutoSave(archive, session, coordinator, "manual-first");
        int providerCalls = 0;

        Task<DocumentSaveResult> save = saves.SaveAsync(
            document,
            session.CurrentFilePath!);
        await manualWrite.WaitUntilReachedAsync();

        try
        {
            bool autoSaved = await autoSave.SaveIfNeededAsync(() =>
            {
                providerCalls++;
                return document;
            });

            Assert.False(autoSaved);
            Assert.Equal(0, providerCalls);
            Assert.Equal(0, archive.DocumentSaveCalls);
        }
        finally
        {
            manualWrite.Release();
        }

        await save;
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task AutoSave_InProgress_ManualSaveWaitsThenCommits()
    {
        ManualAsyncGate autoWrite = new();
        ControlledArchiveService archive = new(documentGate: autoWrite);
        GostDocument document = CreateDocument("shared revision");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        using AutoSaveService autoSave = CreateAutoSave(archive, session, coordinator, "auto-first");
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);

        Task<bool> recovery = autoSave.SaveIfNeededAsync(() => document);
        await autoWrite.WaitUntilReachedAsync();
        Task<DocumentSaveResult> manual = saves.SaveAsync(
            document,
            session.CurrentFilePath!);

        try
        {
            Assert.False(manual.IsCompleted);
            Assert.Equal(0, archive.SnapshotSaveCalls);
            Assert.Equal(PersistenceIoOperation.AutoSave, coordinator.ActiveOperation);
        }
        finally
        {
            autoWrite.Release();
        }

        Assert.True(await recovery);
        DocumentSaveResult result = await manual;

        Assert.True(result.IsCurrentRevision);
        Assert.False(session.IsDirty);
        Assert.Equal(session.ChangeVersion, session.SavedRevision);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task SaveAs_InProgress_AutoSaveSkipsAndDoesNotChangeCurrentPath()
    {
        ManualAsyncGate saveAsWrite = new();
        ControlledArchiveService archive = new(snapshotGate: saveAsWrite);
        GostDocument document = CreateDocument("save as");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);
        using AutoSaveService autoSave = CreateAutoSave(archive, session, coordinator, "save-as-first");
        string oldPath = session.CurrentFilePath!;
        string newPath = _temporaryDirectory.GetPath("new.gost");

        Task<DocumentSaveResult> saveAs = saves.SaveAsync(document, newPath);
        await saveAsWrite.WaitUntilReachedAsync();

        try
        {
            Assert.False(await autoSave.SaveIfNeededAsync(() => document));
            Assert.Equal(oldPath, session.CurrentFilePath);
            Assert.Equal(0, archive.DocumentSaveCalls);
        }
        finally
        {
            saveAsWrite.Release();
        }

        await saveAs;
        Assert.Equal(Path.GetFullPath(newPath), session.CurrentFilePath);
    }

    [Fact]
    public async Task AutoSave_InProgress_SaveAsWaitsAndPublishesNewPathAfterwards()
    {
        ManualAsyncGate autoWrite = new();
        ControlledArchiveService archive = new(documentGate: autoWrite);
        GostDocument document = CreateDocument("save as after recovery");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        using AutoSaveService autoSave = CreateAutoSave(
            archive,
            session,
            coordinator,
            "auto-before-save-as");
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);
        string oldPath = session.CurrentFilePath!;
        string newPath = _temporaryDirectory.GetPath("after-auto.gost");

        Task<bool> recovery = autoSave.SaveIfNeededAsync(() => document);
        await autoWrite.WaitUntilReachedAsync();
        Task<DocumentSaveResult> saveAs = saves.SaveAsync(document, newPath);

        try
        {
            Assert.False(saveAs.IsCompleted);
            Assert.Equal(oldPath, session.CurrentFilePath);
        }
        finally
        {
            autoWrite.Release();
        }

        Assert.True(await recovery);
        DocumentSaveResult result = await saveAs;

        Assert.True(result.IsCurrentRevision);
        Assert.Equal(Path.GetFullPath(newPath), session.CurrentFilePath);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task ConditionalRecoveryCleanup_DoesNotDeleteNewerAutoSave()
    {
        ManualAsyncGate autoWrite = new();
        ControlledArchiveService archive = new(documentGate: autoWrite);
        GostDocument document = CreateDocument("new recovery");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        long manuallySavedRevision = session.ChangeVersion;
        session.MarkSaved(
            session.CurrentFilePath!,
            SavedAt,
            manuallySavedRevision);
        session.RecordMutation();
        PersistenceIoCoordinator coordinator = new();
        using AutoSaveService autoSave = CreateAutoSave(
            archive,
            session,
            coordinator,
            "guarded-cleanup");

        Task<bool> recovery = autoSave.SaveIfNeededAsync(
            () => document);
        await autoWrite.WaitUntilReachedAsync();
        Task<bool> cleanup = autoSave.ClearRecoveryAsync(manuallySavedRevision);

        try
        {
            Assert.False(cleanup.IsCompleted);
        }
        finally
        {
            autoWrite.Release();
        }

        Assert.True(await recovery);
        Assert.False(await cleanup);
        Assert.True(File.Exists(GetRecoveryPointerPath("guarded-cleanup")));
        Assert.Equal(session.ChangeVersion, autoSave.LastSavedChangeVersion);
    }

    [Fact]
    public async Task Save_InProgress_ExportWaitsAndCapturesRevisionAfterEdit()
    {
        ManualAsyncGate saveWrite = new();
        ControlledArchiveService archive = new(snapshotGate: saveWrite);
        ControlledExportService exporter = new();
        GostDocument document = CreateDocument("before save");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);
        DocumentExportService exports = new(exporter, session, coordinator);

        Task<DocumentSaveResult> save = saves.SaveAsync(
            document,
            session.CurrentFilePath!);
        await saveWrite.WaitUntilReachedAsync();
        document.Paragraphs[0].Runs[0].Text = "after save snapshot";
        session.RecordMutation();
        Task<DocumentExportResult> export = exports.ExportToDocxAsync(
            document,
            _temporaryDirectory.GetPath("document.docx"));

        try
        {
            Assert.False(export.IsCompleted);
            Assert.Equal(0, exporter.ExportCalls);
        }
        finally
        {
            saveWrite.Release();
        }

        DocumentSaveResult saveResult = await save;
        DocumentExportResult exportResult = await export;

        Assert.False(saveResult.IsCurrentRevision);
        Assert.Equal(session.ChangeVersion, exportResult.ExportedRevision);
        Assert.Equal("after save snapshot", exporter.ExportedText);
        Assert.True(session.IsDirty);
        Assert.Equal(saveResult.SavedRevision, session.SavedRevision);
    }

    [Fact]
    public async Task Export_InProgress_SaveWaitsAndCapturesEditMadeWhileQueued()
    {
        ManualAsyncGate exportWrite = new();
        ControlledArchiveService archive = new();
        ControlledExportService exporter = new(exportWrite);
        GostDocument document = CreateDocument("exported revision");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);
        DocumentExportService exports = new(exporter, session, coordinator);

        Task<DocumentExportResult> export = exports.ExportToDocxAsync(
            document,
            _temporaryDirectory.GetPath("blocked.docx"));
        await exportWrite.WaitUntilReachedAsync();
        document.Paragraphs[0].Runs[0].Text = "saved revision";
        session.RecordMutation();
        Task<DocumentSaveResult> save = saves.SaveAsync(
            document,
            session.CurrentFilePath!);

        try
        {
            Assert.False(save.IsCompleted);
            Assert.Equal(0, archive.SnapshotSaveCalls);
        }
        finally
        {
            exportWrite.Release();
        }

        DocumentExportResult exportResult = await export;
        DocumentSaveResult saveResult = await save;

        Assert.Equal(2, exportResult.ExportedRevision);
        Assert.Equal("exported revision", exporter.ExportedText);
        Assert.Equal(3, saveResult.SavedRevision);
        Assert.Equal("saved revision", Assert.Single(archive.SnapshotTexts));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task SaveThenSaveAs_QueuedBehindExport_RunInRequestOrder()
    {
        ManualAsyncGate exportWrite = new();
        ControlledArchiveService archive = new();
        ControlledExportService exporter = new(exportWrite);
        GostDocument document = CreateDocument("fifo");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        DocumentExportService exports = new(exporter, session, coordinator);
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);
        string oldPath = session.CurrentFilePath!;
        string newPath = _temporaryDirectory.GetPath("fifo-save-as.gost");

        Task<DocumentExportResult> export = exports.ExportToDocxAsync(
            document,
            _temporaryDirectory.GetPath("fifo.docx"));
        await exportWrite.WaitUntilReachedAsync();

        Task<DocumentSaveResult> save = saves.SaveAsync(document, oldPath);
        Task<DocumentSaveResult> saveAs = saves.SaveAsync(document, newPath);

        try
        {
            Assert.False(save.IsCompleted);
            Assert.False(saveAs.IsCompleted);
        }
        finally
        {
            exportWrite.Release();
        }

        await Task.WhenAll(export, save, saveAs);

        Assert.Equal(
            [Path.GetFullPath(oldPath), Path.GetFullPath(newPath)],
            archive.SnapshotPaths);
        Assert.Equal(Path.GetFullPath(newPath), session.CurrentFilePath);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task FailedOwnedExport_ReleasesCoordinatorForSave()
    {
        ControlledArchiveService archive = new();
        ControlledExportService exporter = new(
            failure: new IOException("export failed"));
        GostDocument document = CreateDocument("after export failure");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        DocumentExportService exports = new(exporter, session, coordinator);
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);

        IOException failure = await Assert.ThrowsAsync<IOException>(() =>
            exports.ExportToDocxAsync(
                document,
                _temporaryDirectory.GetPath("failed.docx")));

        DocumentSaveResult save = await saves.SaveAsync(
            document,
            session.CurrentFilePath!);

        Assert.Equal("export failed", failure.Message);
        Assert.True(save.IsCurrentRevision);
        Assert.False(session.IsDirty);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task FailedOwnedSave_ReleasesCoordinatorForNextSave()
    {
        ManualAsyncGate firstWrite = new();
        ControlledArchiveService archive = new(
            snapshotGate: firstWrite,
            firstSnapshotFailure: new IOException("first failed"));
        GostDocument document = CreateDocument("retry");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);

        Task<DocumentSaveResult> first = saves.SaveAsync(
            document,
            session.CurrentFilePath!);
        await firstWrite.WaitUntilReachedAsync();
        Task<DocumentSaveResult> second = saves.SaveAsync(
            document,
            session.CurrentFilePath!);
        firstWrite.Release();

        IOException failure = await Assert.ThrowsAsync<IOException>(() => first);
        DocumentSaveResult result = await second;

        Assert.Equal("first failed", failure.Message);
        Assert.True(result.IsCurrentRevision);
        Assert.False(session.IsDirty);
        Assert.Equal(2, archive.SnapshotSaveCalls);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task CancelledOwnedAutoSave_ReleasesCoordinatorForManualSave()
    {
        ManualAsyncGate autoWrite = new();
        ControlledArchiveService archive = new(documentGate: autoWrite);
        GostDocument document = CreateDocument("cancel autosave");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        using AutoSaveService autoSave = CreateAutoSave(
            archive,
            session,
            coordinator,
            "cancelled-auto");
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);
        using CancellationTokenSource cancellation = new();

        Task<bool> cancelled = autoSave.SaveIfNeededAsync(
            () => document,
            cancellation.Token);
        await autoWrite.WaitUntilReachedAsync();
        cancellation.Cancel();
        autoWrite.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(-1, autoSave.LastSavedChangeVersion);

        DocumentSaveResult manual = await saves.SaveAsync(
            document,
            session.CurrentFilePath!);

        Assert.True(manual.IsCurrentRevision);
        Assert.False(session.IsDirty);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task CancellationWhileWaiting_DoesNotPublishAndNextSaveCanRun()
    {
        ManualAsyncGate firstWrite = new();
        ControlledArchiveService archive = new(snapshotGate: firstWrite);
        GostDocument document = CreateDocument("cancel waiting");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);

        Task<DocumentSaveResult> first = saves.SaveAsync(
            document,
            session.CurrentFilePath!);
        await firstWrite.WaitUntilReachedAsync();
        using CancellationTokenSource cancellation = new();
        Task<DocumentSaveResult> cancelled = saves.SaveAsync(
            document,
            _temporaryDirectory.GetPath("cancelled.gost"),
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(1, archive.SnapshotSaveCalls);
        firstWrite.Release();
        await first;

        session.RecordMutation();
        DocumentSaveResult retry = await saves.SaveAsync(
            document,
            _temporaryDirectory.GetPath("retry.gost"));

        Assert.True(retry.IsCurrentRevision);
        Assert.Equal(2, archive.SnapshotSaveCalls);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task CancellationAfterOwnership_ReleasesCoordinatorForNextSave()
    {
        ManualAsyncGate ownedWrite = new();
        ControlledArchiveService archive = new(snapshotGate: ownedWrite);
        GostDocument document = CreateDocument("cancel owned");
        DocumentSessionState session = CreateDirtyOpenedSession(document);
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = CreateSaveService(archive, session, coordinator);
        using CancellationTokenSource cancellation = new();

        Task<DocumentSaveResult> cancelled = saves.SaveAsync(
            document,
            session.CurrentFilePath!,
            cancellation.Token);
        await ownedWrite.WaitUntilReachedAsync();
        cancellation.Cancel();
        ownedWrite.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.True(session.IsDirty);

        DocumentSaveResult retry = await saves.SaveAsync(
            document,
            session.CurrentFilePath!);

        Assert.True(retry.IsCurrentRevision);
        Assert.False(session.IsDirty);
        Assert.False(coordinator.IsBusy);
    }

    public void Dispose()
    {
        _temporaryDirectory.Dispose();
    }

    private DocumentSessionState CreateDirtyOpenedSession(
        GostDocument document)
    {
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
        PersistenceIoCoordinator coordinator,
        string directoryName)
    {
        RecoveryStorageService recovery = new(
            archive,
            _temporaryDirectory.GetPath(directoryName));
        return new AutoSaveService(
            recovery,
            session,
            coordinator,
            TimeSpan.FromMinutes(1));
    }

    private string GetRecoveryPointerPath(string directoryName) =>
        Path.Combine(
            _temporaryDirectory.GetPath(directoryName),
            "current.json");

    private sealed class ControlledArchiveService : IArchiveService
    {
        private readonly ManualAsyncGate? _snapshotGate;
        private readonly ManualAsyncGate? _documentGate;
        private readonly Exception? _firstSnapshotFailure;
        private readonly ConcurrentQueue<long> _snapshotRevisions = new();
        private readonly ConcurrentQueue<string> _snapshotTexts = new();
        private readonly ConcurrentQueue<string> _snapshotPaths = new();
        private int _snapshotSaveCalls;
        private int _documentSaveCalls;

        public ControlledArchiveService(
            ManualAsyncGate? snapshotGate = null,
            ManualAsyncGate? documentGate = null,
            Exception? firstSnapshotFailure = null)
        {
            _snapshotGate = snapshotGate;
            _documentGate = documentGate;
            _firstSnapshotFailure = firstSnapshotFailure;
        }

        public int SnapshotSaveCalls => Volatile.Read(ref _snapshotSaveCalls);

        public int DocumentSaveCalls => Volatile.Read(ref _documentSaveCalls);

        public long[] SnapshotRevisions => _snapshotRevisions.ToArray();

        public string[] SnapshotTexts => _snapshotTexts.ToArray();

        public string[] SnapshotPaths => _snapshotPaths.ToArray();

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
            Interlocked.Increment(ref _documentSaveCalls);
            if (_documentGate is not null)
            {
                await _documentGate.SignalAndWaitAsync(cancellationToken);
            }

            await File.WriteAllBytesAsync(
                filePath,
                "recovery"u8.ToArray(),
                cancellationToken);
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
            int call = Interlocked.Increment(ref _snapshotSaveCalls);
            _snapshotRevisions.Enqueue(snapshot.Revision);
            _snapshotTexts.Enqueue(
                snapshot.Document.Paragraphs[0].Runs[0].Text);
            _snapshotPaths.Enqueue(Path.GetFullPath(filePath));

            if (call == 1 && _snapshotGate is not null)
            {
                await _snapshotGate.SignalAndWaitAsync(cancellationToken);
            }

            if (call == 1 && _firstSnapshotFailure is not null)
            {
                throw _firstSnapshotFailure;
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
        private readonly ManualAsyncGate? _gate;
        private readonly Exception? _failure;
        private int _exportCalls;

        public ControlledExportService(
            ManualAsyncGate? gate = null,
            Exception? failure = null)
        {
            _gate = gate;
            _failure = failure;
        }

        public int ExportCalls => Volatile.Read(ref _exportCalls);

        public string? ExportedText { get; private set; }

        public Task ExportToDocxAsync(
            GostDocument document,
            string outputPath,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Coordinated export must use a revision snapshot.");

        public async Task ExportToDocxAsync(
            DocumentPersistenceSnapshot snapshot,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _exportCalls);
            if (_gate is not null)
            {
                await _gate.SignalAndWaitAsync(cancellationToken);
            }

            if (_failure is not null)
            {
                throw _failure;
            }

            ExportedText = snapshot.Document.Paragraphs[0].Runs[0].Text;
        }
    }
}
