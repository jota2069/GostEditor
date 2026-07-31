using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Skia;
using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Layout;

namespace GostEditor.Tests.Layout;

public class ImageLayoutTests
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    static ImageLayoutTests()
    {
        SkiaPlatform.Initialize();
    }

    [Fact]
    public void BuildLayout_ResolvedImage_UsesAttachmentContentAndPlacementBounds()
    {
        TestContext context = CreateContext();
        ImageResult<ImagePlacementInfo> inserted = context.ImageService.InsertPlacement(
            context.Document,
            0,
            new CreateImageRequest(PngBytes, new ImageSize(120, 80), "pixel.png"));

        Assert.True(inserted.IsSuccess, inserted.Error?.Message);

        ImagePlacement placement = Assert.Single(BuildImagePlacements(context));

        Assert.Equal(inserted.Value!.ImageId, placement.ImageId);
        Assert.True(placement.HasContent);
        Assert.Equal(PngBytes, placement.ImageBytes.ToArray());
        Assert.Equal(120, placement.Bounds.Width);
        Assert.Equal(80, placement.Bounds.Height);
        Assert.Equal(0, placement.ParagraphIndex);
    }

    [Fact]
    public void BuildLayout_SharedImageId_ResolvesEveryPlacementFromOneAttachment()
    {
        TestContext context = CreateContext();
        ImageResult<ImagePlacementInfo> first = context.ImageService.InsertPlacement(
            context.Document,
            0,
            new CreateImageRequest(PngBytes, new ImageSize(100, 60), "shared.png"));
        Assert.True(first.IsSuccess, first.Error?.Message);

        ImageResult<ImagePlacementInfo> second = context.ImageService.InsertExistingPlacement(
            context.Document,
            1,
            first.Value!.ImageId,
            new ImagePlacementRequest(new ImageSize(200, 90)));
        Assert.True(second.IsSuccess, second.Error?.Message);

        List<ImagePlacement> placements = BuildImagePlacements(context);

        Assert.Single(context.Document.Images);
        Assert.Equal(2, placements.Count);
        Assert.All(placements, placement =>
        {
            Assert.Equal(first.Value.ImageId, placement.ImageId);
            Assert.True(placement.HasContent);
            Assert.Equal(PngBytes, placement.ImageBytes.ToArray());
        });
        Assert.Equal(
            new[] { 100d, 200d },
            placements.Select(placement => placement.Bounds.Width));
        Assert.Equal(
            new[] { 60d, 90d },
            placements.Select(placement => placement.Bounds.Height));
    }

    [Fact]
    public void BuildLayout_DanglingImage_CreatesPlaceholderWithPlacementBounds()
    {
        TestContext context = CreateContext();
        Guid missingImageId = Guid.NewGuid();
        Paragraph paragraph = CreateDanglingImageParagraph(missingImageId, 140, 75);
        context.Document.Paragraphs.Add(paragraph);

        ImagePlacement placement = Assert.Single(BuildImagePlacements(context));

        Assert.Equal(missingImageId, placement.ImageId);
        Assert.False(placement.HasContent);
        Assert.True(placement.ImageBytes.IsEmpty);
        Assert.Equal(140, placement.Bounds.Width);
        Assert.Equal(75, placement.Bounds.Height);
        Assert.Equal(0, placement.ParagraphIndex);
    }

    [Fact]
    public void GetPositionFromPoint_InsideImageBounds_ReturnsImageHit()
    {
        TestContext context = CreateContext();
        ImageResult<ImagePlacementInfo> inserted = context.ImageService.InsertPlacement(
            context.Document,
            0,
            new CreateImageRequest(PngBytes, new ImageSize(120, 80)));
        Assert.True(inserted.IsSuccess, inserted.Error?.Message);

        RenderedPage page = Assert.Single(context.LayoutManager.BuildLayout(
            context.Document,
            context.Editor,
            new Typeface(FontFamily.Default)));
        ImagePlacement placement = Assert.Single(page.Images);

        DocumentHitResult? hit = context.LayoutManager.GetPositionFromPoint(
            page,
            placement.Bounds.Center);

        Assert.NotNull(hit);
        Assert.True(hit.IsImageHit);
        Assert.Equal(placement.ParagraphIndex, hit.ImageParagraphIndex);
        Assert.Null(hit.TextPosition);
    }

    private static TestContext CreateContext()
    {
        ImageService imageService = new ImageService();
        GostDocument document = new GostDocument();
        DocumentEditor editor = new DocumentEditor(document, imageService);
        document.Paragraphs.Clear();
        PageLayoutManager layoutManager = new PageLayoutManager(imageService);
        return new TestContext(document, editor, imageService, layoutManager);
    }

    private static List<ImagePlacement> BuildImagePlacements(TestContext context)
    {
        return context.LayoutManager
            .BuildLayout(
                context.Document,
                context.Editor,
                new Typeface(FontFamily.Default))
            .SelectMany(page => page.Images)
            .ToList();
    }

    private static Paragraph CreateDanglingImageParagraph(
        Guid imageId,
        double width,
        double height)
    {
        Paragraph paragraph = new Paragraph
        {
            Alignment = GostAlignment.Center,
            FirstLineIndent = 0
        };
        paragraph.Runs.Add(new TextRun(string.Empty));

        SetInternalProperty(paragraph, nameof(Paragraph.ImageId), imageId);
        SetInternalProperty(paragraph, nameof(Paragraph.ImageWidth), width);
        SetInternalProperty(paragraph, nameof(Paragraph.ImageHeight), height);
        return paragraph;
    }

    private static void SetInternalProperty<T>(
        Paragraph paragraph,
        string propertyName,
        T value)
    {
        PropertyInfo property = typeof(Paragraph).GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Свойство {propertyName} не найдено.");
        property.SetValue(paragraph, value);
    }

    private sealed record TestContext(
        GostDocument Document,
        DocumentEditor Editor,
        ImageService ImageService,
        PageLayoutManager LayoutManager);
}
