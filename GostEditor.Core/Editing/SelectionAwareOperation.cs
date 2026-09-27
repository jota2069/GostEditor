using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Editing;

internal sealed class SelectionAwareOperation : IEditOperation
{
    private readonly IEditOperation _inner;
    private readonly SelectionService _selection;
    private readonly Action _updateSelection;
    private SelectionState? _before;
    private SelectionState? _after;

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
        if (!_before.HasValue)
        {
            _before = _selection.CaptureState();
            _inner.Apply(context);
            _updateSelection();
            _after = _selection.CaptureState();
            return;
        }

        _inner.Apply(context);
        _selection.RestoreState(
            _after ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена."));
    }

    public void Revert(DocumentEditingContext context)
    {
        _inner.Revert(context);
        _selection.RestoreState(
            _before ?? throw new InvalidOperationException(
                "Операция ещё не была выполнена."));
    }
}
