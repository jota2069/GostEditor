using GostEditor.Core.Models;
using GostEditor.Core.Interfaces;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Core.TextEngine.Commands;

namespace GostEditor.Tests.TextEngine;

public class DocumentEditorTests
{
    [Fact]
    public void CommandManager_WhenUndoOrRedoFails_KeepsCommandOnOriginalStack()
    {
        CommandManager history = new();
        FaultingEditorCommand command = new();
        int changedEvents = 0;
        history.Changed += (_, _) => changedEvents++;
        history.ExecuteCommand(command);
        command.FailUndo = true;

        Assert.Throws<InvalidOperationException>(history.Undo);
        Assert.Equal(1, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
        Assert.Equal(1, changedEvents);

        command.FailUndo = false;
        history.Undo();
        command.FailExecute = true;
        Assert.Throws<InvalidOperationException>(history.Redo);
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(1, history.RedoCount);
        Assert.Equal(2, changedEvents);
    }
    [Fact]
    public void MutationCommand_WhenActionPartiallyFails_RestoresDocumentAndHistory()
    {
        DocumentEditor editor = new();
        InvalidOperationException failure = new("mutation failed");
        DocumentMutationCommand command = new(
            editor,
            () => new DocumentMutationRange(
                0,
                editor.Document.Paragraphs.Count),
            () => new DocumentMutationRange(
                0,
                editor.Document.Paragraphs.Count),
            () =>
            {
                editor.Document.Paragraphs.Add(new Paragraph
                {
                    Runs = { new TextRun("partial") }
                });
                throw failure;
            },
            DocumentChangeKind.Structure);

        InvalidOperationException actual = Assert.Throws<
            InvalidOperationException>(() =>
            editor.History.ExecuteCommand(command));

        Assert.Same(failure, actual);
        Assert.Single(editor.Document.Paragraphs);
        Assert.Equal(string.Empty, editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Equal(0, editor.History.RedoCount);
    }

    [Fact]
    public void MutationCommand_WhenCommitPredicateRejects_RollsBackMutation()
    {
        DocumentEditor editor = new();
        DocumentMutationCommand command = new(
            editor,
            () => new DocumentMutationRange(
                0,
                editor.Document.Paragraphs.Count),
            () => new DocumentMutationRange(
                0,
                editor.Document.Paragraphs.Count),
            () => editor.Document.Paragraphs.Add(new Paragraph
            {
                Runs = { new TextRun("uncommitted") }
            }),
            DocumentChangeKind.Structure,
            shouldCommit: () => false);

        bool recorded = editor.History.TryExecuteCommand(
            command,
            () => false);

        Assert.False(recorded);
        Assert.Single(editor.Document.Paragraphs);
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void MutationCommand_WhenAfterRangeFails_RestoresChangedParagraphCount()
    {
        DocumentEditor editor = new();
        InvalidOperationException failure = new("range failed");
        DocumentMutationCommand command = new(
            editor,
            () => new DocumentMutationRange(0, 1),
            () => throw failure,
            () =>
            {
                editor.Document.Paragraphs.Add(new Paragraph());
                editor.Document.Paragraphs.Add(new Paragraph());
            },
            DocumentChangeKind.Structure);

        InvalidOperationException actual = Assert.Throws<
            InvalidOperationException>(() =>
            editor.History.ExecuteCommand(command));

        Assert.Same(failure, actual);
        Assert.Single(editor.Document.Paragraphs);
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void TryExecuteCommand_WhenDecisionRejects_UndoesGenericCommand()
    {
        CommandManager history = new();
        StatefulEditorCommand command = new();

        bool recorded = history.TryExecuteCommand(command, () => false);

        Assert.False(recorded);
        Assert.Equal(0, command.Value);
        Assert.Equal(0, history.UndoCount);
    }

    [Fact]
    public void TryExecuteCommand_WhenDecisionThrows_PreservesErrorAndUndoesCommand()
    {
        CommandManager history = new();
        StatefulEditorCommand command = new();
        InvalidOperationException failure = new("decision failed");

        InvalidOperationException actual = Assert.Throws<
            InvalidOperationException>(() =>
            history.TryExecuteCommand(command, () => throw failure));

        Assert.Same(failure, actual);
        Assert.Equal(0, command.Value);
        Assert.Equal(0, history.UndoCount);
    }

    [Fact]
    public void DocumentChanged_WhenSubscriberThrows_CommandRemainsUndoable()
    {
        DocumentEditor editor = new();
        editor.DocumentChanged += (_, _) =>
            throw new InvalidOperationException("observer failed");

        editor.InsertText("A");

        Assert.Equal("A", editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal(1, editor.History.UndoCount);
        editor.History.Undo();
        Assert.Equal(string.Empty, editor.Document.Paragraphs[0].GetPlainText());
    }

    [Fact]
    public void SnapshotCommand_WhenNewSnapshotCaptureFails_RestoresOldState()
    {
        DocumentEditor editor = new();
        SnapshotCommand command = new(
            editor,
            () => editor.Document.Paragraphs.Add(null!));

        Assert.ThrowsAny<Exception>(() =>
            editor.History.ExecuteCommand(command));

        Assert.Single(editor.Document.Paragraphs);
        Assert.NotNull(editor.Document.Paragraphs[0]);
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void CommandManager_WhenChangedSubscriberThrows_KeepsHistoryState()
    {
        CommandManager history = new();
        StatefulEditorCommand command = new();
        history.Changed += (_, _) =>
            throw new InvalidOperationException("observer failed");

        history.ExecuteCommand(command);
        Assert.Equal(1, command.Value);
        Assert.Equal(1, history.UndoCount);

        history.Undo();
        Assert.Equal(0, command.Value);
        Assert.Equal(1, history.RedoCount);
    }
    [Fact]
    public void InsertText_CanBeUndoneAndRedone()
    {
        DocumentEditor editor = new DocumentEditor();

        editor.InsertText("Привет");

        Assert.Equal("Привет", editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal(new DocumentPosition(0, 6), editor.CaretPosition);

        editor.History.Undo();

        Assert.Equal(string.Empty, editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal(new DocumentPosition(0, 0), editor.CaretPosition);

        editor.History.Redo();

        Assert.Equal("Привет", editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal(new DocumentPosition(0, 6), editor.CaretPosition);
    }

    private sealed class FaultingEditorCommand : IEditorCommand
    {
        internal bool FailExecute { get; set; }

        internal bool FailUndo { get; set; }

        public void Execute()
        {
            if (FailExecute)
            {
                throw new InvalidOperationException("execute failed");
            }
        }

        public void Undo()
        {
            if (FailUndo)
            {
                throw new InvalidOperationException("undo failed");
            }
        }
    }

    private sealed class StatefulEditorCommand : IEditorCommand
    {
        internal int Value { get; private set; }

        public void Execute() => Value++;

        public void Undo() => Value--;
    }

    [Fact]
    public void PasteText_WithMultipleLines_IsSingleUndoOperation()
    {
        DocumentEditor editor = new DocumentEditor();

        editor.PasteText("Первая\nВторая\nТретья");

        Assert.Equal(
            new[] { "Первая", "Вторая", "Третья" },
            editor.Document.Paragraphs.Select(paragraph => paragraph.GetPlainText()));

        editor.History.Undo();

        Assert.Single(editor.Document.Paragraphs);
        Assert.Equal(string.Empty, editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal(new DocumentPosition(0, 0), editor.CaretPosition);
    }

    [Fact]
    public void ToggleBold_FormatsOnlySelectedText()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertText("abcdef");
        editor.Document.Paragraphs[0].Runs[0].FontSize = 18;
        editor.Document.Paragraphs[0].Runs[0].Color = 0xFF123456;
        editor.SelectionAnchor = new DocumentPosition(0, 1);
        editor.CaretPosition = new DocumentPosition(0, 4);

        editor.ToggleBold();

        Paragraph paragraph = editor.Document.Paragraphs[0];
        Assert.Equal("abcdef", paragraph.GetPlainText());
        Assert.Collection(
            paragraph.Runs.Where(run => run.Text.Length > 0),
            run =>
            {
                Assert.Equal("a", run.Text);
                Assert.False(run.IsBold);
                Assert.Equal(18, run.FontSize);
                Assert.Equal(0xFF123456u, run.Color);
            },
            run =>
            {
                Assert.Equal("bcd", run.Text);
                Assert.True(run.IsBold);
                Assert.Equal(18, run.FontSize);
                Assert.Equal(0xFF123456u, run.Color);
            },
            run =>
            {
                Assert.Equal("ef", run.Text);
                Assert.False(run.IsBold);
                Assert.Equal(18, run.FontSize);
                Assert.Equal(0xFF123456u, run.Color);
            });
    }

    [Fact]
    public void InsertHeading_CanBeUndoneAndRedone()
    {
        DocumentEditor editor = new DocumentEditor();

        editor.InsertHeading(1, "Глава");

        Assert.Equal(2, editor.Document.Paragraphs.Count);
        Assert.Equal(ParagraphStyle.Heading1, editor.Document.Paragraphs[1].Style);
        Assert.Equal("Глава", editor.Document.Paragraphs[1].GetPlainText());

        editor.History.Undo();
        Assert.Single(editor.Document.Paragraphs);

        editor.History.Redo();
        Assert.Equal(2, editor.Document.Paragraphs.Count);
        Assert.Equal("Глава", editor.Document.Paragraphs[1].GetPlainText());
    }

    [Fact]
    public void FindNext_FromCaret_SelectsCurrentThenAdjacentMatch()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertText("foofoo");
        editor.CaretPosition = new DocumentPosition(0, 0);

        Assert.True(editor.FindNext("foo"));
        Assert.Equal(new DocumentPosition(0, 0), editor.SelectionAnchor);
        Assert.Equal(new DocumentPosition(0, 3), editor.CaretPosition);

        Assert.True(editor.FindNext("foo"));
        Assert.Equal(new DocumentPosition(0, 3), editor.SelectionAnchor);
        Assert.Equal(new DocumentPosition(0, 6), editor.CaretPosition);
    }

    [Fact]
    public void PasteText_WithStandaloneCarriageReturn_CreatesParagraph()
    {
        DocumentEditor editor = new DocumentEditor();

        editor.PasteText("Первая\rВторая");

        Assert.Equal(
            new[] { "Первая", "Вторая" },
            editor.Document.Paragraphs.Select(paragraph => paragraph.GetPlainText()));
    }

    [Fact]
    public void InsertText_ReplacesBackwardSelectionAcrossParagraphs_AsSingleUndoOperation()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.PasteText("abc\ndef");
        editor.SelectionAnchor = new DocumentPosition(1, 2);
        editor.CaretPosition = new DocumentPosition(0, 1);

        editor.InsertText("X");

        Paragraph paragraph = Assert.Single(editor.Document.Paragraphs);
        Assert.Equal("aXf", paragraph.GetPlainText());

        editor.History.Undo();

        Assert.Equal(
            new[] { "abc", "def" },
            editor.Document.Paragraphs.Select(item => item.GetPlainText()));
        Assert.Equal(new DocumentPosition(0, 1), editor.CaretPosition);
        Assert.Equal(new DocumentPosition(1, 2), editor.SelectionAnchor);
    }

    [Fact]
    public void InsertNewLine_InMiddle_PreservesFormattingAndParagraphStyle()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertText("abcdef");
        Paragraph original = editor.Document.Paragraphs[0];
        original.Runs[0].FontSize = 18;
        original.Runs[0].Color = 0xFF123456;
        original.Style = ParagraphStyle.Heading2;
        original.Alignment = GostAlignment.Center;
        original.FirstLineIndent = 0;
        editor.CaretPosition = new DocumentPosition(0, 3);

        editor.InsertNewLine();

        Assert.Equal(2, editor.Document.Paragraphs.Count);
        Assert.Equal("abc", editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal("def", editor.Document.Paragraphs[1].GetPlainText());
        Assert.All(
            editor.Document.Paragraphs.SelectMany(paragraph => paragraph.Runs),
            run =>
            {
                Assert.Equal(18, run.FontSize);
                Assert.Equal(0xFF123456u, run.Color);
            });
        Assert.All(
            editor.Document.Paragraphs,
            paragraph => Assert.Equal(ParagraphStyle.Heading2, paragraph.Style));
    }

    [Fact]
    public void InsertNewLine_AtEnd_PreservesTypingStyleInNewParagraph()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertText("abc");
        TextRun originalRun = editor.Document.Paragraphs[0].Runs[0];
        originalRun.IsItalic = true;
        originalRun.FontSize = 18;
        originalRun.Color = 0xFF123456;

        editor.InsertNewLine();
        editor.InsertText("x");

        TextRun insertedRun = Assert.Single(
            editor.Document.Paragraphs[1].Runs.Where(run => run.Text == "x"));
        Assert.True(insertedRun.IsItalic);
        Assert.Equal(18, insertedRun.FontSize);
        Assert.Equal(0xFF123456u, insertedRun.Color);
    }

    [Fact]
    public void ToggleBold_AtCaret_PreservesOtherTypingProperties()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertText("abc");
        TextRun originalRun = editor.Document.Paragraphs[0].Runs[0];
        originalRun.FontSize = 18;
        originalRun.Color = 0xFF123456;

        editor.ToggleBold();
        editor.InsertText("x");

        TextRun insertedRun = Assert.Single(
            editor.Document.Paragraphs[0].Runs.Where(run => run.Text == "x"));
        Assert.True(insertedRun.IsBold);
        Assert.Equal(18, insertedRun.FontSize);
        Assert.Equal(0xFF123456u, insertedRun.Color);
    }

    [Fact]
    public void Backspace_AtParagraphStart_MergesParagraphs()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.PasteText("Первая\nВторая");
        editor.CaretPosition = new DocumentPosition(1, 0);

        editor.Backspace();

        Paragraph paragraph = Assert.Single(editor.Document.Paragraphs);
        Assert.Equal("ПерваяВторая", paragraph.GetPlainText());
        Assert.Equal(new DocumentPosition(0, 6), editor.CaretPosition);
    }

    [Fact]
    public void InsertImage_CanBeUndoneAndRedoneWithTheSameAttachment()
    {
        DocumentEditor editor = new DocumentEditor();
        byte[] imageData = TestImageData.CreatePng();

        editor.InsertImage(imageData, 320, 200);

        Assert.Equal(2, editor.Document.Paragraphs.Count);
        Guid imageId = Assert.IsType<Guid>(editor.Document.Paragraphs[1].ImageId);
        ImageAttachment attachment = Assert.Single(editor.Document.Images);
        Assert.Equal(imageId, attachment.Id);
        Assert.Equal(imageData, attachment.Data.ToArray());

        editor.History.Undo();
        Assert.Single(editor.Document.Paragraphs);
        Assert.Empty(editor.Document.Images);

        editor.History.Redo();
        Assert.Equal(imageId, editor.Document.Paragraphs[1].ImageId);
        Assert.Equal(imageId, Assert.Single(editor.Document.Images).Id);
        Assert.Equal(
            imageData,
            editor.Document.Images[0].Data.ToArray());
        Assert.Equal(320, editor.Document.Paragraphs[1].ImageWidth);
        Assert.Equal(200, editor.Document.Paragraphs[1].ImageHeight);
    }

    [Fact]
    public void ReplaceImage_UndoAndRedoRestoresBothRegistryVersions()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertImage(TestImageData.CreatePng(), 320, 200);
        Guid originalId = editor.Document.Paragraphs[1].ImageId!.Value;

        ImageResult<ImagePlacementInfo> replaced = editor.ReplaceImage(
            1,
            new ReplaceImageRequest(
                TestImageData.CreatePng(),
                new ImageSize(640, 480),
                "replacement.png"));

        Assert.True(replaced.IsSuccess, replaced.Error?.Message);
        Guid replacementId = replaced.Value!.ImageId;
        Assert.NotEqual(originalId, replacementId);
        Assert.Equal(replacementId, editor.Document.Paragraphs[1].ImageId);
        Assert.Equal(replacementId, Assert.Single(editor.Document.Images).Id);

        editor.History.Undo();

        Assert.Equal(originalId, editor.Document.Paragraphs[1].ImageId);
        Assert.Equal(originalId, Assert.Single(editor.Document.Images).Id);
        Assert.Equal(320, editor.Document.Paragraphs[1].ImageWidth);

        editor.History.Redo();

        Assert.Equal(replacementId, editor.Document.Paragraphs[1].ImageId);
        Assert.Equal(replacementId, Assert.Single(editor.Document.Images).Id);
        Assert.Equal(640, editor.Document.Paragraphs[1].ImageWidth);
    }

    [Fact]
    public void ResizeImage_IsOneUndoablePlacementOnlyCommand()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertImage(TestImageData.CreatePng(), 320, 200);
        Guid imageId = editor.Document.Paragraphs[1].ImageId!.Value;
        ImageAttachment attachment = Assert.Single(editor.Document.Images);

        ImageResult<ImagePlacementInfo> resized =
            editor.ResizeImage(1, new ImageSize(500, 250));

        Assert.True(resized.IsSuccess, resized.Error?.Message);
        Assert.Equal(500, editor.Document.Paragraphs[1].ImageWidth);
        Assert.Equal(imageId, editor.Document.Paragraphs[1].ImageId);
        Assert.Same(attachment, editor.Document.Images[0]);

        editor.History.Undo();

        Assert.Equal(320, editor.Document.Paragraphs[1].ImageWidth);
        Assert.Equal(200, editor.Document.Paragraphs[1].ImageHeight);
        Assert.Equal(imageId, editor.Document.Paragraphs[1].ImageId);
        Assert.Same(attachment, editor.Document.Images[0]);
    }

    [Fact]
    public void RemoveImage_UndoRestoresAttachmentBeforePlacement()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertImage(TestImageData.CreatePng(), 320, 200);
        Guid imageId = editor.Document.Paragraphs[1].ImageId!.Value;

        ImageResult<ImageRemovalInfo> removed = editor.RemoveImage(1);

        Assert.True(removed.IsSuccess, removed.Error?.Message);
        Assert.Empty(editor.Document.Images);
        Assert.Single(editor.Document.Paragraphs);

        editor.History.Undo();

        Assert.Equal(2, editor.Document.Paragraphs.Count);
        Assert.Equal(imageId, editor.Document.Paragraphs[1].ImageId);
        Assert.Equal(imageId, Assert.Single(editor.Document.Images).Id);
    }

    [Fact]
    public void RemoveImage_WithSharedAttachmentDoesNotDeleteAttachment()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertImage(TestImageData.CreatePng(), 320, 200);
        Guid sharedId = editor.Document.Paragraphs[1].ImageId!.Value;
        Assert.True(editor.InsertExistingImage(
            sharedId,
            new ImagePlacementRequest(
                new ImageSize(160, 100),
                "Вторая")).IsSuccess);

        ImageResult<ImageRemovalInfo> removed = editor.RemoveImage(1);

        Assert.True(removed.IsSuccess, removed.Error?.Message);
        Assert.False(removed.Value!.AttachmentRemoved);
        Assert.Single(editor.Document.Images);
        Assert.Equal(sharedId, editor.Document.Paragraphs[1].ImageId);

        editor.History.Undo();

        Assert.Equal(3, editor.Document.Paragraphs.Count);
        Assert.Equal(
            2,
            editor.Document.Paragraphs.Count(
                paragraph => paragraph.ImageId == sharedId));
        Assert.Single(editor.Document.Images);
    }

