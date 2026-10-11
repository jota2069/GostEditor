using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;

namespace GostEditor.Core.DocumentModel;

public enum DocumentGraphIssueCode
{
    EmptyNodeId,
    NullNode,
    MissingRequiredState,
    DuplicateNodeId,
    SharedNodeInstance,
    Cycle,
    UnsupportedNodeType,
    MaximumDepthExceeded,
    DanglingImageReference,
    OrphanImageResource,
    EmptyImageResource,
    InvalidFigureDimensions
}

public enum DocumentGraphIssueSeverity
{
    Warning,
    Error
}

public sealed record DocumentGraphIssue(
    DocumentGraphIssueCode Code,
    DocumentGraphIssueSeverity Severity,
    string Path,
    string Message,
    DocumentNodeId? NodeId = null,
    Guid? ResourceId = null);

public sealed class DocumentGraphValidationReport
{
    internal DocumentGraphValidationReport(
        IReadOnlyList<DocumentGraphIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<DocumentGraphIssue> Issues { get; }

    public bool IsValid => Issues.All(
        issue => issue.Severity != DocumentGraphIssueSeverity.Error);
}

public sealed class InvalidDocumentGraphException : InvalidOperationException
{
    public InvalidDocumentGraphException(DocumentGraphValidationReport report)
        : base(CreateMessage(report))
    {
        Report = report;
    }

    public DocumentGraphValidationReport Report { get; }

    private static string CreateMessage(DocumentGraphValidationReport report)
    {
        DocumentGraphIssue first = report.Issues.First(
            issue => issue.Severity == DocumentGraphIssueSeverity.Error);
        return $"Canonical document graph is invalid: {first.Path}: " +
            first.Message;
    }
}

public static class DocumentGraphValidator
{
    private const int MaxDepth = 256;

    public static DocumentGraphValidationReport Validate(DocumentRoot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Inspect(document).Report;
    }

    internal static DocumentGraphInspection Inspect(DocumentRoot document)
    {
        List<DocumentGraphIssue> issues = new();
        Dictionary<DocumentNodeId, DocumentNode> nodes = new();
        Dictionary<DocumentNodeId, DocumentSection> blockSections = new();
        HashSet<DocumentNode> seen = new(ReferenceEqualityComparer.Instance);
        HashSet<DocumentNode> active = new(ReferenceEqualityComparer.Instance);
        HashSet<Guid> referencedImages = new();

        VisitNode(
            document,
            document,
            null,
            "root",
            0,
            nodes,
            blockSections,
            seen,
            active,
            referencedImages,
            issues);

        foreach ((Guid id, ImageResource resource) in document.Resources.Images
                     .OrderBy(pair => pair.Key))
        {
            if (resource.Data.IsEmpty)
            {
                issues.Add(new DocumentGraphIssue(
                    DocumentGraphIssueCode.EmptyImageResource,
                    DocumentGraphIssueSeverity.Error,
                    $"resources.images[{id}]",
                    "Ресурс изображения не содержит данных.",
                    ResourceId: id));
            }

            if (!referencedImages.Contains(id))
            {
                issues.Add(new DocumentGraphIssue(
                    DocumentGraphIssueCode.OrphanImageResource,
                    DocumentGraphIssueSeverity.Warning,
                    $"resources.images[{id}]",
                    "Ресурс изображения не используется ни одной фигурой.",
                    ResourceId: id));
            }
        }

        return new DocumentGraphInspection(
            new DocumentGraphValidationReport(issues),
            nodes,
            blockSections);
    }

