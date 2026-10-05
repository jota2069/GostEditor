using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    internal const int MaxGenerationCandidates = 64;
    internal const int MaxMaterializationAttempts = 3;
    internal const long MaxMetadataBytes = 64 * 1024;
    internal const long MaxAggregateMetadataBytes = 1024L * 1024;
    internal const long MaxAggregatePackageBytes = 1024L * 1024 * 1024;

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
        _fileSystem.FileExists(LegacyRecoveryFilePath) ||
        HasGenerationDirectory();

    public async Task<RecoveryStartupResult> InspectStartupAsync()
    {
        List<RecoveryStartupIssue> issues = new();
        List<RecoveryCandidateDescriptor> candidates = new();
        HashSet<Guid> inspectedGenerations = new();
        bool hasArtifacts = false;
        long aggregatePackageBytes = 0;
        long aggregateMetadataBytes = 0;
        int generationDirectoryCount = 0;

        RecoveryGenerationPointer? currentPointer = null;
        if (_fileSystem.FileExists(PointerFilePath))
        {
            hasArtifacts = true;
            try
            {
                EnsureMetadataWithinLimit(PointerFilePath);
                (RecoveryGenerationPointer? pointer, _) =
                    await ReadBoundedJsonAsync<RecoveryGenerationPointer>(
                        PointerFilePath,
                        MaxMetadataBytes);
                currentPointer = new RecoveryGenerationPointer
                {
                    GenerationId = ValidatePointer(pointer)
                };
            }
            catch (Exception exception)
            {
                issues.Add(new RecoveryStartupIssue(
                    PointerFilePath,
                    exception));
            }
        }

        if (currentPointer is not null)
        {
            inspectedGenerations.Add(currentPointer.GenerationId);
            RecoveryCandidateDescriptor? current =
                await InspectGenerationMetadataAsync(
                currentPointer.GenerationId,
                RecoveryCandidateKind.CurrentGeneration,
                issues);
            if (current is not null)
            {
                RecoveryStartupCandidate? loaded =
                    await TryLoadCandidateAsync(current, issues);
                if (loaded is not null)
                {
                    return CreateRecoverableResult(
                        loaded,
                        recoverableCandidateCount: 1,
                        issues);
                }
            }
        }

        try
        {
            if (_fileSystem.DirectoryExists(RecoveryDirectoryPath))
            {
                foreach (string directory in
                         _fileSystem.EnumerateDirectories(
                             RecoveryDirectoryPath))
                {
                    string name = Path.GetFileName(directory);
                    if (!TryParseGenerationId(name, out Guid generationId))
                    {
                        continue;
                    }

                    hasArtifacts = true;
                    generationDirectoryCount++;
                    if (generationDirectoryCount > MaxGenerationCandidates)
                    {
                        issues.Add(new RecoveryStartupIssue(
                            RecoveryDirectoryPath,
                            new InvalidDataException(
                                $"Количество поколений recovery превышает " +
                                $"лимит {MaxGenerationCandidates}.")));
                        return CreateCorruptedResult(issues);
                    }

                    if (!inspectedGenerations.Add(generationId))
                    {
                        continue;
                    }

                    RecoveryCandidateDescriptor? candidate =
                        await InspectGenerationMetadataAsync(
                        generationId,
                        RecoveryCandidateKind.OtherGeneration,
                        issues);
                    if (candidate is null)
                    {
                        continue;
                    }

                    if (candidate.PackageLength < 0 ||
                        aggregatePackageBytes >
                        MaxAggregatePackageBytes - candidate.PackageLength)
                    {
                        issues.Add(new RecoveryStartupIssue(
                            candidate.PackagePath,
                            new InvalidDataException(
                                "Совокупный размер recovery превышает " +
                                $"лимит {MaxAggregatePackageBytes} байт.")));
                        return CreateCorruptedResult(issues);
                    }

                    aggregatePackageBytes += candidate.PackageLength;
                    if (candidate.MetadataLength < 0 ||
                        aggregateMetadataBytes >
                        MaxAggregateMetadataBytes - candidate.MetadataLength)
                    {
                        issues.Add(new RecoveryStartupIssue(
                            candidate.MetadataPath,
                            new InvalidDataException(
                                "Совокупный размер метаданных recovery " +
                                $"превышает лимит {MaxAggregateMetadataBytes} байт.")));
                        return CreateCorruptedResult(issues);
                    }

                    aggregateMetadataBytes += candidate.MetadataLength;
                    candidates.Add(candidate);
                }
            }
        }
        catch (Exception exception)
        {
            hasArtifacts = true;
            issues.Add(new RecoveryStartupIssue(
                RecoveryDirectoryPath,
                exception));
            return CreateCorruptedResult(issues);
        }

        if (_fileSystem.FileExists(LegacyRecoveryFilePath))
        {
            hasArtifacts = true;
            RecoveryCandidateDescriptor? legacy =
                await InspectLegacyMetadataAsync(issues);
            if (legacy is not null)
            {
                if (legacy.PackageLength < 0 ||
                    aggregatePackageBytes >
                    MaxAggregatePackageBytes - legacy.PackageLength)
                {
                    issues.Add(new RecoveryStartupIssue(
                        legacy.PackagePath,
                        new InvalidDataException(
                            "Совокупный размер recovery превышает " +
                            $"лимит {MaxAggregatePackageBytes} байт.")));
                    return CreateCorruptedResult(issues);
                }

                if (legacy.MetadataLength < 0 ||
                    aggregateMetadataBytes >
                    MaxAggregateMetadataBytes - legacy.MetadataLength)
                {
                    issues.Add(new RecoveryStartupIssue(
                        legacy.MetadataPath,
                        new InvalidDataException(
                            "Совокупный размер метаданных recovery " +
                            $"превышает лимит {MaxAggregateMetadataBytes} байт.")));
                    return CreateCorruptedResult(issues);
                }

                candidates.Add(legacy);
            }
        }

        int attempts = 0;
        foreach (RecoveryCandidateDescriptor candidate in candidates
                     .OrderByDescending(item =>
                         item.Metadata?.GenerationSequence.HasValue == true)
                     .ThenByDescending(item =>
                         item.Metadata?.GenerationSequence ?? long.MinValue)
                     .ThenByDescending(item =>
                         item.Metadata?.SavedAtUtc ?? DateTimeOffset.MinValue)
                     .ThenBy(item => item.Kind))
        {
            if (attempts >= MaxMaterializationAttempts)
            {
                issues.Add(new RecoveryStartupIssue(
                    RecoveryDirectoryPath,
                    new InvalidDataException(
                        "Достигнут лимит попыток загрузки recovery: " +
                        MaxMaterializationAttempts + ".")));
                break;
            }

            attempts++;
            RecoveryStartupCandidate? loaded =
                await TryLoadCandidateAsync(candidate, issues);
            if (loaded is not null)
            {
                return CreateRecoverableResult(
                    loaded,
                    candidates.Count,
                    issues);
            }
        }

        return new RecoveryStartupResult
        {
            State = hasArtifacts
                ? RecoveryStartupState.Corrupted
                : RecoveryStartupState.None,
            Issues = issues
        };
    }

    private static RecoveryStartupResult CreateRecoverableResult(
        RecoveryStartupCandidate selected,
        int recoverableCandidateCount,
        IReadOnlyList<RecoveryStartupIssue> issues) =>
        new()
        {
            State = RecoveryStartupState.Recoverable,
            Document = selected.Document,
            Metadata = selected.Metadata,
            CandidateKind = selected.Kind,
            RecoverableCandidateCount = recoverableCandidateCount,
            Issues = issues
        };

    private static RecoveryStartupResult CreateCorruptedResult(
        IReadOnlyList<RecoveryStartupIssue> issues) =>
        new()
        {
            State = RecoveryStartupState.Corrupted,
            Issues = issues
        };

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
            SavedAtUtc = _timeProvider.GetUtcNow(),
            GenerationSequence =
                await GetNextGenerationSequenceAsync(previousPointer)
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
        RecoveryGenerationPointer? currentPointer =
            TryReadCurrentPointer();
        string? currentDirectory = currentPointer is null
            ? null
            : GetGenerationDirectory(currentPointer.GenerationId);

        if (_fileSystem.DirectoryExists(RecoveryDirectoryPath))
        {
            foreach (string file in _fileSystem.EnumerateFiles(
                         RecoveryDirectoryPath,
                         "*.tmp"))
            {
                _fileSystem.DeleteFile(file);
            }

            foreach (string file in _fileSystem.EnumerateFiles(
                         RecoveryDirectoryPath,
                         "*.rollback"))
            {
                _fileSystem.DeleteFile(file);
            }

            foreach (string directory in
                     _fileSystem.EnumerateDirectories(RecoveryDirectoryPath))
            {
                string name = Path.GetFileName(directory);
                if ((name.StartsWith(
                         GenerationPrefix,
                         StringComparison.Ordinal) ||
                     name.StartsWith(
                         StagingPrefix,
                         StringComparison.Ordinal)) &&
                    !string.Equals(
                        directory,
                        currentDirectory,
                        StringComparison.Ordinal))
                {
                    _fileSystem.DeleteDirectory(directory);
                }
            }
        }

        _fileSystem.DeleteFile(LegacyRecoveryFilePath);
        _fileSystem.DeleteFile(LegacyMetadataFilePath);
        _fileSystem.DeleteFile(PointerFilePath);

        if (currentDirectory is not null)
        {
            _fileSystem.DeleteDirectory(currentDirectory);
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

    private async Task<RecoveryCandidateDescriptor?>
        InspectGenerationMetadataAsync(
        Guid generationId,
        RecoveryCandidateKind kind,
        ICollection<RecoveryStartupIssue> issues)
    {
        RecoveryGenerationPaths paths =
            CreateGenerationPaths(generationId);

        try
        {
            if (!_fileSystem.FileExists(paths.PackagePath) ||
                !_fileSystem.FileExists(paths.MetadataPath))
            {
                throw new InvalidDataException(
                    "Поколение recovery содержит неполный набор файлов.");
            }

            EnsureMetadataWithinLimit(paths.MetadataPath);

            (RecoveryMetadata? metadata, long metadataLength) =
                await ReadBoundedJsonAsync<RecoveryMetadata>(
                    paths.MetadataPath,
                    MaxMetadataBytes);

            if (metadata is null ||
                metadata.Version != RecoveryMetadata.CurrentVersion ||
                metadata.SessionId != generationId ||
                metadata.GenerationSequence is <= 0)
            {
                throw new InvalidDataException(
                    "Метаданные recovery не соответствуют поколению.");
            }

            return new RecoveryCandidateDescriptor(
                kind,
                paths.PackagePath,
                paths.MetadataPath,
                metadata,
                _fileSystem.GetFileLength(paths.PackagePath),
                metadataLength);
        }
        catch (Exception exception)
        {
            issues.Add(new RecoveryStartupIssue(
                Path.GetDirectoryName(paths.PackagePath)!,
                exception));
            return null;
        }
    }

    private async Task<RecoveryCandidateDescriptor?>
        InspectLegacyMetadataAsync(
        ICollection<RecoveryStartupIssue> issues)
    {
        try
        {
            RecoveryMetadata? metadata = null;
            long metadataLength = 0;
            if (_fileSystem.FileExists(LegacyMetadataFilePath))
            {
                EnsureMetadataWithinLimit(LegacyMetadataFilePath);
                (metadata, metadataLength) =
                    await ReadBoundedJsonAsync<RecoveryMetadata>(
                        LegacyMetadataFilePath,
                        MaxMetadataBytes);
            }

            return new RecoveryCandidateDescriptor(
                RecoveryCandidateKind.Legacy,
                LegacyRecoveryFilePath,
                LegacyMetadataFilePath,
                metadata,
                _fileSystem.GetFileLength(LegacyRecoveryFilePath),
                metadataLength);
        }
        catch (Exception exception)
        {
            issues.Add(new RecoveryStartupIssue(
                LegacyRecoveryFilePath,
                exception));
            return null;
        }
    }

    private async Task<RecoveryStartupCandidate?> TryLoadCandidateAsync(
        RecoveryCandidateDescriptor candidate,
        ICollection<RecoveryStartupIssue> issues)
    {
        try
        {
            GostDocument document =
                await _archiveService.LoadAsync(candidate.PackagePath);
            return new RecoveryStartupCandidate(
                candidate.Kind,
                document,
                candidate.Metadata);
        }
        catch (Exception exception)
        {
            issues.Add(new RecoveryStartupIssue(
                candidate.PackagePath,
                exception));
            return null;
        }
    }

    private void EnsureMetadataWithinLimit(string metadataPath)
    {
        long length = _fileSystem.GetFileLength(metadataPath);
        if (length < 0 || length > MaxMetadataBytes)
        {
            throw new InvalidDataException(
                $"Метаданные recovery превышают лимит {MaxMetadataBytes} байт.");
        }
    }

    private async Task<(T? Value, long Length)> ReadBoundedJsonAsync<T>(
        string path,
        long maximumLength)
    {
        await using Stream source = _fileSystem.OpenRead(path);
        await using MemoryStream buffer = new();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int read = await source.ReadAsync(chunk);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length > maximumLength - read)
            {
                throw new InvalidDataException(
                    $"JSON recovery превышает лимит {maximumLength} байт.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }

        buffer.Position = 0;
        T? value = await JsonSerializer.DeserializeAsync<T>(
            buffer,
            _jsonOptions);
        return (value, buffer.Length);
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

    private async Task<long> GetNextGenerationSequenceAsync(
        RecoveryGenerationPointer? previousPointer)
    {
        if (previousPointer is null)
        {
            return 1;
        }

        string metadataPath = CreateGenerationPaths(
            previousPointer.GenerationId).MetadataPath;
        try
        {
            if (!_fileSystem.FileExists(metadataPath))
            {
                return 1;
            }

            EnsureMetadataWithinLimit(metadataPath);
            (RecoveryMetadata? previous, _) =
                await ReadBoundedJsonAsync<RecoveryMetadata>(
                    metadataPath,
                    MaxMetadataBytes);
            long previousSequence = previous?.GenerationSequence ?? 0;
            if (previousSequence < 0)
            {
                throw new InvalidDataException(
                    "Последовательность опубликованного recovery некорректна.");
            }

            return checked(previousSequence + 1);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            RecordDiagnostic("generation sequence", exception);
            return 1;
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

    private bool HasGenerationDirectory()
    {
        if (!_fileSystem.DirectoryExists(RecoveryDirectoryPath))
        {
            return false;
        }

        return _fileSystem.EnumerateDirectories(RecoveryDirectoryPath)
            .Select(Path.GetFileName)
            .Any(name => name is not null &&
                         TryParseGenerationId(name, out _));
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

    private sealed record RecoveryStartupCandidate(
        RecoveryCandidateKind Kind,
        GostDocument Document,
        RecoveryMetadata? Metadata);

    private sealed record RecoveryCandidateDescriptor(
        RecoveryCandidateKind Kind,
        string PackagePath,
        string MetadataPath,
        RecoveryMetadata? Metadata,
        long PackageLength,
        long MetadataLength);
}
