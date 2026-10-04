using System.IO.Compression;
using System.Text;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.Serialization.Format;
using GostEditor.Core.Serialization.Migrations;
using GostEditor.UI.Views;

namespace GostEditor.Tests.Serialization;

public sealed class GostFormatMigrationContractTests
{
    [Fact]
    public async Task ExplicitVersionZero_IsDistinguishedFromMissingVersion()
    {
        ArchiveService archiveService = new();
        await using MemoryStream source = new(
            await CreateArchiveBytesAsync(
                """{"FormatVersion":0,"Paragraphs":[]}"""));

        GostArchiveLoadResult result =
            await archiveService.LoadWithDiagnosticsAsync(source);

        Assert.Equal(0, result.SourceVersion);
        Assert.Contains(
            result.Diagnostics,
            item => item.Code == "FORMAT_V0_DECLARED");
        Assert.DoesNotContain(
            result.Diagnostics,
            item => item.Code == "FORMAT_V0_ASSUMED");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"1\"")]
    [InlineData("null")]
    [InlineData("2147483648")]
    public async Task InvalidVersionRepresentations_AreRejected(
        string version)
    {
        ArchiveService archiveService = new();
        await using MemoryStream source = new(
            await CreateArchiveBytesAsync(
                $"{{\"FormatVersion\":{version},\"Paragraphs\":[]}}"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            archiveService.LoadWithDiagnosticsAsync(source));
    }

    [Fact]
    public async Task Reader_AcceptsDocumentedCommentsAndCaseInsensitiveDtoFields()
    {
        ArchiveService archiveService = new();
        await using MemoryStream source = new(
            await CreateArchiveBytesAsync(
                """
                {
                  /* compatible legacy producer */
                  "paragraphs": [{"runs": [{"text": "legacy"}]}]
                }
                """));

        GostArchiveLoadResult result =
            await archiveService.LoadWithDiagnosticsAsync(source);

        Assert.Equal(
            "legacy",
            Assert.Single(result.Document.Paragraphs).GetPlainText());
    }

    [Fact]
    public async Task Reader_RejectsDocumentedUnsupportedTrailingComma()
    {
        ArchiveService archiveService = new();
        await using MemoryStream source = new(
            await CreateArchiveBytesAsync(
                """{"FormatVersion":2,"Images":[],"Paragraphs":[],}"""));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            archiveService.LoadWithDiagnosticsAsync(source));
    }

    [Fact]
    public async Task DiagnosticsCollection_IsImmutableForCaller()
    {
        ArchiveService archiveService = new();
        await using MemoryStream source = new(
            await CreateArchiveBytesAsync("""{"Paragraphs":[]}"""));
        GostArchiveLoadResult result =
            await archiveService.LoadWithDiagnosticsAsync(source);
        IList<GostArchiveDiagnostic> diagnostics =
            Assert.IsAssignableFrom<IList<GostArchiveDiagnostic>>(
                result.Diagnostics);

        Assert.Throws<NotSupportedException>(() => diagnostics.Add(new(
            GostArchiveDiagnosticSeverity.Warning,
            "TEST",
            "mutation")));
    }

    [Fact]
    public async Task LegacyInterfaceFallback_ReportsUnknownSourceVersion()
    {
        IArchiveService archiveService = new LegacyOnlyArchiveService();
        await using MemoryStream source = new();

        GostArchiveLoadResult result =
            await archiveService.LoadWithDiagnosticsAsync(source);

        Assert.Null(result.SourceVersion);
        Assert.Null(result.WasMigrated);
        Assert.Equal(GostArchiveFormat.Current, result.CurrentVersion);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(0, "{\"Paragraphs\":[{\"Runs\":[{\"Text\":\"v0\"}]}]}", "v0")]
    [InlineData(1, "{\"FormatVersion\":1,\"Paragraphs\":[{\"Runs\":[{\"Text\":\"v1\"}]}]}", "v1")]
    [InlineData(2, "{\"FormatVersion\":2,\"Images\":[],\"Paragraphs\":[{\"Runs\":[{\"Text\":\"v2\"}]}]}", "v2")]
    public async Task SupportedVersion_LoadSaveReload_ProducesCurrentFormat(
        int sourceVersion,
        string manifest,
        string expectedText)
    {
        ArchiveService archiveService = new();
        await using MemoryStream source = new(
            await CreateArchiveBytesAsync(manifest));

        GostArchiveLoadResult loaded =
            await archiveService.LoadWithDiagnosticsAsync(source);
        await using MemoryStream saved = new();
        await archiveService.SaveAsync(loaded.Document, saved);
        saved.Position = 0;
        GostArchiveLoadResult reloaded =
            await archiveService.LoadWithDiagnosticsAsync(saved);

        Assert.Equal(sourceVersion, loaded.SourceVersion);
        Assert.Equal(sourceVersion != 2, loaded.WasMigrated);
        Assert.Equal(2, reloaded.SourceVersion);
        Assert.False(reloaded.WasMigrated);
        Assert.Equal(
            expectedText,
            Assert.Single(reloaded.Document.Paragraphs).GetPlainText());
    }

    [Fact]
    public async Task OpenWorkflow_ReturnsMigrationDiagnosticsToUiCaller()
    {
        byte[] package = await CreateArchiveBytesAsync(
            """{"Paragraphs":[]}""");
        bool published = false;

        IReadOnlyList<GostArchiveDiagnostic> diagnostics =
            await MainWindow.LoadDocumentForOpenAsync(
                new ArchiveService(),
                () => Task.FromResult<Stream>(new MemoryStream(package)),
                ioCoordinator: null,
                _ => published = true);

        Assert.True(published);
        Assert.Contains(
            diagnostics,
            item => item.Code == "FORMAT_V0_ASSUMED");
        Assert.Contains(
            diagnostics,
            item => item.Code == "FORMAT_V1_MIGRATED");
    }

    [Fact]
    public async Task OpenWorkflow_CancellationDoesNotPublishPartialLoad()
    {
        byte[] package = await CreateArchiveBytesAsync(
            """{"Paragraphs":[]}""");
        bool published = false;
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MainWindow.LoadDocumentForOpenAsync(
                new ArchiveService(),
                () => Task.FromResult<Stream>(new MemoryStream(package)),
                ioCoordinator: null,
                _ => published = true,
                cancellation.Token));

        Assert.False(published);
    }

    [Fact]
    public void Registry_RejectsMigrationThatSkipsAFormatVersion()
    {
        InvalidOperationException exception = Assert.Throws<
            InvalidOperationException>(() => new GostMigrationRegistry(
            [
                new TestMigration(0, 2, updateWorkingVersion: true)
            ]));

        Assert.Contains("следующую версию", exception.Message);
    }

    [Fact]
    public async Task Registry_MissingIntermediateMigrationFailsExplicitly()
    {
        using MemoryStream package = new();
        using (new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
        }
        package.Position = 0;
        using ZipArchive archive = new(package, ZipArchiveMode.Read);
        GostMigrationContext context = new(
            GostFormatVersions.LegacyWithoutVersion,
            new GostDocumentV1Dto(),
            new GostArchiveEntryIndex(archive));
        GostMigrationRegistry registry = new(
        [
            new TestMigration(1, 2, updateWorkingVersion: true)
        ]);

        InvalidOperationException exception = await Assert.ThrowsAsync<
            InvalidOperationException>(() => registry.MigrateToAsync(
            context,
            GostFormatVersions.Current));

        Assert.Contains("Не зарегистрирована миграция", exception.Message);
    }

    [Fact]
    public async Task Registry_MigrationMustAdvanceContextToDeclaredVersion()
    {
        using MemoryStream package = new();
        using (new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
        }
        package.Position = 0;
        using ZipArchive archive = new(package, ZipArchiveMode.Read);
        GostMigrationContext context = new(
            GostFormatVersions.LegacyWithoutVersion,
            new GostDocumentV1Dto(),
            new GostArchiveEntryIndex(archive));
        GostMigrationRegistry registry = new(
        [
            new TestMigration(0, 1, updateWorkingVersion: false)
        ]);

        InvalidOperationException exception = await Assert.ThrowsAsync<
            InvalidOperationException>(() => registry.MigrateToAsync(
            context,
            GostFormatVersions.Legacy));

        Assert.Contains("не обновила рабочую версию", exception.Message);
    }

    private static async Task<byte[]> CreateArchiveBytesAsync(string manifest)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("document.json");
            await using Stream target = entry.Open();
            await target.WriteAsync(Encoding.UTF8.GetBytes(manifest));
        }

        return stream.ToArray();
    }

    private sealed class TestMigration : IGostFormatMigration
    {
        private readonly bool _updateWorkingVersion;

        internal TestMigration(
            int fromVersion,
            int toVersion,
            bool updateWorkingVersion)
        {
            FromVersion = fromVersion;
            ToVersion = toVersion;
            _updateWorkingVersion = updateWorkingVersion;
        }

        public int FromVersion { get; }

        public int ToVersion { get; }

        public Task ApplyAsync(
            GostMigrationContext context,
            CancellationToken cancellationToken = default)
        {
            if (_updateWorkingVersion)
            {
                context.WorkingVersion = ToVersion;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class LegacyOnlyArchiveService : IArchiveService
    {
        public GostDocument CreateNew() => new();

        public Task<GostDocument> LoadAsync(string filePath) =>
            Task.FromResult(new GostDocument());

        public Task<GostDocument> LoadAsync(Stream stream) =>
            Task.FromResult(new GostDocument());

        public Task SaveAsync(
            GostDocument document,
            string filePath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveAsync(
            DocumentPersistenceSnapshot snapshot,
            string filePath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveAsync(
            GostDocument document,
            Stream stream,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveAsync(
            DocumentPersistenceSnapshot snapshot,
            Stream stream,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
