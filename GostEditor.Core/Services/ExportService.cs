using Xceed.Words.NET;
using Xceed.Document.NET;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;

namespace GostEditor.Core.Services;

public class ExportService : IExportService
{
    // Константы ГОСТ 7.32.
    private const string FontName = "Times New Roman";
    private const float FontSize = 14f;
    private const float LineSpacing = 1.5f;
    private const float ParagraphIndent = 1.25f;

    // Поля страницы в сантиметрах.
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
        RecalculateCounters(document);
        AddTitlePage(doc, document.TitlePage);
        AddTableOfContents(doc, document.Sections);
        AddSections(doc, document.Sections, document.Images);
        AddCodeListings(doc, document.CodeListings);

        doc.Save();
    }

    // Пересчитывает все номера рисунков и листингов перед экспортом.
    private void RecalculateCounters(GostDocument document)
    {
        int figureNumber = 1;
        int listingNumber = 1;

        foreach (ImageAttachment image in document.Images)
        {
            image.FigureNumber = figureNumber++;
        }

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
        universityParagraph.Append(titlePage.University)
            .Font(FontName)
            .FontSize(FontSize);
        universityParagraph.Alignment = Alignment.center;

        Paragraph departmentParagraph = doc.InsertParagraph();
        departmentParagraph.Append(titlePage.Department)
            .Font(FontName)
            .FontSize(FontSize);
        departmentParagraph.Alignment = Alignment.center;

        doc.InsertParagraph();
        doc.InsertParagraph();

        Paragraph titleParagraph = doc.InsertParagraph();
        titleParagraph.Append(titlePage.WorkTitle)
            .Font(FontName)
            .FontSize(FontSize)
            .Bold();
        titleParagraph.Alignment = Alignment.center;

        doc.InsertParagraph();
        doc.InsertParagraph();

        Paragraph studentParagraph = doc.InsertParagraph();
        studentParagraph.Append($"Выполнил: {titlePage.StudentName}")
            .Font(FontName)
            .FontSize(FontSize);
        studentParagraph.Alignment = Alignment.right;

        Paragraph groupParagraph = doc.InsertParagraph();
        groupParagraph.Append($"Группа: {titlePage.GroupNumber}")
            .Font(FontName)
            .FontSize(FontSize);
        groupParagraph.Alignment = Alignment.right;

        Paragraph teacherParagraph = doc.InsertParagraph();
        teacherParagraph.Append($"Проверил: {titlePage.TeacherName}")
            .Font(FontName)
            .FontSize(FontSize);
        teacherParagraph.Alignment = Alignment.right;

        doc.InsertParagraph();
        doc.InsertParagraph();

        Paragraph yearParagraph = doc.InsertParagraph();
        yearParagraph.Append(titlePage.Year.ToString())
            .Font(FontName)
            .FontSize(FontSize);
        yearParagraph.Alignment = Alignment.center;

        // Разрыв страницы после титульника.
        doc.InsertParagraph().InsertPageBreakAfterSelf();
    }

    private void AddTableOfContents(DocX doc, List<DocumentSection> sections)
    {
        Paragraph tocHeading = doc.InsertParagraph();
        tocHeading.Append("СОДЕРЖАНИЕ")
            .Font(FontName)
            .FontSize(FontSize)
            .Bold();
        tocHeading.Alignment = Alignment.center;

        doc.InsertParagraph();

        int pageNumber = 3;

        foreach (DocumentSection section in sections)
        {
            Paragraph tocLine = doc.InsertParagraph();
            tocLine.Append($"{section.Title}")
                .Font(FontName)
                .FontSize(FontSize);
            tocLine.Alignment = Alignment.left;
            pageNumber++;
        }

        // Разрыв страницы после содержания.
        doc.InsertParagraph().InsertPageBreakAfterSelf();
    }

    private void AddSections(
        DocX doc,
        List<DocumentSection> sections,
        List<ImageAttachment> images)
    {
        int sectionNumber = 1;

        foreach (DocumentSection section in sections)
        {
            Paragraph headingParagraph = doc.InsertParagraph();
            headingParagraph.Append($"{sectionNumber}. {section.Title}")
                .Font(FontName)
                .FontSize(FontSize)
                .Bold();
            headingParagraph.Alignment = Alignment.left;

            Paragraph contentParagraph = doc.InsertParagraph();
            contentParagraph.Append(section.Content)
                .Font(FontName)
                .FontSize(FontSize);
            contentParagraph.Alignment = Alignment.both;
            ApplyParagraphStyle(contentParagraph);

            // Вставляем картинки относящиеся к этому разделу.
            foreach (ImageAttachment image in images.Where(img => img.Data.Length > 0))
            {
                using MemoryStream imageStream = new MemoryStream(image.Data);
                Xceed.Document.NET.Image docImage = doc.AddImage(imageStream);
                Picture picture = docImage.CreatePicture();
                Paragraph imageParagraph = doc.InsertParagraph();
                imageParagraph.AppendPicture(picture);
                imageParagraph.Alignment = Alignment.center;

                // Подпись рисунка.
                Paragraph captionParagraph = doc.InsertParagraph();
                captionParagraph.Append(
                    $"Рисунок {image.FigureNumber} — {image.Caption}")
                    .Font(FontName)
                    .FontSize(FontSize);
                captionParagraph.Alignment = Alignment.center;
            }

            doc.InsertParagraph();
            sectionNumber++;
        }
    }

    private void AddCodeListings(DocX doc, List<CodeListing> listings)
    {
        List<CodeListing> selectedListings = listings
            .Where(l => l.IsSelected)
            .ToList();

        if (selectedListings.Count == 0)
        {
            return;
        }

        Paragraph listingsHeading = doc.InsertParagraph();
        listingsHeading.Append("ПРИЛОЖЕНИЕ. ЛИСТИНГИ ПРОГРАММНОГО КОДА")
            .Font(FontName)
            .FontSize(FontSize)
            .Bold();
        listingsHeading.Alignment = Alignment.center;

        doc.InsertParagraph();

        foreach (CodeListing listing in selectedListings)
        {
            // Заголовок листинга с относительным путём.
            Paragraph fileNameParagraph = doc.InsertParagraph();
            fileNameParagraph.Append(
                $"Листинг {listing.ListingNumber} — {listing.RelativePath}")
                .Font(FontName)
                .FontSize(FontSize)
                .Italic();
            fileNameParagraph.Alignment = Alignment.left;

            // Код моноширинным шрифтом.
            Paragraph codeParagraph = doc.InsertParagraph();
            codeParagraph.Append(listing.Content)
                .Font("Courier New")
                .FontSize(12);
            codeParagraph.Alignment = Alignment.left;

            doc.InsertParagraph();
        }
    }

    private void ApplyParagraphStyle(Paragraph paragraph)
    {
        // Межстрочный интервал 1.5.
        paragraph.LineSpacingAfter = 0;
        paragraph.LineSpacing = LineSpacing * 240f;

        // Отступ первой строки 1.25 см.
        paragraph.IndentationFirstLine = CmToTwips(ParagraphIndent);
    }

    // Конвертация сантиметров в твипы (1 см = 567 твипов).
    private float CmToTwips(float cm)
    {
        return cm * 567f;
    }
}
