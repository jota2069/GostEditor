using System;

namespace GostEditor.UI.Layout;

public sealed record LayoutBuildStatistics(
    int ParagraphCount,
    int CacheHits,
    int CacheMisses,
    int PageCount,
    TimeSpan Elapsed)
{
    public static LayoutBuildStatistics Empty { get; } =
        new(0, 0, 0, 0, TimeSpan.Zero);
}
