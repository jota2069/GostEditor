using System.IO.Compression;

namespace GostEditor.Core.Serialization.Migrations;

internal sealed class GostArchiveEntryIndex
{
    private readonly Dictionary<string, ZipArchiveEntry> _entries;

    public GostArchiveEntryIndex(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Entries.Count > GostArchiveLimits.MaxEntryCount)
        {
            throw new InvalidDataException(
                $"Архив .gost содержит слишком много записей: " +
                $"{archive.Entries.Count}.");
        }

        _entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long totalUncompressedLength = 0;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName.Length > GostArchiveLimits.MaxEntryNameLength)
            {
                throw new InvalidDataException(
                    "Имя записи архива .gost превышает допустимую длину.");
            }

            if (entry.Length > GostArchiveLimits.MaxUncompressedArchiveBytes -
                totalUncompressedLength)
            {
                throw new InvalidDataException(
                    "Суммарный распакованный размер архива .gost превышает " +
                    $"{GostArchiveLimits.MaxUncompressedArchiveBytes} байт.");
            }

            totalUncompressedLength += entry.Length;
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
            ? new MemoryStream((int)Math.Min(entry.Length, maximumLength))
            : new MemoryStream();

        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length > maximumLength - read)
            {
                throw new InvalidDataException(
                    $"Запись '{entry.FullName}' превышает допустимый размер {maximumLength} байт.");
            }

            await destination.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }

        return destination.ToArray();
    }
}
