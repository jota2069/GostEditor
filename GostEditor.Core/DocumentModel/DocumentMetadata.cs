using System;
using GostEditor.Core.Models;

namespace GostEditor.Core.DocumentModel;

public sealed class DocumentMetadata
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    public TitlePageInfo TitlePage { get; set; } = new();

    public DocumentModules Modules { get; set; } = new();

    public List<CodeListing> CodeListings { get; set; } = new();

    public List<BibliographySource> BibliographySources { get; set; } = new();

    public DocumentCounters Counters { get; set; } = new();
}
