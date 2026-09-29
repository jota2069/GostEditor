using System;
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
    private readonly TimeProvider _timeProvider;

    public DocumentSaveService(
        IArchiveService archiveService,
        DocumentSessionState session)
        : this(archiveService, session, TimeProvider.System)
    {
    }

    internal DocumentSaveService(
        IArchiveService archiveService,
        DocumentSessionState session,
        TimeProvider timeProvider)
    {
        _archiveService = archiveService
            ?? throw new ArgumentNullException(nameof(archiveService));
        _session = session
            ?? throw new ArgumentNullException(nameof(session));
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

        DateTimeOffset savedAt = _timeProvider.GetUtcNow();
        DocumentPersistenceSnapshot snapshot =
            DocumentPersistenceSnapshot.Capture(
                document,
                _session.ChangeVersion,
                savedAt.UtcDateTime);

        await _archiveService.SaveAsync(
            snapshot,
            filePath,
            cancellationToken);

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
}

public sealed record DocumentSaveResult(
    long SavedRevision,
    bool IsCurrentRevision,
    DateTimeOffset SavedAt);
