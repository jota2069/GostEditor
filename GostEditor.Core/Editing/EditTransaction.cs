using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Editing;

public sealed class EditTransaction : IEditOperation
{
    private readonly List<IEditOperation> _operations = new();

    public EditTransaction(string description)
    {
        Description = string.IsNullOrWhiteSpace(description)
            ? "Изменение документа"
            : description;
    }

    public string Description { get; }

    public IReadOnlyList<IEditOperation> Operations => _operations;

    public IReadOnlyCollection<DocumentNodeId> AffectedBlocks =>
        _operations
            .SelectMany(operation => operation.AffectedBlocks)
            .Distinct()
            .ToArray();

    public bool IsEmpty => _operations.Count == 0;

    public void Add(IEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _operations.Add(operation);
    }

    public void Apply(DocumentEditingContext context)
    {
        int appliedCount = 0;
        try
        {
            foreach (IEditOperation operation in _operations)
            {
                operation.Apply(context);
                appliedCount++;
            }
        }
        catch
        {
            for (int index = appliedCount - 1; index >= 0; index--)
            {
                _operations[index].Revert(context);
            }

            throw;
        }
    }

    public void Revert(DocumentEditingContext context)
    {
        for (int index = _operations.Count - 1; index >= 0; index--)
        {
            _operations[index].Revert(context);
        }
    }
}
