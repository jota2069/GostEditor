using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Tests.Layout;

public sealed class IncrementalLayoutFoundationTests
{
    [Fact]
    public void Tracker_KeepsEarliestInvalidatedBlock()
    {
        (DocumentRoot document, ParagraphBlock[] blocks) = CreateDocument(5);
        LayoutInvalidationTracker tracker = new(document);

        tracker.Invalidate(blocks[4].Id, LayoutInvalidationKind.Paint);
        tracker.Invalidate(blocks[2].Id, LayoutInvalidationKind.Metrics);

        Assert.True(tracker.HasInvalidation);
        Assert.Equal(2, tracker.Current.StartBlockIndex);
        Assert.True(tracker.Current.Kind.HasFlag(LayoutInvalidationKind.Paint));
        Assert.True(tracker.Current.Kind.HasFlag(LayoutInvalidationKind.Metrics));
    }

    [Fact]
    public void Consume_ReturnsAndClearsInvalidation()
    {
        (DocumentRoot document, ParagraphBlock[] blocks) = CreateDocument(2);
        LayoutInvalidationTracker tracker = new(document);
        tracker.Invalidate(blocks[1].Id, LayoutInvalidationKind.Paint);

        LayoutInvalidation consumed = tracker.Consume();

        Assert.Equal(1, consumed.StartBlockIndex);
        Assert.False(tracker.HasInvalidation);
        Assert.True(tracker.Current.IsEmpty);
    }

    [Fact]
    public void Planner_PreservesBlocksBeforeDirtyRange()
    {
        IncrementalLayoutPlan plan = IncrementalLayoutPlanner.CreatePlan(
            100,
            new LayoutInvalidation(
                37,
                LayoutInvalidationKind.Metrics));

        Assert.True(plan.RequiresLayout);
        Assert.Equal(37, plan.PreservedBlockCount);
        Assert.Equal(37, plan.StartBlockIndex);
    }

    [Fact]
    public void Planner_WithNoInvalidationPreservesEverything()
    {
        IncrementalLayoutPlan plan = IncrementalLayoutPlanner.CreatePlan(
            100,
            LayoutInvalidation.Empty);

        Assert.False(plan.RequiresLayout);
        Assert.Equal(100, plan.PreservedBlockCount);
        Assert.Equal(100, plan.StartBlockIndex);
    }

    [Fact]
    public void Cache_UsesContentVersionAndAvailableWidth()
    {
        DocumentNodeId id = DocumentNodeId.New();
        BlockLayoutCache<string> cache = new();
        cache.Store(id, contentVersion: 2, availableWidth: 500, "layout");

        Assert.True(cache.TryGet(id, 2, 500, out string? layout));
        Assert.Equal("layout", layout);
        Assert.False(cache.TryGet(id, 3, 500, out _));
        Assert.False(cache.TryGet(id, 2, 400, out _));
    }

    [Fact]
    public void Cache_RetainRemovesOrphanedBlocks()
    {
        DocumentNodeId live = DocumentNodeId.New();
        DocumentNodeId removed = DocumentNodeId.New();
        BlockLayoutCache<string> cache = new();
        cache.Store(live, 1, 500, "live");
        cache.Store(removed, 1, 500, "removed");

        cache.Retain(new HashSet<DocumentNodeId> { live });

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet(live, 1, 500, out _));
        Assert.False(cache.TryGet(removed, 1, 500, out _));
    }

    private static (DocumentRoot, ParagraphBlock[]) CreateDocument(int count)
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        ParagraphBlock[] blocks = Enumerable.Range(0, count)
            .Select(index =>
            {
                ParagraphBlock paragraph = new();
                paragraph.Inlines.Add(new TextInline(index.ToString()));
                section.Blocks.Add(paragraph);
                return paragraph;
            })
            .ToArray();
        document.Sections.Add(section);
        return (document, blocks);
    }
}
