using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Controllers;
using GostEditor.UI.Layout;
using GostEditor.UI.Views;

namespace GostEditor.Tests.Layout;

[Collection(TestCollections.AvaloniaLayout)]
public sealed class EditorFocusHitTestingTests
{
    [Fact]
    public void LoadDocument_RequestsInputFocusAndKeepsCaretValid()
    {
        DocumentEngineView view = new();
        view.ConfigureImageService(new ImageService());
        GostDocument document = CreateDocument("Открытый документ");

        view.LoadDocument(document);

        Assert.True(view.Focusable);
        Assert.True(view.IsInputFocusRequested);
        Assert.Equal(0, view.CurrentCaretPosition.ParagraphIndex);
        Assert.InRange(
            view.CurrentCaretPosition.Offset,
            0,
            document.Paragraphs[0].GetPlainText().Length);
    }

    [Fact]
    public void HitTesting_UsesLeadingAndTrailingCharacterEdges()
    {
        TestContext context = CreateContext("аб");
        RenderedPage page = Assert.Single(context.BuildLayout());
        TextLinePlacement line = Assert.Single(page.Lines);
        double firstX = GetCaretX(line, 0);
        double secondX = GetCaretX(line, 1);
        double characterWidth = secondX - firstX;
        double y = line.Location.Y + line.Line.Height / 2;

        DocumentPosition leading = GetTextPosition(
            context,
            page,
            new Point(firstX + characterWidth * 0.25, y));
        DocumentPosition trailing = GetTextPosition(
            context,
            page,
            new Point(firstX + characterWidth * 0.75, y));

        Assert.Equal(0, leading.Offset);
        Assert.Equal(1, trailing.Offset);
    }

    [Fact]
    public void HitTesting_BeforeAndAfterLine_ClampsToParagraphBounds()
    {
        TestContext context = CreateContext("текст");
        RenderedPage page = Assert.Single(context.BuildLayout());
        TextLinePlacement line = Assert.Single(page.Lines);
        double y = line.Location.Y + line.Line.Height / 2;

        DocumentPosition before = GetTextPosition(
            context,
            page,
            new Point(line.Location.X - 500, y));
        DocumentPosition after = GetTextPosition(
            context,
            page,
            new Point(line.Location.X + line.ParentLayout.MaxWidth + 500, y));

        Assert.Equal(new DocumentPosition(0, 0), before);
        Assert.Equal(new DocumentPosition(0, 5), after);
    }

    [Fact]
    public void HitTesting_EmptyParagraph_NeverReturnsPlaceholderOffset()
    {
        TestContext context = CreateContext(string.Empty);
        RenderedPage page = Assert.Single(context.BuildLayout());
        TextLinePlacement line = Assert.Single(page.Lines);

        DocumentPosition position = GetTextPosition(
            context,
            page,
            new Point(
                line.Location.X + line.ParentLayout.MaxWidth,
                line.Location.Y + line.Line.Height / 2));

        Assert.Equal(new DocumentPosition(0, 0), position);
    }

    [Fact]
    public void HitTesting_WrappedLineAndDocumentEdges_SelectExpectedLine()
    {
        const string text = "Один два три четыре пять шесть семь восемь девять десять";
        TestContext context = CreateContext(text, pageWidth: 210);
        RenderedPage page = Assert.Single(context.BuildLayout());
        Assert.True(page.Lines.Count > 1);
        TextLinePlacement first = page.Lines[0];
        TextLinePlacement last = page.Lines[^1];

        DocumentPosition beforeDocument = GetTextPosition(
            context,
            page,
            new Point(first.Location.X - 100, first.Location.Y - 100));
        DocumentPosition afterDocument = GetTextPosition(
            context,
            page,
            new Point(
                last.Location.X + last.ParentLayout.MaxWidth + 100,
                last.Location.Y + last.Line.Height + 100));
        DocumentPosition firstLineEnd = GetTextPosition(
            context,
            page,
            new Point(
                first.Location.X + first.ParentLayout.MaxWidth + 100,
                first.Location.Y + first.Line.Height / 2));

        Assert.Equal(0, beforeDocument.Offset);
        Assert.InRange(firstLineEnd.Offset, 1, text.Length - 1);
        Assert.Equal(text.Length, afterDocument.Offset);
    }

