using System.Runtime.InteropServices;
using System.IO.Compression;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.Serialization;

public class DocumentPersistenceSnapshotTests
{
    [Fact]
    public async Task Snapshot_DeeplyIsolatesEveryPersistedMutableCategory()
    {
        DateTime originalModifiedAt = new(
            2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        DateTime snapshotModifiedAt = new(
            2026, 8, 2, 11, 0, 0, DateTimeKind.Utc);
        GostDocument live = CreateCompleteDocument(originalModifiedAt);
        DocumentPersistenceSnapshot snapshot =
            DocumentPersistenceSnapshot.Capture(
                live,
                revision: 17,
                snapshotModifiedAt);
        ArchiveService archive = new();

        byte[] beforeMutation = await SerializeAsync(archive, snapshot);

        MutateEveryPersistedMutableField(live);
        Assert.True(MemoryMarshal.TryGetArray(
            live.Images[0].Data,
            out ArraySegment<byte> liveImageBytes));
        liveImageBytes.Array![liveImageBytes.Offset] ^= 0xFF;
        live.Paragraphs.Add(new Paragraph());
        live.CodeListings.Add(new CodeListing());
        live.BibliographySources.Clear();

        byte[] afterMutation = await SerializeAsync(archive, snapshot);
        GostDocument restored = await LoadAsync(archive, afterMutation);

        Assert.Equal(17, snapshot.Revision);
        Assert.Equal(snapshotModifiedAt, snapshot.ModifiedAt);
        AssertPackageContentsEqual(beforeMutation, afterMutation);
        Assert.Equal("Snapshot title", restored.TitlePage.WorkTitle);
        Assert.Equal("Snapshot text", restored.Paragraphs[0].Runs[0].Text);
        Assert.True(restored.Paragraphs[0].Runs[0].IsBold);
        Assert.Equal(GostAlignment.Justify, restored.Paragraphs[0].Alignment);
        Assert.Equal(320, restored.Paragraphs[1].ImageWidth);
        Assert.Equal("original code", restored.CodeListings[0].Content);
        Assert.Equal("original source", restored.BibliographySources[0].Description);
        Assert.Equal(3, restored.Modules.TOCMaxLevel);
        Assert.Equal(12, restored.Counters.PagesCount);
        Assert.Equal(snapshotModifiedAt, restored.ModifiedAt);
        Assert.Equal(800, restored.PageWidth);
        Assert.Equal(120, restored.MarginLeft);
        Assert.Equal(new byte[] { 10, 20, 30 }, restored.Images[0].Data.ToArray());
        Assert.Equal(2, restored.Paragraphs.Count);
        Assert.Single(restored.CodeListings);
        Assert.Single(restored.BibliographySources);
    }

    [Fact]
    public async Task SaveDocument_DoesNotMutateLiveSaveMetadata()
    {
        DateTime modifiedAt = new(
            2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        GostDocument live = CreateCompleteDocument(modifiedAt);
        live.Counters.ImagesCount = 42;
        ArchiveService archive = new();
        await using MemoryStream stream = new();

        await archive.SaveAsync(live, stream);

        GostDocument restored = await LoadAsync(archive, stream.ToArray());
        Assert.Equal("Snapshot title", restored.TitlePage.WorkTitle);
        Assert.Equal("Snapshot text", restored.Paragraphs[0].Runs[0].Text);
        Assert.Equal(1, restored.Counters.ImagesCount);
        Assert.Equal(modifiedAt, live.ModifiedAt);
        Assert.Equal(42, live.Counters.ImagesCount);
    }

    private static GostDocument CreateCompleteDocument(DateTime modifiedAt)
    {
        Guid imageId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        GostDocument document = new()
        {
            TitlePage = new TitlePageInfo
            {
                University = "University",
                Department = "Department",
                Discipline = "Discipline",
                WorkType = "Report",
                WorkTitle = "Snapshot title",
                StudentName = "Student",
                GroupNumber = "Group",
                TeacherName = "Teacher",
                City = "City",
                Year = 2026
            },
            Modules = new DocumentModules
            {
                HasTitlePage = true,
                HasTableOfContents = true,
                HasBibliography = true,
                HasAppendix = true,
                ContentStartPage = 4,
                AutoGenerateTOC = true,
                TOCMaxLevel = 3
            },
            Counters = new DocumentCounters
            {
                ImagesCount = 1,
                TablesCount = 2,
                SourcesCount = 3,
                PagesCount = 12,
                ApplicationsCount = 4
            },
            CreatedAt = new DateTime(
                2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            ModifiedAt = modifiedAt,
            PageWidth = 800,
            PageHeight = 1100,
            MarginLeft = 120,
            MarginRight = 60,
            MarginTop = 70,
            MarginBottom = 80
        };
        document.Paragraphs.Add(new Paragraph
        {
            Runs =
            [
                new TextRun("Snapshot text", isBold: true)
                {
                    IsItalic = true,
                    Color = 0xFF123456,
                    FontSize = 16
                }
            ],
            Alignment = GostAlignment.Justify,
            FirstLineIndent = 42,
            LineSpacing = 1.25,
            Style = ParagraphStyle.Heading2,
            PageBreakBefore = true
        });
        document.Paragraphs.Add(new Paragraph
        {
            ImageId = imageId,
            ImageWidth = 320,
            ImageHeight = 180,
            Runs = [new TextRun("caption")]
        });
        document.MutableImages.Add(new ImageAttachment(
            imageId,
            "image.bin",
            "application/octet-stream",
            new byte[] { 10, 20, 30 },
            "legacy caption",
            7));
        document.CodeListings.Add(new CodeListing
        {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            FileName = "Program.cs",
            RelativePath = "src/Program.cs",
            Language = "csharp",
            Content = "original code",
            IsSelected = true,
            Order = 2,
            ListingNumber = 3
        });
        document.BibliographySources.Add(new BibliographySource
        {
            Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Description = "original source",
            IsSelected = true,
            Order = 5
        });
        return document;
    }

    private static void MutateEveryPersistedMutableField(GostDocument live)
    {
        TitlePageInfo title = live.TitlePage;
        title.University = "changed university";
        title.Department = "changed department";
        title.Discipline = "changed discipline";
        title.WorkType = "changed work type";
        title.WorkTitle = "changed title";
        title.StudentName = "changed student";
        title.GroupNumber = "changed group";
        title.TeacherName = "changed teacher";
        title.City = "changed city";
        title.Year++;

        Paragraph textParagraph = live.Paragraphs[0];
        TextRun run = textParagraph.Runs[0];
        run.Text = "changed text";
        run.IsBold = false;
        run.IsItalic = false;
        run.Color = 0;
        run.FontSize = 99;
        textParagraph.FirstLineIndent = 1;
        textParagraph.LineSpacing = 3;
        textParagraph.Alignment = GostAlignment.Right;
        textParagraph.Style = ParagraphStyle.Normal;
        textParagraph.PageBreakBefore = false;
        textParagraph.Runs.Add(new TextRun("later run"));

        Paragraph imageParagraph = live.Paragraphs[1];
        imageParagraph.ImageId = Guid.NewGuid();
        imageParagraph.ImageWidth = 999;
        imageParagraph.ImageHeight = 888;

        CodeListing listing = live.CodeListings[0];
        listing.Id = Guid.NewGuid();
        listing.FileName = "changed.cs";
        listing.RelativePath = "changed/path.cs";
        listing.Language = "text";
        listing.Content = "changed code";
        listing.IsSelected = false;
        listing.Order = 99;
        listing.ListingNumber = 98;

        BibliographySource source = live.BibliographySources[0];
        source.Id = Guid.NewGuid();
        source.Description = "changed source";
        source.IsSelected = false;
        source.Order = 99;

        DocumentModules modules = live.Modules;
        modules.HasTitlePage = false;
        modules.HasTableOfContents = false;
        modules.HasBibliography = false;
        modules.HasAppendix = false;
        modules.ContentStartPage = 99;
        modules.AutoGenerateTOC = false;
        modules.TOCMaxLevel = 9;

        DocumentCounters counters = live.Counters;
        counters.ImagesCount = 99;
        counters.TablesCount = 99;
        counters.SourcesCount = 99;
        counters.PagesCount = 999;
        counters.ApplicationsCount = 99;

        live.CreatedAt = live.CreatedAt.AddYears(1);
        live.ModifiedAt = live.ModifiedAt.AddYears(1);
        live.PageWidth = 999;
        live.PageHeight = 999;
        live.MarginLeft = 1;
        live.MarginRight = 2;
        live.MarginTop = 3;
        live.MarginBottom = 4;
    }

    private static async Task<byte[]> SerializeAsync(
        ArchiveService archive,
        DocumentPersistenceSnapshot snapshot)
    {
        await using MemoryStream stream = new();
        await archive.SaveAsync(snapshot, stream);
        return stream.ToArray();
    }

    private static async Task<GostDocument> LoadAsync(
        ArchiveService archive,
        byte[] package)
    {
        await using MemoryStream stream = new(package);
        return await archive.LoadAsync(stream);
    }

    private static void AssertPackageContentsEqual(
        byte[] expectedPackage,
        byte[] actualPackage)
    {
        Dictionary<string, byte[]> expected = ReadEntries(expectedPackage);
        Dictionary<string, byte[]> actual = ReadEntries(actualPackage);
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (string path in expected.Keys)
        {
            Assert.Equal(expected[path], actual[path]);
        }
    }

    private static Dictionary<string, byte[]> ReadEntries(byte[] package)
    {
        using MemoryStream stream = new(package);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using Stream entryStream = entry.Open();
                using MemoryStream copy = new();
                entryStream.CopyTo(copy);
                return copy.ToArray();
            });
    }
}
