using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Tests.Infrastructure;
using GostEditor.UI.Services;

namespace GostEditor.Tests.Services;

public class DocumentSaveServiceTests
{
    private static readonly DateTimeOffset SavedAt = new(
        2026, 8, 5, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task SaveCurrentRevision_CommitsSnapshotAndMarksSessionClean()
    {
        DocumentSessionState session = CreateDirtySession();
        GostDocument document = CreateDocument("current");
        CapturingArchiveService archive = new();
        DocumentSaveService service = CreateService(archive, session);
        string path = Path.Combine(Path.GetTempPath(), "current.gost");

        DocumentSaveResult result = await service.SaveAsync(document, path);

        Assert.False(session.IsDirty);
        Assert.Equal(session.ChangeVersion, session.SavedRevision);
        Assert.Equal(session.ChangeVersion, result.SavedRevision);
        Assert.True(result.IsCurrentRevision);
        Assert.Equal(SavedAt.UtcDateTime, document.ModifiedAt);
        Assert.Equal(Path.GetFullPath(path), session.CurrentFilePath);
        Assert.Equal(SavedAt, session.LastSavedAt);
    }

    [Fact]
    public async Task EditDuringBlockedSave_PersistsCapturedRevisionAndRemainsDirty()
    {
        DocumentSessionState session = new();
        session.MarkOpened(Path.Combine(Path.GetTempPath(), "old.gost"));
        session.RecordMutation();
        long capturedRevision = session.ChangeVersion;
        DateTime originalModifiedAt = new(
            2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        GostDocument document = CreateDocument("before snapshot", originalModifiedAt);
        ManualAsyncGate gate = new();
        CapturingArchiveService archive = new(gate);
        DocumentSaveService service = CreateService(archive, session);
        string path = Path.Combine(Path.GetTempPath(), "stale-save-as.gost");

        Task<DocumentSaveResult> save = service.SaveAsync(document, path);
        await gate.WaitUntilReachedAsync();

        document.Paragraphs[0].Runs[0].Text = "after snapshot";
        session.RecordMutation();
        gate.Release();
        DocumentSaveResult result = await save;
        GostDocument persisted = await archive.LoadSavedDocumentAsync();

        Assert.Equal(capturedRevision, result.SavedRevision);
        Assert.False(result.IsCurrentRevision);
        Assert.Equal(capturedRevision + 1, session.ChangeVersion);
        Assert.Equal(capturedRevision, session.SavedRevision);
        Assert.True(session.IsDirty);
        Assert.Equal(Path.GetFullPath(path), session.CurrentFilePath);
        Assert.Equal("before snapshot", persisted.Paragraphs[0].Runs[0].Text);
        Assert.Equal(SavedAt.UtcDateTime, persisted.ModifiedAt);
        Assert.Equal(originalModifiedAt, document.ModifiedAt);
    }

    [Fact]
    public async Task FailedSave_DoesNotMoveSavepointOrModifyLiveSaveMetadata()
    {
        DocumentSessionState session = CreateDirtySession();
        long savedRevision = session.SavedRevision;
        DateTime modifiedAt = new(
            2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        GostDocument document = CreateDocument("failed", modifiedAt);
        CapturingArchiveService archive = new(
            failure: new IOException("commit failed"));
        DocumentSaveService service = CreateService(archive, session);

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => service.SaveAsync(document, "/tmp/failed.gost"));

        Assert.Equal("commit failed", exception.Message);
        Assert.True(session.IsDirty);
        Assert.Equal(savedRevision, session.SavedRevision);
        Assert.Null(session.CurrentFilePath);
        Assert.Null(session.LastSavedAt);
        Assert.Equal(modifiedAt, document.ModifiedAt);
    }

    [Fact]
    public async Task CancelledSave_DoesNotMoveSavepointOrModifyLiveSaveMetadata()
    {
        DocumentSessionState session = CreateDirtySession();
        long savedRevision = session.SavedRevision;
        DateTime modifiedAt = new(
            2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        GostDocument document = CreateDocument("cancelled", modifiedAt);
        ManualAsyncGate gate = new();
        CapturingArchiveService archive = new(gate);
        DocumentSaveService service = CreateService(archive, session);
        using CancellationTokenSource cancellation = new();

        Task<DocumentSaveResult> save = service.SaveAsync(
            document,
            "/tmp/cancelled.gost",
            cancellation.Token);
        await gate.WaitUntilReachedAsync();
        cancellation.Cancel();
        gate.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        Assert.True(session.IsDirty);
        Assert.Equal(savedRevision, session.SavedRevision);
        Assert.Null(session.CurrentFilePath);
        Assert.Null(session.LastSavedAt);
        Assert.Equal(modifiedAt, document.ModifiedAt);
    }

    [Fact]
    public async Task SaveAs_UsesSnapshotRevisionForNewPath()
    {
        DocumentSessionState session = new();
        session.MarkOpened(Path.Combine(Path.GetTempPath(), "old.gost"));
        session.RecordMutation();
        CapturingArchiveService archive = new();
        DocumentSaveService service = CreateService(archive, session);
        string newPath = Path.Combine(Path.GetTempPath(), "new.gost");

        DocumentSaveResult result = await service.SaveAsync(
            CreateDocument("save as"),
            newPath);

        Assert.True(result.IsCurrentRevision);
        Assert.False(session.IsDirty);
        Assert.Equal(result.SavedRevision, session.SavedRevision);
        Assert.Equal(Path.GetFullPath(newPath), session.CurrentFilePath);
    }

    private static DocumentSessionState CreateDirtySession()
    {
        DocumentSessionState session = new();
        session.StartNew();
        session.RecordMutation();
        return session;
    }

    private static GostDocument CreateDocument(
        string text,
        DateTime? modifiedAt = null)
    {
        GostDocument document = new()
        {
            ModifiedAt = modifiedAt ?? DateTime.UtcNow
        };
        document.Paragraphs.Add(new Paragraph
        {
            Runs = [new TextRun(text)]
        });
        return document;
    }

    private static DocumentSaveService CreateService(
        IArchiveService archive,
        DocumentSessionState session) =>
        new(
            archive,
            session,
            new PersistenceIoCoordinator(),
            new ManualUtcTimeProvider(SavedAt));

    private sealed class CapturingArchiveService : IArchiveService
    {
        private readonly ManualAsyncGate? _gate;
        private readonly Exception? _failure;
        private byte[]? _savedPackage;

        public CapturingArchiveService(
            ManualAsyncGate? gate = null,
            Exception? failure = null)
        {
            _gate = gate;
            _failure = failure;
        }

        public GostDocument CreateNew() => throw new NotSupportedException();

        public Task<GostDocument> LoadAsync(string filePath) =>
            throw new NotSupportedException();

        public Task<GostDocument> LoadAsync(Stream stream) =>
            new ArchiveService().LoadAsync(stream);

        public Task SaveAsync(
            GostDocument document,
            string filePath,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "DocumentSaveService must save a revision snapshot.");

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
            if (_gate is not null)
            {
                await _gate.SignalAndWaitAsync(cancellationToken);
            }

            if (_failure is not null)
            {
                throw _failure;
            }

            await using MemoryStream stream = new();
            await new ArchiveService().SaveAsync(
                snapshot,
                stream,
                cancellationToken);
            _savedPackage = stream.ToArray();
        }

        public Task SaveAsync(
            DocumentPersistenceSnapshot snapshot,
            Stream stream,
            CancellationToken cancellationToken = default) =>
            new ArchiveService().SaveAsync(
                snapshot,
                stream,
                cancellationToken);

        public async Task<GostDocument> LoadSavedDocumentAsync()
        {
            Assert.NotNull(_savedPackage);
            await using MemoryStream stream = new(_savedPackage);
            return await new ArchiveService().LoadAsync(stream);
        }
    }
}
