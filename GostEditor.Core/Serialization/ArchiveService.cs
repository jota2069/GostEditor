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

        Debug.WriteLine($"[ARCHIVE] Десериализовано параграфов: {docModel.Paragraphs.Count}");

        GostDocument document = new GostDocument
        {
            PageWidth = docModel.PageWidth,
            PageHeight = docModel.PageHeight,
            MarginLeft = docModel.MarginLeft,
            MarginRight = docModel.MarginRight,
            MarginTop = docModel.MarginTop,
            MarginBottom = docModel.MarginBottom
        };

        int paragraphIndex = 0;

        foreach (ParaModel paraModel in docModel.Paragraphs)
        {
            Paragraph paragraph = new Paragraph
            {
                Alignment = (GostAlignment)paraModel.Alignment,
                Style = (ParagraphStyle)paraModel.Style,
                PageBreakBefore = paraModel.PageBreakBefore
            };

            foreach (RunModel runModel in paraModel.Runs)
            {
                paragraph.Runs.Add(new TextRun
                {
                    Text = runModel.Text,
                    IsBold = runModel.IsBold,
                    IsItalic = runModel.IsItalic,
                    FontSize = runModel.FontSize
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

        // КРИТИЧНО: Загружаем настройки модулей
        document.Modules = docModel.Modules ?? new DocumentModules();

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

        DocModel docModel = new DocModel
        {
            PageWidth = document.PageWidth,
            PageHeight = document.PageHeight,
            MarginLeft = document.MarginLeft,
            MarginRight = document.MarginRight,
            MarginTop = document.MarginTop,
            MarginBottom = document.MarginBottom,
            Modules = document.Modules,  // КРИТИЧНО: Сохраняем настройки модулей
            Paragraphs = new List<ParaModel>()
        };

        int imageCounter = 0;

        foreach (Paragraph paragraph in document.Paragraphs)
        {
            ParaModel paraModel = new ParaModel
            {
                Alignment = (int)paragraph.Alignment,
                Style = (int)paragraph.Style,
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
                    FontSize = run.FontSize
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
}

// ===== DTO МОДЕЛИ ДЛЯ JSON СЕРИАЛИЗАЦИИ =====

internal class DocModel
{
    public double PageWidth { get; set; }
    public double PageHeight { get; set; }
    public double MarginLeft { get; set; }
    public double MarginRight { get; set; }
    public double MarginTop { get; set; }
    public double MarginBottom { get; set; }
    public List<ParaModel> Paragraphs { get; set; } = new List<ParaModel>();
    public DocumentModules? Modules { get; set; }  // ← ДОБАВЛЕНО
}

internal class ParaModel
{
    public int Alignment { get; set; }
    public int Style { get; set; }
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
}
