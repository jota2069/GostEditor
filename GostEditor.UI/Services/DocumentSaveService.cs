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
    private readonly FileContentFingerprintService _fingerprintService;

    public DocumentSaveService(
        IArchiveService archiveService,
        DocumentSessionState session,
        PersistenceIoCoordinator ioCoordinator)
        : this(
            archiveService,
            session,
            ioCoordinator,
            TimeProvider.System,
            new FileContentFingerprintService())
    {
    }

    internal DocumentSaveService(
        IArchiveService archiveService,
        DocumentSessionState session,
        PersistenceIoCoordinator ioCoordinator,
        TimeProvider timeProvider,
        FileContentFingerprintService? fingerprintService = null)
    {
        _archiveService = archiveService
            ?? throw new ArgumentNullException(nameof(archiveService));
        _session = session
            ?? throw new ArgumentNullException(nameof(session));
        _ioCoordinator = ioCoordinator
            ?? throw new ArgumentNullException(nameof(ioCoordinator));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _fingerprintService = fingerprintService
            ?? new FileContentFingerprintService();
    }

    public async Task<DocumentSaveResult> SaveAsync(
        GostDocument document,
        string filePath,
        CancellationToken cancellationToken = default,
        bool overwriteExternalChanges = false)
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

        FileContentFingerprint? expectedFingerprint =
            _session.FileFingerprint;
        bool verifyExternalVersion =
            operation == PersistenceIoOperation.ManualSave &&
            !overwriteExternalChanges;
        if (verifyExternalVersion)
        {
            if (!_session.HasFileFingerprintBaseline)
            {
                FileContentFingerprint? unknownBaselineFile;
                try
                {
                    unknownBaselineFile =
                        await _fingerprintService.TryCaptureAsync(
                            filePath,
                            operationCancellation);
                }
                catch (IOException exception)
                {
                    throw new ExternalFileChangedException(
                        filePath,
                        exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    throw new ExternalFileChangedException(
                        filePath,
                        exception);
                }
                if (unknownBaselineFile is not null)
                {
                    throw new ExternalFileChangedException(filePath);
                }
            }
            else
            {
                await EnsureExternalVersionUnchangedAsync(
                    filePath,
                    expectedFingerprint,
                    operationCancellation);
            }
        }

        DateTimeOffset savedAt = _timeProvider.GetUtcNow();
        DocumentPersistenceSnapshot snapshot =
            DocumentPersistenceSnapshot.Capture(
                document,
                _session.ChangeVersion,
                savedAt.UtcDateTime);

        FileContentFingerprint? committedFingerprint;
        if (_archiveService is ArchiveService archiveService)
        {
            ArchiveFileFingerprint written =
                await archiveService.SaveWithFingerprintAsync(
                snapshot,
                filePath,
                verifyExternalVersion
                    ? token => EnsureExternalVersionUnchangedAsync(
                        filePath,
                        expectedFingerprint,
                        token)
                    : null,
                operationCancellation);
            committedFingerprint = new FileContentFingerprint(
                written.Length,
                written.Sha256);
        }
        else
        {
            await _archiveService.SaveAsync(
                snapshot,
                filePath,
                operationCancellation);

            try
            {
                committedFingerprint =
                    await _fingerprintService.TryCaptureAsync(
                        filePath,
                        CancellationToken.None);
            }
            catch (IOException)
            {
                committedFingerprint = null;
            }
            catch (UnauthorizedAccessException)
            {
                committedFingerprint = null;
            }
        }

        _session.MarkSaved(
            filePath,
            savedAt,
            snapshot.Revision,
            committedFingerprint);
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

    private async Task EnsureExternalVersionUnchangedAsync(
        string filePath,
        FileContentFingerprint? expected,
        CancellationToken cancellationToken)
    {
        FileContentFingerprint? actual;
        try
        {
            actual = await _fingerprintService.TryCaptureAsync(
                filePath,
                cancellationToken);
        }
        catch (IOException exception)
        {
            throw new ExternalFileChangedException(filePath, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ExternalFileChangedException(filePath, exception);
        }

        if (actual != expected)
        {
            throw new ExternalFileChangedException(filePath);
        }
    }
}

public sealed record DocumentSaveResult(
    long SavedRevision,
    bool IsCurrentRevision,
    DateTimeOffset SavedAt);
