namespace GostEditor.Core.Editing;

using System.Diagnostics;

public enum OperationHistoryChangeKind
{
    Executed,
    Undone,
    Redone,
    Cleared
}

public sealed class OperationHistoryChangedEventArgs : EventArgs
{
    public OperationHistoryChangedEventArgs(
        OperationHistoryChangeKind kind,
        IEditOperation? operation,
        long version)
    {
        Kind = kind;
        Operation = operation;
        Version = version;
        TransactionId = operation?.TransactionId;
    }

    public OperationHistoryChangeKind Kind { get; }

    public IEditOperation? Operation { get; }

    public long Version { get; }

    public Guid? TransactionId { get; }
}

public sealed class OperationHistory
{
    private readonly LinkedList<HistoryOperationGroup> _undo = new();
    private readonly LinkedList<HistoryOperationGroup> _redo = new();
    private readonly HashSet<Guid> _undoGroupIds = new();
    private readonly HashSet<Guid> _redoGroupIds = new();
    private readonly DocumentEditingContext _context;
    private readonly OperationHistoryPolicy _policy;
    private readonly TimeProvider _timeProvider;
    private Action? _postApplyValidation;
    private bool _coalescingBoundary;
    private long _retainedPayloadBytes;
    private bool _isApplyingOrReverting;

    public OperationHistory(
        DocumentEditingContext context,
        OperationHistoryPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        _context = context
            ?? throw new ArgumentNullException(nameof(context));
        _policy = policy ?? new OperationHistoryPolicy();
        _policy.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public long RetainedPayloadBytes => _retainedPayloadBytes;

    public Guid? LastUndoGroupId => _undo.Last?.Value.Id;

    internal bool ContainsUndoGroup(Guid id) =>
        _undoGroupIds.Contains(id);

    internal bool ContainsRedoGroup(Guid id) =>
        _redoGroupIds.Contains(id);

    public long Version { get; private set; }

    internal long EvictionVersion { get; private set; }

    public event EventHandler? Changed;

    public event EventHandler<OperationHistoryChangedEventArgs>?
        DetailedChanged;

    public void Execute(IEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        ApplyAndUpdateIndex(operation);
        ValidateOrRevert(operation);
        DateTimeOffset executedAt = _timeProvider.GetUtcNow();
        HistoryCoalescingDescriptor? descriptor = operation switch
        {
            SelectionAwareOperation selectionAware
                when selectionAware.SupportsCoalescing =>
                selectionAware.CoalescingDescriptor,
            IHistoryCoalescingOperation coalescing =>
                coalescing.CoalescingDescriptor,
            _ => null
        };
        HistoryOperationGroup? previous = _undo.Last?.Value;
        if (!_coalescingBoundary && descriptor.HasValue && previous is not null &&
            previous.CanAppend(
                descriptor.Value,
                executedAt,
                _policy.CoalescingWindow))
        {
            long previousSize = previous.EstimatedPayloadBytes;
            previous.Append(operation, descriptor.Value, executedAt);
            _retainedPayloadBytes +=
                previous.EstimatedPayloadBytes - previousSize;
        }
        else
        {
            HistoryOperationGroup group = new(
                operation,
                descriptor,
                executedAt);
            _undo.AddLast(group);
            _undoGroupIds.Add(group.Id);
            _retainedPayloadBytes += group.EstimatedPayloadBytes;
        }

        _coalescingBoundary = !descriptor.HasValue;
        ClearRedo();
        EnforceBudget();
        RaiseChanged(OperationHistoryChangeKind.Executed, operation);
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        HistoryOperationGroup operation = _undo.Last!.Value;
        RevertAndUpdateIndex(operation);
        _undo.RemoveLast();
        _redo.AddLast(operation);
        _undoGroupIds.Remove(operation.Id);
        _redoGroupIds.Add(operation.Id);
        _coalescingBoundary = true;
        RaiseChanged(OperationHistoryChangeKind.Undone, operation);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        HistoryOperationGroup operation = _redo.Last!.Value;
        ApplyAndUpdateIndex(operation);
        ValidateOrRevert(operation);
        _redo.RemoveLast();
        _undo.AddLast(operation);
        _redoGroupIds.Remove(operation.Id);
        _undoGroupIds.Add(operation.Id);
        _coalescingBoundary = true;
        RaiseChanged(OperationHistoryChangeKind.Redone, operation);
        return true;
    }

    public void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0)
        {
            return;
        }

