using System.Collections.ObjectModel;

namespace GostEditor.Core.DocumentModel;

/// <summary>
/// Bounded, transport-neutral storage for namespaced document extensions.
/// Generic document code owns the bytes and limits but does not interpret
/// extension-specific semantics.
/// </summary>
public sealed class DocumentExtensionCatalog
{
    public const int MaxExtensionCount = 64;
    public const int MaxNamespaceLength = 128;
    public const int MaxMediaTypeLength = 128;
    public const int MaxPartBytes = 16 * 1024 * 1024;
    public const int MaxTotalBytes = 24 * 1024 * 1024;

    private readonly SortedDictionary<string, DocumentExtensionPart> _parts =
        new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, DocumentExtensionPart> Parts =>
        new ReadOnlyDictionary<string, DocumentExtensionPart>(_parts);

    public int TotalBytes { get; private set; }

    public void Set(
        string extensionNamespace,
        int schemaVersion,
        string mediaType,
        ReadOnlyMemory<byte> data)
    {
        ValidateNamespace(extensionNamespace);
        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                "Версия extension должна быть положительной.");
        }

        if (string.IsNullOrWhiteSpace(mediaType) ||
            mediaType.Length > MaxMediaTypeLength)
        {
            throw new ArgumentException(
                "Media type extension отсутствует или превышает лимит.",
                nameof(mediaType));
        }

        if (data.Length > MaxPartBytes)
        {
            throw new ArgumentException(
                $"Extension {extensionNamespace} превышает лимит " +
                $"{MaxPartBytes} байт.",
                nameof(data));
        }

        bool replacing = _parts.TryGetValue(
            extensionNamespace,
            out DocumentExtensionPart? previous);
        if (!replacing && _parts.Count >= MaxExtensionCount)
        {
            throw new InvalidOperationException(
                $"Документ не может содержать более {MaxExtensionCount} extensions.");
        }

        int totalBytes = checked(
            TotalBytes - (previous?.Data.Length ?? 0) + data.Length);
        if (totalBytes > MaxTotalBytes)
        {
            throw new InvalidOperationException(
                $"Суммарный размер extensions превышает лимит " +
                $"{MaxTotalBytes} байт.");
        }

        _parts[extensionNamespace] = new DocumentExtensionPart(
            extensionNamespace,
            schemaVersion,
            mediaType,
            data);
        TotalBytes = totalBytes;
    }

    public bool TryGet(
        string extensionNamespace,
        out DocumentExtensionPart? part) =>
        _parts.TryGetValue(extensionNamespace, out part);

    public bool Remove(string extensionNamespace)
    {
        if (!_parts.Remove(extensionNamespace, out DocumentExtensionPart? part))
        {
            return false;
        }

        TotalBytes -= part.Data.Length;
        return true;
    }

    private static void ValidateNamespace(string extensionNamespace)
    {
        if (string.IsNullOrWhiteSpace(extensionNamespace) ||
            extensionNamespace.Length > MaxNamespaceLength ||
            extensionNamespace[0] == '.' ||
            extensionNamespace[^1] == '.' ||
            !extensionNamespace.Contains('.', StringComparison.Ordinal) ||
            extensionNamespace.Any(character =>
                !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or
                    '.' or '-' or '_')))
        {
            throw new ArgumentException(
                "Extension namespace должен быть bounded lowercase namespace.",
                nameof(extensionNamespace));
        }
    }
}

public sealed class DocumentExtensionPart
{
    private readonly byte[] _data;

    internal DocumentExtensionPart(
        string extensionNamespace,
        int schemaVersion,
        string mediaType,
        ReadOnlyMemory<byte> data)
    {
        Namespace = extensionNamespace;
        SchemaVersion = schemaVersion;
        MediaType = mediaType;
        _data = data.ToArray();
    }

    public string Namespace { get; }

    public int SchemaVersion { get; }

    public string MediaType { get; }

    public ReadOnlyMemory<byte> Data => _data;
}