    [Fact]
    public void InvalidImageCommand_DoesNotAddNoOpToHistory()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertText("A");

        ImageResult<ImagePlacementInfo> invalid = editor.InsertImage(
            new CreateImageRequest(
                new byte[] { 1, 2, 3 },
                new ImageSize(100, 100)));

        Assert.False(invalid.IsSuccess);
        editor.History.Undo();
        Assert.Equal(string.Empty, editor.Document.Paragraphs[0].GetPlainText());
    }

    [Fact]
    public void Backspace_AtImageBoundaryDoesNotMergeImageWithText()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertText("Текст");
        editor.InsertImage(TestImageData.CreatePng(), 320, 200);
        Guid imageId = editor.Document.Paragraphs[1].ImageId!.Value;
        editor.CaretPosition = new DocumentPosition(1, 0);

        editor.Backspace();

        Assert.Equal(2, editor.Document.Paragraphs.Count);
        Assert.Equal(imageId, editor.Document.Paragraphs[1].ImageId);
        Assert.Single(editor.Document.Images);
    }

    [Fact]
    public void RangeDeletion_RemovesFullyCoveredImageAndItsAttachment()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertText("A");
        editor.InsertImage(TestImageData.CreatePng(), 320, 200);
        editor.InsertTextBlock();
        editor.InsertText("B");
        editor.SelectionAnchor = new DocumentPosition(0, 1);
        editor.CaretPosition = new DocumentPosition(2, 0);

        editor.DeleteSelection();

        Assert.Single(editor.Document.Paragraphs);
        Assert.Empty(editor.Document.Images);
        Assert.DoesNotContain(
            editor.Document.Paragraphs,
            paragraph => paragraph.IsImage);

        editor.History.Undo();

        Assert.Equal(3, editor.Document.Paragraphs.Count);
        Assert.Single(editor.Document.Images);
        Assert.True(editor.Document.Paragraphs[1].IsImage);
    }

    [Fact]
    public void RangeDeletion_WithPartiallySelectedImageEditsCaptionWithoutMergingBlocks()
    {
        DocumentEditor editor = new DocumentEditor();
        editor.InsertText("Before");
        Assert.True(editor.InsertImage(
            new CreateImageRequest(
                TestImageData.CreatePng(),
                new ImageSize(320, 200),
                "diagram.png",
                "Caption")).IsSuccess);
        editor.InsertTextBlock();
        editor.InsertText("Text");
        Guid imageId = editor.Document.Paragraphs[1].ImageId!.Value;
        editor.SelectionAnchor = new DocumentPosition(1, 3);
        editor.CaretPosition = new DocumentPosition(2, 1);

        editor.DeleteSelection();

        Assert.Equal(3, editor.Document.Paragraphs.Count);
        Assert.Equal(imageId, editor.Document.Paragraphs[1].ImageId);
        Assert.Equal("Cap", editor.Document.Paragraphs[1].GetPlainText());
        Assert.Equal("ext", editor.Document.Paragraphs[2].GetPlainText());
        Assert.Single(editor.Document.Images);
    }

    [Fact]
    public void ExplicitOrphanCleanup_IsUndoable()
    {
        DocumentEditor editor = new DocumentEditor();
        Guid orphanId = Guid.NewGuid();
        editor.Document.MutableImages.Add(new ImageAttachment(
            orphanId,
            "legacy.bin",
            "application/octet-stream",
            new byte[] { 1, 2, 3 }));

        ImageResult<OrphanCleanupResult> removed =
            editor.RemoveOrphanImages(new[] { orphanId });

        Assert.True(removed.IsSuccess, removed.Error?.Message);
        Assert.Empty(editor.Document.Images);

        editor.History.Undo();

        Assert.Equal(orphanId, Assert.Single(editor.Document.Images).Id);
    }
}

