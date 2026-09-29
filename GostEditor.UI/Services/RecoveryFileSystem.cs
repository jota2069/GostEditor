using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GostEditor.UI.Services;

internal interface IRecoveryFileSystem
{
    void CreateDirectory(string path);

    Stream CreateMetadataFile(string path);

    Stream OpenRead(string path);

    Task FlushToDiskAsync(
        Stream stream,
        CancellationToken cancellationToken);

    void MoveDirectory(string sourcePath, string destinationPath);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    string ReadAllText(string path);

    IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern);

    IEnumerable<string> EnumerateDirectories(string path);

    void DeleteFile(string path);

    void DeleteDirectory(string path);
}

internal sealed class PhysicalRecoveryFileSystem : IRecoveryFileSystem
{
    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public Stream CreateMetadataFile(string path) =>
        new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);

    public Stream OpenRead(string path) =>
        new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

    public async Task FlushToDiskAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        await stream.FlushAsync(cancellationToken);
        if (stream is FileStream fileStream)
        {
            fileStream.Flush(flushToDisk: true);
        }
    }

    public void MoveDirectory(string sourcePath, string destinationPath)
    {
        Directory.Move(sourcePath, destinationPath);
    }

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern) =>
        Directory.EnumerateFiles(
            path,
            searchPattern,
            SearchOption.TopDirectoryOnly);

    public IEnumerable<string> EnumerateDirectories(string path) =>
        Directory.EnumerateDirectories(
            path,
            "*",
            SearchOption.TopDirectoryOnly);

    public void DeleteFile(string path)
    {
        File.Delete(path);
    }

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

internal sealed record RecoveryStorageDiagnostic(
    string Operation,
    Exception Exception);
