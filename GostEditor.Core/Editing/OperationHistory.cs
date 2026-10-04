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
    }

    public OperationHistoryChangeKind Kind { get; }

    public IEditOperation? Operation { get; }

    public long Version { get; }
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

        operation.Apply(_context);
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
        operation.Revert(_context);
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
        operation.Apply(_context);
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
        RaiseChanged(OperationHistoryChangeKind.Cleared, null);
    }

    private void RaiseChanged(
        OperationHistoryChangeKind kind,
        IEditOperation? operation)
    {
        Version++;
        InvokeSafely(Changed, EventArgs.Empty);
        InvokeSafely(
            DetailedChanged,
            new OperationHistoryChangedEventArgs(kind, operation, Version));
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
