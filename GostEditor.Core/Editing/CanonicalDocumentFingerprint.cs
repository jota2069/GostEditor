using System.Security.Cryptography;
using System.Text;
using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;

namespace GostEditor.Core.Editing;

/// <summary>
/// Deterministic semantic fingerprint for replay conformance. It includes
/// stable identities and never depends on object identity or dictionary order.
/// </summary>
public static class CanonicalDocumentFingerprint
{
    public static string Compute(DocumentRoot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        DocumentGraphValidationReport report = document.Validate();
        if (!report.IsValid)
        {
            throw new InvalidDocumentGraphException(report);
        }

        using MemoryStream stream = new();
        using (BinaryWriter writer = new(
                   stream,
                   new UTF8Encoding(false, true),
                   leaveOpen: true))
        {
            WriteNode(writer, document);
            WriteResources(writer, document.Resources);
            WriteExtensions(writer, document.Extensions);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()))
            .ToLowerInvariant();
    }

    private static void WriteNode(BinaryWriter writer, DocumentNode node)
    {
        WriteString(writer, node.GetType().FullName ?? node.GetType().Name);
        WriteId(writer, node.Id);
        switch (node)
        {
            case DocumentRoot root:
                writer.Write(root.Metadata.CreatedAtUtc.Ticks);
                writer.Write((int)root.Metadata.CreatedAtUtc.Kind);
                writer.Write(root.Metadata.ModifiedAtUtc.Ticks);
                writer.Write((int)root.Metadata.ModifiedAtUtc.Kind);
                WriteNodes(writer, root.Sections);
                break;

            case DocumentSection section:
                WritePageSettings(writer, section.PageSettings);
                WriteNodes(writer, section.HeaderBlocks);
                WriteNodes(writer, section.Blocks);
                WriteNodes(writer, section.FooterBlocks);
                break;

            case ParagraphBlock paragraph:
                WriteParagraphProperties(writer, paragraph.Properties);
                WriteNodes(writer, paragraph.Inlines);
                break;

            case FigureBlock figure:
                WriteGuid(writer, figure.ImageId);
                writer.Write(BitConverter.DoubleToInt64Bits(figure.Width));
                writer.Write(BitConverter.DoubleToInt64Bits(figure.Height));
                WriteString(writer, figure.Caption);
                break;

            case CodeBlock code:
                WriteString(writer, code.Language);
                WriteString(writer, code.Code);
                WriteString(writer, code.Caption);
                break;

            case PageBreakBlock:
                break;

            case TableBlock table:
                WriteNodes(writer, table.Rows);
                break;

            case TableRow row:
                WriteNodes(writer, row.Cells);
                break;

            case TableCell cell:
                writer.Write(cell.RowSpan);
                writer.Write(cell.ColumnSpan);
                WriteNodes(writer, cell.Blocks);
                break;

            case TextInline text:
                WriteString(writer, text.Text);
                WriteTextStyle(writer, text.Style);
                break;

            case LinkInline link:
                WriteString(writer, link.Text);
                WriteString(writer, link.Target);
                WriteTextStyle(writer, link.Style);
                break;

            case LineBreakInline:
                break;

            default:
                throw new NotSupportedException(
                    $"Fingerprint не поддерживает {node.GetType().Name}.");
        }
    }

    private static void WriteResources(
        BinaryWriter writer,
        DocumentResourceCatalog resources)
    {
        writer.Write(resources.ImagesInOrder.Count);
        foreach (ImageResource image in resources.ImagesInOrder)
        {
            WriteGuid(writer, image.Id);
            WriteString(writer, image.FileName);
            WriteString(writer, image.MediaType);
            writer.Write(image.Data.Length);
            writer.Write(image.Data.Span);
            WriteString(writer, image.Caption);
            writer.Write(image.Order);
        }
    }

    private static void WriteExtensions(
        BinaryWriter writer,
        DocumentExtensionCatalog extensions)
    {
        writer.Write(extensions.Parts.Count);
        foreach ((string key, DocumentExtensionPart part) in extensions.Parts
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            WriteString(writer, key);
            writer.Write(part.SchemaVersion);
            WriteString(writer, part.MediaType);
            writer.Write(part.Data.Length);
            writer.Write(part.Data.Span);
        }
    }

    private static void WritePageSettings(
        BinaryWriter writer,
        DocumentPageSettings settings)
    {
        writer.Write(BitConverter.DoubleToInt64Bits(settings.Width));
        writer.Write(BitConverter.DoubleToInt64Bits(settings.Height));
        writer.Write(BitConverter.DoubleToInt64Bits(settings.MarginLeft));
        writer.Write(BitConverter.DoubleToInt64Bits(settings.MarginRight));
        writer.Write(BitConverter.DoubleToInt64Bits(settings.MarginTop));
        writer.Write(BitConverter.DoubleToInt64Bits(settings.MarginBottom));
    }

    private static void WriteParagraphProperties(
        BinaryWriter writer,
        ParagraphProperties properties)
    {
        writer.Write(BitConverter.DoubleToInt64Bits(
            properties.FirstLineIndent));
        writer.Write(BitConverter.DoubleToInt64Bits(properties.LineSpacing));
        writer.Write((int)properties.Alignment);
        writer.Write((int)properties.Style);
        writer.Write(properties.PageBreakBefore);
    }

    private static void WriteTextStyle(BinaryWriter writer, TextStyle style)
    {
        writer.Write(style.IsBold);
        writer.Write(style.IsItalic);
        writer.Write(style.Color);
        writer.Write(BitConverter.DoubleToInt64Bits(style.FontSize));
        WriteNullableString(writer, style.FontFamily);
    }

    private static void WriteNodes<TNode>(
        BinaryWriter writer,
        IReadOnlyCollection<TNode> nodes)
        where TNode : DocumentNode
    {
        writer.Write(nodes.Count);
        foreach (TNode node in nodes)
        {
            WriteNode(writer, node);
        }
    }

    private static void WriteId(BinaryWriter writer, DocumentNodeId id) =>
        WriteGuid(writer, id.Value);

    private static void WriteGuid(BinaryWriter writer, Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes, bigEndian: true, out _);
        writer.Write(bytes);
    }

    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            WriteString(writer, value);
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
