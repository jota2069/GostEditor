using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;
using GostDocument = GostEditor.Core.Models.GostDocument;

namespace GostEditor.Core.Serialization;

public class DocModel
{
    public double PageWidth { get; set; }
    public double PageHeight { get; set; }
    public double MarginLeft { get; set; }
    public double MarginRight { get; set; }
    public double MarginTop { get; set; }
    public double MarginBottom { get; set; }
    public List<ParaModel> Paragraphs { get; set; } = [];
}

public class ParaModel
{
    public GostAlignment Alignment { get; set; }
    public ParagraphStyle Style { get; set; }
    public bool PageBreakBefore { get; set; }
    public List<RunModel> Runs { get; set; } = [];
    public string? ImageFileName { get; set; }
    public double ImageWidth { get; set; }
    public double ImageHeight { get; set; }
}

public class RunModel
{
    public string Text { get; set; } = "";
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public double FontSize { get; set; }
}

public static class GostArchiveManager
{
    public static async Task SaveAsync(GostDocument document, Stream outputZipStream)
    {
        using ZipArchive archive = new ZipArchive(outputZipStream, ZipArchiveMode.Create, true);

        DocModel docModel = new DocModel
        {
            PageWidth = document.PageWidth,
            PageHeight = document.PageHeight,
            MarginLeft = document.MarginLeft,
            MarginRight = document.MarginRight,
            MarginTop = document.MarginTop,
            MarginBottom = document.MarginBottom
        };

        int imageCounter = 0;

        foreach (Paragraph p in document.Paragraphs)
        {
            ParaModel pModel = new ParaModel
            {
                Alignment = p.Alignment,
                Style = p.Style,
                PageBreakBefore = p.PageBreakBefore,
                ImageWidth = p.ImageWidth,
                ImageHeight = p.ImageHeight
            };

            if (p.ImageData != null)
            {
                string imgName = $"media/img_{imageCounter}.dat";
                pModel.ImageFileName = imgName;
                imageCounter++;

                ZipArchiveEntry imgEntry = archive.CreateEntry(imgName, CompressionLevel.Fastest);
                await using Stream imgStream = imgEntry.Open();
                await imgStream.WriteAsync(p.ImageData);
            }

            foreach (TextRun r in p.Runs)
            {
                pModel.Runs.Add(new RunModel
                {
                    Text = r.Text,
                    IsBold = r.IsBold,
                    IsItalic = r.IsItalic,
                    FontSize = r.FontSize
                });
            }
            docModel.Paragraphs.Add(pModel);
        }

        ZipArchiveEntry jsonEntry = archive.CreateEntry("document.json", CompressionLevel.Optimal);
        await using Stream jsonStream = jsonEntry.Open();
        await JsonSerializer.SerializeAsync(jsonStream, docModel, new JsonSerializerOptions { WriteIndented = true });
    }

    public static async Task<GostDocument> LoadAsync(Stream inputZipStream)
    {
        using ZipArchive archive = new ZipArchive(inputZipStream, ZipArchiveMode.Read, true);

        ZipArchiveEntry? jsonEntry = archive.GetEntry("document.json");
        if (jsonEntry == null) throw new Exception("Файл document.json не найден. Это не формат .gost!");

        await using Stream jsonStream = jsonEntry.Open();
        DocModel? docModel = await JsonSerializer.DeserializeAsync<DocModel>(jsonStream);
        if (docModel == null) throw new Exception("Ошибка чтения структуры документа");

        GostDocument doc = new GostDocument
        {
            PageWidth = docModel.PageWidth,
            PageHeight = docModel.PageHeight,
            MarginLeft = docModel.MarginLeft,
            MarginRight = docModel.MarginRight,
            MarginTop = docModel.MarginTop,
            MarginBottom = docModel.MarginBottom
        };

        foreach (ParaModel pModel in docModel.Paragraphs)
        {
            Paragraph p = new Paragraph
            {
                Alignment = pModel.Alignment,
                Style = pModel.Style,
                PageBreakBefore = pModel.PageBreakBefore,
                ImageWidth = pModel.ImageWidth,
                ImageHeight = pModel.ImageHeight
            };

            if (!string.IsNullOrEmpty(pModel.ImageFileName))
            {
                ZipArchiveEntry? imgEntry = archive.GetEntry(pModel.ImageFileName);
                if (imgEntry != null)
                {
                    await using Stream imgStream = imgEntry.Open();
                    using MemoryStream ms = new MemoryStream();
                    await imgStream.CopyToAsync(ms);
                    p.ImageData = ms.ToArray();
                }
            }

            foreach (RunModel rModel in pModel.Runs)
            {
                p.Runs.Add(new TextRun(rModel.Text)
                {
                    IsBold = rModel.IsBold,
                    IsItalic = rModel.IsItalic,
                    FontSize = rModel.FontSize
                });
            }
            doc.Paragraphs.Add(p);
        }

        if (doc.Paragraphs.Count == 0) doc.Paragraphs.Add(new Paragraph());

        return doc;
    }
}
