namespace GostEditor.Core.Models;

public class GostDocument
{
    public TitlePageInfo TitlePage { get; set; } = new TitlePageInfo();
    public List<DocumentSection> Sections { get; set; } = [];
    public List<CodeListing> CodeListings { get; set; } = [];
    public List<ImageAttachment> Images { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public class TitlePageInfo
{
    public string University { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string WorkTitle { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string GroupNumber { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public int Year { get; set; } = DateTime.Now.Year;
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
    public string Language { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Order { get; set; }
}

public class ImageAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public string Caption { get; set; } = string.Empty;
}