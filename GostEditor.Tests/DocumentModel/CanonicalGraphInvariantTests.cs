using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Editing;
using GostEditor.Core.Editing.Operations;

namespace GostEditor.Tests.DocumentModel;

public sealed class CanonicalGraphInvariantTests
{
    [Fact]
    public void RebuildIndex_IndexesCompleteNestedTreeAndOwners()
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        ParagraphBlock header = CreateParagraph("header");
        TableBlock table = new();
        TableRow row = new();
        TableCell cell = new();
        ParagraphBlock nested = CreateParagraph("nested");
        cell.Blocks.Add(nested);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        section.HeaderBlocks.Add(header);
        section.Blocks.Add(table);
        document.Sections.Add(section);

        document.RebuildIndex();

        DocumentNode[] expected =
        {
            document,
            section,
            header,
            header.Inlines[0],
            table,
            row,
            cell,
            nested,
            nested.Inlines[0]
        };
        foreach (DocumentNode node in expected)
        {
            Assert.Same(node, document.FindNode(node.Id));
        }

        Assert.Same(section, document.FindSectionContaining(header.Id));
        Assert.Same(section, document.FindSectionContaining(nested.Id));
    }

    [Fact]
    public void Validate_WhenDifferentNodesShareId_ReportsDeterministicDuplicate()
    {
        DocumentNodeId duplicateId = DocumentNodeId.Parse(
            "40000000-0000-0000-0000-000000000001");
        DocumentRoot document = new();
        DocumentSection section = new();
        section.Blocks.Add(new ParagraphBlock(duplicateId));
        section.Blocks.Add(new ParagraphBlock(duplicateId));
        document.Sections.Add(section);

        DocumentGraphValidationReport report = document.Validate();

        DocumentGraphIssue issue = Assert.Single(
            report.Issues,
            item => item.Code == DocumentGraphIssueCode.DuplicateNodeId);
        Assert.False(report.IsValid);
        Assert.Equal("root.sections[0].blocks[1]", issue.Path);
        Assert.Equal(duplicateId, issue.NodeId);
    }

    [Fact]
    public void Validate_WhenNodeInstanceHasTwoParents_ReportsSharedOwnership()
    {
        ParagraphBlock shared = CreateParagraph("shared");
        DocumentRoot document = new();
        DocumentSection section = new();
        section.HeaderBlocks.Add(shared);
        section.Blocks.Add(shared);
        document.Sections.Add(section);

        DocumentGraphValidationReport report = document.Validate();

        Assert.Contains(
            report.Issues,
            issue => issue.Code == DocumentGraphIssueCode.SharedNodeInstance &&
                issue.Path == "root.sections[0].blocks[0]");
    }

    [Fact]
    public void Validate_WhenTreeContainsNullNode_ReportsPathInsteadOfThrowing()
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        section.Blocks.Add(null!);
        document.Sections.Add(section);

        DocumentGraphValidationReport report = document.Validate();

        DocumentGraphIssue issue = Assert.Single(
            report.Issues,
            item => item.Code == DocumentGraphIssueCode.NullNode);
        Assert.Equal("root.sections[0].blocks[0]", issue.Path);
        Assert.False(report.IsValid);
    }

    [Fact]
    public void Validate_WhenTableContainsItself_ReportsCycleWithoutRecursingForever()
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        TableBlock table = new();
        TableRow row = new();
        TableCell cell = new();
        cell.Blocks.Add(table);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        section.Blocks.Add(table);
        document.Sections.Add(section);

        DocumentGraphValidationReport report = document.Validate();

        Assert.Contains(
            report.Issues,
            issue => issue.Code == DocumentGraphIssueCode.Cycle);
    }

    [Fact]
    public void Validate_WhenFigureResourceIsMissing_ReportsDanglingReference()
    {
        Guid missingId = Guid.NewGuid();
        DocumentRoot document = new();
        DocumentSection section = new();
        section.Blocks.Add(new FigureBlock(missingId, 10, 20));
        document.Sections.Add(section);

        DocumentGraphValidationReport report = document.Validate();

        DocumentGraphIssue issue = Assert.Single(
            report.Issues,
            item => item.Code == DocumentGraphIssueCode.DanglingImageReference);
        Assert.Equal(missingId, issue.ResourceId);
        Assert.False(report.IsValid);
    }

    [Fact]
    public void Validate_WhenResourceIsOrphaned_ReportsWarningButGraphRemainsValid()
    {
        Guid resourceId = Guid.NewGuid();
        DocumentRoot document = new();
        document.Resources.AddImage(new ImageResource(
            resourceId,
            "image.png",
            "image/png",
            TestImageData.CreatePng()));

        DocumentGraphValidationReport report = document.Validate();

        DocumentGraphIssue issue = Assert.Single(report.Issues);
        Assert.Equal(DocumentGraphIssueCode.OrphanImageResource, issue.Code);
        Assert.Equal(DocumentGraphIssueSeverity.Warning, issue.Severity);
        Assert.True(report.IsValid);
    }

    [Fact]
    public void RebuildIndex_WhenGraphIsInvalid_PreservesLastValidIndex()
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        ParagraphBlock original = CreateParagraph("original");
        section.Blocks.Add(original);
        document.Sections.Add(section);
        document.RebuildIndex();
        section.Blocks.Add(new ParagraphBlock(original.Id));

        InvalidDocumentGraphException exception = Assert.Throws<
            InvalidDocumentGraphException>(document.RebuildIndex);

        Assert.Contains(
            exception.Report.Issues,
            issue => issue.Code == DocumentGraphIssueCode.DuplicateNodeId);
        Assert.Same(original, document.FindNode(original.Id));
    }

    [Fact]
    public void StructuralOperations_KeepIndexConsistentAcrossUndoRedo()
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        ParagraphBlock first = CreateParagraph("first");
        section.Blocks.Add(first);
        document.Sections.Add(section);
        DocumentEditingSession session = new(document);
        ParagraphBlock inserted = CreateParagraph("inserted");

        session.Execute(new InsertBlockOperation(section.Id, 1, inserted));
        Assert.Same(inserted, document.FindBlock(inserted.Id));

        Assert.True(session.History.Undo());
        Assert.Null(document.FindBlock(inserted.Id));

        Assert.True(session.History.Redo());
        Assert.Same(inserted, document.FindBlock(inserted.Id));

        session.Execute(new RemoveBlockOperation(inserted.Id));
        Assert.Null(document.FindBlock(inserted.Id));
        Assert.True(session.History.Undo());
        Assert.Same(inserted, document.FindBlock(inserted.Id));
    }

    [Fact]
    public void TextOperation_RefreshesInlineIndexAcrossApplyUndoRedo()
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        ParagraphBlock paragraph = CreateParagraph("A");
        section.Blocks.Add(paragraph);
        document.Sections.Add(section);
        DocumentEditingSession session = new(document);

        session.Text.InsertText(new DocumentLocation(paragraph.Id, 1), "😀");
        Assert.Equal("A😀", paragraph.GetPlainText());
        Assert.All(
            paragraph.Inlines,
            inline => Assert.Same(inline, document.FindNode(inline.Id)));

        Assert.True(session.History.Undo());
        Assert.Equal("A", paragraph.GetPlainText());
        Assert.All(
            paragraph.Inlines,
            inline => Assert.Same(inline, document.FindNode(inline.Id)));

        Assert.True(session.History.Redo());
        Assert.Equal("A😀", paragraph.GetPlainText());
        Assert.All(
            paragraph.Inlines,
            inline => Assert.Same(inline, document.FindNode(inline.Id)));
    }

    [Fact]
    public void InsertBlock_WhenIdExistsInAnotherSection_DoesNotMutateGraph()
    {
        DocumentRoot document = new();
        DocumentSection firstSection = new();
        DocumentSection secondSection = new();
        ParagraphBlock existing = CreateParagraph("existing");
        firstSection.Blocks.Add(existing);
        document.Sections.Add(firstSection);
        document.Sections.Add(secondSection);
        DocumentEditingSession session = new(document);
        ParagraphBlock duplicate = new(existing.Id);

        Assert.Throws<InvalidOperationException>(() => session.Execute(
            new InsertBlockOperation(secondSection.Id, 0, duplicate)));

        Assert.Empty(secondSection.Blocks);
        Assert.Same(existing, document.FindBlock(existing.Id));
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void History_WhenIndexValidationFails_RollsBackGraphAndKeepsIndex()
    {
        DocumentRoot document = new();
        DocumentSection section = new();
        ParagraphBlock paragraph = CreateParagraph("valid");
        section.Blocks.Add(paragraph);
        document.Sections.Add(section);
        DocumentEditingSession session = new(document);
        InlineNode original = Assert.Single(paragraph.Inlines);

        Assert.Throws<InvalidDocumentGraphException>(() => session.Execute(
            new AddDuplicateInlineOperation(paragraph.Id, original.Id)));

        Assert.Same(original, Assert.Single(paragraph.Inlines));
        Assert.Same(original, document.FindNode(original.Id));
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(0, session.ChangeVersion);
    }

    private static ParagraphBlock CreateParagraph(string text)
    {
        ParagraphBlock paragraph = new();
        paragraph.Inlines.Add(new TextInline(text));
        return paragraph;
    }

    private sealed class AddDuplicateInlineOperation : IEditOperation
    {
        private readonly DocumentNodeId _paragraphId;
        private readonly DocumentNodeId _duplicateId;

        public AddDuplicateInlineOperation(
            DocumentNodeId paragraphId,
            DocumentNodeId duplicateId)
        {
            _paragraphId = paragraphId;
            _duplicateId = duplicateId;
        }

        public string Description => "Invalid duplicate inline";

        public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
            new[] { _paragraphId };

        public DocumentIndexUpdateKind IndexUpdateKind =>
            DocumentIndexUpdateKind.AffectedParagraphs;

        public void Apply(DocumentEditingContext context) =>
            context.GetRequiredParagraph(_paragraphId).Inlines.Add(
                new TextInline(_duplicateId, "duplicate"));

        public void Revert(DocumentEditingContext context) =>
            context.GetRequiredParagraph(_paragraphId).Inlines.RemoveAt(1);
    }
}
