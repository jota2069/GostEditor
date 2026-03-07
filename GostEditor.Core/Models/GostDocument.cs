using System;
using System.Collections.Generic;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.Models;

public class GostDocument
{
    public TitlePageInfo TitlePage { get; set; } = new TitlePageInfo();

    public List<Paragraph> Paragraphs { get; set; } = new List<Paragraph>();

    public List<CodeListing> CodeListings { get; set; } = new List<CodeListing>();
    public List<ImageAttachment> Images { get; set; } = new List<ImageAttachment>();
    public DocumentCounters Counters { get; set; } = new DocumentCounters();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    public double PageWidth { get; set; } = 794.0;
    public double PageHeight { get; set; } = 1123.0;

    public double MarginLeft { get; set; } = 113.0;
    public double MarginRight { get; set; } = 57.0;
    public double MarginTop { get; set; } = 76.0;
    public double MarginBottom { get; set; } = 76.0;

    public double ContentWidth => PageWidth - MarginLeft - MarginRight;
    public double ContentHeight => PageHeight - MarginTop - MarginBottom;
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

public class NavigationItem
{
    public string Title { get; set; } = string.Empty;
    public int ParagraphIndex { get; set; }
    public int Level { get; set; }

    // === ИСПРАВЛЕНИЕ: Хелперы для Левой панели ===
    public bool IsSubChapter => Level > 1; // Если это подраздел - сделаем отступ
    public string FontWeight => Level == 1 ? "Bold" : "Normal"; // Главы делаем жирными
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
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string Caption { get; set; } = string.Empty;
    public int Order { get; set; }
}

public class DocumentCounters
{
    public int ImagesCount { get; set; }
    public int TablesCount { get; set; }
    public int SourcesCount { get; set; }
    public int PagesCount { get; set; }
    public int ApplicationsCount { get; set; }
}
