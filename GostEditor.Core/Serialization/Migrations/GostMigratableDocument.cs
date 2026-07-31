using GostEditor.Core.Models;

namespace GostEditor.Core.Serialization.Migrations;

internal sealed class GostMigratableDocument
{
    public TitlePageInfo? TitlePage { get; set; }
    public List<CodeListing>? CodeListings { get; set; }
    public List<GostMigratableImage> Images { get; } = new();
    public List<BibliographySource>? BibliographySources { get; set; }
    public DocumentModules? Modules { get; set; }
    public DocumentCounters? Counters { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public double PageWidth { get; set; }
    public double PageHeight { get; set; }
    public double MarginLeft { get; set; }
    public double MarginRight { get; set; }
    public double MarginTop { get; set; }
    public double MarginBottom { get; set; }
    public List<GostMigratableParagraph> Paragraphs { get; } = new();
}

internal sealed class GostMigratableImage
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string MediaType { get; init; } = string.Empty;
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public string Caption { get; init; } = string.Empty;
    public int Order { get; init; }
}

internal sealed class GostMigratableParagraph
{
    public int Alignment { get; init; }
    public int Style { get; init; }
    public double? FirstLineIndent { get; init; }
    public double? LineSpacing { get; init; }
    public bool PageBreakBefore { get; init; }
    public List<GostMigratableRun> Runs { get; init; } = new();
    public Guid? ImageId { get; set; }
    public double ImageWidth { get; set; }
    public double ImageHeight { get; set; }
}

internal sealed class GostMigratableRun
{
    public string Text { get; init; } = string.Empty;
    public bool IsBold { get; init; }
    public bool IsItalic { get; init; }
    public double FontSize { get; init; }
    public uint? Color { get; init; }
}
