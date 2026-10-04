using System;
using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Legacy;
using GostEditor.Core.Editing;
using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine.Commands;
using GostEditor.Core.TextEngine.DOM;
using GostDocument = GostEditor.Core.Models.GostDocument;

namespace GostEditor.Core.TextEngine;

public partial class DocumentEditor
{
    public GostDocument Document { get; private set; }
    public IImageService ImageService { get; }
    public DocumentPosition CaretPosition { get; set; }
    public DocumentPosition? SelectionAnchor { get; set; }
    public int? SelectedImageParagraphIndex { get; set; }

    public CommandManager History { get; } = new CommandManager();

    public bool HasSelection => SelectionAnchor.HasValue && SelectionAnchor.Value.CompareTo(CaretPosition) != 0;

    public long DocumentChangeVersion { get; private set; }

    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;

    private bool _isExecutingCommand = false;
    private readonly LegacyDocumentEditingBridge _structuredBridge = new();

    public DocumentEditor()
        : this(new GostDocument(), new ImageService())
    {
    }

    public DocumentEditor(GostDocument document)
        : this(document, new ImageService())
    {
    }

    public DocumentEditor(GostDocument document, IImageService imageService)
    {
        Document = document ?? new GostDocument();
        ImageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        if (Document.Paragraphs.Count == 0)
        {
            Document.Paragraphs.Add(new Paragraph());
        }
        CaretPosition = new DocumentPosition(0, 0);
    }

    public void LoadDocument(GostDocument document)
    {
        if (document is null) return;

        Document = document;
        if (Document.Paragraphs.Count == 0)
        {
            Document.Paragraphs.Add(new Paragraph());
        }
        CaretPosition = new DocumentPosition(0, 0);
        ClearSelection();
        History.Clear();
        NotifyDocumentChanged(
            0,
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint |
            DocumentChangeKind.Resources);
    }

    public DocumentEditingSession CreateStructuredEditingSession() =>
        _structuredBridge.CreateSession(Document);

    public void ApplyStructuredDocument(DocumentRoot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        LoadDocument(new LegacyDocumentAdapter().ToLegacyDocument(document));
    }


    public void ExecuteWithSnapshot(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        ExecuteMutation(
            () => new DocumentMutationRange(
                0,
                Document.Paragraphs.Count),
            () => new DocumentMutationRange(
                0,
                Document.Paragraphs.Count),
            action,
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint |
            DocumentChangeKind.Resources,
            captureImages: true);
    }

    internal void ExecuteMutation(
        Func<DocumentMutationRange> beforeRangeProvider,
        Func<DocumentMutationRange> afterRangeProvider,
        Action action,
        DocumentChangeKind changeKind,
        bool captureImages = false,
        Func<bool>? shouldCommit = null)
    {
        if (_isExecutingCommand)
        {
            action();
            return;
        }

        _isExecutingCommand = true;
        try
        {
            DocumentMutationCommand command = new(
                this,
                beforeRangeProvider,
                afterRangeProvider,
                () =>
                {
                    HashSet<Guid> previouslyReferencedImages =
                        GetReferencedImageIds();
                    action();
                    CleanupLostImageReferences(
                        previouslyReferencedImages);
                },
                changeKind,
                captureImages,
                shouldCommit);
            History.ExecuteCommand(command);
        }
        finally
        {
            _isExecutingCommand = false;
        }
    }

    internal void ExecuteParagraphMutation(
        int startParagraphIndex,
        int beforeParagraphCount,
        Action action,
        DocumentChangeKind changeKind,
        bool captureImages = false,
        Func<bool>? shouldCommit = null)
    {
        int originalParagraphCount = Document.Paragraphs.Count;
        int safeStart = Math.Clamp(
            startParagraphIndex,
            0,
            originalParagraphCount);
        int safeBeforeCount = Math.Clamp(
            beforeParagraphCount,
            0,
            originalParagraphCount - safeStart);

        ExecuteMutation(
            () => new DocumentMutationRange(
                safeStart,
                safeBeforeCount),
            () => new DocumentMutationRange(
                safeStart,
                Math.Max(
                    0,
                    Document.Paragraphs.Count -
                    (originalParagraphCount - safeBeforeCount))),
            action,
            changeKind,
            captureImages,
            shouldCommit);
    }

    internal void NotifyDocumentChanged(
        int startParagraphIndex,
        DocumentChangeKind kind)
    {
        DocumentChangeVersion++;
        DocumentChangedEventArgs args = new(
            startParagraphIndex,
            kind,
            DocumentChangeVersion);
        foreach (EventHandler<DocumentChangedEventArgs> handler in
                 DocumentChanged?.GetInvocationList()
                     .Cast<EventHandler<DocumentChangedEventArgs>>() ?? [])
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError(
                    "DocumentChanged subscriber failed: {0}",
                    exception);
            }
        }
    }


}
