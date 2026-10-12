using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.DocumentModel.Legacy;

/// <summary>
/// Maintains the temporary, one-way legacy view consumed by layout and the
/// frozen .gost persistence boundary. Canonical blocks remain authoritative.
/// </summary>
internal sealed class LegacyTextProjection
{
    private readonly GostDocument _target;
    private readonly Dictionary<DocumentNodeId, Paragraph> _paragraphs = new();
    private readonly Dictionary<Guid, ImageAttachment> _images = new();

    public LegacyTextProjection(DocumentRoot source, GostDocument target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        BindExisting(source ?? throw new ArgumentNullException(nameof(source)));
    }

    public int GetLegacyIndex(DocumentRoot source, DocumentNodeId blockId)
    {
        int index = 0;
        foreach (BlockNode block in source.EnumerateBlocks())
        {
            if (block.Id == blockId)
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    public DocumentLocation ToCanonicalLocation(
        DocumentRoot source,
        DocumentPosition position)
    {
        IReadOnlyList<BlockNode> blocks = source.EnumerateBlocks().ToList();
        if (blocks.Count == 0)
        {
            throw new InvalidOperationException("Документ не содержит блоков.");
        }

        int index = Math.Clamp(position.ParagraphIndex, 0, blocks.Count - 1);
        ParagraphBlock paragraph = blocks[index] as ParagraphBlock
            ?? throw new InvalidOperationException(
                "Текстовая позиция не может ссылаться на изображение.");
        return new DocumentLocation(
            paragraph.Id,
            Math.Clamp(position.Offset, 0, paragraph.TextLength));
    }

    public DocumentPosition ToLegacyPosition(
        DocumentRoot source,
        DocumentLocation location)
    {
        ParagraphBlock paragraph = source.FindBlock(location.BlockId)
            as ParagraphBlock
            ?? throw new InvalidOperationException(
                $"Абзац {location.BlockId} не найден.");
        int index = GetLegacyIndex(source, paragraph.Id);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Абзац {location.BlockId} отсутствует в проекции.");
        }

        return new DocumentPosition(
            index,
            Math.Clamp(location.Offset, 0, paragraph.TextLength));
    }

    public bool IsTextRange(
        DocumentRoot source,
        int startIndex,
        int endIndex)
    {
        IReadOnlyList<BlockNode> blocks = source.EnumerateBlocks().ToList();
        if (startIndex < 0 || endIndex >= blocks.Count || startIndex > endIndex)
        {
            return false;
        }

        for (int index = startIndex; index <= endIndex; index++)
        {
            if (blocks[index] is not ParagraphBlock)
            {
                return false;
            }
        }

        return true;
    }

    public void Project(DocumentRoot source)
    {
        ValidateLossless(source);
        ProjectResources(source);
        List<Paragraph> ordered = new();
        HashSet<DocumentNodeId> liveIds = new();
        foreach (BlockNode block in source.EnumerateBlocks())
        {
            liveIds.Add(block.Id);
            if (!_paragraphs.TryGetValue(block.Id, out Paragraph? paragraph))
            {
                paragraph = new Paragraph();
                _paragraphs.Add(block.Id, paragraph);
            }

            switch (block)
            {
                case ParagraphBlock text:
                    ProjectParagraph(text, paragraph);
                    break;
                case FigureBlock figure:
                    ProjectFigure(figure, paragraph);
                    break;
                case PageBreakBlock:
                    ProjectPageBreak(paragraph);
                    break;
                case CodeBlock code:
                    ProjectCode(code, paragraph);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Legacy layout projection не поддерживает " +
                        $"{block.GetType().Name}.");
            }

            ordered.Add(paragraph);
        }

        foreach (DocumentNodeId removed in _paragraphs.Keys
                     .Where(id => !liveIds.Contains(id))
                     .ToArray())
        {
            _paragraphs.Remove(removed);
        }

        if (!_target.Paragraphs.SequenceEqual(ordered))
        {
            _target.Paragraphs.Clear();
            _target.Paragraphs.AddRange(ordered);
        }
    }

    public void Rebind(DocumentRoot source)
    {
        _paragraphs.Clear();
        BindExisting(source ?? throw new ArgumentNullException(nameof(source)));
    }

    private void BindExisting(DocumentRoot source)
    {
        foreach (ImageAttachment image in _target.Images)
        {
            _images[image.Id] = image;
        }

        IReadOnlyList<BlockNode> blocks = source.EnumerateBlocks().ToList();
        int count = Math.Min(blocks.Count, _target.Paragraphs.Count);
        for (int index = 0; index < count; index++)
        {
            _paragraphs[blocks[index].Id] = _target.Paragraphs[index];
        }

        Project(source);
    }

