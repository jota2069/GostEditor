using GostEditor.Core.IO;

namespace GostEditor.Tests.Infrastructure;

public enum AtomicFileFailurePoint
{
    None,
    Create,
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

internal sealed class FaultInjectingAtomicFileSystem : IAtomicFileSystem
{
    private const int ErrorUnableToMoveReplacement = 1176;
    private const int ErrorUnableToMoveReplacement2 = 1177;

    private readonly PhysicalAtomicFileSystem _inner = new();
    private readonly AtomicFileFailurePoint _failurePoint;
    private readonly Action? _callback;
    private string? _lastTemporaryPath;
    private string? _lastRollbackPath;
    private int _closeAttempts;
    private int _commitAttempts;

    public FaultInjectingAtomicFileSystem(
        AtomicFileFailurePoint failurePoint,
        Action? callback = null)
    {
        _failurePoint = failurePoint;
        _callback = callback;
    }

    public string? LastTemporaryPath =>
        Volatile.Read(ref _lastTemporaryPath);

    public string? LastRollbackPath =>
        Volatile.Read(ref _lastRollbackPath);

    public int CloseAttempts => Volatile.Read(ref _closeAttempts);

    public int CommitAttempts => Volatile.Read(ref _commitAttempts);

    public Stream CreateTemporaryFile(string path)
    {
        ThrowIf(AtomicFileFailurePoint.Create);
        Volatile.Write(ref _lastTemporaryPath, path);
        return _inner.CreateTemporaryFile(path);
    }

    public async Task FlushToDiskAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ThrowIf(AtomicFileFailurePoint.Flush);
        await _inner.FlushToDiskAsync(stream, cancellationToken);
        if (_failurePoint == AtomicFileFailurePoint.CancelAfterFlush)
        {
            _callback?.Invoke();
        }
    }

    public async Task CloseTemporaryFileAsync(Stream stream)
    {
        int closeAttempt = Interlocked.Increment(ref _closeAttempts);
        if (_failurePoint == AtomicFileFailurePoint.Close && closeAttempt == 1)
        {
            throw new InjectedFileSystemIOException("Close");
        }

        await _inner.CloseTemporaryFileAsync(stream);
        if (_failurePoint == AtomicFileFailurePoint.CancelAfterClose)
        {
            _callback?.Invoke();
        }
    }

    public void Commit(
        string temporaryPath,
        string destinationPath,
        string rollbackPath)
    {
        Interlocked.Increment(ref _commitAttempts);
        Volatile.Write(ref _lastRollbackPath, rollbackPath);

        switch (_failurePoint)
        {
            case AtomicFileFailurePoint.CommitBeforeMutation:
                throw new InjectedFileSystemIOException("Commit");

            case AtomicFileFailurePoint.CancelDuringCommit:
                _callback?.Invoke();
                _inner.Commit(
                    temporaryPath,
                    destinationPath,
                    rollbackPath);
                return;

            case AtomicFileFailurePoint.Windows1176:
                throw new InjectedWin32IOException(
                    "ERROR_UNABLE_TO_MOVE_REPLACEMENT",
                    ErrorUnableToMoveReplacement);

            case AtomicFileFailurePoint.Windows1177:
            case AtomicFileFailurePoint.Windows1177AndRestoreUnexpected:
                File.Move(destinationPath, rollbackPath);
                throw new InjectedWin32IOException(
                    "ERROR_UNABLE_TO_MOVE_REPLACEMENT_2",
                    ErrorUnableToMoveReplacement2);

            case AtomicFileFailurePoint.FailAfterReplacement:
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
            AtomicFileFailurePoint.Windows1177AndRestoreUnexpected)
        {
            throw new InvalidOperationException(
                "unexpected rollback restoration failure");
        }

        _inner.RestoreRollback(rollbackPath, destinationPath);
    }

    public void Delete(string path)
    {
        if (_failurePoint == AtomicFileFailurePoint.DeleteUnexpected)
        {
            throw new InvalidOperationException(
                "unexpected cleanup failure");
        }

        _inner.Delete(path);
    }

    private void ThrowIf(AtomicFileFailurePoint point)
    {
        if (_failurePoint == point)
        {
            throw new InjectedFileSystemIOException(point.ToString());
        }
    }
}

internal sealed class InjectedFileSystemIOException : IOException
{
    public InjectedFileSystemIOException(string message)
        : base(message)
    {
    }
}

internal sealed class InjectedWin32IOException : IOException
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
