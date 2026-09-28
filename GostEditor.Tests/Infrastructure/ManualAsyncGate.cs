namespace GostEditor.Tests.Infrastructure;

internal sealed class ManualAsyncGate
{
    private readonly TaskCompletionSource<bool> _reached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitUntilReachedAsync(CancellationToken cancellationToken = default) =>
        _reached.Task.WaitAsync(cancellationToken);

    public async Task SignalAndWaitAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_reached.TrySetResult(true))
        {
            throw new InvalidOperationException(
                "ManualAsyncGate is a one-shot synchronization point.");
        }

        await _release.Task.WaitAsync(cancellationToken);
    }

    public void Release()
    {
        _release.TrySetResult(true);
    }
}
