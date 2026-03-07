using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xceed.Words.NET;
using Xceed.Document.NET;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;

using EngineParagraph = GostEditor.Core.TextEngine.DOM.Paragraph;
using EngineTextRun = GostEditor.Core.TextEngine.DOM.TextRun;
using EngineAlignment = GostEditor.Core.TextEngine.DOM.GostAlignment;
using ParagraphStyle = GostEditor.Core.TextEngine.DOM.ParagraphStyle;

namespace GostEditor.Core.Services;

public class ExportService : IExportService
{
    private const string FontName = "Times New Roman";
    private const float FontSize = 14f;
    private const float LineSpacing = 1.5f;
    private const float ParagraphIndent = 1.25f;

    private const float MarginLeft = 3.0f;
    private const float MarginRight = 1.0f;
    private const float MarginTop = 2.0f;
    private const float MarginBottom = 2.0f;

    public async Task ExportToDocxAsync(GostDocument document, string outputPath)
    {
        await Task.Run(() => BuildDocument(document, outputPath));
    }

    private void BuildDocument(GostDocument document, string outputPath)
    {
        using DocX doc = DocX.Create(outputPath);

        ApplyPageSettings(doc);
        AddPageNumbers(doc);
        RecalculateCounters(document);

        AddTitlePage(doc, document.TitlePage);

        // Автоматическое оглавление Word (TOC) - обновленный формат
        var tocSwitches = new Dictionary<TableOfContentsSwitches, string>
        {
            { TableOfContentsSwitches.O, "1-3" },
            { TableOfContentsSwitches.U, "" },
            { TableOfContentsSwitches.Z, "" },
            { TableOfContentsSwitches.H, "" }
        };
        doc.InsertTableOfContents("СОДЕРЖАНИЕ", tocSwitches);
        doc.InsertParagraph().InsertPageBreakAfterSelf();

        AddBody(doc, document.Paragraphs);
        AddCodeListings(doc, document.CodeListings);

        doc.Save();
    }

    private void RecalculateCounters(GostDocument document)
    {
        int listingNumber = 1;
        foreach (CodeListing listing in document.CodeListings.Where(l => l.IsSelected))
        {
            listing.ListingNumber = listingNumber++;
        }
    }

    private void ApplyPageSettings(DocX doc)
    {
        doc.MarginLeft = CmToTwips(MarginLeft);
        doc.MarginRight = CmToTwips(MarginRight);
        doc.MarginTop = CmToTwips(MarginTop);
        doc.MarginBottom = CmToTwips(MarginBottom);
    }

    private void AddTitlePage(DocX doc, TitlePageInfo titlePage)
    {
        Paragraph universityParagraph = doc.InsertParagraph();
        universityParagraph.Append(titlePage.University).Font(FontName).FontSize(FontSize);
        universityParagraph.Alignment = Alignment.center;

        Paragraph departmentParagraph = doc.InsertParagraph();
        departmentParagraph.Append(titlePage.Department).Font(FontName).FontSize(FontSize);
        departmentParagraph.Alignment = Alignment.center;

        doc.InsertParagraph(); doc.InsertParagraph();

        Paragraph titleParagraph = doc.InsertParagraph();
        titleParagraph.Append(titlePage.WorkTitle).Font(FontName).FontSize(FontSize).Bold();
        titleParagraph.Alignment = Alignment.center;

        doc.InsertParagraph(); doc.InsertParagraph();

        Paragraph studentParagraph = doc.InsertParagraph();
        studentParagraph.Append($"Выполнил: {titlePage.StudentName}").Font(FontName).FontSize(FontSize);
        studentParagraph.Alignment = Alignment.right;

        Paragraph groupParagraph = doc.InsertParagraph();
        groupParagraph.Append($"Группа: {titlePage.GroupNumber}").Font(FontName).FontSize(FontSize);
        groupParagraph.Alignment = Alignment.right;

        Paragraph teacherParagraph = doc.InsertParagraph();
        teacherParagraph.Append($"Проверил: {titlePage.TeacherName}").Font(FontName).FontSize(FontSize);
        teacherParagraph.Alignment = Alignment.right;

        doc.InsertParagraph(); doc.InsertParagraph();

        Paragraph yearParagraph = doc.InsertParagraph();
        yearParagraph.Append(titlePage.Year.ToString()).Font(FontName).FontSize(FontSize);
        yearParagraph.Alignment = Alignment.center;

        doc.InsertParagraph().InsertPageBreakAfterSelf();
    }

