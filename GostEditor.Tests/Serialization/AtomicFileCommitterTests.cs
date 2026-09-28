using GostEditor.Core.IO;
using GostEditor.Tests.Infrastructure;

namespace GostEditor.Tests.Serialization;

public sealed class AtomicFileCommitterTests : IDisposable
{
    private const int ErrorUnableToMoveReplacement = 1176;
    private const int ErrorUnableToMoveReplacement2 = 1177;

    private readonly TestTemporaryDirectory _temporaryDirectory = new();

    [Fact]
    public async Task WriteAsync_WhenTemporaryCreateFails_PreservesExistingDestination()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(AtomicFileFailurePoint.Create);

        await Assert.ThrowsAsync<InjectedFileSystemIOException>(() =>
            committer.WriteAsync(destinationPath, WriteReplacementAsync));

        await AssertOriginalDestinationAndNoArtifactsAsync(destinationPath);
    }

    [Fact]
    public async Task WriteAsync_WhenWriterFailsAfterPartialWrite_PreservesExistingDestinationAndCleansTemp()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(AtomicFileFailurePoint.None);

        await Assert.ThrowsAsync<InjectedFileSystemIOException>(() =>
            committer.WriteAsync(
                destinationPath,
                async (stream, cancellationToken) =>
                {
                    await StreamFaultInjector.WriteThenThrowAsync(
                        stream,
                        "partial"u8.ToArray(),
                        new InjectedFileSystemIOException("write"),
                        cancellationToken);
                }));

        await AssertOriginalDestinationAndNoArtifactsAsync(destinationPath);
    }

    [Fact]
    public async Task WriteAsync_WhenDurableFlushFails_PreservesExistingDestinationAndCleansTemp()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        AtomicFileCommitter committer = CreateCommitter(AtomicFileFailurePoint.Flush);

        await Assert.ThrowsAsync<InjectedFileSystemIOException>(() =>
            committer.WriteAsync(destinationPath, WriteReplacementAsync));

        await AssertOriginalDestinationAndNoArtifactsAsync(destinationPath);
    }

    [Fact]
    public async Task WriteAsync_WhenCloseInitiallyFails_RetriesCloseAndCleansTemp()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        FaultInjectingAtomicFileSystem fileSystem = new(AtomicFileFailurePoint.Close);
        AtomicFileCommitter committer = new(fileSystem);

        await Assert.ThrowsAsync<InjectedFileSystemIOException>(() =>
            committer.WriteAsync(destinationPath, WriteReplacementAsync));

        Assert.Equal(2, fileSystem.CloseAttempts);
        await AssertOriginalDestinationAndNoArtifactsAsync(destinationPath);
    }

    [Fact]
    public async Task WriteAsync_WhenCommitFailsBeforeMutation_PreservesDestinationAndReplacementArtifact()
    {
        string destinationPath = await CreateExistingDestinationAsync();
        FaultInjectingAtomicFileSystem fileSystem = new(AtomicFileFailurePoint.CommitBeforeMutation);
        AtomicFileCommitter committer = new(fileSystem);

        await Assert.ThrowsAsync<InjectedFileSystemIOException>(() =>
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
            new FaultInjectingAtomicFileSystem(
                AtomicFileFailurePoint.CancelAfterFlush,
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
        FaultInjectingAtomicFileSystem fileSystem = new(
            AtomicFileFailurePoint.CancelAfterClose,
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
        FaultInjectingAtomicFileSystem fileSystem = new(
            AtomicFileFailurePoint.CancelDuringCommit,
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
            AtomicFileFailurePoint.DeleteUnexpected);
        InjectedFileSystemIOException primary = new("write");

        InjectedFileSystemIOException actual =
            await Assert.ThrowsAsync<InjectedFileSystemIOException>(() =>
                committer.WriteAsync(
                    destinationPath,
                    async (stream, cancellationToken) =>
                    {
                        await StreamFaultInjector.WriteThenThrowAsync(
                            stream,
                            "partial"u8.ToArray(),
                            primary,
                            cancellationToken);
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
            AtomicFileFailurePoint.DeleteUnexpected);

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
            AtomicFileFailurePoint.Windows1176);

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
            AtomicFileFailurePoint.Windows1177);

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
            AtomicFileFailurePoint.FailAfterReplacement);

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
            AtomicFileFailurePoint.Windows1177AndRestoreUnexpected);

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
    [InlineData(AtomicFileFailurePoint.Create, false, 0)]
    [InlineData(AtomicFileFailurePoint.None, true, 0)]
    [InlineData(AtomicFileFailurePoint.Flush, false, 0)]
    [InlineData(AtomicFileFailurePoint.Close, false, 0)]
    [InlineData(AtomicFileFailurePoint.CommitBeforeMutation, false, 1)]
    public async Task WriteAsync_WhenNewDestinationStageFails_DoesNotPublishPartialFile(
        AtomicFileFailurePoint failurePoint,
        bool writerFails,
        int expectedTemporaryArtifacts)
    {
        string destinationPath =
            _temporaryDirectory.GetPath("new-document.gost");
        AtomicFileCommitter committer = CreateCommitter(failurePoint);

        await Assert.ThrowsAsync<InjectedFileSystemIOException>(() =>
            committer.WriteAsync(
                destinationPath,
                writerFails
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
        AtomicFileCommitter committer = CreateCommitter(AtomicFileFailurePoint.None);

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
        FaultInjectingAtomicFileSystem fileSystem = new(AtomicFileFailurePoint.None);
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
        string destinationPath =
            _temporaryDirectory.GetPath("new-document.gost");
        AtomicFileCommitter committer = CreateCommitter(AtomicFileFailurePoint.None);

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
        AtomicFileCommitter committer = CreateCommitter(AtomicFileFailurePoint.None);

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
        _temporaryDirectory.Dispose();
    }

    private AtomicFileCommitter CreateCommitter(AtomicFileFailurePoint failurePoint) =>
        new(new FaultInjectingAtomicFileSystem(failurePoint));

    private async Task<string> CreateExistingDestinationAsync()
    {
        string destinationPath =
            _temporaryDirectory.GetPath("document.gost");
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
        await StreamFaultInjector.WriteThenThrowAsync(
            stream,
            "partial"u8.ToArray(),
            new InjectedFileSystemIOException("Write"),
            cancellationToken);
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

}
