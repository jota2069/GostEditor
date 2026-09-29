using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;

namespace GostEditor.UI.Services;

public sealed class RecoveryStorageService
{
    private const string RecoveryFileName = "autosave.gost";
    private const string MetadataFileName = "session.json";

    private readonly IArchiveService _archiveService;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    public RecoveryStorageService(IArchiveService archiveService)
        : this(
            archiveService,
            GetDefaultRecoveryDirectory(),
            TimeProvider.System)
    {
    }

    public RecoveryStorageService(
        IArchiveService archiveService,
        string recoveryDirectoryPath)
        : this(
            archiveService,
            recoveryDirectoryPath,
            TimeProvider.System)
    {
    }

    internal RecoveryStorageService(
        IArchiveService archiveService,
        string recoveryDirectoryPath,
        TimeProvider timeProvider)
    {
        _archiveService = archiveService
            ?? throw new ArgumentNullException(nameof(archiveService));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));

        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectoryPath);

        RecoveryDirectoryPath = Path.GetFullPath(recoveryDirectoryPath);
    }

    public string RecoveryDirectoryPath { get; }

    public string RecoveryFilePath =>
        Path.Combine(RecoveryDirectoryPath, RecoveryFileName);

    public string MetadataFilePath =>
        Path.Combine(RecoveryDirectoryPath, MetadataFileName);

    public bool HasRecovery => File.Exists(RecoveryFilePath);

    public async Task<RecoveryMetadata> SaveAsync(
        GostDocument document,
        string? originalFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        Directory.CreateDirectory(RecoveryDirectoryPath);

        string packageTempPath = CreateTemporaryPath(RecoveryFileName);
        string metadataTempPath = CreateTemporaryPath(MetadataFileName);

        RecoveryMetadata metadata = new()
        {
            SessionId = Guid.NewGuid(),
            OriginalFilePath = NormalizeOptionalPath(originalFilePath),
            SavedAtUtc = _timeProvider.GetUtcNow()
        };

        try
        {
            await _archiveService.SaveAsync(
                document,
                packageTempPath,
                cancellationToken);

            await using (FileStream metadataStream = new(
                metadataTempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    metadataStream,
                    metadata,
                    _jsonOptions,
                    cancellationToken);

                await metadataStream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            File.Move(packageTempPath, RecoveryFilePath, overwrite: true);
            File.Move(metadataTempPath, MetadataFilePath, overwrite: true);

            return metadata;
        }
        finally
        {
            TryDeleteFile(packageTempPath);
            TryDeleteFile(metadataTempPath);
        }
    }

    public Task<GostDocument> LoadDocumentAsync()
    {
        if (!File.Exists(RecoveryFilePath))
        {
            throw new FileNotFoundException(
                "Файл автоматического восстановления не найден.",
                RecoveryFilePath);
        }

        return _archiveService.LoadAsync(RecoveryFilePath);
    }

    public async Task<RecoveryMetadata?> LoadMetadataAsync()
    {
        if (!File.Exists(MetadataFilePath))
        {
            return null;
        }

        await using FileStream stream = new(
            MetadataFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        return await JsonSerializer.DeserializeAsync<RecoveryMetadata>(
            stream,
            _jsonOptions);
    }

    public void DeleteRecovery()
    {
        File.Delete(RecoveryFilePath);
        File.Delete(MetadataFilePath);

        if (!Directory.Exists(RecoveryDirectoryPath))
        {
            return;
        }

        foreach (string temporaryFile in Directory.EnumerateFiles(
                     RecoveryDirectoryPath,
                     "*.tmp",
                     SearchOption.TopDirectoryOnly))
        {
            TryDeleteFile(temporaryFile);
        }
    }

    private string CreateTemporaryPath(string baseFileName)
    {
        return Path.Combine(
            RecoveryDirectoryPath,
            $"{baseFileName}.{Guid.NewGuid():N}.tmp");
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

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
