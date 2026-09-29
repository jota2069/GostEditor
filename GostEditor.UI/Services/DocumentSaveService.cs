using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;

namespace GostEditor.UI.Services;

public sealed class DocumentSaveService
{
    private readonly IArchiveService _archiveService;
    private readonly DocumentSessionState _session;
    private readonly PersistenceIoCoordinator _ioCoordinator;
    private readonly TimeProvider _timeProvider;

    public DocumentSaveService(
        IArchiveService archiveService,
        DocumentSessionState session,
        PersistenceIoCoordinator ioCoordinator)
        : this(
            archiveService,
            session,
            ioCoordinator,
            TimeProvider.System)
    {
    }

    internal DocumentSaveService(
        IArchiveService archiveService,
        DocumentSessionState session,
        PersistenceIoCoordinator ioCoordinator,
        TimeProvider timeProvider)
    {
        _archiveService = archiveService
            ?? throw new ArgumentNullException(nameof(archiveService));
        _session = session
            ?? throw new ArgumentNullException(nameof(session));
        _ioCoordinator = ioCoordinator
            ?? throw new ArgumentNullException(nameof(ioCoordinator));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<DocumentSaveResult> SaveAsync(
        GostDocument document,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        PersistenceIoOperation operation = IsSaveAs(filePath)
            ? PersistenceIoOperation.SaveAs
            : PersistenceIoOperation.ManualSave;

        using PersistenceIoCoordinator.PersistenceIoLease ownership =
            await _ioCoordinator.AcquireAsync(operation, cancellationToken);

        CancellationToken operationCancellation =
            ownership.CancellationToken;
        operationCancellation.ThrowIfCancellationRequested();

        DateTimeOffset savedAt = _timeProvider.GetUtcNow();
        DocumentPersistenceSnapshot snapshot =
            DocumentPersistenceSnapshot.Capture(
                document,
                _session.ChangeVersion,
                savedAt.UtcDateTime);

        await _archiveService.SaveAsync(
            snapshot,
            filePath,
            operationCancellation);

        _session.MarkSaved(filePath, savedAt, snapshot.Revision);
        bool isCurrentRevision =
            _session.ChangeVersion == snapshot.Revision;

        if (isCurrentRevision)
        {
            document.ModifiedAt = snapshot.ModifiedAt;
            document.Counters.ImagesCount = document.Paragraphs.Count(
                paragraph => paragraph.ImageId.HasValue);
        }

        return new DocumentSaveResult(
            snapshot.Revision,
            isCurrentRevision,
            savedAt);
    }

    private bool IsSaveAs(string filePath)
    {
        string? currentPath = _session.CurrentFilePath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return true;
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return !string.Equals(
            Path.GetFullPath(currentPath),
            Path.GetFullPath(filePath),
            comparison);
    }
}

public sealed record DocumentSaveResult(
    long SavedRevision,
    bool IsCurrentRevision,
    DateTimeOffset SavedAt);
