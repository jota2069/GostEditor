using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Editing;

internal sealed class HistoryOperationGroup : IEditOperation, IHistoryPayloadSize
{
    private readonly List<IEditOperation> _operations = new();
    private readonly HashSet<DocumentNodeId> _affectedBlocks = new();
    private long _estimatedPayloadBytes = 96L;

    public HistoryOperationGroup(
        IEditOperation operation,
        HistoryCoalescingDescriptor? descriptor,
        DateTimeOffset executedAt)
    {
        Id = Guid.NewGuid();
        _operations.Add(operation);
        _affectedBlocks.UnionWith(operation.AffectedBlocks);
        _estimatedPayloadBytes += Estimate(operation);
        LastDescriptor = descriptor;
        LastExecutedAt = executedAt;
    }

    public Guid Id { get; }

    public string Description => _operations[0].Description;

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        _affectedBlocks;

    public DocumentIndexUpdateKind IndexUpdateKind =>
        DocumentIndexUpdateKind.None;

    public Guid? TransactionId => _operations.Count == 1
        ? _operations[0].TransactionId
        : null;

    public HistoryCoalescingDescriptor? LastDescriptor { get; private set; }

    public DateTimeOffset LastExecutedAt { get; private set; }

    public long EstimatedPayloadBytes => _estimatedPayloadBytes;

    public bool CanAppend(
        HistoryCoalescingDescriptor descriptor,
        DateTimeOffset executedAt,
        TimeSpan window) =>
        LastDescriptor.HasValue &&
        descriptor.CanFollow(LastDescriptor.Value) &&
        executedAt >= LastExecutedAt &&
        executedAt - LastExecutedAt <= window;

    public void Append(
        IEditOperation operation,
        HistoryCoalescingDescriptor descriptor,
        DateTimeOffset executedAt)
    {
        _operations.Add(operation);
        _affectedBlocks.UnionWith(operation.AffectedBlocks);
        _estimatedPayloadBytes += Estimate(operation);
        LastDescriptor = descriptor;
        LastExecutedAt = executedAt;
    }

    private static long Estimate(IEditOperation operation) =>
        HistoryPayloadSizer.Operation(operation);

    public void Apply(DocumentEditingContext context)
    {
        List<int> applied = new();
        try
        {
            for (int index = 0; index < _operations.Count; index++)
            {
                _operations[index].Apply(context);
                applied.Add(index);
                context.UpdateIndex(_operations[index]);
            }
        }
        catch (Exception primary)
        {
            for (int index = applied.Count - 1; index >= 0; index--)
            {
                TryCompensate(
                    primary,
                    () => _operations[applied[index]].Revert(context),
                    context,
                    _operations[applied[index]]);
            }

            throw;
        }
    }

    public void Revert(DocumentEditingContext context)
    {
        List<int> reverted = new();
        try
        {
            for (int index = _operations.Count - 1; index >= 0; index--)
            {
                _operations[index].Revert(context);
                reverted.Add(index);
                context.UpdateIndex(_operations[index]);
            }
        }
        catch (Exception primary)
        {
            foreach (int index in reverted.Order())
            {
                TryCompensate(
                    primary,
                    () => _operations[index].Apply(context),
                    context,
                    _operations[index]);
            }

            throw;
        }
    }

    private static void TryCompensate(
        Exception primary,
        Action action,
        DocumentEditingContext context,
        IEditOperation operation)
    {
        try
        {
            action();
            context.UpdateIndex(operation);
        }
        catch (Exception failure)
        {
            primary.Data["GostEditor.HistoryGroup.RollbackFailure"] = failure;
        }
    }
}
