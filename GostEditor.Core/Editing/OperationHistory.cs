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
    private readonly Stack<IEditOperation> _undo = new();
    private readonly Stack<IEditOperation> _redo = new();
    private readonly DocumentEditingContext _context;

    public OperationHistory(DocumentEditingContext context)
    {
        _context = context
            ?? throw new ArgumentNullException(nameof(context));
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public long Version { get; private set; }

    public event EventHandler? Changed;

    public event EventHandler<OperationHistoryChangedEventArgs>?
        DetailedChanged;

    public void Execute(IEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        ApplyAndUpdateIndex(operation);
        _undo.Push(operation);
        _redo.Clear();
        RaiseChanged(OperationHistoryChangeKind.Executed, operation);
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        IEditOperation operation = _undo.Peek();
        RevertAndUpdateIndex(operation);
        _undo.Pop();
        _redo.Push(operation);
        RaiseChanged(OperationHistoryChangeKind.Undone, operation);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        IEditOperation operation = _redo.Peek();
        ApplyAndUpdateIndex(operation);
        _redo.Pop();
        _undo.Push(operation);
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
        RaiseChanged(
            OperationHistoryChangeKind.Cleared,
            null,
            incrementRevision: false);
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
    }

    private void RevertAndUpdateIndex(IEditOperation operation)
    {
        bool reverted = false;
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
