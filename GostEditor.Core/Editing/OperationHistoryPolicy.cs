namespace GostEditor.Core.Editing;

public sealed class OperationHistoryPolicy
{
    public const long DefaultMaxRetainedBytes = 16 * 1024 * 1024;

    public long MaxRetainedBytes { get; init; } = DefaultMaxRetainedBytes;

    public int MaxUndoGroups { get; init; } = 10_000;

    public TimeSpan CoalescingWindow { get; init; } =
        TimeSpan.FromMilliseconds(750);

    internal void Validate()
    {
        if (MaxRetainedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetainedBytes));
        }

        if (MaxUndoGroups <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxUndoGroups));
        }

        if (CoalescingWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(CoalescingWindow));
        }
    }
}
