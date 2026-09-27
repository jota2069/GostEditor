namespace GostEditor.Core.TextEngine;

[Flags]
public enum DocumentChangeKind
{
    None = 0,
    Paint = 1,
    Metrics = 2,
    Structure = 4,
    Resources = 8
}

public sealed class DocumentChangedEventArgs : EventArgs
{
    public DocumentChangedEventArgs(
        int startParagraphIndex,
        DocumentChangeKind kind,
        long version)
    {
        StartParagraphIndex = Math.Max(0, startParagraphIndex);
        Kind = kind;
        Version = version;
    }

    public int StartParagraphIndex { get; }

    public DocumentChangeKind Kind { get; }

    public long Version { get; }
}
