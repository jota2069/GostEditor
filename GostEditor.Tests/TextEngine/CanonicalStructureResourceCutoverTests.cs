using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.Editing.Operations;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.TextEngine;

public sealed class CanonicalStructureResourceCutoverTests
{
    [Fact]
    public void InsertHeading_AfterCaret_IsCanonicalAndKeepsStableIdentity()
    {
        DocumentEditor editor = new();
        editor.PasteText("before\nafter");
        editor.CaretPosition = new DocumentPosition(0, 3);
        long revision = editor.CanonicalRevision;

        editor.InsertHeading(2, "Section");

        Assert.Equal(new[] { "before", "Section", "after" }, Texts(editor));
        ParagraphBlock heading = Assert.IsType<ParagraphBlock>(
            editor.ActiveEditingSession.Document.EnumerateBlocks().ElementAt(1));
        DocumentNodeId id = heading.Id;
        Assert.Equal(ParagraphStyle.Heading2, heading.Properties.Style);
        Assert.Equal(GostAlignment.Center, heading.Properties.Alignment);
        Assert.False(heading.Properties.PageBreakBefore);
        Assert.Equal(revision + 1, editor.CanonicalRevision);

        editor.History.Undo();
        Assert.Equal(new[] { "before", "after" }, Texts(editor));
        Assert.Equal(new DocumentPosition(0, 3), editor.CaretPosition);
        editor.History.Redo();

        Assert.Equal(id, editor.ActiveEditingSession.Document
            .EnumerateBlocks().ElementAt(1).Id);
        Assert.Equal(new DocumentPosition(1, "Section".Length), editor.CaretPosition);
        Assert.Equal(new[] { "before", "Section", "after" }, Texts(editor));
        Assert.True(editor.ActiveEditingSession.Document.Validate().IsValid);
    }

    [Fact]
    public void InsertPageBreak_IsCanonicalProjectedAndUndoable()
    {
        DocumentEditor editor = new();
        DocumentRoot root = editor.ActiveEditingSession.Document;

        editor.InsertPageBreak();

        PageBreakBlock pageBreak = Assert.IsType<PageBreakBlock>(
            root.EnumerateBlocks().ElementAt(1));
        Assert.True(editor.Document.Paragraphs[1].PageBreakBefore);
        editor.History.Undo();
        Assert.Single(root.EnumerateBlocks());
        editor.History.Redo();
        Assert.Equal(pageBreak.Id, root.EnumerateBlocks().ElementAt(1).Id);
    }

    [Fact]
    public void InsertImage_OwnsImmutableCanonicalResourceAsOneRevision()
    {
        DocumentEditor editor = new();
        byte[] input = TestImageData.CreatePng();
        byte first = input[0];
        long revision = editor.CanonicalRevision;

        ImageResult<ImagePlacementInfo> result = editor.InsertImage(
            new CreateImageRequest(
                input,
                new ImageSize(320, 200),
                "figure.png",
                "Caption"));
        input[0] ^= 0xFF;

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(revision + 1, editor.CanonicalRevision);
        FigureBlock figure = Assert.IsType<FigureBlock>(
            editor.ActiveEditingSession.Document.EnumerateBlocks().ElementAt(1));
        ImageResource resource = Assert.Single(
            editor.ActiveEditingSession.Document.Resources.ImagesInOrder);
        Assert.Equal(figure.ImageId, resource.Id);
        Assert.Equal(first, resource.Data.Span[0]);
        Assert.Equal(first, Assert.Single(editor.Document.Images).Data.Span[0]);
        Assert.True(editor.ActiveEditingSession.Document.Validate().IsValid);

        editor.History.Undo();
        Assert.Empty(editor.ActiveEditingSession.Document.Resources.Images);
        Assert.Empty(editor.Document.Images);
        editor.History.Redo();
        Assert.Same(figure, editor.ActiveEditingSession.Document
            .EnumerateBlocks().ElementAt(1));
        Assert.Same(resource, Assert.Single(
            editor.ActiveEditingSession.Document.Resources.ImagesInOrder));
    }

    [Fact]
    public void RemoveSharedImage_DeletesResourceOnlyAfterLastReference()
    {
        DocumentEditor editor = new();
        editor.InsertImage(TestImageData.CreatePng(), 320, 200);
        Guid imageId = editor.Document.Paragraphs[1].ImageId!.Value;
        Assert.True(editor.InsertExistingImage(
            imageId,
            new ImagePlacementRequest(new ImageSize(160, 100), "Second"))
            .IsSuccess);

        ImageResult<ImageRemovalInfo> first = editor.RemoveImage(1);
        Assert.True(first.IsSuccess);
        Assert.False(first.Value.AttachmentRemoved);
        Assert.Single(editor.ActiveEditingSession.Document.Resources.Images);
        Assert.True(editor.ActiveEditingSession.Document.Validate().IsValid);

        ImageResult<ImageRemovalInfo> last = editor.RemoveImage(1);
        Assert.True(last.IsSuccess);
        Assert.True(last.Value.AttachmentRemoved);
        Assert.Empty(editor.ActiveEditingSession.Document.Resources.Images);

        editor.History.Undo();
        Assert.Single(editor.ActiveEditingSession.Document.Resources.Images);
        Assert.True(editor.ActiveEditingSession.Document.Validate().IsValid);
    }

