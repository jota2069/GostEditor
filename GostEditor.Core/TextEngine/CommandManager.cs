using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.TextEngine.Commands;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

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
        if (!isCommitted)
        {
            return false;
        }

        bool record;
        try
        {
            record = shouldRecord();
        }
        catch (Exception exception)
        {
            UndoAfterDecisionFailure(command, exception);
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }

        if (!record)
        {
            command.Undo();
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

        IEditorCommand command = _undoStack.Peek();
        command.Undo();
        _undoStack.Pop();
        _redoStack.Push(command);
        RaiseChangedSafely();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        IEditorCommand command = _redoStack.Peek();
        command.Execute();
        _redoStack.Pop();
        _undoStack.Push(command);
        RaiseChangedSafely();
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        RaiseChangedSafely();
    }

    private void RecordExecutedCommand(IEditorCommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
        RaiseChangedSafely();
    }

    private static void UndoAfterDecisionFailure(
        IEditorCommand command,
        Exception primaryException)
    {
        try
        {
            command.Undo();
        }
        catch (Exception rollbackException)
        {
            primaryException.Data[
                "GostEditor.CommandManager.RollbackFailure"] =
                rollbackException;
        }
    }

    private void RaiseChangedSafely()
    {
        foreach (EventHandler handler in
                 Changed?.GetInvocationList().Cast<EventHandler>() ?? [])
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                Trace.TraceError(
                    "CommandManager.Changed subscriber failed: {0}",
                    exception);
            }
        }
    }
}
