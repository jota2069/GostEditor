using Avalonia.Media;
using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Controllers;
using GostEditor.UI.Layout;

namespace GostEditor.Tests.Layout;

[Collection(TestCollections.AvaloniaLayout)]
public sealed class IncrementalPageLayoutTests
{
    [Fact]
    public void SecondBuild_ReusesAllParagraphLayouts()
    {
        TestContext context = CreateContext(8);

        context.Layout.BuildLayout(
            context.Document,
            context.Editor,
            context.Typeface);
        LayoutBuildStatistics first = context.Layout.LastStatistics;

        context.Layout.BuildLayout(
            context.Document,
            context.Editor,
            context.Typeface);
        LayoutBuildStatistics second = context.Layout.LastStatistics;

        Assert.Equal(8, first.CacheMisses);
        Assert.Equal(0, first.CacheHits);
        Assert.Equal(8, second.CacheHits);
        Assert.Equal(0, second.CacheMisses);
    }

    [Fact]
    public void LocalTextMutation_InvalidatesOnlyChangedParagraphAndSuffix()
    {
        TestContext context = CreateContext(10);
        _ = new RenderController(
            context.Editor,
            context.Layout,
            context.Typeface);
        context.Layout.BuildLayout(
            context.Document,
            context.Editor,
            context.Typeface);
        context.Editor.CaretPosition = new DocumentPosition(6, 2);

        context.Editor.InsertText("X");
        context.Layout.BuildLayout(
            context.Document,
            context.Editor,
            context.Typeface);

        LayoutBuildStatistics statistics = context.Layout.LastStatistics;
        Assert.Equal(6, statistics.CacheHits);
        Assert.Equal(4, statistics.CacheMisses);
    }

    [Fact]
    public void CaretOnlyRefresh_DoesNotInvalidateTextLayouts()
    {
        TestContext context = CreateContext(5);
        context.Layout.BuildLayout(
            context.Document,
            context.Editor,
            context.Typeface);

        context.Editor.CaretPosition = new DocumentPosition(4, 1);
        context.Layout.BuildLayout(
            context.Document,
            context.Editor,
            context.Typeface);

        Assert.Equal(5, context.Layout.LastStatistics.CacheHits);
        Assert.Equal(0, context.Layout.LastStatistics.CacheMisses);
    }

    private static TestContext CreateContext(int paragraphCount)
    {
        ImageService imageService = new();
        GostDocument document = new();
        document.Paragraphs.Clear();
        for (int index = 0; index < paragraphCount; index++)
        {
            document.Paragraphs.Add(new Paragraph
            {
                Runs = { new TextRun($"Paragraph {index}") }
            });
        }

        DocumentEditor editor = new(document, imageService);
        PageLayoutManager layout = new(imageService);
        return new TestContext(
            document,
            editor,
            layout,
            new Typeface(FontFamily.Default));
    }

    private sealed record TestContext(
        GostDocument Document,
        DocumentEditor Editor,
        PageLayoutManager Layout,
        Typeface Typeface);
}
