using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GostEditor.Core.Interfaces;
using GostEditor.Core.IO;
using GostEditor.Core.Models;

namespace GostEditor.UI.Services;

public sealed class RecoveryStorageService
{
    private const string RecoveryFileName = "autosave.gost";
    private const string MetadataFileName = "session.json";
    private const string PointerFileName = "current.json";
    private const string GenerationPrefix = "generation-";
    private const string StagingPrefix = ".generation-";

    private readonly IArchiveService _archiveService;
    private readonly IAtomicFileCommitter _fileCommitter;
    private readonly IRecoveryFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly object _diagnosticsSync = new();
    private readonly List<RecoveryStorageDiagnostic> _diagnostics = new();
    private readonly JsonSerializerOptions _jsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    public RecoveryStorageService(IArchiveService archiveService)
        : this(
            archiveService,
            GetDefaultRecoveryDirectory(),
            TimeProvider.System,
            new AtomicFileCommitter(),
            new PhysicalRecoveryFileSystem())
    {
    }

    public RecoveryStorageService(
        IArchiveService archiveService,
        string recoveryDirectoryPath)
        : this(
            archiveService,
            recoveryDirectoryPath,
            TimeProvider.System,
            new AtomicFileCommitter(),
            new PhysicalRecoveryFileSystem())
    {
    }

    internal RecoveryStorageService(
        IArchiveService archiveService,
        string recoveryDirectoryPath,
        TimeProvider timeProvider,
        IAtomicFileCommitter? fileCommitter = null,
        IRecoveryFileSystem? fileSystem = null)
    {
        _archiveService = archiveService
            ?? throw new ArgumentNullException(nameof(archiveService));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _fileCommitter = fileCommitter
            ?? new AtomicFileCommitter();
        _fileSystem = fileSystem
            ?? new PhysicalRecoveryFileSystem();

        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectoryPath);

