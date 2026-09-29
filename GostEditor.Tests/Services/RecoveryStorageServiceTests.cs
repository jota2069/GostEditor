using System.Text.Json;
using GostEditor.Core.IO;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Tests.Infrastructure;
using GostEditor.UI.Services;

namespace GostEditor.Tests.Services;

public sealed class RecoveryStorageServiceTests : IDisposable
{
    private static readonly DateTimeOffset SavedAtUtc = new(
        2026,
        9,
        28,
        12,
        0,
        0,
        TimeSpan.Zero);

    private readonly TestTemporaryDirectory _temporaryDirectory = new();
    private readonly RecoveryStorageService _service;

    public RecoveryStorageServiceTests()
    {
        _service = new RecoveryStorageService(
            new ArchiveService(),
            _temporaryDirectory.DirectoryPath,
            new ManualUtcTimeProvider(SavedAtUtc));
    }

    [Fact]
    public async Task SaveAsync_CreatesRecoveryPackageAndMetadata()
    {
        GostDocument document = CreateDocument("Черновик");
        string originalPath = Path.Combine(
            _temporaryDirectory.DirectoryPath,
            "..",
            "original.gost");

        RecoveryMetadata metadata = await _service.SaveAsync(
            document,
            originalPath);

        Assert.True(File.Exists(_service.RecoveryFilePath));
        Assert.True(File.Exists(_service.MetadataFilePath));
        Assert.Equal(RecoveryMetadata.CurrentVersion, metadata.Version);
        Assert.NotEqual(Guid.Empty, metadata.SessionId);
        Assert.Equal(
            Path.GetFullPath(originalPath),
            metadata.OriginalFilePath);
        Assert.Equal(SavedAtUtc, metadata.SavedAtUtc);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripPreservesDocument()
    {
        GostDocument expected = CreateDocument("Текст восстановления");

        await _service.SaveAsync(expected, null);

        GostDocument actual = await _service.LoadDocumentAsync();

        Paragraph paragraph = Assert.Single(actual.Paragraphs);
        Assert.Equal("Текст восстановления", paragraph.GetPlainText());
        Assert.Equal(
            expected.TitlePage.WorkTitle,
            actual.TitlePage.WorkTitle);
        Assert.Equal(
            expected.Modules.ContentStartPage,
            actual.Modules.ContentStartPage);
    }

    [Fact]
    public async Task LoadMetadataAsync_ReturnsSavedMetadata()
    {
        string originalPath =
            _temporaryDirectory.GetPath("document.gost");

        RecoveryMetadata saved = await _service.SaveAsync(
            CreateDocument("Метаданные"),
            originalPath);

        RecoveryMetadata? loaded = await _service.LoadMetadataAsync();

        Assert.NotNull(loaded);
        Assert.Equal(saved.Version, loaded.Version);
        Assert.Equal(saved.SessionId, loaded.SessionId);
        Assert.Equal(saved.OriginalFilePath, loaded.OriginalFilePath);
        Assert.Equal(saved.SavedAtUtc, loaded.SavedAtUtc);
    }

    [Fact]
    public async Task SaveAsync_WithoutOriginalPath_WritesNullPath()
    {
        await _service.SaveAsync(
            CreateDocument("Новый документ"),
            null);

        RecoveryMetadata? metadata =
            await _service.LoadMetadataAsync();

        Assert.NotNull(metadata);
        Assert.Null(metadata.OriginalFilePath);
    }

    [Fact]
    public async Task SaveAsync_OverwritesPreviousRecovery()
    {
        await _service.SaveAsync(
            CreateDocument("Первая версия"),
            null);

        RecoveryMetadata firstMetadata =
            Assert.IsType<RecoveryMetadata>(
                await _service.LoadMetadataAsync());

        await _service.SaveAsync(
            CreateDocument("Вторая версия"),
            null);

        GostDocument loaded = await _service.LoadDocumentAsync();
        RecoveryMetadata secondMetadata =
            Assert.IsType<RecoveryMetadata>(
                await _service.LoadMetadataAsync());

        Assert.Equal(
            "Вторая версия",
            Assert.Single(loaded.Paragraphs).GetPlainText());

        Assert.NotEqual(
            firstMetadata.SessionId,
            secondMetadata.SessionId);
    }

    [Fact]
    public async Task SaveAsync_WhenPointerPublishFails_PreservesPreviousGeneration()
    {
        FailOnCallAtomicCommitter committer = new(failOnCall: 2);
        RecoveryStorageService service = CreateService(committer);

        await service.SaveAsync(CreateDocument("Первая версия"), null);
        string firstPackagePath = service.RecoveryFilePath;

        await Assert.ThrowsAsync<InjectedRecoveryIOException>(() =>
            service.SaveAsync(CreateDocument("Вторая версия"), null));

        GostDocument loaded = await service.LoadDocumentAsync();
        RecoveryMetadata metadata = Assert.IsType<RecoveryMetadata>(
            await service.LoadMetadataAsync());

        Assert.Equal(firstPackagePath, service.RecoveryFilePath);
        Assert.Equal(
            "Первая версия",
            Assert.Single(loaded.Paragraphs).GetPlainText());
        Assert.Equal(
            Path.GetFileName(Path.GetDirectoryName(firstPackagePath)),
            $"generation-{metadata.SessionId:N}");
        Assert.True(File.Exists(firstPackagePath));
        Assert.Equal(2, GetGenerationDirectories().Length);
    }

    [Theory]
    [InlineData(AtomicFileFailurePoint.CommitBeforeMutation, "Первая версия")]
    [InlineData(AtomicFileFailurePoint.Windows1176, "Первая версия")]
    [InlineData(AtomicFileFailurePoint.Windows1177, "Первая версия")]
    [InlineData(AtomicFileFailurePoint.FailAfterReplacement, "Вторая версия")]
    public async Task SaveAsync_WhenPointerCommitPartiallyFails_LeavesConsistentGeneration(
        AtomicFileFailurePoint failurePoint,
        string expectedText)
    {
        await _service.SaveAsync(CreateDocument("Первая версия"), null);
        RecoveryStorageService failingService = CreateService(
            new AtomicFileCommitter(
                new FaultInjectingAtomicFileSystem(failurePoint)));

        await Assert.ThrowsAnyAsync<IOException>(() =>
            failingService.SaveAsync(CreateDocument("Вторая версия"), null));

        GostDocument loaded = await _service.LoadDocumentAsync();
        RecoveryMetadata metadata = Assert.IsType<RecoveryMetadata>(
            await _service.LoadMetadataAsync());

        Assert.Equal(
            expectedText,
            Assert.Single(loaded.Paragraphs).GetPlainText());
        Assert.Equal(
            $"generation-{metadata.SessionId:N}",
            Path.GetFileName(Path.GetDirectoryName(_service.RecoveryFilePath)));
        Assert.True(File.Exists(_service.RecoveryFilePath));
        Assert.True(File.Exists(_service.MetadataFilePath));
    }

    [Fact]
    public async Task SaveAsync_WhenFirstPointerPublishFails_PreservesOrphanGeneration()
    {
        RecoveryStorageService service = CreateService(
            new FailOnCallAtomicCommitter(failOnCall: 1));

        await Assert.ThrowsAsync<InjectedRecoveryIOException>(() =>
            service.SaveAsync(CreateDocument("Осиротевшая версия"), null));

        Assert.True(service.HasRecovery);
        string generation = Assert.Single(GetGenerationDirectories());
        Assert.True(File.Exists(Path.Combine(generation, "autosave.gost")));
        Assert.True(File.Exists(Path.Combine(generation, "session.json")));
    }

    [Fact]
    public async Task SaveAsync_WhenCancelledBeforeGenerationPublish_PreservesCurrentGeneration()
    {
        await _service.SaveAsync(CreateDocument("Стабильная версия"), null);
        string currentPackagePath = _service.RecoveryFilePath;
        using CancellationTokenSource cancellation = new();
        RecoveryStorageService cancellingService = new(
            new CancellingArchiveService(cancellation),
            _temporaryDirectory.DirectoryPath,
            new ManualUtcTimeProvider(SavedAtUtc));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancellingService.SaveAsync(
                CreateDocument("Незавершённая версия"),
                null,
                cancellation.Token));

        GostDocument loaded = await _service.LoadDocumentAsync();
        Assert.Equal(currentPackagePath, _service.RecoveryFilePath);
        Assert.Equal(
            "Стабильная версия",
            Assert.Single(loaded.Paragraphs).GetPlainText());
        Assert.DoesNotContain(
            Directory.GetDirectories(_temporaryDirectory.DirectoryPath),
            path => Path.GetFileName(path).StartsWith(
                ".generation-",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveAsync_KeepsCurrentAndPreviousCommittedGenerations()
    {
        await _service.SaveAsync(CreateDocument("Первая"), null);
        await _service.SaveAsync(CreateDocument("Вторая"), null);
        await _service.SaveAsync(CreateDocument("Третья"), null);

        string[] generations = GetGenerationDirectories();
        Assert.Equal(2, generations.Length);

        GostDocument loaded = await _service.LoadDocumentAsync();
        Assert.Equal(
            "Третья",
            Assert.Single(loaded.Paragraphs).GetPlainText());
    }

    [Fact]
    public async Task InspectStartupAsync_WithoutArtifacts_ReturnsNone()
    {
        RecoveryStartupResult result =
            await _service.InspectStartupAsync();

        Assert.Equal(RecoveryStartupState.None, result.State);
        Assert.Null(result.Document);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task InspectStartupAsync_WhenCurrentIsCorrupted_UsesPreviousGeneration()
    {
        ManualUtcTimeProvider clock = new(SavedAtUtc);
        RecoveryStorageService service = new(
            new ArchiveService(),
            _temporaryDirectory.DirectoryPath,
            clock);
        await service.SaveAsync(CreateDocument("Предыдущее"), null);
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.SaveAsync(CreateDocument("Текущее"), null);
        await File.WriteAllTextAsync(
            service.RecoveryFilePath,
            "corrupted package");

        RecoveryStartupResult result =
            await service.InspectStartupAsync();

        Assert.Equal(RecoveryStartupState.Recoverable, result.State);
        Assert.Equal(
            "Предыдущее",
            Assert.Single(result.Document!.Paragraphs).GetPlainText());
        Assert.Equal(RecoveryCandidateKind.OtherGeneration, result.CandidateKind);
        Assert.Single(result.Issues);
    }

    [Fact]
    public async Task InspectStartupAsync_WhenNewerOrphanIsValid_SelectsOrphan()
    {
        ManualUtcTimeProvider clock = new(SavedAtUtc);
        FailOnCallAtomicCommitter committer = new(failOnCall: 2);
        RecoveryStorageService service = new(
            new ArchiveService(),
            _temporaryDirectory.DirectoryPath,
            clock,
            committer);
        await service.SaveAsync(CreateDocument("Опубликованное"), null);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<InjectedRecoveryIOException>(() =>
            service.SaveAsync(CreateDocument("Более новое"), null));

        RecoveryStartupResult result =
            await service.InspectStartupAsync();

        Assert.Equal(RecoveryStartupState.Recoverable, result.State);
        Assert.Equal(
            "Более новое",
            Assert.Single(result.Document!.Paragraphs).GetPlainText());
        Assert.Equal(RecoveryCandidateKind.OtherGeneration, result.CandidateKind);
        Assert.Equal(2, result.RecoverableCandidateCount);
    }

    [Fact]
    public async Task InspectStartupAsync_WhenPointerIsCorrupted_ScansValidGenerations()
    {
        await _service.SaveAsync(CreateDocument("Доступная копия"), null);
        await File.WriteAllTextAsync(_service.PointerFilePath, "{ invalid");

        RecoveryStartupResult result =
            await _service.InspectStartupAsync();

        Assert.Equal(RecoveryStartupState.Recoverable, result.State);
        Assert.Equal(
            "Доступная копия",
            Assert.Single(result.Document!.Paragraphs).GetPlainText());
        Assert.Equal(RecoveryCandidateKind.OtherGeneration, result.CandidateKind);
        Assert.Single(result.Issues);
    }

    [Fact]
    public async Task InspectStartupAsync_WhenOnlyGenerationIsInvalid_ReturnsCorrupted()
    {
        await _service.SaveAsync(CreateDocument("Повреждаемая"), null);
        File.Delete(_service.MetadataFilePath);

        RecoveryStartupResult result =
            await _service.InspectStartupAsync();

        Assert.Equal(RecoveryStartupState.Corrupted, result.State);
        Assert.Null(result.Document);
        Assert.Single(result.Issues);
    }

    [Fact]
    public async Task LoadMetadataAsync_WhenGenerationIdDoesNotMatch_Throws()
    {
        await _service.SaveAsync(CreateDocument("Несогласованная"), null);
        RecoveryMetadata metadata = Assert.IsType<RecoveryMetadata>(
            await _service.LoadMetadataAsync());
        RecoveryMetadata mismatched = metadata with
        {
            SessionId = Guid.NewGuid()
        };
        await File.WriteAllTextAsync(
            _service.MetadataFilePath,
            JsonSerializer.Serialize(mismatched));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.LoadMetadataAsync());
    }

    [Fact]
    public async Task LoadMetadataAsync_WhenCurrentGenerationMetadataIsMissing_Throws()
    {
        await _service.SaveAsync(CreateDocument("Без метаданных"), null);
        File.Delete(_service.MetadataFilePath);

        FileNotFoundException exception =
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => _service.LoadMetadataAsync());

        Assert.Equal(_service.MetadataFilePath, exception.FileName);
    }

    [Fact]
    public async Task LoadMetadataAsync_WhenCurrentGenerationMetadataIsNull_Throws()
    {
        await _service.SaveAsync(CreateDocument("Пустые метаданные"), null);
        await File.WriteAllTextAsync(_service.MetadataFilePath, "null");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.LoadMetadataAsync());
    }

    [Fact]
    public async Task SaveAsync_WhenPublishAndCleanupFail_PreservesPublishException()
    {
        InjectedRecoveryIOException publishException =
            new("pointer publish");
        InjectedRecoveryException cleanupException =
            new("staging cleanup");
        FaultingRecoveryFileSystem fileSystem = new()
        {
            DeleteDirectoryException = cleanupException
        };
        RecoveryStorageService service = CreateService(
            new AlwaysFailingAtomicCommitter(publishException),
            fileSystem);

        InjectedRecoveryIOException actual =
            await Assert.ThrowsAsync<InjectedRecoveryIOException>(() =>
                service.SaveAsync(CreateDocument("Черновик"), null));

        Assert.Same(publishException, actual);
        RecoveryStorageDiagnostic diagnostic = Assert.Single(
            service.Diagnostics);
        Assert.Equal("staging cleanup", diagnostic.Operation);
        Assert.Same(cleanupException, diagnostic.Exception);
    }

    [Fact]
    public async Task SaveAsync_WhenPostPublishCleanupFails_RemainsSuccessful()
    {
        InjectedRecoveryException cleanupException =
            new("generation enumeration");
        FaultingRecoveryFileSystem fileSystem = new()
        {
            EnumerateDirectoriesException = cleanupException
        };
        RecoveryStorageService service = CreateService(
            new AtomicFileCommitter(),
            fileSystem);

        RecoveryMetadata metadata = await service.SaveAsync(
            CreateDocument("Опубликованная версия"),
            null);

        Assert.Equal(
            metadata.SessionId,
            Assert.IsType<RecoveryMetadata>(
                await service.LoadMetadataAsync()).SessionId);
        Assert.Equal(
            "Опубликованная версия",
            Assert.Single(
                (await service.LoadDocumentAsync()).Paragraphs)
                .GetPlainText());
        RecoveryStorageDiagnostic diagnostic = Assert.Single(
            service.Diagnostics);
        Assert.Equal("generation enumeration", diagnostic.Operation);
        Assert.Same(cleanupException, diagnostic.Exception);
    }

    [Fact]
    public async Task LegacyPair_RemainsReadableAndPreservedAfterGenerationPublish()
    {
        GostDocument legacyDocument = CreateDocument("Старый recovery");
        await new ArchiveService().SaveAsync(
            legacyDocument,
            _service.RecoveryFilePath);
        RecoveryMetadata legacyMetadata = new()
        {
            SessionId = Guid.NewGuid(),
            SavedAtUtc = SavedAtUtc
        };
        await File.WriteAllTextAsync(
            _service.MetadataFilePath,
            JsonSerializer.Serialize(legacyMetadata));

        Assert.True(_service.HasRecovery);
        GostDocument loaded = await _service.LoadDocumentAsync();
        RecoveryMetadata metadata = Assert.IsType<RecoveryMetadata>(
            await _service.LoadMetadataAsync());

        Assert.Equal(
            "Старый recovery",
            Assert.Single(loaded.Paragraphs).GetPlainText());
        Assert.Equal(legacyMetadata.SessionId, metadata.SessionId);

        await _service.SaveAsync(CreateDocument("Новое поколение"), null);

        Assert.True(File.Exists(Path.Combine(
            _temporaryDirectory.DirectoryPath,
            "autosave.gost")));
        Assert.True(File.Exists(Path.Combine(
            _temporaryDirectory.DirectoryPath,
            "session.json")));
    }

    [Fact]
    public void IncompleteStagingDirectory_IsIgnored()
    {
        string staging = _temporaryDirectory.GetPath(
            $".generation-{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "autosave.gost"), "partial");

        Assert.False(_service.HasRecovery);
    }

    [Fact]
    public async Task LoadMetadataAsync_WhenMetadataIsMissing_ReturnsNull()
    {
        RecoveryMetadata? metadata =
            await _service.LoadMetadataAsync();

        Assert.Null(metadata);
    }

    [Fact]
    public async Task LoadDocumentAsync_WhenRecoveryIsMissing_Throws()
    {
        FileNotFoundException exception =
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => _service.LoadDocumentAsync());

        Assert.Equal(
            _service.RecoveryFilePath,
            exception.FileName);
    }

    [Fact]
    public async Task LoadMetadataAsync_WhenMetadataIsCorrupted_ThrowsJsonException()
    {
        await File.WriteAllTextAsync(
            _service.MetadataFilePath,
            "{ invalid json");

        await Assert.ThrowsAsync<JsonException>(
            () => _service.LoadMetadataAsync());
    }

    [Fact]
    public async Task LoadDocumentAsync_WhenRecoveryIsCorrupted_ThrowsInvalidDataException()
    {
        await File.WriteAllTextAsync(
            _service.RecoveryFilePath,
            "this is not a gost archive");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.LoadDocumentAsync());
    }

