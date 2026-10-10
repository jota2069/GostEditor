using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Controllers;
using GostEditor.UI.Layout;

namespace GostEditor.Tests.Layout;

[Collection(TestCollections.AvaloniaLayout)]
public sealed class UnicodeInputNavigationLayoutTests
{
    public static TheoryData<string> ComplexTextElements => new()
    {
        "😀",
        "e\u0301",
        "и\u0306",
        "👍🏽",
        "👨‍👩‍👧‍👦",
        "❤\uFE0F"
    };

    [Theory]
    [MemberData(nameof(ComplexTextElements))]
    public void MouseHitResult_IsAlwaysTextElementBoundary(
        string textElement)
    {
        TestContext context = CreateContext(textElement);
        RenderedPage page = Assert.Single(context.Render.CurrentPages);
        TextLinePlacement line = Assert.Single(page.Lines);
        double startX = GetCaretX(line, 0);
        double endX = GetCaretX(line, textElement.Length);
        double y = line.Location.Y + line.Line.Height / 2;

        for (int sample = 0; sample <= 20; sample++)
        {
            double x = startX + (endX - startX) * sample / 20;
            DocumentHitResult hit = Assert.IsType<DocumentHitResult>(
                context.Layout.GetPositionFromPoint(page, new Point(x, y)));
            DocumentPosition position = Assert.IsType<DocumentPosition>(
                hit.TextPosition);

            Assert.True(context.Editor.TextBoundaries.IsBoundary(
                textElement,
                position.Offset));
        }
    }

    [Fact]
    public async Task VerticalNavigation_PreservesPreferredXAcrossShortLine()
    {
        TestContext context = CreateContext(
            "abcdefghij",
            "👍🏽",
            "abcdefghij");
        context.Editor.CaretPosition = new DocumentPosition(0, 8);
        context.Render.RefreshView();
        double initialX = GetCaretBounds(context.Render).X;

        await MoveAsync(context, Key.Down);
        Assert.Equal(1, context.Editor.CaretPosition.ParagraphIndex);
        Assert.Equal(4, context.Editor.CaretPosition.Offset);

        await MoveAsync(context, Key.Down);
        Assert.Equal(new DocumentPosition(2, 8), context.Editor.CaretPosition);
        Assert.Equal(initialX, GetCaretBounds(context.Render).X, precision: 4);

        await MoveAsync(context, Key.Up);
        Assert.Equal(new DocumentPosition(1, 4), context.Editor.CaretPosition);
        await MoveAsync(context, Key.Up);
        Assert.Equal(new DocumentPosition(0, 8), context.Editor.CaretPosition);
    }

    [Fact]
    public async Task VerticalNavigation_MovesBetweenWrappedVisualLines()
    {
        const string text =
            "Один два три четыре пять шесть семь восемь девять десять";
        TestContext context = CreateContext(pageWidth: 210, text);
        Assert.True(context.Render.CurrentPages[0].Lines.Count > 1);

        await MoveAsync(context, Key.Down);

        Assert.Equal(0, context.Editor.CaretPosition.ParagraphIndex);
        Assert.InRange(context.Editor.CaretPosition.Offset, 1, text.Length);
        Assert.True(context.Editor.TextBoundaries.IsBoundary(
            text,
            context.Editor.CaretPosition.Offset));

        await MoveAsync(context, Key.Up);
        Assert.Equal(new DocumentPosition(0, 0), context.Editor.CaretPosition);
    }

    [Fact]
    public async Task VerticalNavigation_ExtendsSelectionAndStopsAtDocumentEdges()
    {
        TestContext context = CreateContext("first", "second", "third");

        await MoveAsync(context, Key.Up);
        Assert.Equal(new DocumentPosition(0, 0), context.Editor.CaretPosition);

        await MoveAsync(context, Key.Down, isShift: true);
        Assert.True(context.Editor.HasSelection);
        Assert.Equal(new DocumentPosition(0, 0), context.Editor.SelectionAnchor);
        Assert.Equal(1, context.Editor.CaretPosition.ParagraphIndex);

        await MoveAsync(context, Key.Down, isShift: true);
        DocumentPosition lastPosition = context.Editor.CaretPosition;
        Assert.Equal(2, lastPosition.ParagraphIndex);
        await MoveAsync(context, Key.Down, isShift: true);
        Assert.Equal(lastPosition, context.Editor.CaretPosition);
        Assert.Equal(new DocumentPosition(0, 0), context.Editor.SelectionAnchor);
    }

    [Fact]
    public async Task TextInput_InvalidUtf16PayloadIsIgnored()
    {
        TestContext context = CreateContext("safe");
        context.Editor.CaretPosition = new DocumentPosition(0, 4);

        await context.Input.HandleTextInputAsync("\uD83D", clipboard: null);

        Assert.Equal(
            "safe",
            context.Editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal(new DocumentPosition(0, 4), context.Editor.CaretPosition);
        Assert.False(context.Editor.History.CanUndo);
    }

    private static async Task MoveAsync(
        TestContext context,
        Key key,
        bool isShift = false)
    {
        await context.Input.HandleNavigationKeyAsync(
            key,
            isShift);
        context.Render.RefreshView();
    }

    private static Rect GetCaretBounds(RenderController render) =>
        Assert.Single(
            render.CurrentPages,
            page => page.CaretBounds.HasValue).CaretBounds!.Value;

    private static double GetCaretX(
        TextLinePlacement line,
        int offset) =>
        line.Location.X + line.ParentLayout
            .HitTestTextPosition(offset + line.PrefixLength)
            .X;

    private static TestContext CreateContext(
        params string[] paragraphs) =>
        CreateContext(pageWidth: 793, paragraphs);

    private static TestContext CreateContext(
        double pageWidth,
        params string[] paragraphs)
    {
        ImageService imageService = new();
        GostDocument document = new()
        {
            PageWidth = pageWidth,
            MarginLeft = 40,
            MarginRight = 40
        };
        document.Paragraphs.Clear();
        foreach (string text in paragraphs)
        {
            document.Paragraphs.Add(new Paragraph
            {
                FirstLineIndent = 0,
                Runs = { new TextRun(text) }
            });
        }

        DocumentEditor editor = new(document, imageService);
        PageLayoutManager layout = new(imageService);
        RenderController render = new(
            editor,
            layout,
            new Typeface(FontFamily.Default));
        render.AttachUi(new StackPanel());
        return new TestContext(
            editor,
            layout,
            render,
            new TextInputController(editor, render));
    }

    private sealed record TestContext(
        DocumentEditor Editor,
        PageLayoutManager Layout,
        RenderController Render,
        TextInputController Input);
}