    private void AddBody(DocX doc, List<EngineParagraph> paragraphs)
    {
        int figureCounter = 1;

        foreach (EngineParagraph enginePara in paragraphs)
        {
            Paragraph wordPara = doc.InsertParagraph();
            ApplyParagraphStyle(wordPara);

            // Нативный метод разрыва страницы в Word!
            if (enginePara.PageBreakBefore)
            {
                wordPara.InsertPageBreakBeforeSelf();
            }

            wordPara.Alignment = MapAlignment(enginePara.Alignment);

            if (enginePara.Style == ParagraphStyle.Heading1)
            {
                wordPara.Heading(HeadingType.Heading1);
            }
            else if (enginePara.Style == ParagraphStyle.Heading2)
            {
                wordPara.Heading(HeadingType.Heading2);
            }

            if (enginePara.ImageData != null && enginePara.ImageData.Length > 0)
            {
                using MemoryStream imageStream = new MemoryStream(enginePara.ImageData);
                Xceed.Document.NET.Image docImage = doc.AddImage(imageStream);
                Picture picture = docImage.CreatePicture();

                if (enginePara.ImageWidth > 0 && enginePara.ImageHeight > 0)
                {
                    picture.Width = (int)enginePara.ImageWidth;
                    picture.Height = (int)enginePara.ImageHeight;
                }

                wordPara.AppendPicture(picture);

                Paragraph captionPara = doc.InsertParagraph();
                captionPara.Append($"Рисунок {figureCounter} — ").Font(FontName).FontSize(FontSize);
                captionPara.Alignment = Alignment.center;
                figureCounter++;

                foreach (EngineTextRun run in enginePara.Runs)
                {
                    Formatting formatting = new Formatting { FontFamily = new Font(FontName), Size = run.FontSize };
                    if (run.IsBold) formatting.Bold = true;
                    if (run.IsItalic) formatting.Italic = true;

                    captionPara.Append(run.Text, formatting);
                }
            }
            else
            {
                foreach (EngineTextRun run in enginePara.Runs)
                {
                    Formatting formatting = new Formatting { FontFamily = new Font(FontName), Size = run.FontSize };
                    if (run.IsBold) formatting.Bold = true;
                    if (run.IsItalic) formatting.Italic = true;

                    wordPara.Append(run.Text, formatting);
                }
            }
        }
    }

    private Alignment MapAlignment(EngineAlignment alignment)
    {
        if (alignment == EngineAlignment.Left) return Alignment.left;
        if (alignment == EngineAlignment.Center) return Alignment.center;
        if (alignment == EngineAlignment.Right) return Alignment.right;
        if (alignment == EngineAlignment.Justify) return Alignment.both;
        return Alignment.left;
    }

    private void AddCodeListings(DocX doc, List<CodeListing> listings)
    {
        List<CodeListing> selectedListings = listings.Where(l => l.IsSelected).ToList();
        if (selectedListings.Count == 0) return;

        Paragraph listingsHeading = doc.InsertParagraph();
        listingsHeading.InsertPageBreakBeforeSelf();
        listingsHeading.Append("ПРИЛОЖЕНИЕ. ЛИСТИНГИ ПРОГРАММНОГО КОДА")
            .Font(FontName).FontSize(FontSize).Bold();
        listingsHeading.Alignment = Alignment.center;
        listingsHeading.Heading(HeadingType.Heading1);

        doc.InsertParagraph();

        foreach (CodeListing listing in selectedListings)
        {
            Paragraph fileNameParagraph = doc.InsertParagraph();
            fileNameParagraph.Append($"Листинг {listing.ListingNumber} — {listing.RelativePath}")
                .Font(FontName).FontSize(FontSize).Italic();
            fileNameParagraph.Alignment = Alignment.left;

            Paragraph codeParagraph = doc.InsertParagraph();
            codeParagraph.Append(listing.Content).Font("Courier New").FontSize(12);
            codeParagraph.Alignment = Alignment.left;

            doc.InsertParagraph();
        }
    }

    private void ApplyParagraphStyle(Paragraph paragraph)
    {
        paragraph.LineSpacingAfter = 0;
        paragraph.LineSpacing = LineSpacing * 240f;
        paragraph.IndentationFirstLine = CmToTwips(ParagraphIndent);
    }

    private float CmToTwips(float cm)
    {
        return cm * 567f;
    }

    private void AddPageNumbers(DocX doc)
    {
        Footer footer = doc.Footers.Odd;
        if (footer != null)
        {
            Paragraph footerParagraph = footer.Paragraphs.First();
            footerParagraph.Alignment = Alignment.center;
            footerParagraph.AppendPageNumber(PageNumberFormat.normal).Font(FontName).FontSize(FontSize);
        }
    }
}