    [Fact]
    public void InvalidImage_IsAtomicCanonicalNoOp()
    {
        DocumentEditor editor = new();
        long revision = editor.CanonicalRevision;
        int undoCount = editor.ActiveEditingSession.History.UndoCount;

        ImageResult<ImagePlacementInfo> result = editor.InsertImage(
            new CreateImageRequest(
                new byte[] { 1, 2, 3 },
                new ImageSize(100, 100)));

        Assert.False(result.IsSuccess);
        Assert.Equal(revision, editor.CanonicalRevision);
        Assert.Equal(undoCount, editor.ActiveEditingSession.History.UndoCount);
        Assert.Empty(editor.ActiveEditingSession.Document.Resources.Images);
        Assert.Single(editor.ActiveEditingSession.Document.EnumerateBlocks());
    }

    [Fact]
    public void MixedTextHeadingAndImage_UndoRedoUsesOneCanonicalGraph()
    {
        DocumentEditor editor = new();
        DocumentRoot root = editor.ActiveEditingSession.Document;
        editor.InsertText("A");
        editor.InsertHeading(1, "H");
        editor.InsertImage(TestImageData.CreatePng(), 16, 16);

        Assert.Same(root, editor.ActiveEditingSession.Document);
        Assert.Equal(3, editor.ActiveEditingSession.History.UndoCount);
        editor.History.Undo();
        editor.History.Undo();
        editor.History.Undo();
        Assert.Equal(string.Empty, Assert.Single(editor.Document.Paragraphs)
            .GetPlainText());
        Assert.Empty(root.Resources.Images);

        editor.History.Redo();
        editor.History.Redo();
        editor.History.Redo();
        Assert.Equal(new[] { "A", "H", string.Empty }, Texts(editor));
        Assert.IsType<FigureBlock>(root.EnumerateBlocks().ElementAt(2));
        Assert.True(root.Validate().IsValid);
    }

    [Fact]
    public void DeleteSelectionAcrossFigure_IsCanonicalAtomicAndRestoresIdentity()
    {
        DocumentEditor editor = new();
        editor.InsertText("A");
        editor.InsertImage(TestImageData.CreatePng(), 32, 32);
        editor.InsertTextBlock();
        editor.InsertText("B");
        DocumentRoot root = editor.ActiveEditingSession.Document;
        FigureBlock figure = Assert.IsType<FigureBlock>(
            root.EnumerateBlocks().ElementAt(1));
        ImageResource resource = Assert.Single(root.Resources.ImagesInOrder);
        long revision = editor.CanonicalRevision;
        editor.SelectionAnchor = new DocumentPosition(0, 1);
        editor.CaretPosition = new DocumentPosition(2, 0);

        editor.DeleteSelection();

        Assert.Same(root, editor.ActiveEditingSession.Document);
        Assert.Equal(revision + 1, editor.CanonicalRevision);
        Assert.Single(root.EnumerateBlocks());
        Assert.Empty(root.Resources.Images);
        Assert.True(root.Validate().IsValid);

        editor.History.Undo();

        Assert.Same(figure, root.EnumerateBlocks().ElementAt(1));
        Assert.Equal(resource.Id, Assert.Single(root.Resources.ImagesInOrder).Id);
        Assert.True(root.Validate().IsValid);
    }

    [Fact]
    public void InsertTextAcrossFigure_ReplacesSelectionAsOneCanonicalAction()
    {
        DocumentEditor editor = new();
        editor.InsertText("ABC");
        editor.InsertImage(TestImageData.CreatePng(), 32, 32);
        editor.InsertTextBlock();
        editor.InsertText("DEF");
        long revision = editor.CanonicalRevision;
        int undoCount = editor.ActiveEditingSession.History.UndoCount;
        editor.SelectionAnchor = new DocumentPosition(0, 1);
        editor.CaretPosition = new DocumentPosition(2, 2);

        editor.InsertText("X");

        Assert.Equal(new[] { "AXF" }, Texts(editor));
        Assert.Empty(editor.ActiveEditingSession.Document.Resources.Images);
        Assert.Equal(revision + 1, editor.CanonicalRevision);
        Assert.Equal(undoCount + 1, editor.ActiveEditingSession.History.UndoCount);

        editor.History.Undo();
        Assert.Equal(new[] { "ABC", string.Empty, "DEF" }, Texts(editor));
        Assert.Single(editor.ActiveEditingSession.Document.Resources.Images);
    }

