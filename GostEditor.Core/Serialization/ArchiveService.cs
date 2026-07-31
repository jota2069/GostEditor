using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.Serialization;

/// <summary>
/// Сервис для сохранения и загрузки документов в собственном формате (.gost)
/// Формат: ZIP-архив с document.json + изображениями в папке media/
/// </summary>
public class ArchiveService : IArchiveService
{
    private const int CurrentFormatVersion = 1;

    public GostDocument CreateNew()
    {
        GostDocument document = new GostDocument();

        Paragraph welcomeParagraph = new Paragraph();
        welcomeParagraph.Runs.Add(new TextRun
        {
            Text = "Добро пожаловать в GostEditor!",
            FontSize = 14,
            IsBold = false,
            IsItalic = false
        });

        document.Paragraphs.Add(welcomeParagraph);

        return document;
    }

    public async Task<GostDocument> LoadAsync(string filePath)
    {
        await using FileStream fileStream = File.OpenRead(filePath);
        return await LoadAsync(fileStream);
    }

    public async Task<GostDocument> LoadAsync(Stream stream)
    {
        using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        ZipArchiveEntry? jsonEntry = archive.GetEntry("document.json");

        if (jsonEntry == null)
        {
            throw new InvalidDataException("Файл document.json не найден в архиве .gost");
        }

        string jsonText;

        using (Stream jsonStream = jsonEntry.Open())
        using (StreamReader reader = new StreamReader(jsonStream, Encoding.UTF8))
        {
            jsonText = await reader.ReadToEndAsync();
        }

        Debug.WriteLine($"[ARCHIVE] JSON загружен, длина: {jsonText.Length} символов");

        DocModel? docModel = JsonConvert.DeserializeObject<DocModel>(jsonText);

        if (docModel == null)
        {
            throw new InvalidDataException("Не удалось десериализовать document.json");
        }

        docModel = MigrateToCurrentVersion(docModel);

        Debug.WriteLine($"[ARCHIVE] Версия формата: {docModel.FormatVersion}");
        Debug.WriteLine($"[ARCHIVE] Десериализовано параграфов: {docModel.Paragraphs.Count}");

        GostDocument document = new GostDocument
        {
            TitlePage = docModel.TitlePage ?? new TitlePageInfo(),
            CodeListings = docModel.CodeListings ?? new List<CodeListing>(),
            Images = docModel.Images ?? new List<ImageAttachment>(),
            BibliographySources = docModel.BibliographySources ?? new List<BibliographySource>(),
            Modules = docModel.Modules ?? new DocumentModules(),
            Counters = docModel.Counters ?? new DocumentCounters(),
            CreatedAt = docModel.CreatedAt == default ? DateTime.UtcNow : docModel.CreatedAt,
            ModifiedAt = docModel.ModifiedAt == default ? DateTime.UtcNow : docModel.ModifiedAt,
            PageWidth = docModel.PageWidth > 0 ? docModel.PageWidth : 794.0,
            PageHeight = docModel.PageHeight > 0 ? docModel.PageHeight : 1123.0,
            MarginLeft = docModel.MarginLeft > 0 ? docModel.MarginLeft : 113.0,
            MarginRight = docModel.MarginRight > 0 ? docModel.MarginRight : 57.0,
            MarginTop = docModel.MarginTop > 0 ? docModel.MarginTop : 76.0,
            MarginBottom = docModel.MarginBottom > 0 ? docModel.MarginBottom : 76.0
        };

        int paragraphIndex = 0;

        foreach (ParaModel paraModel in docModel.Paragraphs)
        {
            Paragraph paragraph = new Paragraph
            {
                Alignment = (GostAlignment)paraModel.Alignment,
                Style = (ParagraphStyle)paraModel.Style,
                FirstLineIndent = paraModel.FirstLineIndent ?? GetDefaultFirstLineIndent((ParagraphStyle)paraModel.Style),
                LineSpacing = paraModel.LineSpacing ?? new Paragraph().LineSpacing,
                PageBreakBefore = paraModel.PageBreakBefore
            };

            foreach (RunModel runModel in paraModel.Runs)
            {
                paragraph.Runs.Add(new TextRun
                {
                    Text = runModel.Text,
                    IsBold = runModel.IsBold,
                    IsItalic = runModel.IsItalic,
                    FontSize = runModel.FontSize > 0 ? runModel.FontSize : 14.0,
                    Color = runModel.Color ?? 0xFF000000
                });
            }

            if (!string.IsNullOrEmpty(paraModel.ImageFileName))
            {
                ZipArchiveEntry? imageEntry = archive.GetEntry(paraModel.ImageFileName);

                if (imageEntry != null)
                {
                    using Stream imageStream = imageEntry.Open();
                    using MemoryStream memoryStream = new MemoryStream();

                    await imageStream.CopyToAsync(memoryStream);

                    paragraph.ImageData = memoryStream.ToArray();
                    paragraph.ImageWidth = paraModel.ImageWidth;
                    paragraph.ImageHeight = paraModel.ImageHeight;

                    Debug.WriteLine($"[ARCHIVE] Параграф #{paragraphIndex}: Загружено изображение '{paraModel.ImageFileName}', размер: {paragraph.ImageData.Length} байт, {paragraph.ImageWidth}x{paragraph.ImageHeight}");
                }
                else
                {
                    Debug.WriteLine($"[ARCHIVE] ОШИБКА: Изображение не найдено в архиве: {paraModel.ImageFileName}");
                }
            }
            else
            {
                string text = paragraph.GetPlainText();
                Debug.WriteLine($"[ARCHIVE] Параграф #{paragraphIndex}: Текст '{text}' (runs: {paragraph.Runs.Count})");
            }

            document.Paragraphs.Add(paragraph);
            paragraphIndex++;
        }

        Debug.WriteLine($"[ARCHIVE] Модули: TitlePage={document.Modules.HasTitlePage}, TOC={document.Modules.HasTableOfContents}, Bibliography={document.Modules.HasBibliography}, Appendix={document.Modules.HasAppendix}");
        Debug.WriteLine($"[ARCHIVE] ✅ Загружен документ: {document.Paragraphs.Count} параграфов");

        return document;
    }

