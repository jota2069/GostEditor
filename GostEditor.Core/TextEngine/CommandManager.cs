using System.Collections.Generic;
using GostEditor.Core.Interfaces;

namespace GostEditor.Core.TextEngine;

/// <summary>
/// Управляет историей команд редактора для реализации Undo/Redo (Ctrl+Z / Ctrl+Y).
/// </summary>
public class CommandManager
{
    private readonly Stack<IEditorCommand> _undoStack = new Stack<IEditorCommand>();
    private readonly Stack<IEditorCommand> _redoStack = new Stack<IEditorCommand>();

    /// <summary>
    /// Выполняет новую команду и сохраняет её в историю отмен.
    /// </summary>
    public void ExecuteCommand(IEditorCommand command)
    {
        command.Execute();
        RecordExecutedCommand(command);
    }

    /// <summary>
    /// Executes a command and records it only when the completed operation
    /// reports a meaningful state change.
    /// </summary>
    public bool TryExecuteCommand(IEditorCommand command, Func<bool> shouldRecord)
    {
        command.Execute();
        if (!shouldRecord())
        {
            return false;
        }

        RecordExecutedCommand(command);
        return true;
    }

    /// <summary>
    /// Отменяет последнее выполненное действие (Ctrl+Z).
    /// </summary>
    public void Undo()
    {
        if (_undoStack.Count > 0)
        {
            IEditorCommand command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
        }
    }

    /// <summary>
    /// Повторяет последнее отмененное действие (Ctrl+Y).
    /// </summary>
    public void Redo()
    {
        if (_redoStack.Count > 0)
        {
            IEditorCommand command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
        }
    }

    /// <summary>
    /// Очищает историю (используется при переключении разделов или загрузке документа).
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void RecordExecutedCommand(IEditorCommand command)
    {
        _undoStack.Push(command);

        // Сбрасываем ветку повторов при новом действии
        _redoStack.Clear();
    }
}