    private static void VisitNode(
        DocumentNode node,
        DocumentRoot root,
        DocumentSection? owningSection,
        string path,
        int depth,
        IDictionary<DocumentNodeId, DocumentNode> nodes,
        IDictionary<DocumentNodeId, DocumentSection> blockSections,
        ISet<DocumentNode> seen,
        ISet<DocumentNode> active,
        ISet<Guid> referencedImages,
        ICollection<DocumentGraphIssue> issues)
    {
        if (depth > MaxDepth)
        {
            issues.Add(new DocumentGraphIssue(
                DocumentGraphIssueCode.MaximumDepthExceeded,
                DocumentGraphIssueSeverity.Error,
                path,
                $"Глубина дерева превышает лимит {MaxDepth}.",
                node.Id));
            return;
        }

        if (active.Contains(node))
        {
            issues.Add(new DocumentGraphIssue(
                DocumentGraphIssueCode.Cycle,
                DocumentGraphIssueSeverity.Error,
                path,
                "Обнаружен цикл владения узлами.",
                node.Id));
            return;
        }

        if (!seen.Add(node))
        {
            issues.Add(new DocumentGraphIssue(
                DocumentGraphIssueCode.SharedNodeInstance,
                DocumentGraphIssueSeverity.Error,
                path,
                "Один экземпляр узла находится в нескольких местах дерева.",
                node.Id));
            return;
        }

        if (node.Id.IsEmpty)
        {
            issues.Add(new DocumentGraphIssue(
                DocumentGraphIssueCode.EmptyNodeId,
                DocumentGraphIssueSeverity.Error,
                path,
                "Идентификатор узла пуст.",
                node.Id));
        }
        else if (!nodes.TryAdd(node.Id, node))
        {
            issues.Add(new DocumentGraphIssue(
                DocumentGraphIssueCode.DuplicateNodeId,
                DocumentGraphIssueSeverity.Error,
                path,
                $"Идентификатор {node.Id} уже принадлежит другому узлу.",
                node.Id));
        }

        if (node is BlockNode block && owningSection is not null)
        {
            blockSections.TryAdd(block.Id, owningSection);
        }

        active.Add(node);
        switch (node)
        {
            case DocumentRoot documentRoot:
                if (documentRoot.Metadata is null)
                {
                    AddMissingState(
                        issues,
                        path + ".metadata",
                        node.Id,
                        "Document metadata отсутствует.");
                }

                VisitChildren(
                    documentRoot.Sections,
                    root,
                    null,
                    path + ".sections",
                    depth,
                    nodes,
                    blockSections,
                    seen,
                    active,
                    referencedImages,
                    issues);
                break;

            case DocumentSection section:
                if (section.PageSettings is null)
                {
                    AddMissingState(
                        issues,
                        path + ".pageSettings",
                        node.Id,
                        "Page settings секции отсутствуют.");
                }

                VisitChildren(
                    section.HeaderBlocks,
                    root,
                    section,
                    path + ".headerBlocks",
                    depth,
                    nodes,
                    blockSections,
                    seen,
                    active,
                    referencedImages,
                    issues);
                VisitChildren(
                    section.Blocks,
                    root,
                    section,
                    path + ".blocks",
                    depth,
                    nodes,
                    blockSections,
                    seen,
                    active,
                    referencedImages,
                    issues);
                VisitChildren(
                    section.FooterBlocks,
                    root,
                    section,
                    path + ".footerBlocks",
                    depth,
                    nodes,
                    blockSections,
                    seen,
                    active,
                    referencedImages,
                    issues);
                break;

            case ParagraphBlock paragraph:
                if (paragraph.Properties is null)
                {
                    AddMissingState(
                        issues,
                        path + ".properties",
                        node.Id,
                        "Paragraph properties отсутствуют.");
                }

                VisitChildren(
                    paragraph.Inlines,
                    root,
                    owningSection,
                    path + ".inlines",
                    depth,
                    nodes,
                    blockSections,
                    seen,
                    active,
                    referencedImages,
                    issues);
                break;

            case FigureBlock figure:
                referencedImages.Add(figure.ImageId);
                if (!double.IsFinite(figure.Width) || figure.Width <= 0 ||
                    !double.IsFinite(figure.Height) || figure.Height <= 0)
                {
                    issues.Add(new DocumentGraphIssue(
                        DocumentGraphIssueCode.InvalidFigureDimensions,
                        DocumentGraphIssueSeverity.Error,
                        path,
                        "Размеры фигуры должны быть конечными и положительными.",
                        figure.Id,
                        figure.ImageId));
                }

                if (!root.Resources.Images.ContainsKey(figure.ImageId))
                {
                    issues.Add(new DocumentGraphIssue(
                        DocumentGraphIssueCode.DanglingImageReference,
                        DocumentGraphIssueSeverity.Error,
                        path,
                        $"Фигура ссылается на отсутствующий ресурс {figure.ImageId}.",
                        figure.Id,
                        figure.ImageId));
                }

                break;

            case TableBlock table:
                VisitChildren(
                    table.Rows,
                    root,
                    owningSection,
                    path + ".rows",
                    depth,
                    nodes,
                    blockSections,
                    seen,
                    active,
                    referencedImages,
                    issues);
                break;

            case TableRow row:
                VisitChildren(
                    row.Cells,
                    root,
                    owningSection,
                    path + ".cells",
                    depth,
                    nodes,
                    blockSections,
                    seen,
                    active,
                    referencedImages,
                    issues);
                break;

            case TableCell cell:
                if (cell.RowSpan < 1 || cell.ColumnSpan < 1)
                {
                    AddMissingState(
                        issues,
                        path,
                        node.Id,
                        "Table spans должны быть положительными.");
                }

                VisitChildren(
                    cell.Blocks,
                    root,
                    owningSection,
                    path + ".blocks",
                    depth,
                    nodes,
                    blockSections,
                    seen,
                    active,
                    referencedImages,
                    issues);
                break;

            case TextInline text when text.Style is null:
            case LinkInline link when link.Style is null:
                AddMissingState(
                    issues,
                    path + ".style",
                    node.Id,
                    "Text style отсутствует.");
                break;

            case CodeBlock or PageBreakBlock or TextInline or LinkInline or
                LineBreakInline:
                break;

            default:
                issues.Add(new DocumentGraphIssue(
                    DocumentGraphIssueCode.UnsupportedNodeType,
                    DocumentGraphIssueSeverity.Error,
                    path,
                    $"Тип узла {node.GetType().Name} не поддерживается.",
                    node.Id));
                break;
        }

        active.Remove(node);
    }

