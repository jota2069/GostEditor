using DocumentFormat.OpenXml.Packaging;
using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Editing.Operations;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Tests.Infrastructure;
using GostEditor.UI.Services;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace GostEditor.Tests.TextEngine;

public sealed class CanonicalTextEditorCutoverTests
{
    [Fact]
    public void ActiveTextWorkflow_UsesCanonicalGraphHistoryAndStableSelection()
    {
        DocumentEditor editor = new();
        var session = editor.ActiveEditingSession;

        editor.InsertText("A😀e\u0301");
        editor.Backspace();
        editor.InsertNewLine();
        editor.InsertText("B");

        Assert.Same(session, editor.ActiveEditingSession);
        Assert.Equal(4, editor.CanonicalRevision);
        Assert.Equal(4, session.History.UndoCount);
        Assert.Equal(new[] { "A😀", "B" }, Texts(editor));
        Assert.Equal(new DocumentPosition(1, 1), editor.CaretPosition);

        editor.History.Undo();
        Assert.Equal(new[] { "A😀", string.Empty }, Texts(editor));
        Assert.Equal(5, editor.CanonicalRevision);

        editor.History.Redo();
        Assert.Equal(new[] { "A😀", "B" }, Texts(editor));
        Assert.Equal(6, editor.CanonicalRevision);
    }