    [Fact]
    public async Task DeleteRecovery_RemovesPackageMetadataAndTemporaryFiles()
    {
        await _service.SaveAsync(
            CreateDocument("Удаление"),
            null);

        string temporaryFile =
            _temporaryDirectory.GetPath("orphan.tmp");

        await File.WriteAllTextAsync(
            temporaryFile,
            "temporary");

        _service.DeleteRecovery();

        Assert.False(File.Exists(_service.RecoveryFilePath));
        Assert.False(File.Exists(_service.MetadataFilePath));
        Assert.False(File.Exists(temporaryFile));
        Assert.False(_service.HasRecovery);
    }

    [Fact]
    public async Task DeleteRecovery_WhenOldGenerationDeleteFails_PreservesCurrent()
    {
        await _service.SaveAsync(CreateDocument("Предыдущая"), null);
        await _service.SaveAsync(CreateDocument("Текущая"), null);
        string currentPackage = _service.RecoveryFilePath;
        InjectedRecoveryException deleteException =
            new("generation delete");
        RecoveryStorageService failingService = CreateService(
            new AtomicFileCommitter(),
            new FaultingRecoveryFileSystem
            {
                DeleteDirectoryException = deleteException
            });

        InjectedRecoveryException actual = Assert.Throws<
            InjectedRecoveryException>(failingService.DeleteRecovery);

        Assert.Same(deleteException, actual);
        Assert.True(File.Exists(failingService.PointerFilePath));
        Assert.True(File.Exists(currentPackage));
        Assert.True(failingService.HasRecovery);
    }