    private void ProjectResources(DocumentRoot source)
    {
        List<ImageAttachment> ordered = new();
        foreach (ImageResource resource in source.Resources.ImagesInOrder)
        {
            if (!_images.TryGetValue(resource.Id, out ImageAttachment? image) ||
                image.FileName != resource.FileName ||
                image.MediaType != resource.MediaType ||
                image.Caption != resource.Caption ||
                image.Order != resource.Order ||
                !image.Data.Span.SequenceEqual(resource.Data.Span))
            {
                image = new ImageAttachment(
                    resource.Id,
                    resource.FileName,
                    resource.MediaType,
                    resource.Data,
                    resource.Caption,
                    resource.Order);
                _images[resource.Id] = image;
            }

            ordered.Add(image);
        }

        foreach (Guid removed in _images.Keys
                     .Where(id => !source.Resources.Images.ContainsKey(id))
                     .ToArray())
        {
            _images.Remove(removed);
        }

        if (!_target.Images.SequenceEqual(ordered))
        {
            _target.MutableImages.Clear();
            _target.MutableImages.AddRange(ordered);
        }

        _target.Counters.ImagesCount = source.EnumerateBlocks()
            .Count(block => block is FigureBlock);
    }

    private static void ProjectParagraph(
        ParagraphBlock source,
        Paragraph target)
    {
        target.ImageId = null;
        target.ImageWidth = 0;
        target.ImageHeight = 0;
        target.Alignment = source.Properties.Alignment;
        target.FirstLineIndent = source.Properties.FirstLineIndent;
        target.LineSpacing = source.Properties.LineSpacing;
        target.Style = source.Properties.Style;
        target.PageBreakBefore = source.Properties.PageBreakBefore;
        target.Runs.Clear();
        foreach (InlineNode inline in source.Inlines)
        {
            switch (inline)
            {
                case TextInline text:
                    target.Runs.Add(ToRun(text.Text, text.Style));
                    break;
                case LinkInline:
                    throw new NotSupportedException(
                        "Ссылки нельзя без потерь спроецировать в " +
                        "текущую модель .gost v2.");
                case LineBreakInline:
                    target.Runs.Add(new TextRun("\n"));
                    break;
            }
        }

        if (target.Runs.Count == 0)
        {
            target.Runs.Add(new TextRun(string.Empty));
        }
    }

    private static void ProjectFigure(FigureBlock source, Paragraph target)
    {
        target.ImageId = source.ImageId;
        target.ImageWidth = source.Width;
        target.ImageHeight = source.Height;
        if (!string.Equals(
                target.GetPlainText(),
                source.Caption,
                StringComparison.Ordinal))
        {
            target.Runs.Clear();
            target.Runs.Add(new TextRun(source.Caption));
        }
    }

    private static void ProjectPageBreak(Paragraph target)
    {
        target.ImageId = null;
        target.PageBreakBefore = true;
        target.Runs.Clear();
        target.Runs.Add(new TextRun(string.Empty));
    }

    private static void ProjectCode(CodeBlock source, Paragraph target)
    {
        target.ImageId = null;
        target.Style = ParagraphStyle.Code;
        target.FirstLineIndent = 0;
        target.Runs.Clear();
        target.Runs.Add(new TextRun(source.Code));
    }

    private static TextRun ToRun(string text, TextStyle style) =>
        string.IsNullOrWhiteSpace(style.FontFamily)
            ? new TextRun(text, style.IsBold, style.IsItalic)
            {
                Color = style.Color,
                FontSize = style.FontSize
            }
            : throw new NotSupportedException(
                "FontFamily нельзя без потерь спроецировать в " +
                "текущую модель .gost v2.");

    internal static void ValidateLossless(DocumentRoot source)
    {
        if (source.Sections.Count > 1)
        {
            throw new NotSupportedException(
                "Текущая модель .gost v2 не поддерживает несколько секций.");
        }

        foreach (DocumentSection section in source.Sections)
        {
            if (section.HeaderBlocks.Count > 0 ||
                section.FooterBlocks.Count > 0)
            {
                throw new NotSupportedException(
                    "Текущая модель .gost v2 не поддерживает колонтитулы.");
            }
        }

        string? unsupportedExtension = source.Extensions.Parts.Keys
            .FirstOrDefault(key => !string.Equals(
                key,
                LegacyDocumentAdapter.LegacyCompatibilityExtension,
                StringComparison.Ordinal));
        if (unsupportedExtension is not null)
        {
            throw new NotSupportedException(
                $"Extension {unsupportedExtension} нельзя без потерь " +
                "сохранить в текущую модель .gost v2.");
        }

        foreach (BlockNode block in source.EnumerateBlocks())
        {
            if (block is not ParagraphBlock and not FigureBlock and
                not PageBreakBlock and not CodeBlock)
            {
                throw new NotSupportedException(
                    $"Legacy layout projection не поддерживает " +
                    $"{block.GetType().Name}.");
            }

            if (block is not ParagraphBlock paragraph)
            {
                if (block is CodeBlock code &&
                    (!string.IsNullOrEmpty(code.Language) ||
                     !string.IsNullOrEmpty(code.Caption)))
                {
                    throw new NotSupportedException(
                        "Language и caption блока кода нельзя без потерь " +
                        "сохранить в текущую модель .gost v2.");
                }

                continue;
            }

            foreach (InlineNode inline in paragraph.Inlines)
            {
                if (inline is LinkInline)
                {
                    throw new NotSupportedException(
                        "Ссылки нельзя без потерь спроецировать в " +
                        "текущую модель .gost v2.");
                }

                if (inline is TextInline text &&
                    !string.IsNullOrWhiteSpace(text.Style.FontFamily))
                {
                    throw new NotSupportedException(
                        "FontFamily нельзя без потерь спроецировать в " +
                        "текущую модель .gost v2.");
                }
            }
        }
    }
}
