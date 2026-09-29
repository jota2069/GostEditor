using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GostEditor.UI.Services;

public enum PersistenceIoOperation
{
    ManualSave,
    SaveAs,
    Open,
    AutoSave,
    Export,
    RecoveryMaintenance
}

/// <summary>
/// Serializes document I/O operations that must observe and publish a
/// consistent document revision. Editing itself is intentionally not locked.
/// </summary>
public sealed class PersistenceIoCoordinator
{
    private readonly object _sync = new();
    private readonly LinkedList<OwnershipWaiter> _waiters = new();
    private CancellationTokenSource _lifecycleCancellation = new();
    private TaskCompletionSource<bool>? _idleCompletion;
    private Task? _suspensionCompletion;
    private bool _isOwned;
    private bool _isSuspended;
    private int _activeOperation = -1;

    public bool IsBusy => Volatile.Read(ref _activeOperation) >= 0;

    public bool IsSuspended
    {
        get
        {
            lock (_sync)
            {
                return _isSuspended;
            }
        }
    }

    public PersistenceIoOperation? ActiveOperation
    {
        get
        {
            int value = Volatile.Read(ref _activeOperation);
            return value < 0 ? null : (PersistenceIoOperation)value;
        }
    }

    internal ValueTask<PersistenceIoLease> AcquireAsync(
        PersistenceIoOperation operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_isSuspended)
            {
                return ValueTask.FromCanceled<PersistenceIoLease>(
                    new CancellationToken(canceled: true));
            }