    [Fact]
    public void InsertNewLineAcrossFigure_ReplacesSelectionAtomically()
    {
        DocumentEditor editor = CreateFigureSelectionEditor();
        int undoCount = editor.ActiveEditingSession.History.UndoCount;

        editor.InsertNewLine();

        Assert.Equal(new[] { "A", "F" }, Texts(editor));
        Assert.Empty(editor.ActiveEditingSession.Document.Resources.Images);
        Assert.Equal(undoCount + 1, editor.ActiveEditingSession.History.UndoCount);
        editor.History.Undo();
        Assert.Equal(new[] { "ABC", string.Empty, "DEF" }, Texts(editor));
    }

    [Fact]
    public void PasteMultilineAcrossFigure_IsOneCanonicalAction()
    {
        DocumentEditor editor = CreateFigureSelectionEditor();
        int undoCount = editor.ActiveEditingSession.History.UndoCount;

        editor.PasteText("X\nY");

        Assert.Equal(new[] { "AX", "YF" }, Texts(editor));
        Assert.Empty(editor.ActiveEditingSession.Document.Resources.Images);
        Assert.Equal(undoCount + 1, editor.ActiveEditingSession.History.UndoCount);
        editor.History.Undo();
        Assert.Equal(new[] { "ABC", string.Empty, "DEF" }, Texts(editor));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeleteKeysAcrossFigure_AreOneCanonicalAction(bool backspace)
    {
        DocumentEditor editor = CreateFigureSelectionEditor();
        int undoCount = editor.ActiveEditingSession.History.UndoCount;

        if (backspace)
        {
            editor.Backspace();
        }
        else
        {
            editor.DeleteForward();
        }

        Assert.Equal(new[] { "AF" }, Texts(editor));
        Assert.Equal(undoCount + 1, editor.ActiveEditingSession.History.UndoCount);
        editor.History.Undo();
        Assert.Equal(new[] { "ABC", string.Empty, "DEF" }, Texts(editor));
    }

    [Fact]
    public void InsertText_InsideFigureCaption_ReplacesCaptionText()
    {
        DocumentEditor editor = new();
        editor.InsertImage(new CreateImageRequest(
            TestImageData.CreatePng(),
            new ImageSize(32, 32),
            Caption: "ABC"));
        editor.SelectionAnchor = new DocumentPosition(1, 1);
        editor.CaretPosition = new DocumentPosition(1, 2);

        editor.InsertText("X");

        FigureBlock figure = Assert.IsType<FigureBlock>(
            editor.ActiveEditingSession.Document.EnumerateBlocks().ElementAt(1));
        Assert.Equal("AXC", figure.Caption);
        Assert.Equal("AXC", editor.Document.Paragraphs[1].GetPlainText());
        editor.History.Undo();
        Assert.Equal("ABC", editor.Document.Paragraphs[1].GetPlainText());
    }

    [Fact]
    public void InsertText_FromFigureCaptionIntoText_RetainsReplacementInCaption()
    {
        DocumentEditor editor = new();
        editor.InsertImage(new CreateImageRequest(
            TestImageData.CreatePng(),
            new ImageSize(32, 32),
            Caption: "ABC"));
        editor.InsertTextBlock();
        editor.InsertText("DEF");
        editor.SelectionAnchor = new DocumentPosition(1, 1);
        editor.CaretPosition = new DocumentPosition(2, 2);

        editor.InsertText("X");

        Assert.Equal(new[] { string.Empty, "AX", "F" }, Texts(editor));
        Assert.Equal("AX", Assert.IsType<FigureBlock>(
            editor.ActiveEditingSession.Document.EnumerateBlocks().ElementAt(1))
            .Caption);
        editor.History.Undo();
        Assert.Equal(new[] { string.Empty, "ABC", "DEF" }, Texts(editor));
    }

    [Fact]
    public void DirectResourceRemoval_RejectsDanglingReferenceAtomically()
    {
        DocumentEditor editor = new();
        editor.InsertImage(TestImageData.CreatePng(), 16, 16);
        DocumentRoot root = editor.ActiveEditingSession.Document;
        Guid imageId = Assert.Single(root.Resources.ImagesInOrder).Id;
        long revision = editor.CanonicalRevision;

        Assert.Throws<InvalidOperationException>(() =>
            editor.ActiveEditingSession.Execute(
                new RemoveImageResourceOperation(imageId)));

        Assert.Equal(revision, editor.CanonicalRevision);
        Assert.True(root.Resources.Images.ContainsKey(imageId));
        Assert.True(root.Validate().IsValid);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.PositiveInfinity)]
    public void ResizeFigureOperation_RejectsInvalidDimensions(
        double width,
        double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResizeFigureOperation(DocumentNodeId.New(), width, height));
    }

    private static string[] Texts(DocumentEditor editor) =>
        editor.Document.Paragraphs
            .Select(paragraph => paragraph.GetPlainText())
            .ToArray();

    private static DocumentEditor CreateFigureSelectionEditor()
    {
        DocumentEditor editor = new();
        editor.InsertText("ABC");
        editor.InsertImage(TestImageData.CreatePng(), 32, 32);
        editor.InsertTextBlock();
        editor.InsertText("DEF");
        editor.SelectionAnchor = new DocumentPosition(0, 1);
        editor.CaretPosition = new DocumentPosition(2, 2);
        return editor;
    }
}
