using Xceed.Words.NET;
using Xceed.Document.NET;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;

namespace GostEditor.Core.Services;

public class ExportService : IExportService
{
    // Константы ГОСТ 7.32
    private const string FontName = "Times New Roman";
    private const float FontSize = 14f;
    private const float LineSpacing = 1.5f;
    private const float ParagraphIndent = 1.25f;

    // Поля страницы в сантиметрах: левое, правое, верхнее, нижнее
    private const float MarginLeft = 3.0f;
    private const float MarginRight = 1.5f;
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
        AddTitlePage(doc, document.TitlePage);
        AddSections(doc, document.Sections);
        AddCodeListings(doc, document.CodeListings);

        doc.Save();
    }

    private void ApplyPageSettings(DocX doc)
    {
        // Поля страницы в сантиметрах
        doc.MarginLeft = CmToTwips(MarginLeft);
        doc.MarginRight = CmToTwips(MarginRight);
        doc.MarginTop = CmToTwips(MarginTop);
        doc.MarginBottom = CmToTwips(MarginBottom);
    }

    private void AddTitlePage(DocX doc, TitlePageInfo titlePage)
    {
        // Университет
        Paragraph universityParagraph = doc.InsertParagraph();
        universityParagraph.Append(titlePage.University)
            .Font(FontName)
            .FontSize(FontSize);
        universityParagraph.Alignment = Alignment.center;

        // Кафедра
        Paragraph departmentParagraph = doc.InsertParagraph();
        departmentParagraph.Append(titlePage.Department)
            .Font(FontName)
            .FontSize(FontSize);
        departmentParagraph.Alignment = Alignment.center;

        // Пустые строки перед названием работы
        doc.InsertParagraph();
        doc.InsertParagraph();

        // Название работы жирным
        Paragraph titleParagraph = doc.InsertParagraph();
        titleParagraph.Append(titlePage.WorkTitle)
            .Font(FontName)
            .FontSize(FontSize)
            .Bold();
        titleParagraph.Alignment = Alignment.center;

        // Пустые строки перед данными студента
        doc.InsertParagraph();
        doc.InsertParagraph();

        // Студент
        Paragraph studentParagraph = doc.InsertParagraph();
        studentParagraph.Append($"Выполнил: {titlePage.StudentName}")
            .Font(FontName)
            .FontSize(FontSize);
        studentParagraph.Alignment = Alignment.right;

        // Группа
        Paragraph groupParagraph = doc.InsertParagraph();
        groupParagraph.Append($"Группа: {titlePage.GroupNumber}")
            .Font(FontName)
            .FontSize(FontSize);
        groupParagraph.Alignment = Alignment.right;

        // Преподаватель
        Paragraph teacherParagraph = doc.InsertParagraph();
        teacherParagraph.Append($"Проверил: {titlePage.TeacherName}")
            .Font(FontName)
            .FontSize(FontSize);
        teacherParagraph.Alignment = Alignment.right;

        // Год внизу страницы
        doc.InsertParagraph();
        doc.InsertParagraph();

        Paragraph yearParagraph = doc.InsertParagraph();
        yearParagraph.Append(titlePage.Year.ToString())
            .Font(FontName)
            .FontSize(FontSize);
        yearParagraph.Alignment = Alignment.center;

        // Разрыв страницы после титульника
        doc.InsertParagraph().InsertPageBreakAfterSelf();
    }

    private void AddSections(DocX doc, List<DocumentSection> sections)
    {
        foreach (DocumentSection section in sections)
        {
            // Заголовок раздела жирным
            Paragraph headingParagraph = doc.InsertParagraph();
            headingParagraph.Append(section.Title)
                .Font(FontName)
                .FontSize(FontSize)
                .Bold();
            headingParagraph.Alignment = Alignment.left;

            // Текст раздела
            Paragraph contentParagraph = doc.InsertParagraph();
            contentParagraph.Append(section.Content)
                .Font(FontName)
                .FontSize(FontSize);
            contentParagraph.Alignment = Alignment.both;
            ApplyParagraphStyle(contentParagraph);

            doc.InsertParagraph();
        }
    }

    private void AddCodeListings(DocX doc, List<CodeListing> listings)
    {
        if (listings.Count == 0)
        {
            return;
        }

        // Заголовок блока листингов
        Paragraph listingsHeading = doc.InsertParagraph();
        listingsHeading.Append("Листинги программного кода")
            .Font(FontName)
            .FontSize(FontSize)
            .Bold();
        listingsHeading.Alignment = Alignment.left;

        doc.InsertParagraph();

        foreach (CodeListing listing in listings)
        {
            // Название файла
            Paragraph fileNameParagraph = doc.InsertParagraph();
            fileNameParagraph.Append($"Листинг — {listing.FileName}")
                .Font(FontName)
                .FontSize(FontSize)
                .Italic();
            fileNameParagraph.Alignment = Alignment.left;

            // Код моноширинным шрифтом, размер 12
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
        // Межстрочный интервал 1.5
        paragraph.LineSpacingAfter = 0;
        paragraph.LineSpacing = (float)(LineSpacing * 240);

        // Отступ первой строки 1.25 см
        paragraph.IndentationFirstLine = CmToTwips(ParagraphIndent);
    }

    // Конвертация сантиметров в твипы (единица измерения Word)
    // 1 см = 567 твипов
    private float CmToTwips(float cm)
    {
        return cm * 567f;
    }
}