        _undo.Clear();
        _redo.Clear();
        _undoGroupIds.Clear();
        _redoGroupIds.Clear();
        _retainedPayloadBytes = 0;
        _coalescingBoundary = true;
        RaiseChanged(
            OperationHistoryChangeKind.Cleared,
            null,
            incrementRevision: false);
    }

    internal void PublishCompatibilityMutation(bool discardRedo)
    {
        _coalescingBoundary = true;
        if (discardRedo)
        {
            ClearRedo();
        }

        RaiseChanged(OperationHistoryChangeKind.Executed, null);
    }

    internal void SetPostApplyValidation(Action validation) =>
        _postApplyValidation = validation ??
            throw new ArgumentNullException(nameof(validation));

    public void BreakCoalescing()
    {
        if (!_isApplyingOrReverting)
        {
            _coalescingBoundary = true;
        }
    }

    private void EnforceBudget()
    {
        while (_undo.Count > _policy.MaxUndoGroups ||
               RetainedPayloadBytes > _policy.MaxRetainedBytes)
        {
            if (_undo.Count > 0)
            {
                _retainedPayloadBytes -=
                    _undo.First!.Value.EstimatedPayloadBytes;
                _undoGroupIds.Remove(_undo.First.Value.Id);
                _undo.RemoveFirst();
                EvictionVersion = checked(EvictionVersion + 1);
                continue;
            }

            if (_redo.Count > 0)
            {
                _retainedPayloadBytes -=
                    _redo.First!.Value.EstimatedPayloadBytes;
                _redoGroupIds.Remove(_redo.First.Value.Id);
                _redo.RemoveFirst();
                EvictionVersion = checked(EvictionVersion + 1);
                continue;
            }

            break;
        }
    }

    private void ClearRedo()
    {
        foreach (HistoryOperationGroup group in _redo)
        {
            _retainedPayloadBytes -= group.EstimatedPayloadBytes;
        }

        _redo.Clear();
        _redoGroupIds.Clear();
    }

    private void ValidateOrRevert(IEditOperation operation)
    {
        if (_postApplyValidation is null)
        {
            return;
        }

        try
        {
            _postApplyValidation();
        }
        catch (Exception primaryException)
        {
            try
            {
                RevertAndUpdateIndex(operation);
            }
            catch (Exception rollbackException)
            {
                primaryException.Data[
                    "GostEditor.OperationHistory.ValidationRollbackFailure"] =
                    rollbackException;
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(primaryException)
                .Throw();
            throw;
        }
    }

    private void RaiseChanged(
        OperationHistoryChangeKind kind,
        IEditOperation? operation,
        bool incrementRevision = true)
    {
        if (incrementRevision)
        {
            Version = checked(Version + 1);
        }
        InvokeSafely(Changed, EventArgs.Empty);
        InvokeSafely(
            DetailedChanged,
            new OperationHistoryChangedEventArgs(kind, operation, Version));
    }

    private void ApplyAndUpdateIndex(IEditOperation operation)
    {
        bool applied = false;
        _isApplyingOrReverting = true;
        try
        {
            operation.Apply(_context);
            applied = true;
            _context.UpdateIndex(operation);
        }
        catch (Exception primaryException) when (applied)
        {
            CompensateIndexFailure(
                primaryException,
                () => operation.Revert(_context),
                operation);
            throw;
        }
        finally
        {
            _isApplyingOrReverting = false;
        }
    }

    private void RevertAndUpdateIndex(IEditOperation operation)
    {
        bool reverted = false;
        _isApplyingOrReverting = true;
        try
        {
            operation.Revert(_context);
            reverted = true;
            _context.UpdateIndex(operation);
        }
        catch (Exception primaryException) when (reverted)
        {
            CompensateIndexFailure(
                primaryException,
                () => operation.Apply(_context),
                operation);
            throw;
        }
        finally
        {
            _isApplyingOrReverting = false;
        }
    }

    private void CompensateIndexFailure(
        Exception primaryException,
        Action compensate,
        IEditOperation operation)
    {
        try
        {
            compensate();
            _context.UpdateIndex(operation);
        }
        catch (Exception rollbackException)
        {
            primaryException.Data[
                "GostEditor.OperationHistory.IndexRollbackFailure"] =
                rollbackException;
        }
    }

    private void InvokeSafely(EventHandler? handlers, EventArgs args)
    {
        foreach (EventHandler handler in
                 handlers?.GetInvocationList().Cast<EventHandler>() ?? [])
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                Trace.TraceError(
                    "OperationHistory event subscriber failed: {0}",
                    exception);
            }
        }
    }

    private void InvokeSafely<TEventArgs>(
        EventHandler<TEventArgs>? handlers,
        TEventArgs args)
        where TEventArgs : EventArgs
    {
        foreach (EventHandler<TEventArgs> handler in
                 handlers?.GetInvocationList()
                     .Cast<EventHandler<TEventArgs>>() ?? [])
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                Trace.TraceError(
                    "OperationHistory event subscriber failed: {0}",
                    exception);
            }
        }
    }
}
