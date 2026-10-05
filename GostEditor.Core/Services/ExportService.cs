using GostEditor.Core.Interfaces;
using GostEditor.Core.IO;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;

namespace GostEditor.Core.Services;

public class ExportService : IExportService
{
    private readonly IImageService _imageService;
    private readonly IAtomicFileCommitter _fileCommitter;

    public ExportService()
        : this(new ImageService(), new AtomicFileCommitter())
    {
    }

    public ExportService(IImageService imageService)
        : this(imageService, new AtomicFileCommitter())
    {
    }

    internal ExportService(
        IImageService imageService,
        IAtomicFileCommitter fileCommitter)
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        _fileCommitter = fileCommitter ?? throw new ArgumentNullException(nameof(fileCommitter));
    }

    public Task ExportToDocxAsync(
        GostDocument document,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        DocumentPersistenceSnapshot snapshot =
            DocumentPersistenceSnapshot.Capture(
                document,
                revision: 0,
                document.ModifiedAt);

        return ExportToDocxAsync(snapshot, outputPath, cancellationToken);
    }

    public Task ExportToDocxAsync(
        DocumentPersistenceSnapshot snapshot,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        return Task.Run(
            () => _fileCommitter.WriteAsync(
                outputPath,
                (stream, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    new OpenXmlDocxWriter(_imageService)
                        .Write(snapshot.Document, stream, token);
                    return Task.CompletedTask;
                },
                cancellationToken),
            cancellationToken);
    }
}
