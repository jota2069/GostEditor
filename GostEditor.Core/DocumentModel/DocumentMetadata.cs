using System;
namespace GostEditor.Core.DocumentModel;

public sealed class DocumentMetadata
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

}