    public async Task SaveAsync(GostDocument document, string filePath)
    {
        await using FileStream fileStream = File.Create(filePath);
        await SaveAsync(document, fileStream);
    }

    public async Task SaveAsync(GostDocument document, Stream stream)
    {
        using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        document.ModifiedAt = DateTime.UtcNow;

        DocModel docModel = new DocModel
        {
            FormatVersion = CurrentFormatVersion,
            TitlePage = document.TitlePage,
            CodeListings = document.CodeListings,
            Images = document.Images,
            BibliographySources = document.BibliographySources,
            Counters = document.Counters,
            CreatedAt = document.CreatedAt,
            ModifiedAt = document.ModifiedAt,
            PageWidth = document.PageWidth,
            PageHeight = document.PageHeight,
            MarginLeft = document.MarginLeft,
            MarginRight = document.MarginRight,
            MarginTop = document.MarginTop,
            MarginBottom = document.MarginBottom,
            Modules = document.Modules,
            Paragraphs = new List<ParaModel>()
        };

        int imageCounter = 0;

        foreach (Paragraph paragraph in document.Paragraphs)
        {
            ParaModel paraModel = new ParaModel
            {
                Alignment = (int)paragraph.Alignment,
                Style = (int)paragraph.Style,
                FirstLineIndent = paragraph.FirstLineIndent,
                LineSpacing = paragraph.LineSpacing,
                PageBreakBefore = paragraph.PageBreakBefore,
                Runs = new List<RunModel>()
            };

            foreach (TextRun run in paragraph.Runs)
            {
                paraModel.Runs.Add(new RunModel
                {
                    Text = run.Text,
                    IsBold = run.IsBold,
                    IsItalic = run.IsItalic,
                    FontSize = run.FontSize,
                    Color = run.Color
                });
            }

            if (paragraph.ImageData != null && paragraph.ImageData.Length > 0)
            {
                string imageFileName = $"media/img_{imageCounter}.dat";

                ZipArchiveEntry imageEntry = archive.CreateEntry(imageFileName);

                using (Stream imageStream = imageEntry.Open())
                {
                    await imageStream.WriteAsync(paragraph.ImageData, 0, paragraph.ImageData.Length);
                }

                paraModel.ImageFileName = imageFileName;
                paraModel.ImageWidth = paragraph.ImageWidth;
                paraModel.ImageHeight = paragraph.ImageHeight;

                Debug.WriteLine($"[ARCHIVE] Сохранено изображение #{imageCounter}: {imageFileName}, {paragraph.ImageWidth}x{paragraph.ImageHeight}");

                imageCounter++;
            }

            docModel.Paragraphs.Add(paraModel);
        }

        string jsonText = JsonConvert.SerializeObject(docModel, Formatting.Indented);

        ZipArchiveEntry jsonEntry = archive.CreateEntry("document.json");

        using (Stream jsonStream = jsonEntry.Open())
        using (StreamWriter writer = new StreamWriter(jsonStream, Encoding.UTF8))
        {
            await writer.WriteAsync(jsonText);
        }

        Debug.WriteLine($"[ARCHIVE] ✅ Сохранён документ: {document.Paragraphs.Count} параграфов, {imageCounter} изображений");
        Debug.WriteLine($"[ARCHIVE] Модули: TitlePage={document.Modules.HasTitlePage}, TOC={document.Modules.HasTableOfContents}");
    }

