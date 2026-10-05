using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Tests.Infrastructure;
using GostEditor.UI.Services;
using GostEditor.UI.Views;

namespace GostEditor.Tests.Services;

public sealed class Phase1AuditRemediationTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new();

    [Fact]
    public async Task Open_WhenCurrentDocumentChangesDuringLoad_DoesNotPublishLoadedDocument()
    {
        GostDocument current = CreateDocument("local");
        GostDocument loaded = CreateDocument("loaded");
        DocumentSessionState session = new();
        session.StartNew(current);
        DocumentSessionCheckpoint request = session.CaptureCheckpoint(current);
        ManualAsyncGate loadGate = new();
        GatedLoadArchiveService archive = new(loaded, loadGate);
        bool published = false;

        Task open = MainWindow.LoadDocumentForOpenAsync(
            archive,
            () => Task.FromResult<Stream>(new MemoryStream("archive"u8.ToArray())),
            new PersistenceIoCoordinator(),
            session,
            request,
            beforePublishAsync: null,
            (_, _) => published = true);
        await loadGate.WaitUntilReachedAsync();

        current.Paragraphs[0].Runs[0].Text = "local edit";
        session.RecordMutation();
        loadGate.Release();

        await Assert.ThrowsAsync<DocumentSessionChangedException>(() => open);
        Assert.False(published);
        Assert.Same(current, request.Document);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task QueuedSave_WhenOpenPublishesAnotherDocument_CannotChangeNewSession()
    {
        GostDocument original = CreateDocument("A");
        GostDocument opened = CreateDocument("B");
        DocumentSessionState session = new();
        string originalPath = _temporaryDirectory.GetPath("a.gost");
        string openedPath = _temporaryDirectory.GetPath("b.gost");
        session.MarkOpened(original, originalPath);
        session.RecordMutation();
        DocumentSessionCheckpoint openRequest =
            session.CaptureCheckpoint(original);
        ManualAsyncGate loadGate = new();
        GatedLoadArchiveService loader = new(opened, loadGate);
        CapturingArchiveService writer = new();
        PersistenceIoCoordinator coordinator = new();
        DocumentSaveService saves = new(writer, session, coordinator);

        Task open = MainWindow.LoadDocumentForOpenAsync(
            loader,
            () => Task.FromResult<Stream>(new MemoryStream("archive"u8.ToArray())),
            coordinator,
            session,
            openRequest,
            beforePublishAsync: null,
            (document, fingerprint) =>
                session.MarkOpened(document, openedPath, fingerprint));
        await loadGate.WaitUntilReachedAsync();
        Task<DocumentSaveResult> save = saves.SaveAsync(original, originalPath);
        Assert.False(save.IsCompleted);

        loadGate.Release();
        await open;
        long openedRevision = session.ChangeVersion;
        FileContentFingerprint? openedFingerprint = session.FileFingerprint;

        await Assert.ThrowsAsync<DocumentSessionChangedException>(() => save);
        Assert.Equal(0, writer.SaveCalls);
        Assert.Equal(Path.GetFullPath(openedPath), session.CurrentFilePath);
        Assert.Equal(openedRevision, session.ChangeVersion);
        Assert.Equal(openedRevision, session.SavedRevision);
        Assert.Equal(openedFingerprint, session.FileFingerprint);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task QueuedExport_WhenDocumentIsReplaced_DoesNotExportStaleGraph()
    {
        GostDocument original = CreateDocument("A");
        GostDocument replacement = CreateDocument("B");
        DocumentSessionState session = new();
        session.StartNew(original);
        PersistenceIoCoordinator coordinator = new();
        CapturingExportService exporter = new();
        DocumentExportService exports = new(exporter, session, coordinator);
        using PersistenceIoCoordinator.PersistenceIoLease ownership =
            await coordinator.AcquireAsync(PersistenceIoOperation.Open);

        Task<DocumentExportResult> export = exports.ExportToDocxAsync(
            original,
            _temporaryDirectory.GetPath("stale.docx"));
        session.StartNew(replacement);
        ownership.Dispose();

        await Assert.ThrowsAsync<DocumentSessionChangedException>(() => export);
        Assert.Equal(0, exporter.ExportCalls);
    }

    [Fact]
    public async Task Save_WhenDocumentIsReplacedAfterSnapshot_DoesNotPublishIntoReplacementSession()
    {
        GostDocument original = CreateDocument("A");
        GostDocument replacement = CreateDocument("B");
        DocumentSessionState session = new();
        session.StartNew(original);
        session.RecordMutation();
        ManualAsyncGate writeGate = new();
        CapturingArchiveService archive = new(writeGate);
        DocumentSaveService saves = new(
            archive,
            session,
            new PersistenceIoCoordinator());

        Task<DocumentSaveResult> save = saves.SaveAsync(
            original,
            _temporaryDirectory.GetPath("old.gost"));
        await writeGate.WaitUntilReachedAsync();
        session.StartNew(replacement);
        long replacementRevision = session.ChangeVersion;
        writeGate.Release();

        DocumentSaveResult result = await save;
        Assert.False(result.SessionUpdated);
        Assert.False(result.IsCurrentRevision);
        Assert.Equal(replacementRevision, session.ChangeVersion);
        Assert.Equal(replacementRevision, session.SavedRevision);
        Assert.Null(session.CurrentFilePath);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task SaveAs_WhenTargetChangesAfterRequest_PreservesExternalReplacement()
    {
        string target = _temporaryDirectory.GetPath("copy.gost");
        ArchiveService archive = new();
        await archive.SaveAsync(CreateDocument("X"), target);
        GostDocument local = CreateDocument("local");
        DocumentSessionState session = new();
        session.StartNew(local);
        session.RecordMutation();
        ManualAsyncGate secondCapture = new();
        GateOnCaptureFingerprintService fingerprints =
            new(secondCapture, gateOnCapture: 2);
        DocumentSaveService saves = new(
            archive,
            session,
            new PersistenceIoCoordinator(),
            TimeProvider.System,
            fingerprints);

        Task<DocumentSaveResult> save = saves.SaveAsync(local, target);
        await secondCapture.WaitUntilReachedAsync();
        await archive.SaveAsync(CreateDocument("Y"), target);
        secondCapture.Release();

        await Assert.ThrowsAsync<ExternalFileChangedException>(() => save);
        GostDocument persisted = await archive.LoadAsync(target);
        Assert.Equal("Y", Assert.Single(persisted.Paragraphs).GetPlainText());
        Assert.Null(session.CurrentFilePath);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task SaveAs_WhenTargetChangesImmediatelyBeforeCommit_PreservesExternalReplacement()
    {
        string target = _temporaryDirectory.GetPath("final-copy.gost");
        ArchiveService archive = new();
        await archive.SaveAsync(CreateDocument("X"), target);
        GostDocument local = CreateDocument("local");
        DocumentSessionState session = new();
        session.StartNew(local);
        session.RecordMutation();
        ManualAsyncGate finalCapture = new();
        GateOnCaptureFingerprintService fingerprints =
            new(finalCapture, gateOnCapture: 3);
        DocumentSaveService saves = new(
            archive,
            session,
            new PersistenceIoCoordinator(),
            TimeProvider.System,
            fingerprints);

        Task<DocumentSaveResult> save = saves.SaveAsync(local, target);
        await finalCapture.WaitUntilReachedAsync();
        await archive.SaveAsync(CreateDocument("Y"), target);
        finalCapture.Release();

        await Assert.ThrowsAsync<ExternalFileChangedException>(() => save);
        Assert.Equal(
            "Y",
            Assert.Single(
                (await archive.LoadAsync(target)).Paragraphs).GetPlainText());
        Assert.Null(session.CurrentFilePath);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void DocumentPublication_WhenSessionTransitionThrows_RollsBackGraphAndSession()
    {
        GostDocument original = CreateDocument("A");
        GostDocument replacement = CreateDocument("B");
        string originalPath = _temporaryDirectory.GetPath("original.gost");
        FileContentFingerprint fingerprint = new(10, "baseline");
        DocumentSessionState session = new();
        session.MarkOpened(original, originalPath, fingerprint);
        long originalRevision = session.ChangeVersion;
        GostDocument visibleDocument = original;
        session.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(
                    DocumentSessionState.DocumentGeneration))
            {
                throw new InvalidOperationException("publication failed");
            }
        };

        InvalidOperationException exception = Assert.Throws<
            InvalidOperationException>(() =>
            MainWindow.PublishDocumentAtomically(
                session,
                () => visibleDocument = replacement,
                _ => visibleDocument = original,
                () => session.MarkOpened(
                    replacement,
                    _temporaryDirectory.GetPath("replacement.gost"))));

        Assert.Equal("publication failed", exception.Message);
        Assert.Same(original, visibleDocument);
        Assert.Same(
            original,
            session.CaptureCheckpoint(original).Document);
        Assert.Equal(Path.GetFullPath(originalPath), session.CurrentFilePath);
        Assert.Equal(fingerprint, session.FileFingerprint);
        Assert.Equal(originalRevision, session.ChangeVersion);
        Assert.Equal(originalRevision, session.SavedRevision);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task AutoSave_WhenDocumentIdentityChangesDuringWrite_RemovesStaleRecovery()
    {
        GostDocument original = CreateDocument("A");
        GostDocument replacement = CreateDocument("B");
        DocumentSessionState session = new();
        session.StartNew(original);
        session.RecordMutation();
        ManualAsyncGate writeGate = new();
        GatedRecoveryArchiveService archive = new(writeGate);
        RecoveryStorageService recovery = new(
            archive,
            _temporaryDirectory.GetPath("stale-recovery"));
        using AutoSaveService autoSave = new(
            recovery,
            session,
            new PersistenceIoCoordinator(),
            TimeSpan.FromMinutes(1));

        Task<bool> save = autoSave.SaveIfNeededAsync(() => original);
        await writeGate.WaitUntilReachedAsync();
        session.StartNew(replacement);
        writeGate.Release();

        Assert.False(await save);
        Assert.False(recovery.HasRecovery);
        Assert.Equal(-1, autoSave.LastSavedChangeVersion);
    }

    [Fact]
    public async Task Open_WhenSemanticRecoveryDeletionFails_DoesNotPublishLoadedDocument()
    {
        GostDocument current = CreateDocument("current");
        DocumentSessionState session = new();
        session.StartNew(current);
        DocumentSessionCheckpoint request = session.CaptureCheckpoint(current);
        bool published = false;

        IOException failure = await Assert.ThrowsAsync<IOException>(() =>
            MainWindow.LoadDocumentForOpenAsync(
                new GatedLoadArchiveService(CreateDocument("loaded")),
                () => Task.FromResult<Stream>(
                    new MemoryStream("archive"u8.ToArray())),
                new PersistenceIoCoordinator(),
                session,
                request,
                () => Task.FromException(new IOException("delete failed")),
                (_, _) => published = true));

        Assert.Equal("delete failed", failure.Message);
        Assert.False(published);
        Assert.True(session.IsUnchanged(request));
    }

    public void Dispose() => _temporaryDirectory.Dispose();

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

    private sealed class GatedLoadArchiveService : IArchiveService
    {
        private readonly GostDocument _document;
        private readonly ManualAsyncGate? _gate;

        internal GatedLoadArchiveService(
            GostDocument document,
            ManualAsyncGate? gate = null)
        {
            _document = document;
            _gate = gate;
        }

        public GostDocument CreateNew() => throw new NotSupportedException();
        public Task<GostDocument> LoadAsync(string filePath) =>
            Task.FromResult(_document);
        public Task<GostDocument> LoadAsync(Stream stream) =>
            Task.FromResult(_document);
        public async Task<GostArchiveLoadResult> LoadWithDiagnosticsAsync(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            if (_gate is not null)
            {
                await _gate.SignalAndWaitAsync(cancellationToken);
            }

            return new GostArchiveLoadResult(
                _document,
                GostArchiveFormat.Current,
                GostArchiveFormat.Current,
                []);
        }

        public Task SaveAsync(GostDocument document, string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SaveAsync(DocumentPersistenceSnapshot snapshot, string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SaveAsync(GostDocument document, Stream stream, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SaveAsync(DocumentPersistenceSnapshot snapshot, Stream stream, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingArchiveService : IArchiveService
    {
        private readonly ManualAsyncGate? _gate;

        internal CapturingArchiveService(ManualAsyncGate? gate = null)
        {
            _gate = gate;
        }

        public int SaveCalls { get; private set; }
        public GostDocument CreateNew() => throw new NotSupportedException();
        public Task<GostDocument> LoadAsync(string filePath) => throw new NotSupportedException();
        public Task<GostDocument> LoadAsync(Stream stream) => throw new NotSupportedException();
        public Task SaveAsync(GostDocument document, string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public async Task SaveAsync(DocumentPersistenceSnapshot snapshot, string filePath, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (_gate is not null)
            {
                await _gate.SignalAndWaitAsync(cancellationToken);
            }
        }
        public Task SaveAsync(GostDocument document, Stream stream, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SaveAsync(DocumentPersistenceSnapshot snapshot, Stream stream, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingExportService : IExportService
    {
        public int ExportCalls { get; private set; }
        public Task ExportToDocxAsync(GostDocument document, string outputPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ExportToDocxAsync(DocumentPersistenceSnapshot snapshot, string outputPath, CancellationToken cancellationToken = default)
        {
            ExportCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class GatedRecoveryArchiveService : IArchiveService
    {
        private readonly ManualAsyncGate _gate;

        internal GatedRecoveryArchiveService(ManualAsyncGate gate)
        {
            _gate = gate;
        }

        public GostDocument CreateNew() => throw new NotSupportedException();
        public Task<GostDocument> LoadAsync(string filePath) => throw new NotSupportedException();
        public Task<GostDocument> LoadAsync(Stream stream) => throw new NotSupportedException();
        public async Task SaveAsync(
            GostDocument document,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            await _gate.SignalAndWaitAsync(cancellationToken);
            await File.WriteAllBytesAsync(
                filePath,
                "recovery"u8.ToArray(),
                cancellationToken);
        }
        public Task SaveAsync(DocumentPersistenceSnapshot snapshot, string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SaveAsync(GostDocument document, Stream stream, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SaveAsync(DocumentPersistenceSnapshot snapshot, Stream stream, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class GateOnCaptureFingerprintService
        : FileContentFingerprintService
    {
        private readonly ManualAsyncGate _gate;
        private readonly int _gateOnCapture;
        private int _captures;

        internal GateOnCaptureFingerprintService(
            ManualAsyncGate gate,
            int gateOnCapture)
        {
            _gate = gate;
            _gateOnCapture = gateOnCapture;
        }

        public override async Task<FileContentFingerprint?> TryCaptureAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _captures) == _gateOnCapture)
            {
                await _gate.SignalAndWaitAsync(cancellationToken);
            }

            return await base.TryCaptureAsync(filePath, cancellationToken);
        }
    }
}
