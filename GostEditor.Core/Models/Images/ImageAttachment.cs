using System;

namespace GostEditor.Core.Models;

/// <summary>
/// Immutable binary image resource owned by a <see cref="GostDocument"/>.
/// Placement-specific state is stored on the referencing paragraph.
/// </summary>
public sealed class ImageAttachment
{
    private readonly byte[] _data;

    internal ImageAttachment(
        Guid id,
        string fileName,
        string mediaType,
        ReadOnlyMemory<byte> data,
        string caption = "",
        int order = 0)
    {
        Id = id;
        FileName = fileName ?? string.Empty;
        MediaType = string.IsNullOrWhiteSpace(mediaType)
            ? "application/octet-stream"
            : mediaType;
        _data = data.ToArray();
        Caption = caption ?? string.Empty;
        Order = order;
    }

    public Guid Id { get; }

    public string FileName { get; }

    public string MediaType { get; }

    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>
    /// Compatibility-only metadata imported from v0/v1 root attachments.
    /// Visible figure captions are stored in Paragraph.Runs.
    /// </summary>
    public string Caption { get; }

    /// <summary>
    /// Compatibility-only metadata. Figure order is derived from paragraph order.
    /// </summary>
    public int Order { get; }
}