    private static double GetDefaultFirstLineIndent(ParagraphStyle style)
    {
        return style is ParagraphStyle.Heading1 or ParagraphStyle.Heading2 or ParagraphStyle.Heading3 or ParagraphStyle.Code
            ? 0
            : new Paragraph().FirstLineIndent;
    }

    private static DocModel MigrateToCurrentVersion(DocModel docModel)
    {
        // FormatVersion == 0 means that the field was absent in a legacy
        // document. Versions 0 and 1 currently share the same DTO shape, so
        // no migration is required. Future migrations should be chained here
        // before the DTO is mapped to the public GostDocument model.
        return docModel;
    }
}

// ===== DTO МОДЕЛИ ДЛЯ JSON СЕРИАЛИЗАЦИИ =====

internal class DocModel
{
    public int FormatVersion { get; set; }
    public TitlePageInfo? TitlePage { get; set; }
    public List<CodeListing>? CodeListings { get; set; }
    public List<ImageAttachment>? Images { get; set; }
    public List<BibliographySource>? BibliographySources { get; set; }
    public DocumentModules? Modules { get; set; }
    public DocumentCounters? Counters { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public double PageWidth { get; set; }
    public double PageHeight { get; set; }
    public double MarginLeft { get; set; }
    public double MarginRight { get; set; }
    public double MarginTop { get; set; }
    public double MarginBottom { get; set; }
    public List<ParaModel> Paragraphs { get; set; } = new List<ParaModel>();
}

internal class ParaModel
{
    public int Alignment { get; set; }
    public int Style { get; set; }
    public double? FirstLineIndent { get; set; }
    public double? LineSpacing { get; set; }
    public bool PageBreakBefore { get; set; }
    public List<RunModel> Runs { get; set; } = new List<RunModel>();
    public string? ImageFileName { get; set; }
    public double ImageWidth { get; set; }
    public double ImageHeight { get; set; }
}

internal class RunModel
{
    public string Text { get; set; } = string.Empty;
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public double FontSize { get; set; }
    public uint? Color { get; set; }
}
