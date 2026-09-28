using GostEditor.Core.IO;

namespace GostEditor.Tests.Serialization;

public sealed class AtomicFileCommitterTests : IDisposable
{
    private const int ErrorUnableToMoveReplacement = 1176;
    private const int ErrorUnableToMoveReplacement2 = 1177;

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "GostEditor.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteAsync_WhenTemporaryCreateFails_PreservesExistingDestination()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(FailurePoint.Create);

        await Assert.ThrowsAsync<InjectedIOException>(() =>
            committer.WriteAsync(destinationPath, WriteReplacementAsync));

        await AssertOriginalDestinationAndNoArtifactsAsync(destinationPath);
    }

    [Fact]
    public async Task WriteAsync_WhenWriterFailsAfterPartialWrite_PreservesExistingDestinationAndCleansTemp()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(FailurePoint.None);

        await Assert.ThrowsAsync<InjectedIOException>(() =>
            committer.WriteAsync(
                destinationPath,
                async (stream, cancellationToken) =>
                {
                    await stream.WriteAsync(
                        "partial"u8.ToArray(),
                        cancellationToken);
                    throw new InjectedIOException("write");
                }));

        await AssertOriginalDestinationAndNoArtifactsAsync(destinationPath);
    }

    [Fact]
    public async Task WriteAsync_WhenDurableFlushFails_PreservesExistingDestinationAndCleansTemp()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(FailurePoint.Flush);

        await Assert.ThrowsAsync<InjectedIOException>(() =>
            committer.WriteAsync(destinationPath, WriteReplacementAsync));

        await AssertOriginalDestinationAndNoArtifactsAsync(destinationPath);
    }

    [Fact]
    public async Task WriteAsync_WhenCloseInitiallyFails_RetriesCloseAndCleansTemp()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        FaultInjectingFileSystem fileSystem = new(FailurePoint.Close);
        AtomicFileCommitter committer = new(fileSystem);

        await Assert.ThrowsAsync<InjectedIOException>(() =>
            committer.WriteAsync(destinationPath, WriteReplacementAsync));

        Assert.Equal(2, fileSystem.CloseAttempts);
        await AssertOriginalDestinationAndNoArtifactsAsync(destinationPath);
    }

    [Fact]
    public async Task WriteAsync_WhenCommitFailsBeforeMutation_PreservesDestinationAndReplacementArtifact()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        FaultInjectingFileSystem fileSystem = new(FailurePoint.CommitBeforeMutation);
        AtomicFileCommitter committer = new(fileSystem);

        await Assert.ThrowsAsync<InjectedIOException>(() =>
            committer.WriteAsync(destinationPath, WriteReplacementAsync));

        Assert.Equal(
            "original"u8.ToArray(),
            await File.ReadAllBytesAsync(destinationPath));
        await AssertSingleArtifactContainsAsync(
            GetTemporaryFiles(destinationPath),
            "replacement");
        Assert.Empty(GetRollbackFiles(destinationPath));
    }

    [Fact]
    public async Task WriteAsync_WhenCancelledAfterFlush_PreservesExistingDestinationAndCleansTemp()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        using CancellationTokenSource cancellation = new();
        AtomicFileCommitter committer = new(
            new FaultInjectingFileSystem(
                FailurePoint.CancelAfterFlush,
                cancellation.Cancel));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            committer.WriteAsync(
                destinationPath,
                WriteReplacementAsync,
                cancellation.Token));

        await AssertOriginalDestinationAndNoArtifactsAsync(destinationPath);
    }

    [Fact]
    public async Task WriteAsync_WhenCancelledImmediatelyBeforeCommit_DoesNotPublishReplacement()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        using CancellationTokenSource cancellation = new();
        FaultInjectingFileSystem fileSystem = new(
            FailurePoint.CancelAfterClose,
            cancellation.Cancel);
        AtomicFileCommitter committer = new(fileSystem);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            committer.WriteAsync(
                destinationPath,
                WriteReplacementAsync,
                cancellation.Token));

        Assert.Equal(0, fileSystem.CommitAttempts);
        await AssertOriginalDestinationAndNoArtifactsAsync(destinationPath);
    }

    [Fact]
    public async Task WriteAsync_WhenCancelledAfterCommitStarts_ReportsSuccessfulCommit()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        using CancellationTokenSource cancellation = new();
        FaultInjectingFileSystem fileSystem = new(
            FailurePoint.CancelDuringCommit,
            cancellation.Cancel);
        AtomicFileCommitter committer = new(fileSystem);

        await committer.WriteAsync(
            destinationPath,
            WriteReplacementAsync,
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(
            "replacement"u8.ToArray(),
            await File.ReadAllBytesAsync(destinationPath));
        Assert.Empty(GetTemporaryFiles(destinationPath));
        Assert.Empty(GetRollbackFiles(destinationPath));
    }

    [Fact]
    public async Task WriteAsync_WhenUnexpectedCleanupFails_PreservesPrimaryFailureAndRecordsDiagnostic()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(
            FailurePoint.DeleteUnexpected);
        InjectedIOException primary = new("write");

        InjectedIOException actual =
            await Assert.ThrowsAsync<InjectedIOException>(() =>
                committer.WriteAsync(
                    destinationPath,
                    async (stream, cancellationToken) =>
                    {
                        await stream.WriteAsync(
                            "partial"u8.ToArray(),
                            cancellationToken);
                        throw primary;
                    }));

        Assert.Same(primary, actual);
        AtomicFileCommitDiagnostic diagnostic = Assert.Single(
            GetSecondaryDiagnostics(actual));
        Assert.Equal("temporary-file cleanup", diagnostic.Operation);
        Assert.IsType<InvalidOperationException>(diagnostic.Exception);
        Assert.Equal(
            "original"u8.ToArray(),
            await File.ReadAllBytesAsync(destinationPath));
        Assert.Single(GetTemporaryFiles(destinationPath));
    }

    [Fact]
    public async Task WriteAsync_WhenPostCommitCleanupFails_ReportsSuccessAndPreservesRollback()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(
            FailurePoint.DeleteUnexpected);

        await committer.WriteAsync(destinationPath, WriteReplacementAsync);

        Assert.Equal(
            "replacement"u8.ToArray(),
            await File.ReadAllBytesAsync(destinationPath));
        Assert.Empty(GetTemporaryFiles(destinationPath));
        await AssertSingleArtifactContainsAsync(
            GetRollbackFiles(destinationPath),
            "original");
    }

    [Fact]
    public async Task WriteAsync_WhenWindows1176Occurs_PreservesOriginalAndReplacement()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(
            FailurePoint.Windows1176);

        InjectedWin32IOException exception =
            await Assert.ThrowsAsync<InjectedWin32IOException>(() =>
                committer.WriteAsync(destinationPath, WriteReplacementAsync));

        Assert.Equal(ErrorUnableToMoveReplacement, exception.Win32ErrorCode);
        Assert.Equal(
            "original"u8.ToArray(),
            await File.ReadAllBytesAsync(destinationPath));
        await AssertSingleArtifactContainsAsync(
            GetTemporaryFiles(destinationPath),
            "replacement");
        Assert.Empty(GetRollbackFiles(destinationPath));
        Assert.Equal(
            new AtomicCommitArtifactState(
                AtomicFileEntryState.File,
                AtomicFileEntryState.File,
                AtomicFileEntryState.Missing),
            GetArtifactState(exception));
    }

    [Fact]
    public async Task WriteAsync_WhenWindows1177Occurs_RestoresOriginalAndPreservesReplacement()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(
            FailurePoint.Windows1177);

        InjectedWin32IOException exception =
            await Assert.ThrowsAsync<InjectedWin32IOException>(() =>
                committer.WriteAsync(destinationPath, WriteReplacementAsync));

        Assert.Equal(ErrorUnableToMoveReplacement2, exception.Win32ErrorCode);
        Assert.Equal(
            "original"u8.ToArray(),
            await File.ReadAllBytesAsync(destinationPath));
        await AssertSingleArtifactContainsAsync(
            GetTemporaryFiles(destinationPath),
            "replacement");
        Assert.Empty(GetRollbackFiles(destinationPath));
        Assert.Equal(
            new AtomicCommitArtifactState(
                AtomicFileEntryState.File,
                AtomicFileEntryState.File,
                AtomicFileEntryState.Missing),
            GetArtifactState(exception));
    }

    [Fact]
    public async Task WriteAsync_WhenReplaceMutatesThenFails_PreservesNewDestinationAndOldRollback()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(
            FailurePoint.FailAfterReplacement);

        await Assert.ThrowsAsync<InjectedWin32IOException>(() =>
            committer.WriteAsync(destinationPath, WriteReplacementAsync));

        Assert.Equal(
            "replacement"u8.ToArray(),
            await File.ReadAllBytesAsync(destinationPath));
        Assert.Empty(GetTemporaryFiles(destinationPath));
        await AssertSingleArtifactContainsAsync(
            GetRollbackFiles(destinationPath),
            "original");
    }

    [Fact]
    public async Task WriteAsync_WhenRollbackRestorationFails_PreservesPrimaryAndBothArtifacts()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(
            FailurePoint.Windows1177AndRestoreUnexpected);

        InjectedWin32IOException exception =
            await Assert.ThrowsAsync<InjectedWin32IOException>(() =>
                committer.WriteAsync(destinationPath, WriteReplacementAsync));

        Assert.Equal(ErrorUnableToMoveReplacement2, exception.Win32ErrorCode);
        AtomicFileCommitDiagnostic diagnostic = Assert.Single(
            GetSecondaryDiagnostics(exception));
        Assert.Equal("rollback restoration", diagnostic.Operation);
        Assert.IsType<InvalidOperationException>(diagnostic.Exception);
        Assert.False(File.Exists(destinationPath));
        await AssertSingleArtifactContainsAsync(
            GetTemporaryFiles(destinationPath),
            "replacement");
        await AssertSingleArtifactContainsAsync(
            GetRollbackFiles(destinationPath),
            "original");
    }

    [Theory]
    [InlineData(FailurePoint.Create, 0)]
    [InlineData(FailurePoint.Write, 0)]
    [InlineData(FailurePoint.Flush, 0)]
    [InlineData(FailurePoint.Close, 0)]
    [InlineData(FailurePoint.CommitBeforeMutation, 1)]
    public async Task WriteAsync_WhenNewDestinationStageFails_DoesNotPublishPartialFile(
        FailurePoint failurePoint,
        int expectedTemporaryArtifacts)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string destinationPath = Path.Combine(
            _temporaryDirectory,
            "new-document.gost");
        AtomicFileCommitter committer = CreateCommitter(failurePoint);

        await Assert.ThrowsAsync<InjectedIOException>(() =>
            committer.WriteAsync(
                destinationPath,
                failurePoint == FailurePoint.Write
                    ? WriteThenFailAsync
                    : WriteReplacementAsync));

        Assert.False(File.Exists(destinationPath));
        Assert.Equal(
            expectedTemporaryArtifacts,
            GetTemporaryFiles(destinationPath).Length);
        Assert.Empty(GetRollbackFiles(destinationPath));
    }

    [Fact]
    public async Task WriteAsync_WhenSuccessful_AtomicallyReplacesDestination()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(FailurePoint.None);

        await committer.WriteAsync(destinationPath, WriteReplacementAsync);

        Assert.Equal(
            "replacement"u8.ToArray(),
            await File.ReadAllBytesAsync(destinationPath));
        Assert.Empty(GetTemporaryFiles(destinationPath));
        Assert.Empty(GetRollbackFiles(destinationPath));
    }

    [Fact]
    public async Task WriteAsync_CreatesTransactionArtifactsInDestinationDirectory()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        FaultInjectingFileSystem fileSystem = new(FailurePoint.None);
        AtomicFileCommitter committer = new(fileSystem);

        await committer.WriteAsync(destinationPath, WriteReplacementAsync);

        Assert.NotNull(fileSystem.LastTemporaryPath);
        Assert.NotNull(fileSystem.LastRollbackPath);
        string expectedDirectory =
            Path.GetDirectoryName(Path.GetFullPath(destinationPath))!;
        Assert.Equal(
            expectedDirectory,
            Path.GetDirectoryName(fileSystem.LastTemporaryPath));
        Assert.Equal(
            expectedDirectory,
            Path.GetDirectoryName(fileSystem.LastRollbackPath));
        Assert.EndsWith(
            ".tmp",
            fileSystem.LastTemporaryPath,
            StringComparison.Ordinal);
        Assert.EndsWith(
            ".rollback",
            fileSystem.LastRollbackPath,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WhenDestinationDoesNotExist_CreatesCommittedFile()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string destinationPath = Path.Combine(
            _temporaryDirectory,
            "new-document.gost");
        AtomicFileCommitter committer = CreateCommitter(FailurePoint.None);

        await committer.WriteAsync(destinationPath, WriteReplacementAsync);

        Assert.Equal(
            "replacement"u8.ToArray(),
            await File.ReadAllBytesAsync(destinationPath));
        Assert.Empty(GetTemporaryFiles(destinationPath));
        Assert.Empty(GetRollbackFiles(destinationPath));
    }

    [Fact]
    public async Task WriteAsync_OnUnix_PreservesExistingDestinationMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string destinationPath = await CreateExistingDestinationAsync();
        UnixFileMode expectedMode =
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.GroupRead;
        File.SetUnixFileMode(destinationPath, expectedMode);
        AtomicFileCommitter committer = CreateCommitter(FailurePoint.None);

        await committer.WriteAsync(destinationPath, WriteReplacementAsync);

        Assert.Equal(expectedMode, File.GetUnixFileMode(destinationPath));
    }

    [Fact]
    public async Task WriteAsync_OnWindows_ReplacesExistingDestinationAndRemovesRollback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = new();

        await committer.WriteAsync(destinationPath, WriteReplacementAsync);

        Assert.Equal(
            "replacement"u8.ToArray(),
            await File.ReadAllBytesAsync(destinationPath));
        Assert.Empty(GetTemporaryFiles(destinationPath));
        Assert.Empty(GetRollbackFiles(destinationPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private AtomicFileCommitter CreateCommitter(FailurePoint failurePoint) =>
        new(new FaultInjectingFileSystem(failurePoint));

    private async Task<string> CreateExistingDestinationAsync()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string destinationPath = Path.Combine(
            _temporaryDirectory,
            "document.gost");
        await File.WriteAllBytesAsync(
            destinationPath,
            "original"u8.ToArray());
        return destinationPath;
    }

    private static async Task WriteReplacementAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(
            "replacement"u8.ToArray(),
            cancellationToken);
    }

    private static async Task WriteThenFailAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(
            "partial"u8.ToArray(),
            cancellationToken);
        throw new InjectedIOException("Write");
    }

    private static async Task AssertOriginalDestinationAndNoArtifactsAsync(
        string destinationPath)
    {
        Assert.Equal(
            "original"u8.ToArray(),
            await File.ReadAllBytesAsync(destinationPath));
        Assert.Empty(GetTemporaryFiles(destinationPath));
        Assert.Empty(GetRollbackFiles(destinationPath));
    }

    private static async Task AssertSingleArtifactContainsAsync(
        string[] paths,
        string expectedContents)
    {
        string path = Assert.Single(paths);
        Assert.Equal(
            System.Text.Encoding.UTF8.GetBytes(expectedContents),
            await File.ReadAllBytesAsync(path));
    }

    private static string[] GetTemporaryFiles(string destinationPath) =>
        GetArtifacts(destinationPath, "tmp");

    private static string[] GetRollbackFiles(string destinationPath) =>
        GetArtifacts(destinationPath, "rollback");

    private static string[] GetArtifacts(
        string destinationPath,
        string extension)
    {
        string directoryPath = Path.GetDirectoryName(destinationPath)!;
        string pattern =
            $"{Path.GetFileName(destinationPath)}.*.{extension}";
        return Directory.GetFiles(directoryPath, pattern);
    }

    private static AtomicCommitArtifactState GetArtifactState(
        Exception exception) =>
        Assert.IsType<AtomicCommitArtifactState>(
            exception.Data[AtomicFileCommitter.ArtifactStateDataKey]);

    private static IReadOnlyList<AtomicFileCommitDiagnostic>
        GetSecondaryDiagnostics(Exception exception) =>
        Assert.IsType<List<AtomicFileCommitDiagnostic>>(
            exception.Data[
                AtomicFileCommitter.SecondaryDiagnosticsDataKey]);

    public enum FailurePoint
    {
        None,
        Create,
        Write,
        Flush,
        Close,
        CommitBeforeMutation,
        CancelAfterFlush,
        CancelAfterClose,
        CancelDuringCommit,
        DeleteUnexpected,
        Windows1176,
        Windows1177,
        Windows1177AndRestoreUnexpected,
        FailAfterReplacement
    }

    private sealed class FaultInjectingFileSystem : IAtomicFileSystem
    {
        private readonly PhysicalAtomicFileSystem _inner = new();
        private readonly FailurePoint _failurePoint;
        private readonly Action? _callback;

        public FaultInjectingFileSystem(
            FailurePoint failurePoint,
            Action? callback = null)
        {
            _failurePoint = failurePoint;
            _callback = callback;
        }

        public string? LastTemporaryPath { get; private set; }

        public string? LastRollbackPath { get; private set; }

        public int CloseAttempts { get; private set; }

        public int CommitAttempts { get; private set; }

        public Stream CreateTemporaryFile(string path)
        {
            ThrowIf(FailurePoint.Create);
            LastTemporaryPath = path;
            return _inner.CreateTemporaryFile(path);
        }

        public async Task FlushToDiskAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            ThrowIf(FailurePoint.Flush);
            await _inner.FlushToDiskAsync(stream, cancellationToken);
            if (_failurePoint == FailurePoint.CancelAfterFlush)
            {
                _callback?.Invoke();
            }
        }

        public async Task CloseTemporaryFileAsync(Stream stream)
        {
            CloseAttempts++;
            if (_failurePoint == FailurePoint.Close && CloseAttempts == 1)
            {
                throw new InjectedIOException("Close");
            }

            await _inner.CloseTemporaryFileAsync(stream);
            if (_failurePoint == FailurePoint.CancelAfterClose)
            {
                _callback?.Invoke();
            }
        }

        public void Commit(
            string temporaryPath,
            string destinationPath,
            string rollbackPath)
        {
            CommitAttempts++;
            LastRollbackPath = rollbackPath;

            switch (_failurePoint)
            {
                case FailurePoint.CommitBeforeMutation:
                    throw new InjectedIOException("Commit");

                case FailurePoint.CancelDuringCommit:
                    _callback?.Invoke();
                    _inner.Commit(
                        temporaryPath,
                        destinationPath,
                        rollbackPath);
                    return;

                case FailurePoint.Windows1176:
                    throw new InjectedWin32IOException(
                        "ERROR_UNABLE_TO_MOVE_REPLACEMENT",
                        ErrorUnableToMoveReplacement);

                case FailurePoint.Windows1177:
                case FailurePoint.Windows1177AndRestoreUnexpected:
                    File.Move(destinationPath, rollbackPath);
                    throw new InjectedWin32IOException(
                        "ERROR_UNABLE_TO_MOVE_REPLACEMENT_2",
                        ErrorUnableToMoveReplacement2);

                case FailurePoint.FailAfterReplacement:
                    _inner.Commit(
                        temporaryPath,
                        destinationPath,
                        rollbackPath);
                    throw new InjectedWin32IOException(
                        "failure after replacement",
                        ErrorUnableToMoveReplacement2);

                default:
                    _inner.Commit(
                        temporaryPath,
                        destinationPath,
                        rollbackPath);
                    return;
            }
        }

        public AtomicFileEntryState GetEntryState(string path) =>
            _inner.GetEntryState(path);

        public void RestoreRollback(
            string rollbackPath,
            string destinationPath)
        {
            if (_failurePoint ==
                FailurePoint.Windows1177AndRestoreUnexpected)
            {
                throw new InvalidOperationException(
                    "unexpected rollback restoration failure");
            }

            _inner.RestoreRollback(rollbackPath, destinationPath);
        }

        public void Delete(string path)
        {
            if (_failurePoint == FailurePoint.DeleteUnexpected)
            {
                throw new InvalidOperationException(
                    "unexpected cleanup failure");
            }

            _inner.Delete(path);
        }

        private void ThrowIf(FailurePoint point)
        {
            if (_failurePoint == point)
            {
                throw new InjectedIOException(point.ToString());
            }
        }
    }

    private sealed class InjectedIOException : IOException
    {
        public InjectedIOException(string message)
            : base(message)
        {
        }
    }

    private sealed class InjectedWin32IOException : IOException
    {
        public InjectedWin32IOException(
            string message,
            int win32ErrorCode)
            : base(message)
        {
            Win32ErrorCode = win32ErrorCode;
            HResult = unchecked((int)(0x80070000u | (uint)win32ErrorCode));
        }

        public int Win32ErrorCode { get; }
    }
}
