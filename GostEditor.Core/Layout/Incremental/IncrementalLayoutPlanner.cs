namespace GostEditor.Core.Layout.Incremental;

public readonly record struct IncrementalLayoutPlan(
    int PreservedBlockCount,
    int StartBlockIndex,
    LayoutInvalidationKind Kind)
{
    public bool RequiresLayout => Kind != LayoutInvalidationKind.None;
}

public static class IncrementalLayoutPlanner
{
    public static IncrementalLayoutPlan CreatePlan(
        int totalBlockCount,
        LayoutInvalidation invalidation)
    {
        if (totalBlockCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBlockCount));
        }

        if (invalidation.IsEmpty)
        {
            return new IncrementalLayoutPlan(
                totalBlockCount,
                totalBlockCount,
                LayoutInvalidationKind.None);
        }

        int start = Math.Clamp(
            invalidation.StartBlockIndex,
            0,
            totalBlockCount);

        return new IncrementalLayoutPlan(
            start,
            start,
            invalidation.Kind);
    }
}