    [Fact]
    public void MultilineSelectionAndFormatting_AreCanonicalSingleUserActions()
    {
        DocumentEditor editor = new();
        editor.PasteText("alpha\nbeta\ngamma");
        editor.SelectionAnchor = new DocumentPosition(0, 2);
        editor.CaretPosition = new DocumentPosition(2, 2);

        editor.InsertText("X");
        Assert.Equal(new[] { "alXmma" }, Texts(editor));
        Assert.Equal(2, editor.ActiveEditingSession.History.UndoCount);

        editor.SelectionAnchor = new DocumentPosition(0, 0);
        editor.CaretPosition = new DocumentPosition(0, 3);
        editor.ToggleBold();
        Assert.Equal(
            "alX",
            string.Concat(editor.Document.Paragraphs[0].Runs
                .Where(run => run.IsBold)
                .Select(run => run.Text)));

        editor.History.Undo();
        Assert.DoesNotContain(
            editor.Document.Paragraphs[0].Runs,
            run => run.IsBold);
        editor.History.Undo();
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, Texts(editor));
    }

    [Fact]
    public void LoadDocument_ReplacesCanonicalSessionAndResetsCaretAndHistory()
    {
        DocumentEditor editor = new();
        editor.InsertText("old");
        var previousSession = editor.ActiveEditingSession;
        GostDocument replacement = CreateDocument("new");

        editor.LoadDocument(replacement);

        Assert.NotSame(previousSession, editor.ActiveEditingSession);
        Assert.Same(replacement, editor.Document);
        Assert.Equal(new DocumentPosition(0, 0), editor.CaretPosition);
        Assert.False(editor.History.CanUndo);
        Assert.Equal(0, editor.CanonicalRevision);
    }

    [Fact]
    public async Task CanonicalTyping_SaveAndOpen_PreservesTextFormattingAndLegacyData()
    {
        using TestTemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.DirectoryPath, "cutover.gost");
        GostDocument document = CreateDocument("Start");
        document.TitlePage.University = "Test University";
        DocumentEditor editor = new(document, new ImageService());
        editor.InsertImage(TestImageData.CreatePng(), 16, 16);
        editor.CaretPosition = new DocumentPosition(0, 5);
        editor.InsertText(" exact");
        editor.SelectionAnchor = new DocumentPosition(0, 6);
        editor.CaretPosition = new DocumentPosition(0, 11);
        editor.ToggleItalic();

        DocumentSessionState state = new();
        state.StartNew(document);
        state.RecordMutation();
        DocumentSaveService save = new(
            new ArchiveService(),
            state,
            new PersistenceIoCoordinator());
        DocumentSaveResult result = await save.SaveAsync(document, path);
        GostDocument reopened = await new ArchiveService().LoadAsync(path);

        Assert.True(result.IsCurrentRevision);
        Assert.Equal("Start exact", reopened.Paragraphs[0].GetPlainText());
        Assert.Contains(
            reopened.Paragraphs[0].Runs,
            run => run.Text == "exact" && run.IsItalic);
        Assert.Equal("Test University", reopened.TitlePage.University);
        Assert.True(reopened.Paragraphs[1].IsImage);
        Assert.Single(reopened.Images);
    }

    [Fact]
    public async Task CanonicalTyping_AutoSaveAndRecovery_UsesCurrentProjection()
    {
        using TestTemporaryDirectory temporary = new();
        GostDocument document = CreateDocument("draft");
        DocumentEditor editor = new(document);
        DocumentSessionState state = new();
        state.StartNew(document);
        editor.DocumentChanged += (_, _) => state.RecordMutation();
        RecoveryStorageService recovery = new(
            new ArchiveService(),
            temporary.DirectoryPath);
        using AutoSaveService autoSave = new(
            recovery,
            state,
            new PersistenceIoCoordinator(),
            TimeSpan.FromMinutes(1));

        editor.CaretPosition = new DocumentPosition(0, 5);
        editor.InsertText(" recovered");
        bool saved = await autoSave.SaveIfNeededAsync(() => editor.Document);
        GostDocument restored = await recovery.LoadDocumentAsync();

        Assert.True(saved);
        Assert.Equal(
            "draft recovered",
            Assert.Single(restored.Paragraphs).GetPlainText());
    }

    [Fact]
    public async Task CanonicalTyping_DocxExport_ContainsCurrentText()
    {
        using TestTemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.DirectoryPath, "cutover.docx");
        GostDocument document = CreateDocument("export");
        DocumentEditor editor = new(document);
        editor.CaretPosition = new DocumentPosition(0, 6);
        editor.InsertText(" current");
        DocumentSessionState state = new();
        state.StartNew(document);
        state.RecordMutation();
        DocumentExportService export = new(
            new ExportService(),
            state,
            new PersistenceIoCoordinator());

        await export.ExportToDocxAsync(document, path);

        using WordprocessingDocument package =
            WordprocessingDocument.Open(path, isEditable: false);
        MainDocumentPart mainPart = Assert.IsType<MainDocumentPart>(
            package.MainDocumentPart);
        W.Document wordDocument = Assert.IsType<W.Document>(
            mainPart.Document);
        string text = string.Concat(
            wordDocument.Descendants<W.Text>()
                .Select(item => item.Text));
        Assert.Contains("export current", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MixedTextAndImageHistory_PreservesSessionNodeIdAndRevision()
    {
        DocumentEditor editor = new();
        var session = editor.ActiveEditingSession;
        DocumentNodeId paragraphId = Assert.IsType<ParagraphBlock>(
            Assert.Single(session.Document.EnumerateBlocks())).Id;

        editor.InsertText("A");
        long afterText = editor.CanonicalRevision;
        editor.InsertImage(TestImageData.CreatePng(), 16, 16);
        long afterImage = editor.CanonicalRevision;
        editor.History.Undo();
        long afterImageUndo = editor.CanonicalRevision;
        editor.History.Undo();

        Assert.Same(session, editor.ActiveEditingSession);
        Assert.Equal(string.Empty, editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal(
            paragraphId,
            Assert.IsType<ParagraphBlock>(
                Assert.Single(session.Document.EnumerateBlocks())).Id);
        Assert.True(afterText < afterImage);
        Assert.True(afterImage < afterImageUndo);
        Assert.True(afterImageUndo < editor.CanonicalRevision);

        editor.History.Redo();
        editor.History.Redo();
        Assert.Equal("A", editor.Document.Paragraphs[0].GetPlainText());
        Assert.True(editor.Document.Paragraphs[1].IsImage);
        Assert.Same(session, editor.ActiveEditingSession);
    }

    [Fact]
    public void NewLegacyCommandAfterCanonicalUndo_DiscardsCanonicalRedo()
    {
        DocumentEditor editor = new();
        var session = editor.ActiveEditingSession;
        editor.InsertText("A");
        editor.History.Undo();
        Assert.True(session.History.CanRedo);

        editor.InsertImage(TestImageData.CreatePng(), 16, 16);

        Assert.False(editor.History.CanRedo);
        Assert.False(session.History.CanRedo);
    }

    [Fact]
    public void SameValueFormatting_IsNoOpAndKeepsInlineIdentity()
    {
        DocumentEditor editor = new(CreateDocument("plain"));
        editor.SelectionAnchor = new DocumentPosition(0, 0);
        editor.CaretPosition = new DocumentPosition(0, 5);
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(
            Assert.Single(editor.ActiveEditingSession.Document
                .EnumerateBlocks()));
        DocumentNodeId inlineId = Assert.Single(paragraph.Inlines).Id;
        long revision = editor.CanonicalRevision;

        editor.SetFontSize(14);

        Assert.Equal(revision, editor.CanonicalRevision);
        Assert.Equal(inlineId, Assert.Single(paragraph.Inlines).Id);
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void SameValueTypingStyle_IsNoOpAtCollapsedCaret()
    {
        DocumentEditor editor = new(CreateDocument("plain"))
        {
            CaretPosition = new DocumentPosition(0, 2)
        };

        editor.SetFontSize(14);

        Assert.Equal(0, editor.CanonicalRevision);
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void TypingStyleUndo_ClearsCachedStyleBeforeTyping()
    {
        DocumentEditor editor = new();
        editor.ToggleBold();
        editor.History.Undo();

        editor.InsertText("normal");

        Assert.DoesNotContain(
            editor.Document.Paragraphs[0].Runs,
            run => run.IsBold);
    }

    [Fact]
    public void ClearFormattingUndo_RestoresSelectionAndFormatting()
    {
        GostDocument document = CreateDocument("bold");
        document.Paragraphs[0].Runs[0].IsBold = true;
        DocumentEditor editor = new(document)
        {
            SelectionAnchor = new DocumentPosition(0, 0),
            CaretPosition = new DocumentPosition(0, 4)
        };

        editor.ClearFormatting();
        Assert.False(editor.HasSelection);
        editor.History.Undo();

        Assert.True(editor.HasSelection);
        Assert.Equal(new DocumentPosition(0, 0), editor.SelectionAnchor);
        Assert.Equal(new DocumentPosition(0, 4), editor.CaretPosition);
        Assert.True(editor.Document.Paragraphs[0].Runs[0].IsBold);
    }

    [Fact]
    public void LegacyProjection_RejectsLossyFontFamilyWithoutRevision()
    {
        DocumentEditor editor = new(CreateDocument("text"));
        var session = editor.ActiveEditingSession;
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(
            Assert.Single(session.Document.EnumerateBlocks()));
        DocumentRange range = new(
            new DocumentLocation(paragraph.Id, 0),
            new DocumentLocation(paragraph.Id, paragraph.TextLength));

        Assert.Throws<NotSupportedException>(() =>
            session.Formatting.SetFontFamily(range, "Inter"));
        Assert.Equal(0, editor.CanonicalRevision);
        Assert.Equal("text", editor.Document.Paragraphs[0].GetPlainText());
    }

    [Fact]
    public void DirectCanonicalEdit_RejectsLossyStyleAtomically()
    {
        DocumentEditor editor = new(CreateDocument("text"));
        var session = editor.ActiveEditingSession;
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(
            Assert.Single(session.Document.EnumerateBlocks()));

        Assert.Throws<NotSupportedException>(() =>
            session.Text.InsertText(
                new DocumentLocation(paragraph.Id, 4),
                " loss",
                new TextStyle { FontFamily = "Inter" }));

        Assert.Equal(0, session.Revision);
        Assert.False(session.History.CanUndo);
        Assert.Equal("text", paragraph.GetPlainText());
        Assert.Equal("text", editor.Document.Paragraphs[0].GetPlainText());
    }

    [Fact]
    public void DirectCanonicalEdit_RejectsUnsupportedBlockAtomically()
    {
        DocumentEditor editor = new(CreateDocument("text"));
        var session = editor.ActiveEditingSession;
        DocumentSection section = Assert.Single(session.Document.Sections);
        TableBlock table = new();

        Assert.Throws<NotSupportedException>(() => session.Execute(
            new InsertBlockOperation(section.Id, 1, table)));

        Assert.Equal(0, session.Revision);
        Assert.False(session.History.CanUndo);
        Assert.Null(session.Document.FindBlock(table.Id));
        Assert.Single(session.Document.EnumerateBlocks());
        Assert.Single(editor.Document.Paragraphs);
    }

    [Fact]
    public void CrossImageDeleteUndo_PreservesOlderCanonicalCommandIdentity()
    {
        DocumentEditor editor = new();
        editor.PasteText("one\ntwo");
        editor.CaretPosition = new DocumentPosition(1, 3);
        editor.InsertText("!");
        ParagraphBlock edited = Assert.IsType<ParagraphBlock>(
            editor.ActiveEditingSession.Document.EnumerateBlocks().ElementAt(1));
        DocumentNodeId editedId = edited.Id;

        editor.CaretPosition = new DocumentPosition(0, 3);
        editor.InsertImage(TestImageData.CreatePng(), 16, 16);
        editor.SelectionAnchor = new DocumentPosition(0, 3);
        editor.CaretPosition = new DocumentPosition(2, 4);
        editor.DeleteSelection();
        Assert.Single(editor.Document.Paragraphs);

        editor.History.Undo();
        Assert.Equal(editedId, Assert.IsType<ParagraphBlock>(
            editor.ActiveEditingSession.Document.EnumerateBlocks()
                .ElementAt(2)).Id);
        editor.History.Undo();
        editor.History.Undo();

        Assert.Equal(new[] { "one", "two" }, Texts(editor));
        Assert.Equal(editedId, Assert.IsType<ParagraphBlock>(
            editor.ActiveEditingSession.Document.EnumerateBlocks()
                .ElementAt(1)).Id);
    }

    [Fact]
    public void MousePositionedCaret_TypesAtCanonicalLocation()
    {
        DocumentEditor editor = new(CreateDocument("ac"));
        editor.CaretPosition = new DocumentPosition(0, 1);

        editor.InsertText("b");

        Assert.Equal("abc", editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal(new DocumentPosition(0, 2), editor.CaretPosition);
    }

    [Fact]
    public async Task EditDuringAsyncSave_PublishesCapturedRevisionOnly()
    {
        using TestTemporaryDirectory temporary = new();
        string path = Path.Combine(temporary.DirectoryPath, "async.gost");
        GostDocument document = CreateDocument("first");
        DocumentEditor editor = new(document);
        DocumentSessionState state = new();
        state.StartNew(document);
        editor.DocumentChanged += (_, _) => state.RecordMutation();
        ManualAsyncGate gate = new();
        CapturingArchiveService archive = new(gate);
        DocumentSaveService save = new(
            archive,
            state,
            new PersistenceIoCoordinator());

        Task<DocumentSaveResult> pending = save.SaveAsync(document, path);
        await gate.WaitUntilReachedAsync();
        editor.CaretPosition = new DocumentPosition(0, 5);
        editor.InsertText(" second");
        gate.Release();
        DocumentSaveResult result = await pending;

        Assert.False(result.IsCurrentRevision);
        Assert.Equal("first", archive.CapturedText);
        Assert.Equal("first second", document.Paragraphs[0].GetPlainText());
        Assert.True(state.IsDirty);
    }

    private static GostDocument CreateDocument(string text)
    {
        GostDocument document = new();
        document.Paragraphs.Clear();
        document.Paragraphs.Add(new Paragraph
        {
            Runs = { new TextRun(text) }
        });
        return document;
    }

    private static string[] Texts(DocumentEditor editor) =>
        editor.Document.Paragraphs
            .Select(paragraph => paragraph.GetPlainText())
            .ToArray();

    private sealed class CapturingArchiveService : IArchiveService
    {
        private readonly ManualAsyncGate _gate;

        public CapturingArchiveService(ManualAsyncGate gate) => _gate = gate;

        public string? CapturedText { get; private set; }

        public GostDocument CreateNew() => throw new NotSupportedException();

        public Task<GostDocument> LoadAsync(string filePath) =>
            throw new NotSupportedException();

        public Task<GostDocument> LoadAsync(Stream stream) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            GostDocument document,
            string filePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task SaveAsync(
            DocumentPersistenceSnapshot snapshot,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            CapturedText = snapshot.Document.Paragraphs[0].GetPlainText();
            await _gate.SignalAndWaitAsync(cancellationToken);
        }

        public Task SaveAsync(
            GostDocument document,
            Stream stream,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            DocumentPersistenceSnapshot snapshot,
            Stream stream,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
