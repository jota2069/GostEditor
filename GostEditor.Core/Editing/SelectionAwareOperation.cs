using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Editing;

internal sealed class SelectionAwareOperation : IEditOperation
{
    private readonly IEditOperation _inner;
    private readonly SelectionService _selection;
    private readonly Action _updateSelection;
    private SelectionState? _before;
    private SelectionState? _after;
    private bool _hasCompletedFirstApply;

    public SelectionAwareOperation(
        IEditOperation inner,
        SelectionService selection,
        Action updateSelection)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _selection = selection
            ?? throw new ArgumentNullException(nameof(selection));
        _updateSelection = updateSelection
            ?? throw new ArgumentNullException(nameof(updateSelection));
    }

    public string Description => _inner.Description;

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        _inner.AffectedBlocks;

    public void Apply(DocumentEditingContext context)
    {
        if (!_hasCompletedFirstApply)
        {
            _before = _selection.CaptureState();
            bool innerApplied = false;
            try
            {
                _inner.Apply(context);
                innerApplied = true;
                _updateSelection();
                _after = _selection.CaptureState();
                _hasCompletedFirstApply = true;
            }
            catch (Exception exception)
            {
                Compensate(
                    innerApplied
                        ? () => _inner.Revert(context)
                        : null,
                    () => _selection.RestoreState(_before.Value),
                    exception);
                _before = null;
                _after = null;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(exception)
                    .Throw();
                throw;
            }
            return;
        }

        try
        {
            _inner.Apply(context);
        }
        catch (Exception exception)
        {
            Compensate(
                null,
                null,
                exception);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(exception)
                .Throw();
            throw;
        }

        try
        {
            _selection.RestoreState(
                _after ?? throw new InvalidOperationException(
                    "Операция ещё не была выполнена."));
        }
        catch (Exception exception)
        {
            Compensate(
                () => _inner.Revert(context),
                () => _selection.RestoreState(
                    _before ?? throw new InvalidOperationException(
                        "Операция ещё не была выполнена.")),
                exception);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(exception)
                .Throw();
            throw;
        }
    }

    public void Revert(DocumentEditingContext context)
    {
        try
        {
            _inner.Revert(context);
        }
        catch (Exception exception)
        {
            Compensate(
                null,
                null,
                exception);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(exception)
                .Throw();
            throw;
        }

        try
        {
            _selection.RestoreState(
                _before ?? throw new InvalidOperationException(
                    "Операция ещё не была выполнена."));
        }
        catch (Exception exception)
        {
            Compensate(
                () => _inner.Apply(context),
                () => _selection.RestoreState(
                    _after ?? throw new InvalidOperationException(
                        "Операция ещё не была выполнена.")),
                exception);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(exception)
                .Throw();
            throw;
        }
    }

    private static void Compensate(
        Action? restoreDocument,
        Action? restoreSelection,
        Exception primaryException)
    {
        List<Exception> failures = new();
        try
        {
            restoreDocument?.Invoke();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            restoreSelection?.Invoke();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count > 0)
        {
            primaryException.Data[
                "GostEditor.SelectionAwareOperation.RollbackFailures"] =
                failures.ToArray();
        }
    }
}
