using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Layout.Incremental;

[Flags]
public enum LayoutInvalidationKind
{
    None = 0,
    Paint = 1,
    Metrics = 2,
    Structure = 4,
    All = Paint | Metrics | Structure
}

public readonly record struct LayoutInvalidation(
    int StartBlockIndex,
    LayoutInvalidationKind Kind)
{
    public bool IsEmpty => StartBlockIndex < 0 ||
        Kind == LayoutInvalidationKind.None;

    public static LayoutInvalidation Empty =>
        new(-1, LayoutInvalidationKind.None);
}
