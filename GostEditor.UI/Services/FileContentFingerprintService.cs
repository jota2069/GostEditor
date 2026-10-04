using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GostEditor.UI.Services;

public sealed record FileContentFingerprint(
    long Length,
    string Sha256);

public class FileContentFingerprintService
{
    public virtual async Task<FileContentFingerprint?> TryCaptureAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            await using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920,
                useAsync: true);
            return await CaptureAsync(stream, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async Task<FileContentFingerprint> CaptureAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = await stream.ReadAsync(
                   buffer,
                   cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            length = checked(length + read);
        }

        return new FileContentFingerprint(
            length,
            Convert.ToHexString(hash.GetHashAndReset()));
    }
}

public sealed class ExternalFileChangedException : IOException
{
    public ExternalFileChangedException(
        string filePath,
        Exception? innerException = null)
        : base(
            "Файл был изменён или заменён другим процессом после открытия: " +
            Path.GetFullPath(filePath),
            innerException)
    {
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }
}
