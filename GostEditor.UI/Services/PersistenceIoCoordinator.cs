using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GostEditor.UI.Services;

public enum PersistenceIoOperation
{
    ManualSave,
    SaveAs,
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
    private bool _isOwned;
    private int _activeOperation = -1;

    public bool IsBusy => Volatile.Read(ref _activeOperation) >= 0;

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
            if (!_isOwned && _waiters.Count == 0)
            {
                _isOwned = true;
                Volatile.Write(ref _activeOperation, (int)operation);
                return ValueTask.FromResult(new PersistenceIoLease(this));
            }

            OwnershipWaiter waiter = new(operation, cancellationToken);
            waiter.Node = _waiters.AddLast(waiter);
            waiter.CancellationRegistration = cancellationToken.Register(
                static state =>
                {
                    CancellationState cancellationState =
                        (CancellationState)state!;
                    cancellationState.Coordinator.CancelWaiter(
                        cancellationState.Waiter);
                },
                new CancellationState(this, waiter));

            return new ValueTask<PersistenceIoLease>(waiter.Task);
        }
    }

    internal bool TryAcquire(
        PersistenceIoOperation operation,
        out PersistenceIoLease? lease)
    {
        lock (_sync)
        {
            if (_isOwned || _waiters.Count > 0)
            {
                lease = null;
                return false;
            }

            _isOwned = true;
            Volatile.Write(ref _activeOperation, (int)operation);
            lease = new PersistenceIoLease(this);
            return true;
        }
    }

    private void Release()
    {
        OwnershipWaiter? next = null;

        lock (_sync)
        {
            while (_waiters.First is not null)
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
            }
        }

        if (next is not null)
        {
            next.CancellationRegistration.Dispose();
            next.Completion.TrySetResult(new PersistenceIoLease(this));
        }
    }

    private void CancelWaiter(OwnershipWaiter waiter)
    {
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
        }

        waiter.Completion.TrySetCanceled(waiter.CancellationToken);
    }

    internal sealed class PersistenceIoLease : IDisposable
    {
        private PersistenceIoCoordinator? _owner;

        internal PersistenceIoLease(PersistenceIoCoordinator owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release();
        }
    }

    private sealed class OwnershipWaiter
    {
        internal OwnershipWaiter(
            PersistenceIoOperation operation,
            CancellationToken cancellationToken)
        {
            Operation = operation;
            CancellationToken = cancellationToken;
        }

        internal PersistenceIoOperation Operation { get; }

        internal CancellationToken CancellationToken { get; }

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
