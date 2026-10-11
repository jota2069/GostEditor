using System;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;

namespace GostEditor.Core.DocumentModel;

public sealed class DocumentRoot : DocumentNode
{
    private IReadOnlyDictionary<DocumentNodeId, DocumentNode> _nodeIndex =
        new Dictionary<DocumentNodeId, DocumentNode>();
    private IReadOnlyDictionary<DocumentNodeId, DocumentSection> _blockSections =
        new Dictionary<DocumentNodeId, DocumentSection>();
    private IReadOnlyDictionary<DocumentNodeId, IReadOnlyList<DocumentNodeId>>
        _paragraphInlineIds =
            new Dictionary<DocumentNodeId, IReadOnlyList<DocumentNodeId>>();

    public DocumentRoot()
    {
        RebuildIndex();
    }

    public DocumentRoot(DocumentNodeId id)
        : base(id)
    {
        RebuildIndex();
    }

    public DocumentMetadata Metadata { get; set; } = new();

    public DocumentResourceCatalog Resources { get; } = new();

    public DocumentExtensionCatalog Extensions { get; } = new();

    public List<DocumentSection> Sections { get; } = new();

    public IEnumerable<BlockNode> EnumerateBlocks() =>
        Sections.SelectMany(section => section.Blocks);

    public DocumentNode? FindNode(DocumentNodeId id) =>
        _nodeIndex.GetValueOrDefault(id);

    public BlockNode? FindBlock(DocumentNodeId id) =>
        FindNode(id) as BlockNode;

    public DocumentSection? FindSectionContaining(DocumentNodeId blockId) =>
        _blockSections.GetValueOrDefault(blockId);

    public DocumentGraphValidationReport Validate() =>
        DocumentGraphValidator.Validate(this);

    public void RebuildIndex()
    {
        DocumentGraphInspection inspection =
            DocumentGraphValidator.Inspect(this);
        if (!inspection.Report.IsValid)
        {
            throw new InvalidDocumentGraphException(inspection.Report);
        }

        _nodeIndex = inspection.Nodes;
        _blockSections = inspection.BlockSections;
        _paragraphInlineIds = inspection.Nodes.Values
            .OfType<ParagraphBlock>()
            .ToDictionary(
                paragraph => paragraph.Id,
                paragraph => (IReadOnlyList<DocumentNodeId>)paragraph.Inlines
                    .Select(inline => inline.Id)
                    .ToArray());
    }

    internal void RefreshParagraphIndexes(
        IEnumerable<DocumentNodeId> paragraphIds)
    {
        ArgumentNullException.ThrowIfNull(paragraphIds);
        DocumentNodeId[] ids = paragraphIds
            .Distinct()
            .OrderBy(id => id.Value)
            .ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        Dictionary<DocumentNodeId, DocumentNode> nodes = new(_nodeIndex);
        Dictionary<DocumentNodeId, IReadOnlyList<DocumentNodeId>> inlineIds =
            new(_paragraphInlineIds);
        List<(DocumentNodeId Id, ParagraphBlock Paragraph)> paragraphs = new();

        foreach (DocumentNodeId id in ids)
        {
            if (!nodes.TryGetValue(id, out DocumentNode? node) ||
                node is not ParagraphBlock paragraph)
            {
                throw new InvalidOperationException(
                    $"Абзац {id} отсутствует в document index.");
            }

            paragraphs.Add((id, paragraph));
            if (inlineIds.Remove(id, out IReadOnlyList<DocumentNodeId>? oldIds))
            {
                foreach (DocumentNodeId oldId in oldIds)
                {
                    nodes.Remove(oldId);
                }
            }
        }

        HashSet<DocumentNode> instances = new(
            nodes.Values,
            ReferenceEqualityComparer.Instance);
        List<DocumentGraphIssue> issues = new();

        foreach ((DocumentNodeId id, ParagraphBlock paragraph) in paragraphs)
        {
            List<DocumentNodeId> currentIds = new();
            if (paragraph.Properties is null)
            {
                issues.Add(CreateIndexIssue(
                    DocumentGraphIssueCode.MissingRequiredState,
                    id,
                    $"index.paragraphs[{id}].properties",
                    "Paragraph properties отсутствуют."));
            }

            for (int index = 0; index < paragraph.Inlines.Count; index++)
            {
                InlineNode? inline = paragraph.Inlines[index];
                string path = $"index.paragraphs[{id}].inlines[{index}]";
                if (inline is null)
                {
                    issues.Add(CreateIndexIssue(
                        DocumentGraphIssueCode.NullNode,
                        id,
                        path,
                        "Коллекция paragraph inlines содержит null-узел."));
                    continue;
                }

                if (inline is not TextInline and not LinkInline and
                    not LineBreakInline)
                {
                    issues.Add(CreateIndexIssue(
                        DocumentGraphIssueCode.UnsupportedNodeType,
                        inline.Id,
                        path,
                        $"Тип inline {inline.GetType().Name} не поддерживается."));
                    continue;
                }

                if ((inline is TextInline text && text.Style is null) ||
                    (inline is LinkInline link && link.Style is null))
                {
                    issues.Add(CreateIndexIssue(
                        DocumentGraphIssueCode.MissingRequiredState,
                        inline.Id,
                        path + ".style",
                        "Text style отсутствует."));
                }

                if (!instances.Add(inline))
                {
                    issues.Add(CreateIndexIssue(
                        DocumentGraphIssueCode.SharedNodeInstance,
                        inline.Id,
                        path,
                        "Один экземпляр inline находится в нескольких местах."));
                    continue;
                }

                if (!nodes.TryAdd(inline.Id, inline))
                {
                    issues.Add(CreateIndexIssue(
                        DocumentGraphIssueCode.DuplicateNodeId,
                        inline.Id,
                        path,
                        $"Идентификатор {inline.Id} уже принадлежит другому узлу."));
                    continue;
                }

                currentIds.Add(inline.Id);
            }

            inlineIds[id] = currentIds;
        }

        DocumentGraphValidationReport report = new(issues);
        if (!report.IsValid)
        {
            throw new InvalidDocumentGraphException(report);
        }

        _nodeIndex = nodes;
        _paragraphInlineIds = inlineIds;
    }

    private static DocumentGraphIssue CreateIndexIssue(
        DocumentGraphIssueCode code,
        DocumentNodeId nodeId,
        string path,
        string message) =>
        new(
            code,
            DocumentGraphIssueSeverity.Error,
            path,
            message,
            nodeId);

    public int GetBlockIndex(DocumentNodeId blockId)
    {
        int index = 0;
        foreach (DocumentSection section in Sections)
        {
            foreach (BlockNode block in section.Blocks)
            {
                if (block.Id == blockId)
                {
                    return index;
                }

                index++;
            }
        }

        return -1;
    }

    public void EnsureEditableStructure()
    {
        if (Sections.Count == 0)
        {
            Sections.Add(new DocumentSection());
        }

        if (Sections.All(section => section.Blocks.Count == 0))
        {
            Sections[0].Blocks.Add(new ParagraphBlock());
        }

        RebuildIndex();
    }
}
