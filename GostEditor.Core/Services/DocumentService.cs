using System.IO.Compression;
using System.Text.Json;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;

namespace GostEditor.Core.Services;

public class DocumentService : IDocumentService
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string ManifestEntry = "manifest.json";
    private const string ImagesFolder = "images/";

    public GostDocument CreateNew()
    {
        return new GostDocument();
    }

    public async Task<GostDocument> LoadAsync(string filePath)
    {
        using ZipArchive zip = ZipFile.OpenRead(filePath);

        ZipArchiveEntry manifestEntry = zip.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException("Файл повреждён: manifest.json не найден.");

        GostDocument document;

        using (Stream stream = manifestEntry.Open())
        {
            document = await JsonSerializer.DeserializeAsync<GostDocument>(stream, JsonOptions)
                ?? throw new InvalidDataException("Не удалось десериализовать документ.");
        }

        foreach (ImageAttachment image in document.Images)
        {
            ZipArchiveEntry? imageEntry = zip.GetEntry($"{ImagesFolder}{image.Id}");

            if (imageEntry is null)
            {
                continue;
            }

            using Stream imageStream = imageEntry.Open();
            using MemoryStream memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream);
            image.Data = memoryStream.ToArray();
        }

        return document;
    }

    public async Task SaveAsync(GostDocument document, string filePath)
    {
        document.ModifiedAt = DateTime.UtcNow;

        List<ImageAttachment> imagesMetadata = document.Images
            .Select(img => new ImageAttachment
            {
                Id = img.Id,
                FileName = img.FileName,
                Caption = img.Caption,
                Data = []
            })
            .ToList();

        GostDocument documentForSerialization = new GostDocument
        {
            TitlePage = document.TitlePage,
            Sections = document.Sections,
            CodeListings = document.CodeListings,
            Images = imagesMetadata,
            CreatedAt = document.CreatedAt,
            ModifiedAt = document.ModifiedAt
        };

        using ZipArchive zip = ZipFile.Open(filePath, ZipArchiveMode.Create);

        ZipArchiveEntry manifestZipEntry =
            zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);

        using (Stream manifestStream = manifestZipEntry.Open())
        {
            await JsonSerializer.SerializeAsync(
                manifestStream, documentForSerialization, JsonOptions);
        }

        foreach (ImageAttachment image in document.Images.Where(img => img.Data.Length > 0))
        {
            ZipArchiveEntry imageZipEntry = zip.CreateEntry(
                $"{ImagesFolder}{image.Id}",
                CompressionLevel.Fastest);

            using Stream imageStream = imageZipEntry.Open();
            await imageStream.WriteAsync(image.Data);
        }
    }
}