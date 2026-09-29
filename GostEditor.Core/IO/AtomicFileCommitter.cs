using System.Diagnostics;

namespace GostEditor.Core.IO;

public interface IAtomicFileCommitter
{
    Task WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken = default);
}

internal enum AtomicFileEntryState
{
    Missing,
    File,
    Other,
    Unknown
}

internal sealed record AtomicCommitArtifactState(
    AtomicFileEntryState Destination,
    AtomicFileEntryState Replacement,
    AtomicFileEntryState Rollback);

internal sealed record AtomicFileCommitDiagnostic(
    string Operation,
    Exception Exception);

internal interface IAtomicFileSystem
{
    Stream CreateTemporaryFile(string path);

    Task FlushToDiskAsync(
        Stream stream,
        CancellationToken cancellationToken);

    Task CloseTemporaryFileAsync(Stream stream);

    void Commit(
        string temporaryPath,
        string destinationPath,
        string rollbackPath);

    AtomicFileEntryState GetEntryState(string path);

    void RestoreRollback(string rollbackPath, string destinationPath);

    void Delete(string path);
}

public sealed class AtomicFileCommitter : IAtomicFileCommitter
{
    internal const string ArtifactStateDataKey =
        "GostEditor.AtomicFileCommit.ArtifactState";
    internal const string SecondaryDiagnosticsDataKey =
        "GostEditor.AtomicFileCommit.SecondaryDiagnostics";

    private readonly IAtomicFileSystem _fileSystem;

    public AtomicFileCommitter()
        : this(new PhysicalAtomicFileSystem())
    {
    }

