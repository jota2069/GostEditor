using System;
using System.Threading;
using System.Threading.Tasks;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;

namespace GostEditor.UI.Services;

public sealed class DocumentExportService
{
    private readonly IExportService _exportService;
    private readonly DocumentSessionState _session;
    private readonly PersistenceIoCoordinator _ioCoordinator;

    public DocumentExportService(
        IExportService exportService,
        DocumentSessionState session,
        PersistenceIoCoordinator ioCoordinator)
    {
        _exportService = exportService
            ?? throw new ArgumentNullException(nameof(exportService));
        _session = session
            ?? throw new ArgumentNullException(nameof(session));
        _ioCoordinator = ioCoordinator
            ?? throw new ArgumentNullException(nameof(ioCoordinator));
    }

    public async Task<DocumentExportResult> ExportToDocxAsync(
        GostDocument document,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        using PersistenceIoCoordinator.PersistenceIoLease ownership =
            await _ioCoordinator.AcquireAsync(
                PersistenceIoOperation.Export,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        long revision = _session.ChangeVersion;
        DocumentPersistenceSnapshot snapshot =
            DocumentPersistenceSnapshot.Capture(
                document,
                revision,
                document.ModifiedAt);

        await _exportService.ExportToDocxAsync(
            snapshot,
            outputPath,
            cancellationToken);

        return new DocumentExportResult(revision);
    }
}

public sealed record DocumentExportResult(long ExportedRevision);