    [Fact]
    public async Task DeleteRecovery_WhenCurrentGenerationDeleteFails_RemainsDiscoverable()
    {
        await _service.SaveAsync(CreateDocument("Текущая"), null);
        string currentPackage = _service.RecoveryFilePath;
        InjectedRecoveryException deleteException =
            new("current generation delete");
        RecoveryStorageService failingService = CreateService(
            new AtomicFileCommitter(),
            new FaultingRecoveryFileSystem
            {
                DeleteDirectoryException = deleteException
            });

        Assert.Throws<InjectedRecoveryException>(
            failingService.DeleteRecovery);

        Assert.False(File.Exists(failingService.PointerFilePath));
        Assert.True(File.Exists(currentPackage));
        Assert.True(failingService.HasRecovery);
        RecoveryStartupResult inspection =
            await failingService.InspectStartupAsync();
        Assert.Equal(RecoveryStartupState.Recoverable, inspection.State);
    }

    public void Dispose()
    {
        _temporaryDirectory.Dispose();
    }

    private static GostDocument CreateDocument(string text)
    {
        return new GostDocument
        {
            TitlePage =
            {
                WorkTitle = "Тестовый документ"
            },
            Modules =
            {
                ContentStartPage = 5
            },
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new TextRun(text)
                    }
                }
            }
        };
    }

    private RecoveryStorageService CreateService(
        IAtomicFileCommitter fileCommitter,
        IRecoveryFileSystem? fileSystem = null) =>
        new(
            new ArchiveService(),
            _temporaryDirectory.DirectoryPath,
            new ManualUtcTimeProvider(SavedAtUtc),
            fileCommitter,
            fileSystem);

    private string[] GetGenerationDirectories() =>
        Directory.GetDirectories(
            _temporaryDirectory.DirectoryPath,
            "generation-*",
            SearchOption.TopDirectoryOnly);

    private sealed class FailOnCallAtomicCommitter : IAtomicFileCommitter
    {
        private readonly AtomicFileCommitter _inner = new();
        private readonly int _failOnCall;
        private int _calls;

        internal FailOnCallAtomicCommitter(int failOnCall)
        {
            _failOnCall = failOnCall;
        }

        public Task WriteAsync(
            string destinationPath,
            Func<Stream, CancellationToken, Task> writeAsync,
            CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref _calls);
            return call == _failOnCall
                ? Task.FromException(
                    new InjectedRecoveryIOException("pointer publish"))
                : _inner.WriteAsync(
                    destinationPath,
                    writeAsync,
                    cancellationToken);
        }
    }

    private sealed class InjectedRecoveryIOException : IOException
    {
        internal InjectedRecoveryIOException(string message)
            : base(message)
        {
        }
    }

    private sealed class InjectedRecoveryException : Exception
    {
        internal InjectedRecoveryException(string message)
            : base(message)
        {
        }
    }

    private sealed class AlwaysFailingAtomicCommitter : IAtomicFileCommitter
    {
        private readonly Exception _exception;

        internal AlwaysFailingAtomicCommitter(Exception exception)
        {
            _exception = exception;
        }

        public Task WriteAsync(
            string destinationPath,
            Func<Stream, CancellationToken, Task> writeAsync,
            CancellationToken cancellationToken = default) =>
            Task.FromException(_exception);
    }

    private sealed class FaultingRecoveryFileSystem : IRecoveryFileSystem
    {
        private readonly PhysicalRecoveryFileSystem _inner = new();

        internal Exception? DeleteDirectoryException { get; init; }

        internal Exception? EnumerateDirectoriesException { get; init; }

        public void CreateDirectory(string path) =>
            _inner.CreateDirectory(path);

        public Stream CreateMetadataFile(string path) =>
            _inner.CreateMetadataFile(path);

        public Stream OpenRead(string path) => _inner.OpenRead(path);

        public Task FlushToDiskAsync(
            Stream stream,
            CancellationToken cancellationToken) =>
            _inner.FlushToDiskAsync(stream, cancellationToken);

        public void MoveDirectory(
            string sourcePath,
            string destinationPath) =>
            _inner.MoveDirectory(sourcePath, destinationPath);

        public bool FileExists(string path) => _inner.FileExists(path);

        public bool DirectoryExists(string path) =>
            _inner.DirectoryExists(path);

        public string ReadAllText(string path) =>
            _inner.ReadAllText(path);

        public IEnumerable<string> EnumerateFiles(
            string path,
            string searchPattern) =>
            _inner.EnumerateFiles(path, searchPattern);

        public IEnumerable<string> EnumerateDirectories(string path)
        {
            if (EnumerateDirectoriesException is not null)
            {
                throw EnumerateDirectoriesException;
            }

            return _inner.EnumerateDirectories(path);
        }

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public void DeleteDirectory(string path)
        {
            if (DeleteDirectoryException is not null)
            {
                throw DeleteDirectoryException;
            }

            _inner.DeleteDirectory(path);
        }
    }

    private sealed class CancellingArchiveService : IArchiveService
    {
        private readonly ArchiveService _inner = new();
        private readonly CancellationTokenSource _cancellation;

        internal CancellingArchiveService(
            CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public GostDocument CreateNew() => _inner.CreateNew();

        public Task<GostDocument> LoadAsync(string filePath) =>
            _inner.LoadAsync(filePath);

        public Task<GostDocument> LoadAsync(Stream stream) =>
            _inner.LoadAsync(stream);

        public async Task SaveAsync(
            GostDocument document,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            await _inner.SaveAsync(document, filePath, cancellationToken);
            _cancellation.Cancel();
        }

        public Task SaveAsync(
            DocumentPersistenceSnapshot snapshot,
            string filePath,
            CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(snapshot, filePath, cancellationToken);

        public Task SaveAsync(
            GostDocument document,
            Stream stream,
            CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(document, stream, cancellationToken);

        public Task SaveAsync(
            DocumentPersistenceSnapshot snapshot,
            Stream stream,
            CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(snapshot, stream, cancellationToken);
    }
}