        RecoveryDirectoryPath = Path.GetFullPath(recoveryDirectoryPath);
    }

    public string RecoveryDirectoryPath { get; }

    public string RecoveryFilePath =>
        TryResolveCurrentGeneration(out RecoveryGenerationPaths paths)
            ? paths.PackagePath
            : LegacyRecoveryFilePath;

    public string MetadataFilePath =>
        TryResolveCurrentGeneration(out RecoveryGenerationPaths paths)
            ? paths.MetadataPath
            : LegacyMetadataFilePath;

    internal string PointerFilePath =>
        Path.Combine(RecoveryDirectoryPath, PointerFileName);

    internal IReadOnlyList<RecoveryStorageDiagnostic> Diagnostics
    {
        get
        {
            lock (_diagnosticsSync)
            {
                return _diagnostics.ToArray();
            }
        }
    }

    public bool HasRecovery =>
        _fileSystem.FileExists(PointerFilePath) ||
        _fileSystem.FileExists(LegacyRecoveryFilePath);

    private string LegacyRecoveryFilePath =>
        Path.Combine(RecoveryDirectoryPath, RecoveryFileName);

    private string LegacyMetadataFilePath =>
        Path.Combine(RecoveryDirectoryPath, MetadataFileName);

    public async Task<RecoveryMetadata> SaveAsync(
        GostDocument document,
        string? originalFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        _fileSystem.CreateDirectory(RecoveryDirectoryPath);

        RecoveryGenerationPointer? previousPointer =
            TryReadCurrentPointer();
        Guid generationId = Guid.NewGuid();
        string stagingDirectory = Path.Combine(
            RecoveryDirectoryPath,
            $"{StagingPrefix}{generationId:N}.tmp");
        string generationDirectory = GetGenerationDirectory(generationId);

        RecoveryMetadata metadata = new()
        {
            SessionId = generationId,
            OriginalFilePath = NormalizeOptionalPath(originalFilePath),
            SavedAtUtc = _timeProvider.GetUtcNow()
        };

        _fileSystem.CreateDirectory(stagingDirectory);

        try
        {
            string packagePath = Path.Combine(
                stagingDirectory,
                RecoveryFileName);
            string metadataPath = Path.Combine(
                stagingDirectory,
                MetadataFileName);

            await _archiveService.SaveAsync(
                document,
                packagePath,
                cancellationToken);

            await WriteMetadataAsync(
                metadataPath,
                metadata,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            _fileSystem.MoveDirectory(stagingDirectory, generationDirectory);

            RecoveryGenerationPointer pointer = new()
            {
                GenerationId = generationId
            };

            await _fileCommitter.WriteAsync(
                PointerFilePath,
                (stream, token) => JsonSerializer.SerializeAsync(
                        stream,
                        pointer,
                        _jsonOptions,
                        token),
                cancellationToken);

            CleanupAfterSuccessfulPublish(
                generationId,
                previousPointer?.GenerationId);

            return metadata;
        }
        finally
        {
            TryCleanupDirectory(stagingDirectory, "staging cleanup");
        }
    }

    public async Task<GostDocument> LoadDocumentAsync()
    {
        RecoveryGenerationPaths paths =
            await ResolveCurrentGenerationAsync();

        if (!_fileSystem.FileExists(paths.PackagePath))
        {
            throw new FileNotFoundException(
                "Файл автоматического восстановления не найден.",
                paths.PackagePath);
        }

        return await _archiveService.LoadAsync(paths.PackagePath);
    }

    public async Task<RecoveryMetadata?> LoadMetadataAsync()
    {
        RecoveryGenerationPaths paths =
            await ResolveCurrentGenerationAsync();

        if (!_fileSystem.FileExists(paths.MetadataPath))
        {
            if (paths.GenerationId.HasValue)
            {
                throw new FileNotFoundException(
                    "Метаданные текущего поколения recovery не найдены.",
                    paths.MetadataPath);
            }

            return null;
        }

        await using Stream stream = _fileSystem.OpenRead(paths.MetadataPath);

        RecoveryMetadata? metadata =
            await JsonSerializer.DeserializeAsync<RecoveryMetadata>(
                stream,
                _jsonOptions);

        if (paths.GenerationId.HasValue && metadata is null)
        {
            throw new InvalidDataException(
                "Метаданные текущего поколения recovery пусты.");
        }

        if (paths.GenerationId.HasValue &&
            metadata!.SessionId != paths.GenerationId.Value)
        {
            throw new InvalidDataException(
                "Метаданные recovery относятся к другому поколению.");
        }

        return metadata;
    }

    public void DeleteRecovery()
    {
        _fileSystem.DeleteFile(PointerFilePath);
        _fileSystem.DeleteFile(LegacyRecoveryFilePath);
        _fileSystem.DeleteFile(LegacyMetadataFilePath);

        if (!_fileSystem.DirectoryExists(RecoveryDirectoryPath))
        {
            return;
        }

        foreach (string file in _fileSystem.EnumerateFiles(
                     RecoveryDirectoryPath,
                     "*.tmp"))
        {
            TryCleanupFile(file, "temporary file cleanup");
        }

        foreach (string file in _fileSystem.EnumerateFiles(
                     RecoveryDirectoryPath,
                     "*.rollback"))
        {
            TryCleanupFile(file, "rollback cleanup");
        }

        foreach (string directory in
                 _fileSystem.EnumerateDirectories(RecoveryDirectoryPath))
        {
            string name = Path.GetFileName(directory);
            if (name.StartsWith(GenerationPrefix, StringComparison.Ordinal) ||
                name.StartsWith(StagingPrefix, StringComparison.Ordinal))
            {
                TryCleanupDirectory(directory, "generation cleanup");
            }
        }
    }

    private async Task WriteMetadataAsync(
        string path,
        RecoveryMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using Stream stream = _fileSystem.CreateMetadataFile(path);

        await JsonSerializer.SerializeAsync(
            stream,
            metadata,
            _jsonOptions,
            cancellationToken);
        await _fileSystem.FlushToDiskAsync(stream, cancellationToken);
    }

    private async Task<RecoveryGenerationPaths> ResolveCurrentGenerationAsync()
    {
        if (!_fileSystem.FileExists(PointerFilePath))
        {
            return RecoveryGenerationPaths.Legacy(
                LegacyRecoveryFilePath,
                LegacyMetadataFilePath);
        }

        await using Stream stream = _fileSystem.OpenRead(PointerFilePath);

        RecoveryGenerationPointer? pointer =
            await JsonSerializer.DeserializeAsync<RecoveryGenerationPointer>(
                stream,
                _jsonOptions);

        return CreateGenerationPaths(ValidatePointer(pointer));
    }

    private bool TryResolveCurrentGeneration(
        out RecoveryGenerationPaths paths)
    {
        RecoveryGenerationPointer? pointer = TryReadCurrentPointer();
        if (pointer is null)
        {
            paths = null!;
            return false;
        }

        paths = CreateGenerationPaths(pointer.GenerationId);
        return true;
    }

    private RecoveryGenerationPointer? TryReadCurrentPointer()
    {
        if (!_fileSystem.FileExists(PointerFilePath))
        {
            return null;
        }

        try
        {
            string json = _fileSystem.ReadAllText(PointerFilePath);
            RecoveryGenerationPointer? pointer =
                JsonSerializer.Deserialize<RecoveryGenerationPointer>(
                    json,
                    _jsonOptions);
            return pointer is null || pointer.GenerationId == Guid.Empty
                ? null
                : pointer;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid ValidatePointer(
        RecoveryGenerationPointer? pointer)
    {
        if (pointer is null ||
            pointer.Version != RecoveryGenerationPointer.CurrentVersion ||
            pointer.GenerationId == Guid.Empty)
        {
            throw new InvalidDataException(
                "Указатель recovery отсутствует или имеет неподдерживаемый формат.");
        }

        return pointer.GenerationId;
    }

    private RecoveryGenerationPaths CreateGenerationPaths(Guid generationId)
    {
        string directory = GetGenerationDirectory(generationId);
        return new RecoveryGenerationPaths(
            generationId,
            Path.Combine(directory, RecoveryFileName),
            Path.Combine(directory, MetadataFileName));
    }

    private string GetGenerationDirectory(Guid generationId) =>
        Path.Combine(
            RecoveryDirectoryPath,
            $"{GenerationPrefix}{generationId:N}");

    private void CleanupAfterSuccessfulPublish(
        Guid currentGenerationId,
        Guid? previousGenerationId)
    {
        try
        {
            foreach (string directory in
                     _fileSystem.EnumerateDirectories(RecoveryDirectoryPath))
            {
                string name = Path.GetFileName(directory);
                if (name.StartsWith(StagingPrefix, StringComparison.Ordinal))
                {
                    TryCleanupDirectory(directory, "staging cleanup");
                    continue;
                }

                if (!previousGenerationId.HasValue ||
                    !TryParseGenerationId(name, out Guid generationId) ||
                    generationId == currentGenerationId ||
                    generationId == previousGenerationId)
                {
                    continue;
                }

                TryCleanupDirectory(directory, "old generation cleanup");
            }
        }
        catch (Exception exception)
        {
            RecordDiagnostic("generation enumeration", exception);
        }
    }

    private static bool TryParseGenerationId(
        string directoryName,
        out Guid generationId)
    {
        generationId = Guid.Empty;
        return directoryName.StartsWith(
                   GenerationPrefix,
                   StringComparison.Ordinal) &&
               Guid.TryParseExact(
                   directoryName[GenerationPrefix.Length..],
                   "N",
                   out generationId);
    }

    private static string GetDefaultRecoveryDirectory()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "Не удалось определить каталог локальных данных приложения.");
        }

        return Path.Combine(
            localApplicationData,
            "GostEditor",
            "Recovery");
    }

    private static string? NormalizeOptionalPath(string? filePath)
    {
        return string.IsNullOrWhiteSpace(filePath)
            ? null
            : Path.GetFullPath(filePath);
    }

    private void TryCleanupFile(string filePath, string operation)
    {
        try
        {
            _fileSystem.DeleteFile(filePath);
        }
        catch (Exception exception)
        {
            RecordDiagnostic(operation, exception);
        }
    }

    private void TryCleanupDirectory(
        string directoryPath,
        string operation)
    {
        try
        {
            _fileSystem.DeleteDirectory(directoryPath);
        }
        catch (Exception exception)
        {
            RecordDiagnostic(operation, exception);
        }
    }

    private void RecordDiagnostic(
        string operation,
        Exception exception)
    {
        lock (_diagnosticsSync)
        {
            _diagnostics.Add(new RecoveryStorageDiagnostic(
                operation,
                exception));
        }
    }

    private sealed record RecoveryGenerationPointer
    {
        internal const int CurrentVersion = 1;

        public int Version { get; init; } = CurrentVersion;

        public Guid GenerationId { get; init; }
    }

    private sealed record RecoveryGenerationPaths(
        Guid? GenerationId,
        string PackagePath,
        string MetadataPath)
    {
        internal static RecoveryGenerationPaths Legacy(
            string packagePath,
            string metadataPath) =>
            new(null, packagePath, metadataPath);
    }
}
