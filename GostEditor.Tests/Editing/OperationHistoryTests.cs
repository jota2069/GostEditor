using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Editing;
using GostEditor.Core.Editing.Operations;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.Editing;

public sealed class OperationHistoryTests
{
    [Fact]
    public void InsertText_CanUndoAndRedoWithoutDocumentSnapshot()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("Привет");

        session.Text.InsertText(
            new DocumentLocation(paragraph.Id, 6),
            "мир");

        Assert.Equal("Приветмир", paragraph.GetPlainText());
        Assert.True(session.History.CanUndo);

        Assert.True(session.History.Undo());
        Assert.Equal("Привет", paragraph.GetPlainText());

        Assert.True(session.History.Redo());
        Assert.Equal("Приветмир", paragraph.GetPlainText());
    }

    [Fact]
    public void DeleteText_CanUndoAndRedo()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("abcdef");

        session.Text.DeleteText(
            new DocumentLocation(paragraph.Id, 2),
            3);

        Assert.Equal("abf", paragraph.GetPlainText());
        session.History.Undo();
        Assert.Equal("abcdef", paragraph.GetPlainText());
        session.History.Redo();
        Assert.Equal("abf", paragraph.GetPlainText());
    }

    [Fact]
    public void Transaction_UndoRevertsAllOperationsInReverseOrder()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("A");
        EditTransaction transaction = new("Пакетный ввод");
        transaction.Add(new InsertTextOperation(paragraph.Id, 1, "B"));
        transaction.Add(new InsertTextOperation(paragraph.Id, 2, "C"));

        session.Execute(transaction);

        Assert.Equal("ABC", paragraph.GetPlainText());
        Assert.Equal(1, session.History.UndoCount);

        session.History.Undo();
        Assert.Equal("A", paragraph.GetPlainText());

        session.History.Redo();
        Assert.Equal("ABC", paragraph.GetPlainText());
    }

    [Fact]
    public void TextFormatting_CanUndo()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("Текст");
        DocumentRange range = new(
            new DocumentLocation(paragraph.Id, 0),
            new DocumentLocation(paragraph.Id, 5));

        session.Formatting.SetBold(range, true);

        TextInline run = Assert.IsType<TextInline>(
            Assert.Single(paragraph.Inlines));
        Assert.True(run.Style.IsBold);

        session.History.Undo();
        run = Assert.IsType<TextInline>(Assert.Single(paragraph.Inlines));
        Assert.False(run.Style.IsBold);
    }

    [Fact]
    public void ParagraphFormatting_CanUndo()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("Текст");
        ParagraphProperties changed = paragraph.Properties.Clone();
        changed.Alignment = GostAlignment.Center;
        changed.FirstLineIndent = 0;

        session.Formatting.SetParagraphProperties(paragraph.Id, changed);

        Assert.Equal(GostAlignment.Center, paragraph.Properties.Alignment);
        Assert.Equal(0, paragraph.Properties.FirstLineIndent);

        session.History.Undo();
        Assert.Equal(GostAlignment.Left, paragraph.Properties.Alignment);
        Assert.Equal(47, paragraph.Properties.FirstLineIndent);
    }

    [Fact]
    public void InsertAndRemoveBlock_CanUndoAndRedo()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("Первый");
        DocumentSection section = session.Document.Sections[0];
        ParagraphBlock second = new();
        second.Inlines.Add(new TextInline("Второй"));

        session.Execute(new InsertBlockOperation(
            section.Id,
            1,
            second));
        Assert.Equal(2, section.Blocks.Count);

        session.Execute(new RemoveBlockOperation(paragraph.Id));
        Assert.Single(section.Blocks);
        Assert.Equal(second.Id, section.Blocks[0].Id);

        session.History.Undo();
        Assert.Equal(2, section.Blocks.Count);
        Assert.Equal(paragraph.Id, section.Blocks[0].Id);

        session.History.Undo();
        Assert.Single(section.Blocks);
        Assert.Equal(paragraph.Id, section.Blocks[0].Id);

        session.History.Redo();
        session.History.Redo();
        Assert.Single(section.Blocks);
        Assert.Equal(second.Id, section.Blocks[0].Id);
    }

    [Fact]
    public void NewOperation_ClearsRedoBranch()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("A");

        session.Text.InsertText(
            new DocumentLocation(paragraph.Id, 1),
            "B");
        session.History.Undo();
        Assert.True(session.History.CanRedo);

        session.Text.InsertText(
            new DocumentLocation(paragraph.Id, 1),
            "C");

        Assert.False(session.History.CanRedo);
        Assert.Equal("AC", paragraph.GetPlainText());
    }

    private static (DocumentEditingSession, ParagraphBlock) CreateSession(
        string text)
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        ParagraphBlock paragraph = new();
        paragraph.Inlines.Add(new TextInline(text));
        section.Blocks.Add(paragraph);
        document.Sections.Add(section);
        return (new DocumentEditingSession(document), paragraph);
    }
}