    private static void AddMissingState(
        ICollection<DocumentGraphIssue> issues,
        string path,
        DocumentNodeId nodeId,
        string message) =>
        issues.Add(new DocumentGraphIssue(
            DocumentGraphIssueCode.MissingRequiredState,
            DocumentGraphIssueSeverity.Error,
            path,
            message,
            nodeId));

    private static void VisitChildren<TNode>(
        IEnumerable<TNode> children,
        DocumentRoot root,
        DocumentSection? owningSection,
        string path,
        int depth,
        IDictionary<DocumentNodeId, DocumentNode> nodes,
        IDictionary<DocumentNodeId, DocumentSection> blockSections,
        ISet<DocumentNode> seen,
        ISet<DocumentNode> active,
        ISet<Guid> referencedImages,
        ICollection<DocumentGraphIssue> issues)
        where TNode : DocumentNode
    {
        int index = 0;
        foreach (TNode child in children)
        {
            if (child is null)
            {
                issues.Add(new DocumentGraphIssue(
                    DocumentGraphIssueCode.NullNode,
                    DocumentGraphIssueSeverity.Error,
                    $"{path}[{index}]",
                    "Коллекция дерева содержит null-узел."));
                index++;
                continue;
            }

            VisitNode(
                child,
                root,
                child is DocumentSection section ? section : owningSection,
                $"{path}[{index}]",
                depth + 1,
                nodes,
                blockSections,
                seen,
                active,
                referencedImages,
                issues);
            index++;
        }
    }
}

internal sealed record DocumentGraphInspection(
    DocumentGraphValidationReport Report,
    IReadOnlyDictionary<DocumentNodeId, DocumentNode> Nodes,
    IReadOnlyDictionary<DocumentNodeId, DocumentSection> BlockSections);
