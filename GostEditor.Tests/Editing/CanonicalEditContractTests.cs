using System.Text.Json;
using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Editing;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.Editing;

public sealed class CanonicalEditContractTests
{
    private static readonly DocumentNodeId RootId = Id(1);
    private static readonly DocumentNodeId FirstSectionId = Id(2);
    private static readonly DocumentNodeId SecondSectionId = Id(3);
    private static readonly DocumentNodeId FirstParagraphId = Id(4);
    private static readonly DocumentNodeId SecondParagraphId = Id(5);
    private static readonly DocumentNodeId FirstInlineId = Id(6);
    private static readonly DocumentNodeId SecondInlineId = Id(7);

    private readonly CanonicalEditExecutor _executor = new();

    [Fact]
    public void Apply_UsesExplicitTransactionAndRevisionContract()
    {
        DocumentEditingSession session = CreateSession();
        List<OperationHistoryChangedEventArgs> events = new();
        session.Changed += (_, args) => events.Add(args);
        CanonicalEditEnvelope envelope = Envelope(
            0,
            new InsertTextData(FirstParagraphId, 5, "!"));

        CanonicalApplyResult result = _executor.Apply(session, envelope);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.CurrentRevision);
        Assert.Equal(1, session.Revision);
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(
            "Hello!",
            GetParagraph(session, FirstParagraphId).GetPlainText());
        OperationHistoryChangedEventArgs changed = Assert.Single(events);
        Assert.Equal(envelope.TransactionId, changed.TransactionId);
        Assert.Equal(1, changed.Version);
    }

    [Fact]
    public void Apply_TransactionCreatesOneRevisionAndUndoRedoCreateNewRevisions()
    {
        DocumentEditingSession session = CreateSession();
        CanonicalEditEnvelope envelope = Envelope(
            0,
            new InsertTextData(FirstParagraphId, 5, "!"),
            new ChangeTextStyleData(
                SecondParagraphId,
                0,
                5,
                new CanonicalTextStylePatch(IsBold: true)));

        Assert.True(_executor.Apply(session, envelope).IsSuccess);
        Assert.Equal(1, session.Revision);
        Assert.Equal(1, session.History.UndoCount);

        Assert.True(session.History.Undo());
        Assert.Equal(2, session.Revision);
        Assert.Equal("Hello", GetParagraph(session, FirstParagraphId).GetPlainText());

        Assert.True(session.History.Redo());
        Assert.Equal(3, session.Revision);
        Assert.Equal("Hello!", GetParagraph(session, FirstParagraphId).GetPlainText());

        session.History.Clear();
        Assert.Equal(3, session.Revision);
    }

    [Fact]
    public void Apply_UndoRedoPreserveGeneratedInlineIdentities()
    {
        DocumentEditingSession session = CreateSession();
        CanonicalEditEnvelope envelope = Envelope(
            0,
            new InsertTextData(FirstParagraphId, 2, "X"));
        Assert.True(_executor.Apply(session, envelope).IsSuccess);
        string appliedFingerprint = CanonicalDocumentFingerprint.Compute(
            session.Document);
        DocumentNodeId[] appliedIds = GetParagraph(session, FirstParagraphId)
            .Inlines.Select(inline => inline.Id).ToArray();

        Assert.True(session.History.Undo());
        Assert.True(session.History.Redo());

        Assert.Equal(
            appliedFingerprint,
            CanonicalDocumentFingerprint.Compute(session.Document));
        Assert.Equal(
            appliedIds,
            GetParagraph(session, FirstParagraphId)
                .Inlines.Select(inline => inline.Id));
    }

    [Fact]
    public void Split_UndoRedoPreserveStructureAndGeneratedIdentities()
    {
        DocumentEditingSession session = CreateSession();
        DocumentNodeId splitId = Id(40);
        Assert.True(_executor.Apply(
            session,
            Envelope(
                0,
                new SplitParagraphData(FirstParagraphId, 2, splitId)))
            .IsSuccess);
        string applied = CanonicalDocumentFingerprint.Compute(session.Document);

        Assert.True(session.History.Undo());
        Assert.Null(session.Document.FindBlock(splitId));
        Assert.True(session.History.Redo());

        Assert.Equal(
            applied,
            CanonicalDocumentFingerprint.Compute(session.Document));
        Assert.NotNull(session.Document.FindBlock(splitId));
    }

    [Fact]
    public void StructuralTransaction_MoveAndRemoveUndoRedoAtomically()
    {
        DocumentEditingSession session = CreateSession();
        string initial = CanonicalDocumentFingerprint.Compute(session.Document);
        CanonicalEditEnvelope envelope = Envelope(
            0,
            new MoveBlockData(SecondParagraphId, SecondSectionId, 0),
            new RemoveBlockData(SecondParagraphId));

        Assert.True(_executor.Apply(session, envelope).IsSuccess);
        string applied = CanonicalDocumentFingerprint.Compute(session.Document);
        Assert.Null(session.Document.FindBlock(SecondParagraphId));

        Assert.True(session.History.Undo());
        Assert.Equal(initial, CanonicalDocumentFingerprint.Compute(session.Document));

        Assert.True(session.History.Redo());
        Assert.Equal(applied, CanonicalDocumentFingerprint.Compute(session.Document));
    }

    [Fact]
    public void Replay_SameInitialGraphAndSequenceProduceSameSemanticGraph()
    {
        DocumentEditingSession first = CreateSession();
        DocumentEditingSession second = CreateSession();
        DocumentNodeId splitId = Id(20);
        DocumentNodeId insertedId = Id(21);
        DocumentNodeId insertedInlineId = Id(22);
        IReadOnlyList<CanonicalEditEnvelope> sequence =
        [
            Envelope(
                0,
                new InsertTextData(FirstParagraphId, 5, "!"),
                new ChangeTextStyleData(
                    SecondParagraphId,
                    0,
                    5,
                    new CanonicalTextStylePatch(IsBold: true))),
            Envelope(1, new DeleteTextData(FirstParagraphId, 1, 2)),
            Envelope(
                2,
                new ReplaceTextData(
                    new DocumentLocation(SecondParagraphId, 1),
                    new DocumentLocation(SecondParagraphId, 4),
                    "i")),
            Envelope(3, new SplitParagraphData(FirstParagraphId, 2, splitId)),
            Envelope(4, new MergeParagraphsData(FirstParagraphId, splitId)),
            Envelope(
                5,
                new ChangeParagraphPropertiesData(
                    SecondParagraphId,
                    new CanonicalParagraphPropertiesData(
                        0,
                        1.25,
                        GostAlignment.Center,
                        ParagraphStyle.Heading2,
                        true))),
            Envelope(
                6,
                new InsertParagraphData(
                    FirstSectionId,
                    2,
                    new CanonicalParagraphData(
                        insertedId,
                        DefaultProperties(),
                        [
                            new CanonicalTextInlineData(
                                insertedInlineId,
                                "New",
                                DefaultStyle())
                        ]))),
            Envelope(
                7,
                new MoveBlockData(insertedId, SecondSectionId, 0)),
            Envelope(8, new RemoveBlockData(insertedId))
        ];

        IReadOnlyList<CanonicalApplyResult> firstResults =
            _executor.Replay(first, sequence);
        IReadOnlyList<CanonicalApplyResult> secondResults =
            _executor.Replay(second, sequence);

        Assert.All(firstResults, result => Assert.True(result.IsSuccess));
        Assert.All(secondResults, result => Assert.True(result.IsSuccess));
        Assert.Equal(sequence.Count, firstResults.Count);
        Assert.Equal(9, first.Revision);
        Assert.Equal(9, second.Revision);
        Assert.Equal(
            CanonicalDocumentFingerprint.Compute(first.Document),
            CanonicalDocumentFingerprint.Compute(second.Document));
    }

    [Fact]
    public void Apply_StaleRevisionRejectsWithoutChangingGraphOrHistory()
    {
        DocumentEditingSession session = CreateSession();
        string before = CanonicalDocumentFingerprint.Compute(session.Document);
        CanonicalEditEnvelope stale = Envelope(
            5,
            new InsertTextData(FirstParagraphId, 5, "!"));

        CanonicalApplyResult result = _executor.Apply(session, stale);

        Assert.False(result.IsSuccess);
        Assert.Equal(CanonicalApplyFailureCode.StaleRevision, result.Failure?.Code);
        Assert.Equal(0, session.Revision);
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(before, CanonicalDocumentFingerprint.Compute(session.Document));
    }

    [Fact]
    public void Apply_InvalidTargetRejectsWithoutChangingRevision()
    {
        DocumentEditingSession session = CreateSession();
        CanonicalEditEnvelope envelope = Envelope(
            0,
            new DeleteTextData(Id(999), 0, 1));

        CanonicalApplyResult result = _executor.Apply(session, envelope);

        Assert.Equal(CanonicalApplyFailureCode.InvalidTarget, result.Failure?.Code);
        Assert.Equal(0, session.Revision);
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void Apply_NoOpPropertiesRejectsWithoutCreatingRevision()
    {
        DocumentEditingSession session = CreateSession();
        CanonicalEditEnvelope envelope = Envelope(
            0,
            new ChangeParagraphPropertiesData(
                FirstParagraphId,
                DefaultProperties()));

        CanonicalApplyResult result = _executor.Apply(session, envelope);

        Assert.Equal(
            CanonicalApplyFailureCode.InvalidOperation,
            result.Failure?.Code);
        Assert.Equal(0, session.Revision);
    }

    [Fact]
    public void Apply_NoOpTextStyleRejectsWithoutCreatingRevision()
    {
        DocumentEditingSession session = CreateSession();
        CanonicalEditEnvelope envelope = Envelope(
            0,
            new ChangeTextStyleData(
                FirstParagraphId,
                0,
                5,
                new CanonicalTextStylePatch(IsBold: false)));

        CanonicalApplyResult result = _executor.Apply(session, envelope);

        Assert.Equal(
            CanonicalApplyFailureCode.InvalidOperation,
            result.Failure?.Code);
        Assert.Equal(0, session.Revision);
    }

    [Fact]
    public void Apply_OversizedTextRejectsAtBoundedContract()
    {
        DocumentEditingSession session = CreateSession();
        string oversized = new(
            'x',
            CanonicalEditContract.MaxTextCharactersPerOperation + 1);

        CanonicalApplyResult result = _executor.Apply(
            session,
            Envelope(
                0,
                new InsertTextData(FirstParagraphId, 0, oversized)));

        Assert.Equal(
            CanonicalApplyFailureCode.InvalidOperation,
            result.Failure?.Code);
        Assert.Equal(0, session.Revision);
    }

    [Fact]
    public void Apply_DuplicateOperationIdentityRejectsEnvelope()
    {
        DocumentEditingSession session = CreateSession();
        CanonicalEditEnvelope valid = Envelope(
            0,
            new InsertTextData(FirstParagraphId, 5, "!"),
            new InsertTextData(SecondParagraphId, 5, "!"));
        CanonicalEditEnvelope duplicate = valid with
        {
            Operations =
            [
                valid.Operations[0],
                valid.Operations[1] with
                {
                    OperationId = valid.Operations[0].OperationId
                }
            ]
        };

        CanonicalApplyResult result = _executor.Apply(session, duplicate);

        Assert.Equal(
            CanonicalApplyFailureCode.InvalidEnvelope,
            result.Failure?.Code);
        Assert.Equal(0, session.Revision);
    }

    [Fact]
    public void Apply_WhenLaterOperationFails_RollsBackWholeTransaction()
    {
        DocumentEditingSession session = CreateSession();
        string before = CanonicalDocumentFingerprint.Compute(session.Document);
        CanonicalEditEnvelope envelope = Envelope(
            0,
            new RemoveBlockData(SecondParagraphId),
            new InsertTextData(SecondParagraphId, 5, "!"));

        CanonicalApplyResult result = _executor.Apply(session, envelope);

        Assert.Equal(CanonicalApplyFailureCode.ApplyFailed, result.Failure?.Code);
        Assert.Equal(0, session.Revision);
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(before, CanonicalDocumentFingerprint.Compute(session.Document));
    }

    [Fact]
    public void Apply_WhenListenerThrows_CommitsConsistentState()
    {
        DocumentEditingSession session = CreateSession();
        session.Changed += (_, _) =>
            throw new InvalidOperationException("listener failure");

        CanonicalApplyResult result = _executor.Apply(
            session,
            Envelope(0, new InsertTextData(FirstParagraphId, 5, "!")));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, session.Revision);
        Assert.Equal("Hello!", GetParagraph(session, FirstParagraphId).GetPlainText());
    }

    [Fact]
    public void Envelope_RoundTripsThroughJsonWithoutRuntimeReferences()
    {
        CanonicalEditEnvelope expected = Envelope(
            0,
            new InsertParagraphData(
                FirstSectionId,
                1,
                new CanonicalParagraphData(
                    Id(30),
                    DefaultProperties(),
                    [
                        new CanonicalTextInlineData(Id(31), "Text", DefaultStyle()),
                        new CanonicalLinkInlineData(
                            Id(32),
                            "Link",
                            "https://example.test",
                            DefaultStyle()),
                        new CanonicalLineBreakInlineData(Id(33))
                    ])));

        string json = JsonSerializer.Serialize(expected);
        CanonicalEditEnvelope actual = JsonSerializer.Deserialize<
            CanonicalEditEnvelope>(json)!;

        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.TransactionId, actual.TransactionId);
        InsertParagraphData data = Assert.IsType<InsertParagraphData>(
            Assert.Single(actual.Operations).Data);
        Assert.Equal(Id(30), data.Paragraph.Id);
        Assert.Collection(
            data.Paragraph.Inlines,
            inline => Assert.IsType<CanonicalTextInlineData>(inline),
            inline => Assert.IsType<CanonicalLinkInlineData>(inline),
            inline => Assert.IsType<CanonicalLineBreakInlineData>(inline));
        Assert.DoesNotContain(
            typeof(CanonicalEditEnvelope).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name?.StartsWith(
                "Avalonia",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Fingerprint_IsIndependentOfObjectIdentityAndDictionaryInsertionOrder()
    {
        DocumentRoot first = CreateDocument();
        DocumentRoot second = CreateDocument();
        first.Extensions.Set("org.example.z", 1, "text/plain", "z"u8.ToArray());
        first.Extensions.Set("org.example.a", 1, "text/plain", "a"u8.ToArray());
        second.Extensions.Set("org.example.a", 1, "text/plain", "a"u8.ToArray());
        second.Extensions.Set("org.example.z", 1, "text/plain", "z"u8.ToArray());

        Assert.NotSame(first, second);
        Assert.Equal(
            CanonicalDocumentFingerprint.Compute(first),
            CanonicalDocumentFingerprint.Compute(second));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    public void Apply_InvalidEnvelopeRevisionContractRejects(
        long baseRevision,
        long resultRevision)
    {
        DocumentEditingSession session = CreateSession();
        CanonicalEditEnvelope valid = Envelope(
            0,
            new InsertTextData(FirstParagraphId, 5, "!"));
        CanonicalEditEnvelope invalid = valid with
        {
            BaseRevision = baseRevision,
            ResultRevision = resultRevision
        };

        CanonicalApplyResult result = _executor.Apply(session, invalid);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, session.Revision);
    }

    private static CanonicalEditEnvelope Envelope(
        long baseRevision,
        params CanonicalOperationData[] operations)
    {
        Guid transactionId = GuidFor(1000 + checked((int)baseRevision));
        CanonicalEditOperation[] entries = operations.Select(
            (data, index) => new CanonicalEditOperation(
                CanonicalEditContract.CurrentOperationVersion,
                GuidFor(2000 + checked((int)baseRevision) * 10 + index),
                data)).ToArray();
        return new CanonicalEditEnvelope(
            CanonicalEditContract.CurrentEnvelopeVersion,
            transactionId,
            baseRevision,
            baseRevision + 1,
            entries);
    }

    private static DocumentEditingSession CreateSession() =>
        new(CreateDocument());

    private static DocumentRoot CreateDocument()
    {
        DateTime timestamp = new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        DocumentRoot document = new(RootId)
        {
            Metadata = new DocumentMetadata
            {
                CreatedAtUtc = timestamp,
                ModifiedAtUtc = timestamp
            }
        };
        DocumentSection firstSection = new(FirstSectionId);
        ParagraphBlock first = new(FirstParagraphId);
        first.Inlines.Add(new TextInline(
            FirstInlineId,
            "Hello",
            new TextStyle()));
        ParagraphBlock second = new(SecondParagraphId);
        second.Inlines.Add(new TextInline(
            SecondInlineId,
            "World",
            new TextStyle()));
        firstSection.Blocks.Add(first);
        firstSection.Blocks.Add(second);
        document.Sections.Add(firstSection);
        document.Sections.Add(new DocumentSection(SecondSectionId));
        document.RebuildIndex();
        return document;
    }

    private static ParagraphBlock GetParagraph(
        DocumentEditingSession session,
        DocumentNodeId id) =>
        Assert.IsType<ParagraphBlock>(session.Document.FindBlock(id));

    private static CanonicalParagraphPropertiesData DefaultProperties() =>
        new(
            47,
            1.5,
            GostAlignment.Left,
            ParagraphStyle.Normal,
            false);

    private static CanonicalTextStyleData DefaultStyle() =>
        new(false, false, 0xFF000000, 14, null);

    private static DocumentNodeId Id(int value) =>
        new(GuidFor(value));

    private static Guid GuidFor(int value) =>
        new(value, 0, 0, new byte[8]);
}