public sealed class StructuredEditingIntegrationTests
{
    [Fact]
    public void ReplaceRangeAcrossParagraphs_IsSingleUndoOperation()
    {
        (DocumentEditingSession session, ParagraphBlock first, ParagraphBlock second) =
            CreateTwoParagraphSession("abc", "def");
        DocumentRange range = new(
            new DocumentLocation(first.Id, 1),
            new DocumentLocation(second.Id, 2));

        session.Text.ReplaceRange(range, "X");

        ParagraphBlock only = Assert.IsType<ParagraphBlock>(
            Assert.Single(session.Document.Sections[0].Blocks));
        Assert.Equal("aXf", only.GetPlainText());
        Assert.Equal(1, session.History.UndoCount);

        Assert.True(session.History.Undo());
        Assert.Equal(
            new[] { "abc", "def" },
            session.Document.Sections[0].Blocks
                .Cast<ParagraphBlock>()
                .Select(item => item.GetPlainText()));

        Assert.True(session.History.Redo());
        only = Assert.IsType<ParagraphBlock>(
            Assert.Single(session.Document.Sections[0].Blocks));
        Assert.Equal("aXf", only.GetPlainText());
    }

    [Fact]
    public void InsertParagraphBreak_PreservesStableBlockIdentifiers()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("abcdef");
        DocumentNodeId originalId = paragraph.Id;

        session.Text.InsertParagraphBreak(
            new DocumentLocation(paragraph.Id, 3));

        Assert.Equal(2, session.Document.Sections[0].Blocks.Count);
        ParagraphBlock first = Assert.IsType<ParagraphBlock>(
            session.Document.Sections[0].Blocks[0]);
        ParagraphBlock second = Assert.IsType<ParagraphBlock>(
            session.Document.Sections[0].Blocks[1]);
        Assert.Equal(originalId, first.Id);
        Assert.Equal("abc", first.GetPlainText());
        Assert.Equal("def", second.GetPlainText());
        DocumentNodeId secondId = second.Id;

        session.History.Undo();
        first = Assert.IsType<ParagraphBlock>(
            Assert.Single(session.Document.Sections[0].Blocks));
        Assert.Equal(originalId, first.Id);
        Assert.Equal("abcdef", first.GetPlainText());

