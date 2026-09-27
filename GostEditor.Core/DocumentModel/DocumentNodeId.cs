using System;

namespace GostEditor.Core.DocumentModel;

/// <summary>
/// Stable in-memory identifier of a document node.
/// The current .gost v2 format does not persist node identifiers; persistence
/// is intentionally deferred until a future format version is specified.
/// </summary>
public readonly record struct DocumentNodeId(Guid Value)
{
    public static DocumentNodeId Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public static DocumentNodeId New() => new(Guid.NewGuid());

    public static DocumentNodeId Parse(string value) =>
        new(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}
