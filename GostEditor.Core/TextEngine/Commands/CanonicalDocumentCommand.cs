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
    private Guid? _undoGroupId;
    private bool _isCommitted;

    public CanonicalDocumentCommand(
        DocumentEditingSession session,
        Action action)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public bool IsCommitted => _isCommitted;

    public bool IsUndoable => _undoGroupId.HasValue &&
        _session.History.ContainsUndoGroup(_undoGroupId.Value);

    public bool IsRedoable => _undoGroupId.HasValue &&
        _session.History.ContainsRedoGroup(_undoGroupId.Value);

    public bool SharesUndoGroup(CanonicalDocumentCommand other) =>
        _undoGroupId.HasValue && _undoGroupId == other._undoGroupId;

    public long HistoryEvictionVersion =>
        _session.History.EvictionVersion;

    public void Execute()
    {
        if (!_isFirstExecution)
        {
            RedoRecordedOperations();
            return;
        }

        long revisionBefore = _session.Revision;
        _action();
        _isCommitted = _session.Revision != revisionBefore;
        _undoGroupId = _isCommitted
            ? _session.History.LastUndoGroupId
            : null;
        _isFirstExecution = false;
    }

    public void Undo()
    {
        if (!_isCommitted)
        {
            return;
        }

        if (!_undoGroupId.HasValue ||
            !_session.History.ContainsUndoGroup(_undoGroupId.Value) ||
            !_session.History.Undo())
        {
            throw new InvalidOperationException(
                "Canonical history не содержит ожидаемую undo group.");
        }
    }

    private void RedoRecordedOperations()
    {
        if (!_isCommitted)
        {
            return;
        }

        if (!_undoGroupId.HasValue ||
            !_session.History.ContainsRedoGroup(_undoGroupId.Value) ||
            !_session.History.Redo())
        {
            throw new InvalidOperationException(
                "Canonical history не содержит ожидаемую redo group.");
        }
    }
}