    [Fact]
    public void HitTesting_UsesLogicalPageCoordinatesAtZoomedScale()
    {
        TestContext context = CreateContext("масштаб");
        RenderedPage page = Assert.Single(context.BuildLayout());
        TextLinePlacement line = Assert.Single(page.Lines);
        Point logicalPoint = GetPointAtOffset(line, 4);
        const double zoom = 1.75;
        Border pageControl = new()
        {
            Width = context.Document.PageWidth,
            Height = context.Document.PageHeight
        };
        LayoutTransformControl zoomContainer = new()
        {
            LayoutTransform = new ScaleTransform(zoom, zoom),
            Child = pageControl
        };
        zoomContainer.Measure(Size.Infinity);
        zoomContainer.Arrange(new Rect(zoomContainer.DesiredSize));
        Point viewportPoint = Assert.IsType<Point>(
            pageControl.TranslatePoint(logicalPoint, zoomContainer));
        Point pagePoint = Assert.IsType<Point>(
            zoomContainer.TranslatePoint(viewportPoint, pageControl));

        Assert.NotEqual(logicalPoint, viewportPoint);
        Assert.InRange(Math.Abs(logicalPoint.X - pagePoint.X), 0, 0.0001);
        Assert.InRange(Math.Abs(logicalPoint.Y - pagePoint.Y), 0, 0.0001);

        DocumentPosition position = GetTextPosition(
            context,
            page,
            pagePoint);

        Assert.Equal(new DocumentPosition(0, 4), position);
    }

    [Fact]
    public void HitTesting_AcrossPageBoundary_UsesLinesOnClickedPage()
    {
        TestContext context = CreateContext("Первая страница");
        context.Document.Paragraphs.Add(new Paragraph
        {
            PageBreakBefore = true,
            Runs = { new TextRun("Вторая страница") }
        });
        List<RenderedPage> pages = context.BuildLayout();
        Assert.Equal(2, pages.Count);
        TextLinePlacement secondPageLine = Assert.Single(pages[1].Lines);

        DocumentPosition position = GetTextPosition(
            context,
            pages[1],
            new Point(
                secondPageLine.Location.X - 100,
                secondPageLine.Location.Y - 100));

        Assert.Equal(new DocumentPosition(1, 0), position);
    }

    [Fact]
    public void MouseHit_CanPlaceCaretInsertTextAndExtendSelection()
    {
        TestContext context = CreateContext("abcdef");
        StackPanel pagesPanel = new();
        RenderController render = new(
            context.Editor,
            context.Layout,
            context.Typeface);
        render.AttachUi(pagesPanel);
        MouseController mouse = new(
            context.Editor,
            render);
        RenderedPage page = Assert.Single(render.CurrentPages);
        TextLinePlacement line = Assert.Single(page.Lines);

        Point offsetTwo = GetPointAtOffset(line, 2);
        Assert.Equal(2, GetTextPosition(context, page, offsetTwo).Offset);
        mouse.HandlePointerPressed(0, offsetTwo, isShift: false);
        context.Editor.InsertText("X");
        Assert.Equal("abXcdef", context.Document.Paragraphs[0].GetPlainText());

        render.RefreshView();
        line = Assert.Single(render.CurrentPages[0].Lines);
        mouse.HandlePointerPressed(0, GetPointAtOffset(line, 1), isShift: false);
        mouse.HandlePointerPressed(0, GetPointAtOffset(line, 5), isShift: true);

        Assert.True(context.Editor.HasSelection);
        Assert.Equal(new DocumentPosition(0, 1), context.Editor.SelectionAnchor);
        Assert.Equal(new DocumentPosition(0, 5), context.Editor.CaretPosition);
    }

    private static TestContext CreateContext(
        string text,
        double pageWidth = 793)
    {
        ImageService imageService = new();
        GostDocument document = CreateDocument(text);
        document.PageWidth = pageWidth;
        document.MarginLeft = 40;
        document.MarginRight = 40;
        DocumentEditor editor = new(document, imageService);
        return new TestContext(
            document,
            editor,
            new PageLayoutManager(imageService),
            new Typeface(FontFamily.Default));
    }

    private static GostDocument CreateDocument(string text)
    {
        GostDocument document = new();
        document.Paragraphs.Clear();
        document.Paragraphs.Add(new Paragraph
        {
            FirstLineIndent = 0,
            Runs = { new TextRun(text) }
        });
        return document;
    }

    private static DocumentPosition GetTextPosition(
        TestContext context,
        RenderedPage page,
        Point point)
    {
        DocumentHitResult hit = Assert.IsType<DocumentHitResult>(
            context.Layout.GetPositionFromPoint(page, point));
        Assert.False(hit.IsImageHit);
        return Assert.IsType<DocumentPosition>(hit.TextPosition);
    }

    private static Point GetPointAtOffset(
        TextLinePlacement line,
        int offset) => new(
            GetCaretX(line, offset),
            line.Location.Y + line.Line.Height / 2);

    private static double GetCaretX(
        TextLinePlacement line,
        int offset) =>
        line.Location.X + line.ParentLayout
            .HitTestTextPosition(offset + line.PrefixLength)
            .X;

    private sealed record TestContext(
        GostDocument Document,
        DocumentEditor Editor,
        PageLayoutManager Layout,
        Typeface Typeface)
    {
        public List<RenderedPage> BuildLayout() =>
            Layout.BuildLayout(Document, Editor, Typeface);
    }
}
