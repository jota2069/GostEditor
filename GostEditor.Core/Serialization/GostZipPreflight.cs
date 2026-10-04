using System.Buffers.Binary;

namespace GostEditor.Core.Serialization;

internal static class GostZipPreflight
{
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint CentralDirectoryHeaderSignature = 0x02014B50;
    private const int EndOfCentralDirectorySize = 22;
    private const int MaximumCommentLength = ushort.MaxValue;
    private const int CentralDirectoryHeaderSize = 46;

    public static void Validate(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new InvalidDataException(
                "Для проверки архива .gost требуется seekable stream.");
        }

        long start = stream.Position;
        long length = stream.Length - start;
        if (length < EndOfCentralDirectorySize ||
            length > GostArchiveLimits.MaxArchiveBytes)
        {
            throw new InvalidDataException(
                "Размер или структура архива .gost недопустимы.");
        }

        int tailLength = (int)Math.Min(
            length,
            EndOfCentralDirectorySize + MaximumCommentLength);
        byte[] tail = new byte[tailLength];
        stream.Position = start + length - tailLength;
        stream.ReadExactly(tail);

        int eocdIndex = FindEndOfCentralDirectory(tail);
        ReadOnlySpan<byte> eocd = tail.AsSpan(eocdIndex);
        ushort diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(eocd[4..]);
        ushort centralDisk = BinaryPrimitives.ReadUInt16LittleEndian(eocd[6..]);
        ushort entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(eocd[8..]);
        ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(eocd[10..]);
        uint centralSize = BinaryPrimitives.ReadUInt32LittleEndian(eocd[12..]);
        uint centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(eocd[16..]);

        if (diskNumber != 0 || centralDisk != 0 ||
            entriesOnDisk != totalEntries ||
            totalEntries == ushort.MaxValue ||
            centralSize == uint.MaxValue ||
            centralOffset == uint.MaxValue)
        {
            throw new InvalidDataException(
                "Многотомные и ZIP64 архивы .gost не поддерживаются.");
        }

        if (totalEntries > GostArchiveLimits.MaxEntryCount ||
            centralSize > GostArchiveLimits.MaxCentralDirectoryBytes ||
            centralOffset > length ||
            centralSize > length - centralOffset)
        {
            throw new InvalidDataException(
                "Центральный каталог архива .gost превышает допустимые пределы.");
        }

        ValidateCentralDirectory(
            stream,
            start + centralOffset,
            centralSize,
            totalEntries);
        stream.Position = start;
    }

    private static int FindEndOfCentralDirectory(byte[] tail)
    {
        for (int index = tail.Length - EndOfCentralDirectorySize;
             index >= 0;
             index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    tail.AsSpan(index, 4)) != EndOfCentralDirectorySignature)
            {
                continue;
            }

            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                tail.AsSpan(index + 20, 2));
            if (index + EndOfCentralDirectorySize + commentLength == tail.Length)
            {
                return index;
            }
        }

        throw new InvalidDataException(
            "В архиве .gost не найден корректный центральный каталог.");
    }

    private static void ValidateCentralDirectory(
        Stream stream,
        long offset,
        long declaredSize,
        int declaredCount)
    {
        stream.Position = offset;
        long consumed = 0;
        Span<byte> header = stackalloc byte[CentralDirectoryHeaderSize];
        for (int index = 0; index < declaredCount; index++)
        {
            if (declaredSize - consumed < CentralDirectoryHeaderSize)
            {
                throw new InvalidDataException(
                    "Центральный каталог архива .gost усечён.");
            }

            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) !=
                CentralDirectoryHeaderSignature)
            {
                throw new InvalidDataException(
                    "Центральный каталог архива .gost повреждён.");
            }

            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                header[28..]);
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(
                header[30..]);
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                header[32..]);
            if (nameLength > GostArchiveLimits.MaxEntryNameLength)
            {
                throw new InvalidDataException(
                    "Имя записи архива .gost превышает допустимую длину.");
            }

            long recordSize = CentralDirectoryHeaderSize +
                (long)nameLength + extraLength + commentLength;
            if (recordSize > declaredSize - consumed)
            {
                throw new InvalidDataException(
                    "Центральный каталог архива .gost повреждён.");
            }

            stream.Position += recordSize - CentralDirectoryHeaderSize;
            consumed += recordSize;
        }

        if (consumed != declaredSize)
        {
            throw new InvalidDataException(
                "Количество записей центрального каталога .gost не совпадает " +
                "с его размером.");
        }
    }
}
