using GostEditor.Core.Models;

namespace GostEditor.Core.Serialization.Format;

internal static class GostFormatVersions
{
    public const int LegacyWithoutVersion = 0;
    public const int Legacy = 1;
    public const int Current = 2;
}

internal sealed class GostDocumentV1Dto
{
    public int FormatVersion { get; set; }
    public TitlePageInfo? TitlePage { get; set; }
    public List<CodeListing>? CodeListings { get; set; }
    public List<GostImageV1Dto>? Images { get; set; }
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
    public List<GostParagraphV1Dto> Paragraphs { get; set; } = new();
}

internal sealed class GostImageV1Dto
{
    public Guid Id { get; set; }
    public string? FileName { get; set; }
    public byte[]? Data { get; set; }
    public string? Caption { get; set; }
    public int Order { get; set; }
}

internal sealed class GostParagraphV1Dto
{
    public int Alignment { get; set; }
    public int Style { get; set; }
    public double? FirstLineIndent { get; set; }
    public double? LineSpacing { get; set; }
    public bool PageBreakBefore { get; set; }
    public List<GostRunDto> Runs { get; set; } = new();
    public string? ImageFileName { get; set; }
    public double ImageWidth { get; set; }
    public double ImageHeight { get; set; }
}

internal sealed class GostDocumentV2Dto
{
    public int FormatVersion { get; set; } = GostFormatVersions.Current;
    public TitlePageInfo? TitlePage { get; set; }
    public List<CodeListing>? CodeListings { get; set; }
    public List<GostImageV2Dto> Images { get; set; } = new();
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
    public List<GostParagraphV2Dto> Paragraphs { get; set; } = new();
}

internal sealed class GostImageV2Dto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string MediaType { get; set; } = GostMediaTypes.Binary;
    public string Caption { get; set; } = string.Empty;
    public int Order { get; set; }
}

internal sealed class GostParagraphV2Dto
{
    public int Alignment { get; set; }
    public int Style { get; set; }
    public double? FirstLineIndent { get; set; }
    public double? LineSpacing { get; set; }
    public bool PageBreakBefore { get; set; }
    public List<GostRunDto> Runs { get; set; } = new();
    public Guid? ImageId { get; set; }
    public double ImageWidth { get; set; }
    public double ImageHeight { get; set; }
}

internal sealed class GostRunDto
{
    public string Text { get; set; } = string.Empty;
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public double FontSize { get; set; }
    public uint? Color { get; set; }
}
