using GostEditor.Core.Interfaces;
using GostEditor.Core.IO;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Core.Serialization.Format;
using System.Security.Cryptography;

namespace GostEditor.Core.Serialization;

/// <summary>
/// Loads and saves versioned .gost ZIP packages.
/// </summary>
public class ArchiveService : IArchiveService
{
    private readonly GostArchivePackageReader _reader;
    private readonly GostArchivePackageWriter _writer;
    private readonly IAtomicFileCommitter _fileCommitter;

    public ArchiveService()
        : this(
            new GostArchivePackageReader(),
            new GostArchivePackageWriter(),
            new AtomicFileCommitter())
    {
    }

    internal ArchiveService(
        GostArchivePackageReader reader,
        GostArchivePackageWriter writer,
        IAtomicFileCommitter fileCommitter)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _fileCommitter = fileCommitter
            ?? throw new ArgumentNullException(nameof(fileCommitter));
    }

    public GostDocument CreateNew()
    {
        GostDocument document = new();
        Paragraph welcomeParagraph = new();
        welcomeParagraph.Runs.Add(new TextRun
        {
            Text = "Добро пожаловать в GostEditor!",
            FontSize = 14,
            IsBold = false,
            IsItalic = false
        });
        document.Paragraphs.Add(welcomeParagraph);
        return document;
    }

    public async Task<GostDocument> LoadAsync(string filePath)
    {
        GostArchiveLoadResult result = await LoadWithDiagnosticsAsync(filePath);
        return result.Document;
    }

    public async Task<GostArchiveLoadResult> LoadWithDiagnosticsAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using FileStream fileStream = File.OpenRead(filePath);
        return await LoadWithDiagnosticsAsync(fileStream, cancellationToken);
    }

    public async Task<GostDocument> LoadAsync(Stream stream)
    {
        GostArchiveLoadResult result = await LoadWithDiagnosticsAsync(stream);
        return result.Document;
    }

    public async Task<GostArchiveLoadResult> LoadWithDiagnosticsAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        GostArchiveReadResult result = await _reader.ReadAsync(
            stream,
            cancellationToken);
        return new GostArchiveLoadResult(
            GostDocumentMaterializer.Materialize(result.Document),
            result.SourceVersion,
            GostFormatVersions.Current,
            result.Diagnostics);
    }

    public Task SaveAsync(
        GostDocument document,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        DocumentPersistenceSnapshot snapshot =
            DocumentPersistenceSnapshot.Capture(
                document,
                revision: 0,
                document.ModifiedAt);

        return SaveAsync(snapshot, filePath, cancellationToken);
    }

    public Task SaveAsync(
        DocumentPersistenceSnapshot snapshot,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return _fileCommitter.WriteAsync(
            filePath,
            (stream, writerCancellationToken) =>
                _writer.WriteAsync(
                    snapshot.Document,
                    stream,
                    writerCancellationToken),
            cancellationToken);
    }

    public async Task<ArchiveFileFingerprint> SaveWithFingerprintAsync(
        DocumentPersistenceSnapshot snapshot,
        string filePath,
        Func<CancellationToken, Task>? beforeCommitAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using MemoryStream package = new();
        await _writer.WriteAsync(
            snapshot.Document,
            package,
            cancellationToken);

        if (!package.TryGetBuffer(out ArraySegment<byte> buffer))
        {
            throw new InvalidOperationException(
                "Не удалось получить подготовленный пакет .gost.");
        }

        int length = checked((int)package.Length);
        string sha256 = Convert.ToHexString(
            SHA256.HashData(buffer.AsSpan(0, length)));
        package.Position = 0;

        await _fileCommitter.WriteAsync(
            filePath,
            async (destination, token) =>
            {
                package.Position = 0;
                await package.CopyToAsync(destination, token);
            },
            beforeCommitAsync ?? (static _ => Task.CompletedTask),
            cancellationToken);

        return new ArchiveFileFingerprint(length, sha256);
    }

    public Task SaveAsync(
        DocumentPersistenceSnapshot snapshot,
        string filePath,
        Func<CancellationToken, Task> beforeCommitAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(beforeCommitAsync);

        return _fileCommitter.WriteAsync(
            filePath,
            (stream, writerCancellationToken) =>
                _writer.WriteAsync(
                    snapshot.Document,
                    stream,
                    writerCancellationToken),
            beforeCommitAsync,
            cancellationToken);
    }

    public Task SaveAsync(
        GostDocument document,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        DocumentPersistenceSnapshot snapshot =
            DocumentPersistenceSnapshot.Capture(
                document,
                revision: 0,
                document.ModifiedAt);

        return SaveAsync(snapshot, stream, cancellationToken);
    }

    public Task SaveAsync(
        DocumentPersistenceSnapshot snapshot,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(stream);

        return _writer.WriteAsync(
            snapshot.Document,
            stream,
            cancellationToken);
    }
}

public sealed record ArchiveFileFingerprint(
    long Length,
    string Sha256);