    internal AtomicFileCommitter(IAtomicFileSystem fileSystem)
    {
        _fileSystem = fileSystem
            ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(writeAsync);

        string fullDestinationPath = Path.GetFullPath(destinationPath);
        string? directoryPath = Path.GetDirectoryName(fullDestinationPath);
        if (string.IsNullOrEmpty(directoryPath))
        {
            throw new ArgumentException(
                "Не удалось определить каталог файла назначения.",
                nameof(destinationPath));
        }

        string transactionId = Guid.NewGuid().ToString("N");
        string fileName = Path.GetFileName(fullDestinationPath);
        string temporaryPath = Path.Combine(
            directoryPath,
            $"{fileName}.{transactionId}.tmp");
        string rollbackPath = Path.Combine(
            directoryPath,
            $"{fileName}.{transactionId}.rollback");

        Stream? temporaryStream = null;
        bool commitStarted = false;
        bool committed = false;
        Exception? primaryException = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            temporaryStream = _fileSystem.CreateTemporaryFile(temporaryPath);

            await writeAsync(temporaryStream, cancellationToken);
            await _fileSystem.FlushToDiskAsync(
                temporaryStream,
                cancellationToken);

            await _fileSystem.CloseTemporaryFileAsync(temporaryStream);
            temporaryStream = null;

            // This is the cancellation commit point. Once Commit starts, its
            // result wins over a later cancellation request.
            cancellationToken.ThrowIfCancellationRequested();
            commitStarted = true;
            _fileSystem.Commit(
                temporaryPath,
                fullDestinationPath,
                rollbackPath);
            committed = true;
        }
        catch (Exception exception)
        {
            primaryException = exception;
            if (commitStarted)
            {
                ReconcileFailedCommit(
                    temporaryPath,
                    fullDestinationPath,
                    rollbackPath,
                    exception);
            }

            throw;
        }
        finally
        {
            if (temporaryStream is not null)
            {
                await TryCloseTemporaryFileAsync(
                    temporaryStream,
                    primaryException);
            }

            if (!commitStarted)
            {
                TryDeleteArtifact(
                    temporaryPath,
                    primaryException,
                    "temporary-file cleanup");
            }
            else if (committed)
            {
                TryDeleteArtifact(
                    rollbackPath,
                    primaryException: null,
                    "post-commit rollback cleanup");
            }
        }
    }

    private async Task TryCloseTemporaryFileAsync(
        Stream temporaryStream,
        Exception? primaryException)
    {
        try
        {
            await _fileSystem.CloseTemporaryFileAsync(temporaryStream);
        }
        catch (Exception exception)
        {
            RecordSecondaryException(
                primaryException,
                "temporary-file close retry",
                exception);
        }
    }

    private void ReconcileFailedCommit(
        string temporaryPath,
        string destinationPath,
        string rollbackPath,
        Exception primaryException)
    {
        AtomicCommitArtifactState state = InspectArtifactState(
            temporaryPath,
            destinationPath,
            rollbackPath,
            primaryException);
        TryRecordArtifactState(primaryException, state);

        if (state.Destination != AtomicFileEntryState.Missing ||
            state.Rollback != AtomicFileEntryState.File)
        {
            return;
        }

        try
        {
            _fileSystem.RestoreRollback(rollbackPath, destinationPath);
        }
        catch (Exception exception)
        {
            RecordSecondaryException(
                primaryException,
                "rollback restoration",
                exception);
        }

        AtomicCommitArtifactState reconciledState = InspectArtifactState(
            temporaryPath,
            destinationPath,
            rollbackPath,
            primaryException);
        TryRecordArtifactState(primaryException, reconciledState);
    }

    private AtomicCommitArtifactState InspectArtifactState(
        string temporaryPath,
        string destinationPath,
        string rollbackPath,
        Exception primaryException) =>
        new(
            InspectArtifact(
                destinationPath,
                "destination state inspection",
                primaryException),
            InspectArtifact(
                temporaryPath,
                "replacement state inspection",
                primaryException),
            InspectArtifact(
                rollbackPath,
                "rollback state inspection",
                primaryException));

    private AtomicFileEntryState InspectArtifact(
        string path,
        string operation,
        Exception primaryException)
    {
        try
        {
            return _fileSystem.GetEntryState(path);
        }
        catch (Exception exception)
        {
            RecordSecondaryException(primaryException, operation, exception);
            return AtomicFileEntryState.Unknown;
        }
    }

    private void TryDeleteArtifact(
        string path,
        Exception? primaryException,
        string operation)
    {
        try
        {
            _fileSystem.Delete(path);
        }
        catch (Exception exception)
        {
            RecordSecondaryException(primaryException, operation, exception);
        }
    }

    private static void RecordSecondaryException(
        Exception? primaryException,
        string operation,
        Exception secondaryException)
    {
        if (primaryException is null)
        {
            try
            {
                Trace.TraceWarning(
                    "Atomic file commit {0} failed: {1}",
                    operation,
                    secondaryException);
            }
            catch
            {
                // A diagnostic listener must not turn a successful commit into
                // a reported save failure.
            }

            return;
        }

        try
        {
            if (primaryException.Data[SecondaryDiagnosticsDataKey]
                is not List<AtomicFileCommitDiagnostic> diagnostics)
            {
                diagnostics = [];
                primaryException.Data[SecondaryDiagnosticsDataKey] = diagnostics;
            }

            diagnostics.Add(new AtomicFileCommitDiagnostic(
                operation,
                secondaryException));
        }
        catch
        {
            // Diagnostic recording must never replace the primary failure.
        }
    }

    private static void TryRecordArtifactState(
        Exception primaryException,
        AtomicCommitArtifactState state)
    {
        try
        {
            primaryException.Data[ArtifactStateDataKey] = state;
        }
        catch
        {
            // Diagnostic recording must never replace the primary failure.
        }
    }
}

internal sealed class PhysicalAtomicFileSystem : IAtomicFileSystem
{
    public Stream CreateTemporaryFile(string path) =>
        new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 4096,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough
            });

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

    public Task CloseTemporaryFileAsync(Stream stream) =>
        stream.DisposeAsync().AsTask();

    public void Commit(
        string temporaryPath,
        string destinationPath,
        string rollbackPath)
    {
        if (File.Exists(destinationPath))
        {
            PreserveUnixFileMode(temporaryPath, destinationPath);
            File.Replace(
                temporaryPath,
                destinationPath,
                rollbackPath,
                ignoreMetadataErrors: true);
            return;
        }

        File.Move(temporaryPath, destinationPath);
    }

    public AtomicFileEntryState GetEntryState(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Directory)
                ? AtomicFileEntryState.Other
                : AtomicFileEntryState.File;
        }
        catch (FileNotFoundException)
        {
            return AtomicFileEntryState.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return AtomicFileEntryState.Missing;
        }
    }

    public void RestoreRollback(
        string rollbackPath,
        string destinationPath) =>
        File.Move(rollbackPath, destinationPath);

    public void Delete(string path) => File.Delete(path);

    private static void PreserveUnixFileMode(
        string temporaryPath,
        string destinationPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        UnixFileMode destinationMode = File.GetUnixFileMode(destinationPath);
        File.SetUnixFileMode(temporaryPath, destinationMode);
    }
}
