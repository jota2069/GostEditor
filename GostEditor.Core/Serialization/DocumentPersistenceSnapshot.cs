using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.Serialization;

/// <summary>
/// A revision-bound, deeply isolated view of a document for persistence.
/// </summary>
public sealed class DocumentPersistenceSnapshot
{
    private DocumentPersistenceSnapshot(
        long revision,
        GostDocument document)
    {
        Revision = revision;
        Document = document;
    }

    public long Revision { get; }

    public DateTime ModifiedAt => Document.ModifiedAt;

    internal GostDocument Document { get; }

    public static DocumentPersistenceSnapshot Capture(
        GostDocument document,
        long revision,
        DateTime modifiedAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        GostDocument copy = CloneDocument(document);
        copy.ModifiedAt = modifiedAt;
        copy.Counters.ImagesCount = copy.Paragraphs.Count(
            paragraph => paragraph.ImageId.HasValue);

        return new DocumentPersistenceSnapshot(revision, copy);
    }

    private static GostDocument CloneDocument(GostDocument source)
    {
        GostDocument target = new()
        {
            TitlePage = CloneTitlePage(source.TitlePage),
            Paragraphs = source.Paragraphs.Select(CloneParagraph).ToList(),
            CodeListings = source.CodeListings.Select(CloneCodeListing).ToList(),
            BibliographySources = source.BibliographySources
                .Select(CloneBibliographySource)
                .ToList(),
            Modules = CloneModules(source.Modules),
            Counters = CloneCounters(source.Counters),
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            PageWidth = source.PageWidth,
            PageHeight = source.PageHeight,
            MarginLeft = source.MarginLeft,
            MarginRight = source.MarginRight,
            MarginTop = source.MarginTop,
            MarginBottom = source.MarginBottom
        };

        foreach (ImageAttachment image in source.Images)
        {
            target.MutableImages.Add(new ImageAttachment(
                image.Id,
                image.FileName,
                image.MediaType,
                image.Data,
                image.Caption,
                image.Order));
        }

        return target;
    }

    private static Paragraph CloneParagraph(Paragraph source) => new()
    {
        Runs = source.Runs.Select(CloneRun).ToList(),
        FirstLineIndent = source.FirstLineIndent,
        LineSpacing = source.LineSpacing,
        Alignment = source.Alignment,
        ImageId = source.ImageId,
        ImageWidth = source.ImageWidth,
        ImageHeight = source.ImageHeight,
        Style = source.Style,
        PageBreakBefore = source.PageBreakBefore
    };

    private static TextRun CloneRun(TextRun source) => new()
    {
        Text = source.Text,
        IsBold = source.IsBold,
        IsItalic = source.IsItalic,
        Color = source.Color,
        FontSize = source.FontSize
    };

    private static TitlePageInfo CloneTitlePage(TitlePageInfo source) => new()
    {
        University = source.University,
        Department = source.Department,
        Discipline = source.Discipline,
        WorkType = source.WorkType,
        WorkTitle = source.WorkTitle,
        StudentName = source.StudentName,
        GroupNumber = source.GroupNumber,
        TeacherName = source.TeacherName,
        Year = source.Year,
        City = source.City
    };

    private static CodeListing CloneCodeListing(CodeListing source) => new()
    {
        Id = source.Id,
        FileName = source.FileName,
        RelativePath = source.RelativePath,
        Language = source.Language,
        Content = source.Content,
        IsSelected = source.IsSelected,
        Order = source.Order,
        ListingNumber = source.ListingNumber
    };

    private static BibliographySource CloneBibliographySource(
        BibliographySource source) => new()
        {
            Id = source.Id,
            Description = source.Description,
            IsSelected = source.IsSelected,
            Order = source.Order
        };

    private static DocumentModules CloneModules(DocumentModules source) => new()
    {
        HasTitlePage = source.HasTitlePage,
        HasTableOfContents = source.HasTableOfContents,
        HasBibliography = source.HasBibliography,
        HasAppendix = source.HasAppendix,
        ContentStartPage = source.ContentStartPage,
        AutoGenerateTOC = source.AutoGenerateTOC,
        TOCMaxLevel = source.TOCMaxLevel
    };

    private static DocumentCounters CloneCounters(DocumentCounters source) =>
        new()
        {
            ImagesCount = source.ImagesCount,
            TablesCount = source.TablesCount,
            SourcesCount = source.SourcesCount,
            PagesCount = source.PagesCount,
            ApplicationsCount = source.ApplicationsCount
        };
}
