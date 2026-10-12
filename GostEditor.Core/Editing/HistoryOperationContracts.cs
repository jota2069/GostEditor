using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Editing;

internal enum TextCoalescingKind
{
    None,
    Typing,
    Backspace,
    DeleteForward
}

internal readonly record struct HistoryCoalescingDescriptor(
    TextCoalescingKind Kind,
    DocumentNodeId ParagraphId,
    int Offset,
    int Length,
    int StyleHash)
{
    public bool CanFollow(HistoryCoalescingDescriptor previous) =>
        Kind == previous.Kind &&
        Kind != TextCoalescingKind.None &&
        ParagraphId == previous.ParagraphId &&
        StyleHash == previous.StyleHash &&
        Kind switch
        {
            TextCoalescingKind.Typing =>
                Offset == checked(previous.Offset + previous.Length),
            TextCoalescingKind.Backspace =>
                checked(Offset + Length) == previous.Offset,
            TextCoalescingKind.DeleteForward => Offset == previous.Offset,
            _ => false
        };
}

internal interface IHistoryCoalescingOperation
{
    HistoryCoalescingDescriptor CoalescingDescriptor { get; }
}

internal interface IHistoryPayloadSize
{
    long EstimatedPayloadBytes { get; }
}