public sealed class DocumentEditorFoundationIntegrationTests
{
    [Fact]
    public void InsertText_UndoDoesNotReplaceUnchangedParagraphs()
    {
        GostDocument document = new();
        document.Paragraphs.Clear();
        for (int index = 0; index < 1000; index++)
        {
            document.Paragraphs.Add(new Paragraph
            {
                Runs = { new TextRun($"P{index}") }
            });
        }

        DocumentEditor editor = new(document);
        Paragraph firstReference = document.Paragraphs[0];
        Paragraph lastReference = document.Paragraphs[^1];
        editor.CaretPosition = new DocumentPosition(500, 4);

        editor.InsertText("X");
        editor.History.Undo();
        editor.History.Redo();

        Assert.Same(firstReference, document.Paragraphs[0]);
        Assert.Same(lastReference, document.Paragraphs[^1]);
        Assert.Equal("P500X", document.Paragraphs[500].GetPlainText());
    }

    [Fact]
    public void DocumentChanged_ReportsLocalizedMutationAndMonotonicVersion()
    {
        DocumentEditor editor = new();
        editor.PasteText("A\nB\nC");
        List<DocumentChangedEventArgs> events = new();
        editor.DocumentChanged += (_, args) => events.Add(args);
        editor.CaretPosition = new DocumentPosition(1, 1);

        editor.InsertText("X");
        editor.History.Undo();
        editor.History.Redo();

        Assert.Equal(3, events.Count);
        Assert.All(events, item => Assert.Equal(1, item.StartParagraphIndex));
        Assert.Equal(
            events.Select(item => item.Version).OrderBy(item => item),
            events.Select(item => item.Version));
        Assert.All(
            events,
            item => Assert.True(
                item.Kind.HasFlag(DocumentChangeKind.Metrics)));
    }

    [Fact]
    public void StructuredSession_CanBeProjectedBackIntoActiveEditor()
    {
        DocumentEditor editor = new();
        editor.InsertText("A");
        var session = editor.CreateStructuredEditingSession();
        var paragraph = Assert.IsType<
            GostEditor.Core.DocumentModel.Blocks.ParagraphBlock>(
            session.Document.Sections[0].Blocks[0]);
        session.Text.InsertText(
            new GostEditor.Core.DocumentModel.DocumentLocation(
                paragraph.Id,
                1),
            "B");

        editor.ApplyStructuredDocument(session.Document);

        Assert.Equal("AB", editor.Document.Paragraphs[0].GetPlainText());
        Assert.False(editor.History.CanUndo);
    }
}
