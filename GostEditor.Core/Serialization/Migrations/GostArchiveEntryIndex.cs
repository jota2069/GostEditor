using System.IO.Compression;

namespace GostEditor.Core.Serialization.Migrations;

internal sealed class GostArchiveEntryIndex
{
    public const long MaxManifestBytes = 16L * 1024 * 1024;
    public const long MaxImageBytes = 256L * 1024 * 1024;

    private readonly Dictionary<string, ZipArchiveEntry> _entries;

    public GostArchiveEntryIndex(ZipArchive archive)
    {
        _entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!_entries.TryAdd(entry.FullName, entry))
            {
                throw new InvalidDataException(
                    $"Архив .gost содержит повторяющуюся запись '{entry.FullName}'.");
            }
        }
    }

    public IEnumerable<string> Names => _entries.Keys;

    public bool TryGet(string entryName, out ZipArchiveEntry entry)
    {
        return _entries.TryGetValue(entryName, out entry!);
    }

    public ZipArchiveEntry GetRequired(string entryName, string errorMessage)
    {
        if (!_entries.TryGetValue(entryName, out ZipArchiveEntry? entry))
        {
            throw new InvalidDataException(errorMessage);
        }

        return entry;
    }

    public static async Task<byte[]> ReadBytesAsync(
        ZipArchiveEntry entry,
        long maximumLength,
        CancellationToken cancellationToken = default)
    {
        if (entry.Length > maximumLength)
        {
            throw new InvalidDataException(
                $"Запись '{entry.FullName}' превышает допустимый размер {maximumLength} байт.");
        }

        await using Stream source = entry.Open();
        using MemoryStream destination = entry.Length > 0 && entry.Length <= int.MaxValue
            ? new MemoryStream((int)entry.Length)
            : new MemoryStream();

        await source.CopyToAsync(destination, cancellationToken);

        if (destination.Length > maximumLength)
        {
            throw new InvalidDataException(
                $"Запись '{entry.FullName}' превышает допустимый размер {maximumLength} байт.");
        }

        return destination.ToArray();
    }
}
