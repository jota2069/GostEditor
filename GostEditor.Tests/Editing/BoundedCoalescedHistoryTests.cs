using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Editing;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.Commands;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.Editing;

public sealed class BoundedCoalescedHistoryTests
{
    [Fact]
    public void Typing_OneThousandCharacters_IsOneLinearUndoGroup()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession(string.Empty);

        for (int index = 0; index < 1_000; index++)
        {
            session.Text.InsertTyping(
                new DocumentLocation(paragraph.Id, index),
                "x");
        }

        Assert.Equal(1_000, session.Revision);
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(1_000, paragraph.TextLength);
        Assert.InRange(session.History.RetainedPayloadBytes, 1, 500_000);

        Assert.True(session.History.Undo());
        Assert.Equal(string.Empty, paragraph.GetPlainText());
        Assert.Equal(1_001, session.Revision);

        Assert.True(session.History.Redo());
        Assert.Equal(new string('x', 1_000), paragraph.GetPlainText());
        Assert.Equal(1_002, session.Revision);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(10_000)]
    [InlineData(50_000)]
    public void TypingCorpus_RetainedPayloadGrowth_IsBoundedAndNearLinear(
        int characterCount)
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession(string.Empty,
                new OperationHistoryPolicy
                {
                    MaxRetainedBytes = 4 * 1024 * 1024
                });
        for (int offset = 0; offset < characterCount; offset++)
        {
            session.Text.InsertTyping(
                new DocumentLocation(paragraph.Id, offset),
                "x");
        }

        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(characterCount, paragraph.TextLength);
        Assert.True(
            session.History.RetainedPayloadBytes <=
            512L * characterCount,
            $"Retained {session.History.RetainedPayloadBytes} bytes for " +
            $"{characterCount} characters.");
    }

    [Fact]
    public void CompositionCommit_IsIsolatedFromAdjacentTyping()
    {
        DocumentEditor editor = new();
        editor.InsertText("a");
        editor.InsertCompositionText("漢字");
        editor.InsertText("b");

        Assert.Equal(3, editor.History.UndoCount);
        editor.History.Undo();
        Assert.Equal("a漢字", editor.Document.Paragraphs[0].GetPlainText());
        editor.History.Undo();
        Assert.Equal("a", editor.Document.Paragraphs[0].GetPlainText());
    }

    [Fact]
    public void OversizedPaste_IsNotRetainedPastMemoryBudget()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession(
                string.Empty,
                new OperationHistoryPolicy
                {
                    MaxRetainedBytes = 8 * 1024,
                    MaxUndoGroups = 10
                });
        string largeText = new('x', 100_000);

        session.Text.ReplaceSelection(largeText);

        Assert.Equal(largeText, paragraph.GetPlainText());
        Assert.Equal(1, session.Revision);
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(0, session.History.RetainedPayloadBytes);
    }

    [Fact]
    public void GroupedUndo_WhenChildIndexUpdateFails_CompensatesCurrentChild()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession(string.Empty);
        MutableCounter counter = new();
        FaultingGroupedOperation first = new(counter, paragraph.Id, 0);
        FaultingGroupedOperation second = new(counter, paragraph.Id, 1);
        session.Execute(first);
        session.Execute(second);
        second.FailIndexUpdate = true;

        Assert.Throws<InvalidOperationException>(() =>
            session.History.Undo());

        Assert.Equal(2, counter.Value);
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(0, session.History.RedoCount);
    }

    [Fact]
    public void CompatibilityHistory_PrunesOldestBuriedCanonicalEviction()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession(
                string.Empty,
                new OperationHistoryPolicy
                {
                    MaxRetainedBytes = 1024 * 1024,
                    MaxUndoGroups = 2
                });
        CommandManager commands = new();

        for (int offset = 0; offset < 3; offset++)
        {
            int capturedOffset = offset;
            commands.ExecuteCommand(new CanonicalDocumentCommand(
                session,
                () => session.Text.InsertText(
                    new DocumentLocation(paragraph.Id, capturedOffset),
                    "x")));
        }

        Assert.Equal(2, session.History.UndoCount);
        Assert.Equal(2, commands.UndoCount);
        commands.Undo();
        commands.Undo();
        Assert.Equal("x", paragraph.GetPlainText());
        Assert.False(commands.CanUndo);
    }

    [Fact]
    public void BackspaceAndDeleteForward_FormSeparateDirectionalGroups()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession("abcdef");

        session.Text.DeleteBackward(
            new DocumentLocation(paragraph.Id, 5), 1);
        session.Text.DeleteBackward(
            new DocumentLocation(paragraph.Id, 4), 1);

        Assert.Equal("abcd", paragraph.GetPlainText());
        Assert.Equal(1, session.History.UndoCount);
        Assert.True(session.History.Undo());
        Assert.Equal("abcdef", paragraph.GetPlainText());

        session.History.BreakCoalescing();
        session.Text.DeleteForward(
            new DocumentLocation(paragraph.Id, 1), 1);
        session.Text.DeleteForward(
            new DocumentLocation(paragraph.Id, 1), 1);

        Assert.Equal("adef", paragraph.GetPlainText());
        Assert.Equal(1, session.History.UndoCount);
        Assert.True(session.History.Undo());
        Assert.Equal("abcdef", paragraph.GetPlainText());
    }

    [Fact]
    public void TimeStyleAndExplicitBoundaries_EndTypingGroup()
    {
        ManualTimeProvider clock = new();
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession(string.Empty, timeProvider: clock);
        TextStyle normal = new();

        session.Text.InsertTyping(
            new DocumentLocation(paragraph.Id, 0), "a", normal);
        clock.Advance(TimeSpan.FromSeconds(1));
        session.Text.InsertTyping(
            new DocumentLocation(paragraph.Id, 1), "b", normal);
        session.History.BreakCoalescing();
        session.Text.InsertTyping(
            new DocumentLocation(paragraph.Id, 2), "c", normal);
        session.Text.InsertTyping(
            new DocumentLocation(paragraph.Id, 3), "d",
            new TextStyle { IsBold = true });

        Assert.Equal("abcd", paragraph.GetPlainText());
        Assert.Equal(4, session.History.UndoCount);
    }

    [Fact]
    public void DirectSelectionMovement_EndsTypingGroupEvenIfCaretReturns()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession(string.Empty);
        session.Text.InsertTyping(
            new DocumentLocation(paragraph.Id, 0), "a");

        session.Selection.MoveCaret(new DocumentLocation(paragraph.Id, 0));
        session.Selection.MoveCaret(new DocumentLocation(paragraph.Id, 1));
        session.Text.InsertTyping(
            new DocumentLocation(paragraph.Id, 1), "b");

        Assert.Equal(2, session.History.UndoCount);
        Assert.True(session.History.Undo());
        Assert.Equal("a", paragraph.GetPlainText());
    }

    [Fact]
    public void MemoryBudget_EvictsOldestUndoWithoutChangingDocumentOrRevision()
    {
        (DocumentEditingSession session, ParagraphBlock paragraph) =
            CreateSession(
                "A",
                new OperationHistoryPolicy
                {
                    MaxRetainedBytes = 600,
                    MaxUndoGroups = 10
                });

        session.Text.InsertText(
            new DocumentLocation(paragraph.Id, 1), "B");
        session.Text.InsertText(
            new DocumentLocation(paragraph.Id, 2), "C");

        Assert.Equal("ABC", paragraph.GetPlainText());
        Assert.Equal(2, session.Revision);
        Assert.Equal(1, session.History.UndoCount);
        Assert.InRange(session.History.RetainedPayloadBytes, 1, 600);

        Assert.True(session.History.Undo());
        Assert.Equal("AB", paragraph.GetPlainText());
        Assert.False(session.History.Undo());
        Assert.Equal(3, session.Revision);
    }

    [Fact]
    public void EditorTyping_CoalescesCanonicalAndCompatibilityUndoStacks()
    {
        DocumentEditor editor = new();

        editor.InsertText("a");
        editor.InsertText("b");
        editor.InsertText("c");

        Assert.Equal(3, editor.CanonicalRevision);
        Assert.Equal(1, editor.ActiveEditingSession.History.UndoCount);
        Assert.Equal(1, editor.History.UndoCount);

        editor.History.Undo();

        Assert.Equal(string.Empty, editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal(4, editor.CanonicalRevision);
        Assert.Equal(1, editor.History.RedoCount);

        editor.History.Redo();
        Assert.Equal("abc", editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal(5, editor.CanonicalRevision);
    }

    [Fact]
    public void CaretMoveAndSaveBoundary_EndEditorTypingGroup()
    {
        DocumentEditor editor = new();
        editor.InsertText("ab");

        editor.CaretPosition = new DocumentPosition(0, 0);
        editor.CaretPosition = new DocumentPosition(0, 2);
        editor.InsertText("c");
        editor.BreakHistoryCoalescing();
        editor.InsertText("d");

        Assert.Equal(3, editor.History.UndoCount);
        editor.History.Undo();
        Assert.Equal("abc", editor.Document.Paragraphs[0].GetPlainText());
        editor.History.Undo();
        Assert.Equal("ab", editor.Document.Paragraphs[0].GetPlainText());
        editor.History.Undo();
        Assert.Equal(string.Empty, editor.Document.Paragraphs[0].GetPlainText());
    }

    private static (DocumentEditingSession, ParagraphBlock) CreateSession(
        string text,
        OperationHistoryPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        ParagraphBlock paragraph = new();
        paragraph.Inlines.Add(new TextInline(text));
        section.Blocks.Add(paragraph);
        document.Sections.Add(section);
        return (new DocumentEditingSession(document, policy, timeProvider),
            paragraph);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private sealed class MutableCounter
    {
        internal int Value { get; set; }
    }

    private sealed class FaultingGroupedOperation :
        IEditOperation,
        IHistoryCoalescingOperation
    {
        private readonly MutableCounter _counter;
        private readonly DocumentNodeId _paragraphId;

        internal FaultingGroupedOperation(
            MutableCounter counter,
            DocumentNodeId paragraphId,
            int offset)
        {
            _counter = counter;
            _paragraphId = paragraphId;
            CoalescingDescriptor = new HistoryCoalescingDescriptor(
                TextCoalescingKind.Typing,
                paragraphId,
                offset,
                1,
                0);
        }

        internal bool FailIndexUpdate { get; set; }

        public string Description => "Fault-injection operation";

        public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
            FailIndexUpdate
                ? new[] { DocumentNodeId.New() }
                : new[] { _paragraphId };

        public DocumentIndexUpdateKind IndexUpdateKind => FailIndexUpdate
            ? DocumentIndexUpdateKind.AffectedParagraphs
            : DocumentIndexUpdateKind.None;

        public HistoryCoalescingDescriptor CoalescingDescriptor { get; }

        public void Apply(DocumentEditingContext context) => _counter.Value++;

        public void Revert(DocumentEditingContext context) => _counter.Value--;
    }
}
