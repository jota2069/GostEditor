namespace GostEditor.Tests.Infrastructure;

internal sealed class TestTemporaryDirectory : IDisposable
{
    private readonly object _disposeSync = new();
    private readonly Action<string> _deleteDirectory;
    private int _disposed;

    public TestTemporaryDirectory()
        : this(path => Directory.Delete(path, recursive: true))
    {
    }

    internal TestTemporaryDirectory(Action<string> deleteDirectory)
    {
        _deleteDirectory = deleteDirectory
            ?? throw new ArgumentNullException(nameof(deleteDirectory));

        string testRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "GostEditor.Tests"));

        DirectoryPath = Path.Combine(
            testRoot,
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string GetPath(params string[] relativeSegments)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(relativeSegments);

        string candidate = DirectoryPath;
        foreach (string segment in relativeSegments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(segment);
            if (Path.IsPathRooted(segment))
            {
                throw new ArgumentException(
                    "Temporary paths must be relative.",
                    nameof(relativeSegments));
            }

            candidate = Path.Combine(candidate, segment);
        }

        string fullPath = Path.GetFullPath(candidate);
        string relativePath = Path.GetRelativePath(DirectoryPath, fullPath);
        if (relativePath == ".." ||
            relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException(
                "Temporary paths must stay inside the owned directory.",
                nameof(relativeSegments));
        }

        return fullPath;
    }

    public void Dispose()
    {
        lock (_disposeSync)
        {
            if (_disposed != 0)
            {
                return;
            }

            if (Directory.Exists(DirectoryPath))
            {
                _deleteDirectory(DirectoryPath);
            }

            Volatile.Write(ref _disposed, 1);
        }
    }
}
