using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using Xceed.Document.NET;
using Xceed.Words.NET;

namespace GostEditor.Core.Services;

public class ExportService : IExportService
{
    private const string GlobalFontName = "Times New Roman";
    private const double GlobalFontSize = 14D;
    private const float ParagraphIndentCm = 1.25f;
    private const float CmToPoints = 28.35f; // 1 см = 28.35 пунктов

    public async Task ExportToDocxAsync(GostDocument document, string outputPath)
    {
        await Task.Run(() => BuildDocument(document, outputPath));
    }

    private void BuildDocument(GostDocument document, string outputPath)
    {
        Debug.WriteLine($"[EXPORT] Начало экспорта. Параграфов: {document.Paragraphs.Count}");

        using FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        DocX doc = DocX.Create(fs);

        try
        {
            ApplyPageSettings(doc);
            AddPageNumbers(doc);

            if (document.Modules.HasTitlePage) AddTitlePage(doc, document.TitlePage);
            if (document.Modules.HasTableOfContents) AddTableOfContents(doc, document.Modules.TOCMaxLevel);

            AddBody(doc, document.Paragraphs);

            if (document.Modules.HasBibliography)
            {
                AddBibliography(doc, document.BibliographySources);
            }

            if (document.Modules.HasAppendix && document.CodeListings.Any(listing => listing.IsSelected))
            {
                Debug.WriteLine($"[EXPORT] Добавление приложений. Листингов: {document.CodeListings.Count(l => l.IsSelected)}");
                AddCodeListings(doc, document.CodeListings);
            }

            doc.Save();
            Debug.WriteLine($"[EXPORT] Документ сохранён успешно");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EXPORT ERROR] {ex}");
            throw;
        }
        finally
        {
            doc.Dispose();
        }
    }

    private void ApplyPageSettings(DocX doc)
    {
        doc.MarginLeft = CmToPoints * 3.0f;   // 3 см
        doc.MarginRight = CmToPoints * 1.5f;  // 1.5 см
        doc.MarginTop = CmToPoints * 2.0f;    // 2 см
        doc.MarginBottom = CmToPoints * 2.0f; // 2 см
    }

    private void AddPageNumbers(DocX doc)
    {
        doc.AddFooters();
        if (doc.Footers.Odd is { } footer)
        {
            Paragraph footerParagraph = footer.Paragraphs.Count > 0 ? footer.Paragraphs[0] : footer.InsertParagraph();
            footerParagraph.Alignment = Alignment.center;
            footerParagraph.AppendPageNumber(PageNumberFormat.normal).Font(new Font(GlobalFontName)).FontSize(GlobalFontSize);
        }
    }

    private void AddTitlePage(DocX doc, TitlePageInfo titlePage)
    {
        doc.InsertParagraph("Министерство науки и высшего образования Российской Федерации").Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Alignment = Alignment.center;
        doc.InsertParagraph("Федеральное государственное бюджетное образовательное учреждение").Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Alignment = Alignment.center;
        doc.InsertParagraph("высшего образования").Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Alignment = Alignment.center;
        doc.InsertParagraph(titlePage.University).Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Bold().Alignment = Alignment.center;
        doc.InsertParagraph(titlePage.Department).Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Alignment = Alignment.center;

        doc.InsertParagraph(); doc.InsertParagraph();

        doc.InsertParagraph(titlePage.WorkType).Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Alignment = Alignment.center;
        doc.InsertParagraph($"по дисциплине «{titlePage.Discipline}»").Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Alignment = Alignment.center;
        doc.InsertParagraph($"на тему «{titlePage.WorkTitle}»").Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Alignment = Alignment.center;

        for (int i = 0; i < 5; i++) doc.InsertParagraph();

        doc.InsertParagraph("Выполнил:").Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Bold().Alignment = Alignment.right;
        doc.InsertParagraph($"студент группы {titlePage.GroupNumber}").Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Alignment = Alignment.right;
        doc.InsertParagraph(titlePage.StudentName).Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Alignment = Alignment.right;

        doc.InsertParagraph("Принял:").Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Bold().Alignment = Alignment.right;
        doc.InsertParagraph(titlePage.TeacherName).Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Alignment = Alignment.right;

        for (int i = 0; i < 5; i++) doc.InsertParagraph();

        doc.InsertParagraph($"{titlePage.City}, {titlePage.Year} г.").Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Alignment = Alignment.center;
        doc.InsertParagraph().InsertPageBreakAfterSelf();
    }

    private void AddTableOfContents(DocX doc, int maxLevel)
    {
        int normalizedMaxLevel = Math.Clamp(maxLevel, 1, 3);
        Dictionary<TableOfContentsSwitches, string> switches = new Dictionary<TableOfContentsSwitches, string>
        {
            [TableOfContentsSwitches.O] = $"1-{normalizedMaxLevel}",
            [TableOfContentsSwitches.H] = string.Empty,
            [TableOfContentsSwitches.Z] = string.Empty,
            [TableOfContentsSwitches.U] = string.Empty
        };

        doc.InsertTableOfContents(
            "СОДЕРЖАНИЕ",
            switches,
            "Heading1",
            null);
        doc.InsertParagraph().InsertPageBreakAfterSelf();
    }

    private void AddBody(DocX doc, List<GostEditor.Core.TextEngine.DOM.Paragraph> paragraphs)
    {
        int figureCounter = 1;

        foreach (GostEditor.Core.TextEngine.DOM.Paragraph engineParagraph in paragraphs)
        {
            if (engineParagraph.ImageData is { Length: > 0 })
            {
                InsertImage(doc, engineParagraph, ref figureCounter);
                continue;
            }

            Paragraph wordParagraph = doc.InsertParagraph();
            ApplyGostStyle(wordParagraph, engineParagraph);

            bool textAdded = false;
            foreach (GostEditor.Core.TextEngine.DOM.TextRun run in engineParagraph.Runs)
            {
                if (string.IsNullOrEmpty(run.Text)) continue;

                var appender = wordParagraph.Append(run.Text)
                                            .Font(new Font(GlobalFontName))
                                            .FontSize(run.FontSize > 0 ? run.FontSize : GlobalFontSize);

                if (run.IsBold) appender.Bold();
                if (run.IsItalic) appender.Italic();

                textAdded = true;
            }

            if (!textAdded) wordParagraph.Append("\u00A0").Font(new Font(GlobalFontName)).FontSize(GlobalFontSize);
        }
    }

    private void InsertImage(DocX doc, GostEditor.Core.TextEngine.DOM.Paragraph enginePara, ref int counter)
    {
        try
        {
            using MemoryStream ms = new MemoryStream(enginePara.ImageData!);
            Image img = doc.AddImage(ms);
            Picture pic = img.CreatePicture();
            pic.Width = enginePara.ImageWidth > 0 ? (int)enginePara.ImageWidth : 450;
            pic.Height = enginePara.ImageHeight > 0 ? (int)enginePara.ImageHeight : 300;

            doc.InsertParagraph().AppendPicture(pic).Alignment = Alignment.center;
            doc.InsertParagraph($"Рисунок {counter++} — Подпись").Font(new Font(GlobalFontName)).FontSize(12D).Alignment = Alignment.center;
        }
        catch { /* Игнорируем битые картинки */ }
    }

    private void AddCodeListings(DocX doc, List<CodeListing> listings)
    {
        List<CodeListing> selectedListings = listings.Where(listing => listing.IsSelected).ToList();
        if (selectedListings.Count == 0)
        {
            Debug.WriteLine("[EXPORT] Нет выбранных листингов для приложения");
            return;
        }

        Paragraph listingsHeading = doc.InsertParagraph("ПРИЛОЖЕНИЕ А");
        listingsHeading.Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Bold().Alignment = Alignment.center;
        listingsHeading.InsertPageBreakBeforeSelf();
        listingsHeading.Heading(HeadingType.Heading1);

        doc.InsertParagraph();

        int counter = 1;

        foreach (CodeListing listing in selectedListings)
        {
            // Заголовок листинга: 12pt курсив, выравнивание слева
            doc.InsertParagraph($"Листинг {counter} — файл {listing.RelativePath}")
                .Font(new Font(GlobalFontName))
                .FontSize(12D)
                .Italic()
                .Alignment = Alignment.left;

            Debug.WriteLine($"[EXPORT] Листинг {counter}: {listing.RelativePath}");

            string[] lines = listing.Content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            Debug.WriteLine($"[EXPORT]   Строк кода: {lines.Length}");

            foreach (string line in lines)
            {
                string cleanLine = line.Replace("\t", "    ");

                if (string.IsNullOrWhiteSpace(cleanLine))
                {
                    cleanLine = "\u00A0";
                }

                // Код: 10pt Consolas, выравнивание слева
                doc.InsertParagraph(cleanLine)
                    .Font(new Font("Consolas"))
                    .FontSize(10D)
                    .Alignment = Alignment.left;
            }

            doc.InsertParagraph();

            counter++;
        }

        Debug.WriteLine($"[EXPORT] Добавлено листингов: {selectedListings.Count}");
    }

    private void AddBibliography(DocX doc, List<BibliographySource> sources)
    {
        List<BibliographySource> selectedSources = sources
            .Where(source => source.IsSelected && !string.IsNullOrWhiteSpace(source.Description))
            .OrderBy(source => source.Order)
            .ToList();

        if (selectedSources.Count == 0)
        {
            return;
        }

        Paragraph heading = doc.InsertParagraph("СПИСОК ИСПОЛЬЗОВАННЫХ ИСТОЧНИКОВ");
        heading.Font(new Font(GlobalFontName)).FontSize(GlobalFontSize).Bold().Alignment = Alignment.center;
        heading.InsertPageBreakBeforeSelf();
        heading.Heading(HeadingType.Heading1);

        doc.InsertParagraph();

        for (int i = 0; i < selectedSources.Count; i++)
        {
            doc.InsertParagraph($"{i + 1}. {selectedSources[i].Description}")
                .Font(new Font(GlobalFontName))
                .FontSize(GlobalFontSize)
                .Alignment = Alignment.both;
        }
    }

    private void ApplyGostStyle(Paragraph wordPara, GostEditor.Core.TextEngine.DOM.Paragraph enginePara)
    {
        if (enginePara.FirstLineIndent > 0)
            wordPara.IndentationFirstLine = CmToPoints * ParagraphIndentCm;

        wordPara.Alignment = enginePara.Alignment switch
        {
            GostEditor.Core.TextEngine.DOM.GostAlignment.Center => Alignment.center,
            GostEditor.Core.TextEngine.DOM.GostAlignment.Right => Alignment.right,
            GostEditor.Core.TextEngine.DOM.GostAlignment.Justify => Alignment.both,
            _ => Alignment.left
        };

        if (enginePara.Style == GostEditor.Core.TextEngine.DOM.ParagraphStyle.Heading1)
            wordPara.Heading(HeadingType.Heading1);
        else if (enginePara.Style == GostEditor.Core.TextEngine.DOM.ParagraphStyle.Heading2)
            wordPara.Heading(HeadingType.Heading2);
    }
}
