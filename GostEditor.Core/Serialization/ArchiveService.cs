using GostEditor.Core.Interfaces;
using GostEditor.Core.IO;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using FileStream fileStream = File.OpenRead(filePath);
        return await LoadAsync(fileStream);
    }

    public async Task<GostDocument> LoadAsync(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        GostArchiveReadResult result = await _reader.ReadAsync(stream);
        return GostDocumentMaterializer.Materialize(result.Document);
    }

    public Task SaveAsync(GostDocument document, string filePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return _fileCommitter.WriteAsync(
            filePath,
            (stream, cancellationToken) =>
                _writer.WriteAsync(document, stream, cancellationToken));
    }

    public Task SaveAsync(GostDocument document, Stream stream)
    {
        return _writer.WriteAsync(document, stream);
    }
}
