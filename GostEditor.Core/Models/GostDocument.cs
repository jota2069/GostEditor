namespace GostEditor.Core.Models;

public class GostDocument
{
    public TitlePageInfo TitlePage { get; set; } = new TitlePageInfo();
    public List<DocumentSection> Sections { get; set; } = [];
    public List<CodeListing> CodeListings { get; set; } = [];
    public List<ImageAttachment> Images { get; set; } = [];
    public DocumentCounters Counters { get; set; } = new DocumentCounters();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public class TitlePageInfo
{
    public string University { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Discipline { get; set; } = string.Empty;
    public string WorkType { get; set; } = string.Empty;
    public string WorkTitle { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string GroupNumber { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public int Year { get; set; } = DateTime.Now.Year;

    public string City { get; set; } = string.Empty;
}

public class DocumentSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Order { get; set; }
}

public class CodeListing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;
    public int Order { get; set; }
    public int ListingNumber { get; set; }
}

public class ImageAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public string Caption { get; set; } = string.Empty;
    public int FigureNumber { get; set; }
}

public class DocumentCounters
{
    public int FigureCount { get; set; } = 0;
    public int TableCount { get; set; } = 0;
    public int ListingCount { get; set; } = 0;
}
