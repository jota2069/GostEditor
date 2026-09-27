using GostEditor.Core.DocumentModel;
using GostEditor.Core.Layout.Incremental;

namespace GostEditor.Core.Editing;

public sealed class DocumentEditingSession
{
    public DocumentEditingSession(DocumentRoot document)
    {
        Document = document
            ?? throw new ArgumentNullException(nameof(document));
        Document.EnsureEditableStructure();

        InvalidationTracker = new LayoutInvalidationTracker(Document);
        Context = new DocumentEditingContext(
            Document,
            InvalidationTracker);
        History = new OperationHistory(Context);
        History.DetailedChanged += OnHistoryChanged;
        Selection = new SelectionService(Document);
        Text = new TextEditingService(this);
        Formatting = new FormattingService(this);
    }

    public DocumentRoot Document { get; }

    public DocumentEditingContext Context { get; }

    public OperationHistory History { get; }

    public SelectionService Selection { get; }

    public TextEditingService Text { get; }

    public FormattingService Formatting { get; }

    public LayoutInvalidationTracker InvalidationTracker { get; }

    public long ChangeVersion => History.Version;

    public event EventHandler<OperationHistoryChangedEventArgs>? Changed;

    public void Execute(IEditOperation operation) =>
        History.Execute(operation);

    public void Execute(EditTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (!transaction.IsEmpty)
        {
            History.Execute(transaction);
        }
    }

    private void OnHistoryChanged(
        object? sender,
        OperationHistoryChangedEventArgs e) =>
        Changed?.Invoke(this, e);
}
