using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using SkiaSharp;
using Xceed.Document.NET;
using Xceed.Words.NET;
using XceedColor = Xceed.Drawing.Color;

namespace GostEditor.Core.Services;

public class ExportService : IExportService
{
    private const string GlobalFontName = "Times New Roman";
    private const double GlobalFontSize = 14D;
    private const float ParagraphIndentCm = 1.25f;
    private const float CmToPoints = 28.35f; // 1 см = 28.35 пунктов
    private const double DipToPoints = 72D / 96D;

    private readonly IImageService _imageService;

    public ExportService()
        : this(new ImageService())
    {
    }

    public ExportService(IImageService imageService)
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
    }

    public Task ExportToDocxAsync(GostDocument document, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        return Task.Run(() => BuildDocument(document, outputPath));
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

            AddBody(doc, document);

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

    private void AddBody(DocX doc, GostDocument document)
    {
        int figureCounter = 1;

        for (int paragraphIndex = 0;
             paragraphIndex < document.Paragraphs.Count;
             paragraphIndex++)
        {
            GostEditor.Core.TextEngine.DOM.Paragraph engineParagraph =
                document.Paragraphs[paragraphIndex];

            if (engineParagraph.ImageId.HasValue)
            {
                ImageResult<ResolvedImagePlacement> resolved =
                    _imageService.ResolvePlacement(document, paragraphIndex);
                if (!resolved.IsSuccess)
                {
                    throw CreateImageResolutionException(
                        paragraphIndex,
                        engineParagraph.ImageId.Value,
                        resolved.Error);
                }

                InsertImage(
                    doc,
                    engineParagraph,
                    resolved.Value!,
                    figureCounter);
                figureCounter++;
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

    private static void InsertImage(
        DocX doc,
        GostEditor.Core.TextEngine.DOM.Paragraph engineParagraph,
        ResolvedImagePlacement image,
        int figureNumber)
    {
        int width = ConvertDipToDocxPoints(
            image.Size.Width,
            "ширина",
            image,
            figureNumber);
        int height = ConvertDipToDocxPoints(
            image.Size.Height,
            "высота",
            image,
            figureNumber);

        Picture picture;
        try
        {
            using MemoryStream ms = new MemoryStream(image.Content.Data.ToArray());
            Image img = doc.AddImage(ms);
            picture = img.CreatePicture();
            picture.Width = width;
            picture.Height = height;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Не удалось экспортировать рисунок {figureNumber} " +
                $"из абзаца {image.ParagraphIndex} " +
                $"(ImageId={image.ImageId}): данные изображения повреждены " +
                "или имеют неподдерживаемый формат.",
                ex);
        }

        doc.InsertParagraph()
            .AppendPicture(picture)
            .Alignment = Alignment.center;
        Paragraph captionParagraph = doc.InsertParagraph();
        captionParagraph.Alignment = Alignment.center;
        captionParagraph
            .Append($"Рисунок {figureNumber}")
            .Font(new Font(GlobalFontName))
            .FontSize(12D);

        if (!string.IsNullOrWhiteSpace(engineParagraph.GetPlainText()))
        {
            captionParagraph
                .Append(" — ")
                .Font(new Font(GlobalFontName))
                .FontSize(12D);

            foreach (GostEditor.Core.TextEngine.DOM.TextRun run in engineParagraph.Runs)
            {
                if (string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                Formatting formatting = new Formatting
                {
                    FontFamily = new Font(GlobalFontName),
                    Size = run.FontSize > 0 ? run.FontSize : 12D,
                    FontColor = ToDrawingColor(run.Color),
                    Bold = run.IsBold,
                    Italic = run.IsItalic
                };
                captionParagraph.Append(run.Text, formatting);
            }
        }
    }

    private static int ConvertDipToDocxPoints(
        double value,
        string dimensionName,
        ResolvedImagePlacement image,
        int figureNumber)
    {
        try
        {
            int points = checked((int)Math.Round(
                value * DipToPoints,
                MidpointRounding.AwayFromZero));
            if (points <= 0)
            {
                throw new OverflowException();
            }

            return points;
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException(
                $"Не удалось экспортировать рисунок {figureNumber} " +
                $"из абзаца {image.ParagraphIndex} " +
                $"(ImageId={image.ImageId}): {dimensionName} {value} DIP " +
                "не может быть представлена в DOCX.",
                ex);
        }
    }

    private static InvalidDataException CreateImageResolutionException(
        int paragraphIndex,
        Guid imageId,
        ImageError? error)
    {
        string details = error is null
            ? "сервис изображений не вернул ни результат, ни описание ошибки"
            : $"{error.Code}: {error.Message}";

        return new InvalidDataException(
            $"Не удалось получить изображение из абзаца {paragraphIndex} " +
            $"(ImageId={imageId}) для экспорта: {details}.");
    }

    private static XceedColor ToDrawingColor(uint argb)
    {
        return new XceedColor(
            new SKColor(
                (byte)(argb >> 16),
                (byte)(argb >> 8),
                (byte)argb,
                (byte)(argb >> 24)));
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