        session.History.Redo();
        second = Assert.IsType<ParagraphBlock>(
            session.Document.Sections[0].Blocks[1]);
        Assert.Equal(secondId, second.Id);
    }

    [Fact]
    public void InsertParagraphBreak_UndoAndRedoRestoreValidCaret()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("abcdef");
        session.Selection.MoveCaret(new DocumentLocation(paragraph.Id, 3));

        session.Text.InsertParagraphBreakAtCaret();
        DocumentNodeId secondId = session.Selection.Caret.BlockId;
        Assert.NotEqual(paragraph.Id, secondId);
        Assert.Equal(0, session.Selection.Caret.Offset);

        Assert.True(session.History.Undo());
        Assert.Equal(new DocumentLocation(paragraph.Id, 3), session.Selection.Caret);

        Assert.True(session.History.Redo());
        Assert.Equal(new DocumentLocation(secondId, 0), session.Selection.Caret);

        Assert.True(session.History.Undo());
        session.Text.InsertText(session.Selection.Caret, "X");
        Assert.Equal("abcXdef", paragraph.GetPlainText());
    }

    [Fact]
    public void DeleteText_AtParagraphEnd_DoesNotCreateHistoryEntry()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("abc");

        session.Text.DeleteText(
            new DocumentLocation(paragraph.Id, paragraph.TextLength),
            1);

        Assert.Equal("abc", paragraph.GetPlainText());
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(0, session.ChangeVersion);
    }

    [Fact]
    public void FormattingAcrossParagraphs_IsOneTransaction()
    {
        (DocumentEditingSession session, ParagraphBlock first, ParagraphBlock second) =
            CreateTwoParagraphSession("abc", "def");
        DocumentRange range = new(
            new DocumentLocation(first.Id, 1),
            new DocumentLocation(second.Id, 2));

        session.Formatting.SetBold(range, true);

        Assert.Equal(1, session.History.UndoCount);
        Assert.Contains(
            first.Inlines.OfType<TextInline>(),
            inline => inline.Text == "bc" && inline.Style.IsBold);
        Assert.Contains(
            second.Inlines.OfType<TextInline>(),
            inline => inline.Text == "de" && inline.Style.IsBold);

        session.History.Undo();
        Assert.All(
            first.Inlines.Concat(second.Inlines).OfType<TextInline>(),
            inline => Assert.False(inline.Style.IsBold));
    }

    [Fact]
    public void Selection_SelectAllCoversFirstAndLastParagraph()
    {
        (DocumentEditingSession session, ParagraphBlock first, ParagraphBlock second) =
            CreateTwoParagraphSession("abc", "def");

        session.Selection.SelectAll();

        DocumentRange range = session.Selection.GetRange();
        Assert.Equal(new DocumentLocation(first.Id, 0), range.Start);
        Assert.Equal(new DocumentLocation(second.Id, 3), range.End);
    }

    [Fact]
    public void Session_ReportsOperationAndMonotonicVersion()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("A");
        List<OperationHistoryChangedEventArgs> events = new();
        session.Changed += (_, args) => events.Add(args);

        session.Text.InsertText(
            new DocumentLocation(paragraph.Id, 1),
            "B");
        session.History.Undo();
        session.History.Redo();

        Assert.Equal(3, events.Count);
        Assert.Equal(OperationHistoryChangeKind.Executed, events[0].Kind);
        Assert.Equal(OperationHistoryChangeKind.Undone, events[1].Kind);
        Assert.Equal(OperationHistoryChangeKind.Redone, events[2].Kind);
        Assert.Equal(new long[] { 1, 2, 3 }, events.Select(item => item.Version));
        Assert.Equal(3L, session.ChangeVersion);
    }

    private static (DocumentEditingSession, ParagraphBlock) CreateSession(
        string text)
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        ParagraphBlock paragraph = new();
        paragraph.Inlines.Add(new TextInline(text));
        section.Blocks.Add(paragraph);
        document.Sections.Add(section);
        return (new DocumentEditingSession(document), paragraph);
    }

    private static (
        DocumentEditingSession Session,
        ParagraphBlock First,
        ParagraphBlock Second) CreateTwoParagraphSession(
        string firstText,
        string secondText)
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        ParagraphBlock first = new();
        first.Inlines.Add(new TextInline(firstText));
        ParagraphBlock second = new();
        second.Inlines.Add(new TextInline(secondText));
        section.Blocks.Add(first);
        section.Blocks.Add(second);
        document.Sections.Add(section);
        return (new DocumentEditingSession(document), first, second);
    }
}