            CancellationTokenSource operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _lifecycleCancellation.Token);

            if (!_isOwned && _waiters.Count == 0)
            {
                _isOwned = true;
                Volatile.Write(ref _activeOperation, (int)operation);
                return ValueTask.FromResult(
                    new PersistenceIoLease(
                        this,
                        operationCancellation));
            }

            OwnershipWaiter waiter = new(
                operation,
                operationCancellation);
            waiter.Node = _waiters.AddLast(waiter);
            waiter.CancellationRegistration =
                operationCancellation.Token.Register(
                static state =>
                {
                    CancellationState cancellationState =
                        (CancellationState)state!;
                    cancellationState.Coordinator.CancelWaiter(
                        cancellationState.Waiter);
                },
                new CancellationState(this, waiter));

            if (waiter.State == WaiterState.Cancelled)
            {
                waiter.DisposeCancellationResources();
            }

            return new ValueTask<PersistenceIoLease>(waiter.Task);
        }
    }

    internal bool TryAcquire(
        PersistenceIoOperation operation,
        CancellationToken cancellationToken,
        out PersistenceIoLease? lease)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_isSuspended || _isOwned || _waiters.Count > 0)
            {
                lease = null;
                return false;
            }

            _isOwned = true;
            Volatile.Write(ref _activeOperation, (int)operation);
            lease = new PersistenceIoLease(
                this,
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _lifecycleCancellation.Token));
            return true;
        }
    }

    public Task SuspendAndDrainAsync()
    {
        CancellationTokenSource cancellation;
        Task drainTask;
        TaskCompletionSource<bool> suspensionCompletion;

        lock (_sync)
        {
            if (_isSuspended)
            {
                return _suspensionCompletion
                    ?? throw new InvalidOperationException(
                        "Задача приостановки I/O не была создана.");
            }

            _isSuspended = true;

            if (!_isOwned && _waiters.Count == 0)
            {
                drainTask = Task.CompletedTask;
            }
            else
            {
                _idleCompletion ??= new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                drainTask = _idleCompletion.Task;
            }

            cancellation = _lifecycleCancellation;
            suspensionCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _suspensionCompletion = suspensionCompletion.Task;
        }

        Exception? cancellationFailure = null;
        try
        {
            cancellation.Cancel();
        }
        catch (Exception exception)
        {
            cancellationFailure = exception;
        }

        _ = CompleteSuspensionAsync(
            drainTask,
            suspensionCompletion,
            cancellationFailure);

        return suspensionCompletion.Task;
    }

    public void Resume()
    {
        CancellationTokenSource previousCancellation;

        lock (_sync)
        {
            if (!_isSuspended)
            {
                return;
            }

            if (_isOwned || _waiters.Count > 0)
            {
                throw new InvalidOperationException(
                    "Нельзя возобновить I/O до завершения всех операций.");
            }

            if (_suspensionCompletion?.IsCompleted != true)
            {
                throw new InvalidOperationException(
                    "Нельзя возобновить I/O до завершения отмены shutdown.");
            }

            previousCancellation = _lifecycleCancellation;
            _lifecycleCancellation = new CancellationTokenSource();
            _idleCompletion = null;
            _suspensionCompletion = null;
            _isSuspended = false;
        }

        previousCancellation.Dispose();
    }

    private void Release()
    {
        OwnershipWaiter? next = null;
        TaskCompletionSource<bool>? idleCompletion = null;

        lock (_sync)
        {
            while (!_isSuspended && _waiters.First is not null)
            {
                next = _waiters.First.Value;
                _waiters.RemoveFirst();
                next.Node = null;

                if (next.State == WaiterState.Waiting)
                {
                    next.State = WaiterState.Owned;
                    Volatile.Write(
                        ref _activeOperation,
                        (int)next.Operation);
                    break;
                }

                next = null;
            }

            if (next is null)
            {
                _isOwned = false;
                Volatile.Write(ref _activeOperation, -1);
                if (_isSuspended && _waiters.Count == 0)
                {
                    idleCompletion = _idleCompletion;
                }
            }
        }

        if (next is not null)
        {
            next.CancellationRegistration.Dispose();
            next.Completion.TrySetResult(
                new PersistenceIoLease(
                    this,
                    next.TakeCancellationSource()));
        }

        idleCompletion?.TrySetResult(true);
    }

    private void CancelWaiter(OwnershipWaiter waiter)
    {
        TaskCompletionSource<bool>? idleCompletion = null;

        lock (_sync)
        {
            if (waiter.State != WaiterState.Waiting)
            {
                return;
            }

            waiter.State = WaiterState.Cancelled;
            if (waiter.Node?.List is not null)
            {
                _waiters.Remove(waiter.Node);
                waiter.Node = null;
            }

            if (_isSuspended && !_isOwned && _waiters.Count == 0)
            {
                idleCompletion = _idleCompletion;
            }
        }

        waiter.Completion.TrySetCanceled(waiter.CancellationToken);
        waiter.DisposeCancellationSource();
        idleCompletion?.TrySetResult(true);
    }

    private static async Task CompleteSuspensionAsync(
        Task drainTask,
        TaskCompletionSource<bool> completion,
        Exception? cancellationFailure)
    {
        try
        {
            await drainTask;
            if (cancellationFailure is not null)
            {
                throw new InvalidOperationException(
                    "Одна из операций завершилась ошибкой при отмене shutdown.",
                    cancellationFailure);
            }

            completion.TrySetResult(true);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    internal sealed class PersistenceIoLease : IDisposable
    {
        private PersistenceIoCoordinator? _owner;
        private CancellationTokenSource? _cancellationSource;

        internal PersistenceIoLease(
            PersistenceIoCoordinator owner,
            CancellationTokenSource cancellationSource)
        {
            _owner = owner;
            _cancellationSource = cancellationSource;
        }

        public CancellationToken CancellationToken =>
            _cancellationSource?.Token
            ?? new CancellationToken(canceled: true);

        public void Dispose()
        {
            PersistenceIoCoordinator? owner =
                Interlocked.Exchange(ref _owner, null);
            CancellationTokenSource? cancellationSource =
                Interlocked.Exchange(ref _cancellationSource, null);

            try
            {
                owner?.Release();
            }
            finally
            {
                cancellationSource?.Dispose();
            }
        }
    }

    private sealed class OwnershipWaiter
    {
        internal OwnershipWaiter(
            PersistenceIoOperation operation,
            CancellationTokenSource cancellationSource)
        {
            Operation = operation;
            _cancellationSource = cancellationSource;
        }

        internal PersistenceIoOperation Operation { get; }

        internal CancellationToken CancellationToken =>
            _cancellationSource?.Token
            ?? new CancellationToken(canceled: true);

        private CancellationTokenSource? _cancellationSource;

        internal TaskCompletionSource<PersistenceIoLease> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task<PersistenceIoLease> Task => Completion.Task;

        internal LinkedListNode<OwnershipWaiter>? Node { get; set; }

        internal CancellationTokenRegistration CancellationRegistration
        {
            get;
            set;
        }

        internal WaiterState State { get; set; }

        internal CancellationTokenSource TakeCancellationSource() =>
            Interlocked.Exchange(ref _cancellationSource, null)
            ?? throw new InvalidOperationException(
                "Источник отмены ownership уже передан или освобождён.");

        internal void DisposeCancellationSource()
        {
            Interlocked.Exchange(ref _cancellationSource, null)?.Dispose();
        }

        internal void DisposeCancellationResources()
        {
            CancellationRegistration.Dispose();
            DisposeCancellationSource();
        }
    }

    private sealed record CancellationState(
        PersistenceIoCoordinator Coordinator,
        OwnershipWaiter Waiter);

    private enum WaiterState
    {
        Waiting,
        Owned,
        Cancelled
    }
}
