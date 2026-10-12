using GostEditor.Core.Editing;
using GostEditor.Core.Interfaces;

namespace GostEditor.Core.TextEngine.Commands;

/// <summary>
/// Compatibility command for the legacy UI command stack. The document
/// mutation and its inverse are owned exclusively by canonical history.
/// </summary>
internal sealed class CanonicalDocumentCommand : IEditorCommand
{
    private readonly DocumentEditingSession _session;
    private readonly Action _action;
    private bool _isFirstExecution = true;
    private int _operationCount;

    public CanonicalDocumentCommand(
        DocumentEditingSession session,
        Action action)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public bool IsCommitted => _operationCount > 0;

    public void Execute()
    {
        if (!_isFirstExecution)
        {
            RedoRecordedOperations();
            return;
        }

        int undoCountBefore = _session.History.UndoCount;
        _action();
        _operationCount = _session.History.UndoCount - undoCountBefore;
        _isFirstExecution = false;
    }

    public void Undo()
    {
        int undone = 0;
        try
        {
            for (; undone < _operationCount; undone++)
            {
                if (!_session.History.Undo())
                {
                    throw new InvalidOperationException(
                        "Canonical history не содержит ожидаемую операцию.");
                }
            }
        }
        catch (Exception exception)
        {
            Compensate(
                undone,
                _session.History.Redo,
                "GostEditor.CanonicalDocumentCommand.UndoCompensationFailures",
                exception);
            throw;
        }
    }

    private void RedoRecordedOperations()
    {
        int redone = 0;
        try
        {
            for (; redone < _operationCount; redone++)
            {
                if (!_session.History.Redo())
                {
                    throw new InvalidOperationException(
                        "Canonical history не содержит ожидаемую redo operation.");
                }
            }
        }
        catch (Exception exception)
        {
            Compensate(
                redone,
                _session.History.Undo,
                "GostEditor.CanonicalDocumentCommand.RedoCompensationFailures",
                exception);
            throw;
        }
    }

    private static void Compensate(
        int count,
        Func<bool> compensate,
        string dataKey,
        Exception primaryException)
    {
        List<Exception> failures = new();
        for (int index = 0; index < count; index++)
        {
            try
            {
                if (!compensate())
                {
                    failures.Add(new InvalidOperationException(
                        "Canonical history compensation недоступна."));
                    break;
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                break;
            }
        }

        if (failures.Count > 0)
        {
            primaryException.Data[dataKey] = failures.ToArray();
        }
    }
}
