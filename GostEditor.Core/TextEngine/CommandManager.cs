using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.TextEngine.Commands;

namespace GostEditor.Core.TextEngine;

/// <summary>
/// Управляет историей обратимых операций редактора.
/// </summary>
public class CommandManager
{
    private readonly Stack<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public int UndoCount => _undoStack.Count;

    public int RedoCount => _redoStack.Count;

    public event EventHandler? Changed;

    public void ExecuteCommand(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute();

        if (command is DocumentMutationCommand mutation &&
            !mutation.IsCommitted)
        {
            return;
        }

        RecordExecutedCommand(command);
    }

    public bool TryExecuteCommand(
        IEditorCommand command,
        Func<bool> shouldRecord)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(shouldRecord);

        command.Execute();
        bool isCommitted = command is not DocumentMutationCommand mutation ||
                           mutation.IsCommitted;
        if (!isCommitted || !shouldRecord())
        {
            return false;
        }

        RecordExecutedCommand(command);
        return true;
    }

    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        IEditorCommand command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        IEditorCommand command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RecordExecutedCommand(IEditorCommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
