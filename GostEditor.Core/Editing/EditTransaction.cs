using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Editing;

public sealed class EditTransaction : IEditOperation
{
    private readonly List<IEditOperation> _operations = new();

    public EditTransaction(string description, Guid? transactionId = null)
    {
        Description = string.IsNullOrWhiteSpace(description)
            ? "Изменение документа"
            : description;
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Transaction ID не может быть пустым.",
                nameof(transactionId));
        }

        TransactionId = transactionId;
    }

    public string Description { get; }

    public Guid? TransactionId { get; }

    public IReadOnlyList<IEditOperation> Operations => _operations;

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        _operations
            .SelectMany(operation => operation.AffectedBlocks)
            .Distinct()
            .ToArray();

    public bool IsEmpty => _operations.Count == 0;

    public DocumentIndexUpdateKind IndexUpdateKind =>
        DocumentIndexUpdateKind.None;

    public void Add(IEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _operations.Add(operation);
    }

    public void Apply(DocumentEditingContext context)
    {
        List<int> applied = new();
        for (int index = 0; index < _operations.Count; index++)
        {
            try
            {
                _operations[index].Apply(context);
                applied.Add(index);
                context.UpdateIndex(_operations[index]);
            }
            catch (Exception primaryException)
            {
                List<Exception> rollbackFailures = new();
                for (int appliedIndex = applied.Count - 1;
                     appliedIndex >= 0;
                     appliedIndex--)
                {
                    try
                    {
                        _operations[applied[appliedIndex]].Revert(context);
                        context.UpdateIndex(_operations[applied[appliedIndex]]);
                    }
                    catch (Exception rollbackException)
                    {
                        rollbackFailures.Add(rollbackException);
                    }
                }

                ThrowPrimaryWithRollbackFailures(
                    primaryException,
                    rollbackFailures);
            }
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
                context.UpdateIndex(_operations[index]);
                reverted.Add(index);
            }
        }
        catch (Exception primaryException)
        {
            List<Exception> compensationFailures = new();
            foreach (int index in reverted.Order())
            {
                try
                {
                    _operations[index].Apply(context);
                    context.UpdateIndex(_operations[index]);
                }
                catch (Exception compensationException)
                {
                    compensationFailures.Add(compensationException);
                }
            }

            ThrowPrimaryWithRollbackFailures(
                primaryException,
                compensationFailures);
        }
    }

    private static void ThrowPrimaryWithRollbackFailures(
        Exception primaryException,
        IReadOnlyCollection<Exception> rollbackFailures)
    {
        if (rollbackFailures.Count > 0)
        {
            primaryException.Data[
                "GostEditor.EditTransaction.RollbackFailures"] =
                rollbackFailures.ToArray();
        }

        System.Runtime.ExceptionServices.ExceptionDispatchInfo
            .Capture(primaryException)
            .Throw();
    }
}
