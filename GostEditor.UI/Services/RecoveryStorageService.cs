using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;

namespace GostEditor.UI.Services;

public sealed class RecoveryStorageService
{
    private const string RecoveryFileName = "autosave.gost";
    private const string MetadataFileName = "session.json";

    private readonly IArchiveService _archiveService;
    private readonly JsonSerializerOptions _jsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    public RecoveryStorageService(IArchiveService archiveService)
        : this(archiveService, GetDefaultRecoveryDirectory())
    {
    }

    public RecoveryStorageService(
        IArchiveService archiveService,
        string recoveryDirectoryPath)
    {
        _archiveService = archiveService
            ?? throw new ArgumentNullException(nameof(archiveService));

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
        string? originalFilePath)
    {
        ArgumentNullException.ThrowIfNull(document);

        Directory.CreateDirectory(RecoveryDirectoryPath);

        string packageTempPath = CreateTemporaryPath(RecoveryFileName);
        string metadataTempPath = CreateTemporaryPath(MetadataFileName);

        RecoveryMetadata metadata = new()
        {
            SessionId = Guid.NewGuid(),
            OriginalFilePath = NormalizeOptionalPath(originalFilePath),
            SavedAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            await _archiveService.SaveAsync(document, packageTempPath);

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
                    _jsonOptions);

                await metadataStream.FlushAsync();
            }

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
