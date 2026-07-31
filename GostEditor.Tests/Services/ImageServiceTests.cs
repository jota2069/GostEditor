using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.Services;

public class ImageServiceTests
{
    private readonly ImageService _service = new ImageService();

    [Fact]
    public void InsertPlacement_CreatesAttachmentAndReferenceAndOwnsPayload()
    {
        GostDocument document = CreateDocumentWithTextParagraph();
        byte[] source = TestImageData.CreatePng();
        byte expectedFirstByte = source[0];

        ImageResult<ImagePlacementInfo> result = _service.InsertPlacement(
            document,
            1,
            new CreateImageRequest(
                source,
                new ImageSize(320, 180),
                "/tmp/diagram.png",
                "Диаграмма"));

        AssertSuccess(result);
        ImagePlacementInfo placement = result.Value!;
        Assert.Equal(1, placement.ParagraphIndex);
        Assert.Single(document.Images);
        Assert.Equal(placement.ImageId, document.Images[0].Id);
        Assert.Equal("diagram.png", document.Images[0].FileName);
        Assert.Equal("image/png", document.Images[0].MediaType);
        Assert.Equal(placement.ImageId, document.Paragraphs[1].ImageId);
        Assert.Equal(320, document.Paragraphs[1].ImageWidth);
        Assert.Equal(180, document.Paragraphs[1].ImageHeight);
        Assert.Equal("Диаграмма", document.Paragraphs[1].GetPlainText());
        Assert.Equal(1, document.Counters.ImagesCount);

        source[0] ^= 0xFF;

        Assert.Equal(expectedFirstByte, document.Images[0].Data.Span[0]);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    [InlineData(100, -1)]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.PositiveInfinity)]
    public void InsertPlacement_WithInvalidSize_IsAtomic(
        double width,
        double height)
    {
        GostDocument document = CreateDocumentWithTextParagraph();

        ImageResult<ImagePlacementInfo> result = _service.InsertPlacement(
            document,
            1,
            new CreateImageRequest(
                TestImageData.CreatePng(),
                new ImageSize(width, height)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ImageErrorCode.InvalidDimensions, result.Error?.Code);
        Assert.Single(document.Paragraphs);
        Assert.Empty(document.Images);
    }

    [Fact]
    public void InsertPlacement_WithInvalidPayload_IsAtomic()
    {
        GostDocument document = CreateDocumentWithTextParagraph();

        ImageResult<ImagePlacementInfo> result = _service.InsertPlacement(
            document,
            1,
            new CreateImageRequest(
                new byte[] { 1, 2, 3, 4 },
                new ImageSize(100, 100)));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            ImageErrorCode.UnsupportedOrInvalidImage,
            result.Error?.Code);
        Assert.Single(document.Paragraphs);
        Assert.Empty(document.Images);
    }

    [Fact]
    public void InsertPlacement_WithInvalidIndex_IsAtomic()
    {
        GostDocument document = CreateDocumentWithTextParagraph();

        ImageResult<ImagePlacementInfo> result = _service.InsertPlacement(
            document,
            2,
            new CreateImageRequest(
                TestImageData.CreatePng(),
                new ImageSize(100, 100)));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            ImageErrorCode.ParagraphIndexOutOfRange,
            result.Error?.Code);
        Assert.Single(document.Paragraphs);
        Assert.Empty(document.Images);
    }

    [Fact]
    public void ResolvePlacement_ReturnsCanonicalContent()
    {
        GostDocument document = new GostDocument();
        ImagePlacementInfo placement = Insert(document, 0);

        ImageResult<ResolvedImagePlacement> resolved =
            _service.ResolvePlacement(document, 0);

        AssertSuccess(resolved);
        Assert.Equal(placement.ImageId, resolved.Value!.ImageId);
        Assert.Equal(TestImageData.CreatePng(), resolved.Value.Content.Data.ToArray());
    }

    [Fact]
    public void ReplacePlacementContent_UniqueReferenceUsesCopyOnWrite()
    {
        GostDocument document = new GostDocument();
        ImagePlacementInfo original = Insert(document, 0, caption: "Подпись");
        byte[] replacementBytes = TestImageData.CreatePng();

        ImageResult<ImagePlacementInfo> replaced =
            _service.ReplacePlacementContent(
                document,
                0,
                new ReplaceImageRequest(
                    replacementBytes,
                    new ImageSize(640, 480),
                    "new.png"));

        AssertSuccess(replaced);
        Assert.NotEqual(original.ImageId, replaced.Value!.ImageId);
        Assert.Single(document.Images);
        Assert.Equal(replaced.Value.ImageId, document.Images[0].Id);
        Assert.Equal("new.png", document.Images[0].FileName);
        Assert.Equal("Подпись", document.Paragraphs[0].GetPlainText());
        Assert.Equal(640, document.Paragraphs[0].ImageWidth);
        Assert.Equal(480, document.Paragraphs[0].ImageHeight);

        byte expectedFirstByte = document.Images[0].Data.Span[0];
        replacementBytes[0] ^= 0xFF;
        Assert.Equal(expectedFirstByte, document.Images[0].Data.Span[0]);
    }

    [Fact]
    public void ReplacePlacementContent_SharedReferenceChangesOnlySelectedPlacement()
    {
        GostDocument document = new GostDocument();
        ImagePlacementInfo first = Insert(document, 0);
        ImageResult<ImagePlacementInfo> second = _service.InsertExistingPlacement(
            document,
            1,
            first.ImageId,
            new ImagePlacementRequest(new ImageSize(200, 100), "Вторая"));
        AssertSuccess(second);

        ImageResult<ImagePlacementInfo> replaced =
            _service.ReplacePlacementContent(
                document,
                0,
                new ReplaceImageRequest(
                    TestImageData.CreatePng(),
                    new ImageSize(400, 300),
                    "replacement.png"));

        AssertSuccess(replaced);
        Assert.Equal(2, document.Images.Count);
        Assert.NotEqual(first.ImageId, document.Paragraphs[0].ImageId);
        Assert.Equal(first.ImageId, document.Paragraphs[1].ImageId);
        Assert.Contains(document.Images, image => image.Id == first.ImageId);
    }

    [Fact]
    public void ResizePlacement_ChangesOnlyPlacementSize()
    {
        GostDocument document = new GostDocument();
        ImagePlacementInfo inserted = Insert(document, 0);
        ImageAttachment attachment = document.Images[0];

        ImageResult<ImagePlacementInfo> resized = _service.ResizePlacement(
            document,
            0,
            new ImageSize(777, 333));

        AssertSuccess(resized);
        Assert.Equal(inserted.ImageId, document.Paragraphs[0].ImageId);
        Assert.Same(attachment, document.Images[0]);
        Assert.Equal(777, document.Paragraphs[0].ImageWidth);
        Assert.Equal(333, document.Paragraphs[0].ImageHeight);
    }

    [Fact]
    public void RemovePlacement_RemovesLastReferenceButPreservesLegacyOrphan()
    {
        GostDocument document = new GostDocument();
        ImagePlacementInfo inserted = Insert(document, 0);
        Guid legacyId = Guid.NewGuid();
        document.MutableImages.Add(new ImageAttachment(
            legacyId,
            "legacy.bin",
            "application/octet-stream",
            new byte[] { 9, 8, 7 },
            "legacy caption",
            4));

        ImageResult<ImageRemovalInfo> removed =
            _service.RemovePlacement(document, 0);

        AssertSuccess(removed);
        Assert.True(removed.Value!.AttachmentRemoved);
        Assert.DoesNotContain(document.Images, item => item.Id == inserted.ImageId);
        Assert.Contains(document.Images, item => item.Id == legacyId);
        Assert.Single(document.Paragraphs);
        Assert.False(document.Paragraphs[0].IsImage);
    }

    [Fact]
    public void RemovePlacement_SharedAttachmentRemains()
    {
        GostDocument document = new GostDocument();
        ImagePlacementInfo first = Insert(document, 0);
        AssertSuccess(_service.InsertExistingPlacement(
            document,
            1,
            first.ImageId,
            new ImagePlacementRequest(new ImageSize(100, 100))));

        ImageResult<ImageRemovalInfo> removed =
            _service.RemovePlacement(document, 0);

        AssertSuccess(removed);
        Assert.False(removed.Value!.AttachmentRemoved);
        Assert.Single(document.Images);
        Assert.Equal(first.ImageId, document.Paragraphs[0].ImageId);
    }

    [Fact]
    public void RemoveAndReplace_CanRepairDanglingPlacement()
    {
        GostDocument replaceDocument = new GostDocument();
        Paragraph dangling = CreateDanglingParagraph();
        replaceDocument.Paragraphs.Add(dangling);

        ImageResult<ImagePlacementInfo> replaced =
            _service.ReplacePlacementContent(
                replaceDocument,
                0,
                new ReplaceImageRequest(
                    TestImageData.CreatePng(),
                    new ImageSize(100, 50),
                    "repaired.png"));

        AssertSuccess(replaced);
        Assert.Single(replaceDocument.Images);
        Assert.Equal(replaced.Value!.ImageId, dangling.ImageId);

        GostDocument removeDocument = new GostDocument();
        removeDocument.Paragraphs.Add(CreateDanglingParagraph());
        ImageResult<ImageRemovalInfo> removed =
            _service.RemovePlacement(removeDocument, 0);

        AssertSuccess(removed);
        Assert.False(removed.Value!.AttachmentRemoved);
        Assert.Single(removeDocument.Paragraphs);
        Assert.False(removeDocument.Paragraphs[0].IsImage);
    }

    [Fact]
    public void RemoveOrphans_IsExplicitAndAtomic()
    {
        GostDocument document = new GostDocument();
        ImagePlacementInfo used = Insert(document, 0);
        Guid orphanId = Guid.NewGuid();
        document.MutableImages.Add(new ImageAttachment(
            orphanId,
            "orphan.png",
            "image/png",
            TestImageData.CreatePng()));

        ImageResult<OrphanCleanupResult> invalid =
            _service.RemoveOrphans(document, new[] { orphanId, used.ImageId });

        Assert.False(invalid.IsSuccess);
        Assert.Equal(ImageErrorCode.ImageStillReferenced, invalid.Error?.Code);
        Assert.Contains(document.Images, image => image.Id == orphanId);

        ImageResult<OrphanCleanupResult> valid =
            _service.RemoveOrphans(document, new[] { orphanId });

        AssertSuccess(valid);
        Assert.DoesNotContain(document.Images, image => image.Id == orphanId);
        Assert.Contains(document.Images, image => image.Id == used.ImageId);
    }

    [Fact]
    public void Inspect_ReportsDanglingDuplicateInvalidAndOrphanStates()
    {
        GostDocument document = new GostDocument();
        Guid duplicateId = Guid.NewGuid();
        document.MutableImages.Add(new ImageAttachment(
            duplicateId,
            "a.png",
            "image/png",
            TestImageData.CreatePng()));
        document.MutableImages.Add(new ImageAttachment(
            duplicateId,
            "b.png",
            "image/png",
            TestImageData.CreatePng()));
        document.MutableImages.Add(new ImageAttachment(
            Guid.NewGuid(),
            "orphan.bin",
            "application/octet-stream",
            ReadOnlyMemory<byte>.Empty));
        document.Paragraphs.Add(CreateDanglingParagraph());

        ImageIntegrityReport report = _service.Inspect(document);

        Assert.False(report.IsValid);
        Assert.Contains(
            report.Issues,
            issue => issue.Code == ImageIntegrityIssueCode.DuplicateImageId);
        Assert.Contains(
            report.Issues,
            issue => issue.Code == ImageIntegrityIssueCode.DanglingImageReference);
        Assert.Contains(
            report.Issues,
            issue => issue.Code == ImageIntegrityIssueCode.EmptyImageData);
        Assert.Contains(
            report.Issues,
            issue => issue.Code == ImageIntegrityIssueCode.OrphanAttachment);
    }

    private ImagePlacementInfo Insert(
        GostDocument document,
        int index,
        string caption = "")
    {
        ImageResult<ImagePlacementInfo> result = _service.InsertPlacement(
            document,
            index,
            new CreateImageRequest(
                TestImageData.CreatePng(),
                new ImageSize(320, 180),
                "test.png",
                caption));
        AssertSuccess(result);
        return result.Value!;
    }

    private static GostDocument CreateDocumentWithTextParagraph()
    {
        GostDocument document = new GostDocument();
        document.Paragraphs.Add(new Paragraph());
        return document;
    }

    private static Paragraph CreateDanglingParagraph()
    {
        Paragraph paragraph = new Paragraph
        {
            ImageId = Guid.NewGuid(),
            ImageWidth = 100,
            ImageHeight = 50
        };
        paragraph.Runs.Add(new TextRun("dangling"));
        return paragraph;
    }

    private static void AssertSuccess<T>(ImageResult<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
    }
}
