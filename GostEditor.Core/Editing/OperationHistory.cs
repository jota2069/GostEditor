namespace GostEditor.Core.Editing;

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

        IEditOperation operation = _undo.Pop();
        operation.Revert(_context);
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

        IEditOperation operation = _redo.Pop();
        operation.Apply(_context);
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
        Changed?.Invoke(this, EventArgs.Empty);
        DetailedChanged?.Invoke(
            this,
            new OperationHistoryChangedEventArgs(
                kind,
                operation,
                Version));
    }
}
