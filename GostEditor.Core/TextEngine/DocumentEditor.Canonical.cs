using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.DocumentModel.Legacy;
using GostEditor.Core.Editing;
using GostEditor.Core.TextEngine.Commands;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine;

public partial class DocumentEditor
{
    internal List<BlockNode> CaptureCanonicalBlocks(
        DocumentMutationRange range)
    {
        IReadOnlyList<BlockNode> blocks = _editingSession.Document
            .EnumerateBlocks()
            .ToList();
        DocumentMutationRange normalized = range.Normalize(blocks.Count);
        return blocks
            .Skip(normalized.Start)
            .Take(normalized.Count)
            .ToList();
    }

    internal void RebaseCanonicalAfterLegacyMutation(
        IReadOnlyList<BlockNode>? preferredBlocks = null,
        DocumentMutationRange? preferredRange = null,
        bool discardCanonicalRedo = false)
    {
        ReconcileCanonicalAfterLegacyMutation(
            preferredBlocks,
            preferredRange,
            discardCanonicalRedo);
        CaretPosition = NormalizePosition(CaretPosition);
        SelectionAnchor = SelectionAnchor.HasValue
            ? NormalizePosition(SelectionAnchor.Value)
            : null;
    }

    private void ReconcileCanonicalAfterLegacyMutation(
        IReadOnlyList<BlockNode>? preferredBlocks,
        DocumentMutationRange? preferredRange,
        bool discardCanonicalRedo)
    {
        DocumentRoot imported = _structuredBridge
            .CreateSession(Document)
            .Document;
        IReadOnlyList<BlockNode> current = _editingSession.Document
            .EnumerateBlocks()
            .ToList();
        IReadOnlyList<BlockNode> replacement = imported
            .EnumerateBlocks()
            .ToList();
        List<BlockNode> reconciled = ReuseUnchangedCanonicalBlocks(
            current,
            replacement);
        ApplyPreferredCanonicalBlocks(
            reconciled,
            preferredBlocks,
            preferredRange);

        DocumentSection section = _editingSession.Document.Sections[0];
        section.Blocks.Clear();
        section.Blocks.AddRange(reconciled);
        _editingSession.Document.Resources.ClearImages();
        foreach (ImageResource image in imported.Resources.ImagesInOrder)
        {
            _editingSession.Document.Resources.AddImage(image);
        }
        _editingSession.Document.RebuildIndex();
        _legacyProjection.Rebind(_editingSession.Document);

        DocumentLocation caret = GetNearestCanonicalTextLocation(
            NormalizePosition(CaretPosition));
        _editingSession.Selection.MoveCaret(caret);
        _editingSession.InvalidationTracker.InvalidateStructure(0);
        _editingSession.PublishCompatibilityMutation(discardCanonicalRedo);
    }

    private void ApplyPreferredCanonicalBlocks(
        List<BlockNode> reconciled,
        IReadOnlyList<BlockNode>? preferredBlocks,
        DocumentMutationRange? preferredRange)
    {
        if (preferredBlocks is null || preferredRange is null)
        {
            return;
        }

        DocumentMutationRange range = preferredRange.Value.Normalize(
            reconciled.Count);
        int count = Math.Min(range.Count, preferredBlocks.Count);
        for (int index = 0; index < count; index++)
        {
            int targetIndex = range.Start + index;
            BlockNode preferred = preferredBlocks[index];
            if (MatchesLegacy(preferred, Document.Paragraphs[targetIndex]))
            {
                reconciled[targetIndex] = preferred;
            }
        }
    }

    private DocumentLocation GetNearestCanonicalTextLocation(
        DocumentPosition position)
    {
        IReadOnlyList<BlockNode> blocks = _editingSession.Document
            .EnumerateBlocks()
            .ToList();
        int index = Math.Clamp(position.ParagraphIndex, 0, blocks.Count - 1);
        if (blocks[index] is ParagraphBlock exact)
        {
            return new DocumentLocation(
                exact.Id,
                Math.Clamp(position.Offset, 0, exact.TextLength));
        }

        for (int distance = 1; distance < blocks.Count; distance++)
        {
            int before = index - distance;
            if (before >= 0 && blocks[before] is ParagraphBlock previous)
            {
                return new DocumentLocation(previous.Id, previous.TextLength);
            }

            int after = index + distance;
            if (after < blocks.Count && blocks[after] is ParagraphBlock next)
            {
                return new DocumentLocation(next.Id, 0);
            }
        }

        throw new InvalidOperationException(
            "Документ не содержит текстового абзаца для caret.");
    }

    private List<BlockNode> ReuseUnchangedCanonicalBlocks(
        IReadOnlyList<BlockNode> current,
        IReadOnlyList<BlockNode> replacement)
    {
        int oldCount = current.Count;
        int newCount = replacement.Count;
        Dictionary<int, BlockNode> reusedByNewIndex = new();
        int prefix = 0;
        while (prefix < oldCount && prefix < newCount &&
               MatchesLegacy(current[prefix], Document.Paragraphs[prefix]))
        {
            reusedByNewIndex[prefix] = current[prefix];
            prefix++;
        }

        int suffix = 0;
        while (suffix < oldCount - prefix && suffix < newCount - prefix)
        {
            int oldIndex = oldCount - suffix - 1;
            int newIndex = newCount - suffix - 1;
            if (!MatchesLegacy(
                    current[oldIndex],
                    Document.Paragraphs[newIndex]))
            {
                break;
            }

            reusedByNewIndex[newIndex] = current[oldIndex];
            suffix++;
        }

        foreach ((int index, BlockNode reused) in reusedByNewIndex)
        {
            if (reused is FigureBlock target &&
                replacement[index] is FigureBlock source)
            {
                target.Width = source.Width;
                target.Height = source.Height;
                target.Caption = source.Caption;
            }
        }

        return replacement
            .Select((block, index) =>
                reusedByNewIndex.GetValueOrDefault(index) ?? block)
            .ToList();
    }

    private void InitializeCanonicalSession()
    {
        if (_editingSession is not null)
        {
            _editingSession.Changed -= OnCanonicalDocumentChanged;
        }

        _editingSession = _structuredBridge.CreateSession(Document);
        _editingSession.RequireLegacyLosslessProjection(
            () => LegacyTextProjection.ValidateLossless(
                _editingSession.Document));
        _legacyProjection = new LegacyTextProjection(
            _editingSession.Document,
            Document);
        _editingSession.Changed += OnCanonicalDocumentChanged;
    }

    private void ExecuteCanonicalEdit(
        Action action,
        params int[] legacyParagraphIndices)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureCanonicalMatchesLegacy(legacyParagraphIndices);
        LegacyTextProjection.ValidateLossless(_editingSession.Document);
        SynchronizeCanonicalSelection();
        CanonicalDocumentCommand command = new(_editingSession, action);
        History.TryExecuteCommand(command, () => command.IsCommitted);
        SynchronizeLegacySelectionFromCanonical();
    }

    private void ExecuteCanonicalStructuralEdit(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureCanonicalMatchesLegacy(Enumerable.Range(
            0,
            Document.Paragraphs.Count).ToArray());
        LegacyTextProjection.ValidateLossless(_editingSession.Document);
        SynchronizeCanonicalSelectionForStructure();
        CanonicalDocumentCommand command = new(_editingSession, action);
        History.TryExecuteCommand(command, () => command.IsCommitted);
        if (command.IsCommitted)
        {
            SynchronizeLegacySelectionFromCanonical();
        }
    }

    private void SynchronizeCanonicalSelectionForStructure()
    {
        DocumentLocation caret = GetNearestCanonicalTextLocation(CaretPosition);
        if (SelectionAnchor.HasValue)
        {
            DocumentLocation anchor = GetNearestCanonicalTextLocation(
                SelectionAnchor.Value);
            _editingSession.Selection.SetSelection(anchor, caret);
            return;
        }

        _editingSession.Selection.MoveCaret(caret);
    }

    private void SynchronizeLegacySelectionFromCanonical()
    {
        _caretPosition = _legacyProjection.ToLegacyPosition(
            _editingSession.Document,
            _editingSession.Selection.Caret);
        _selectionAnchor = _editingSession.Selection.Anchor.HasValue
            ? _legacyProjection.ToLegacyPosition(
                _editingSession.Document,
                _editingSession.Selection.Anchor.Value)
            : null;
    }

    private void EnsureCanonicalMatchesLegacy(
        IReadOnlyCollection<int> paragraphIndices)
    {
        IReadOnlyList<BlockNode> blocks = _editingSession.Document
            .EnumerateBlocks()
            .ToList();
        if (blocks.Count != Document.Paragraphs.Count)
        {
            throw new InvalidOperationException(
                "Legacy text view изменён вне canonical editing path.");
        }

        foreach (int index in paragraphIndices.Distinct())
        {
            if (index < 0 || index >= blocks.Count ||
                !MatchesLegacy(blocks[index], Document.Paragraphs[index]))
            {
                throw new InvalidOperationException(
                    "Legacy text view изменён вне canonical editing path.");
            }
        }
    }

    private void SynchronizeCanonicalSelection()
    {
        DocumentLocation caret = _legacyProjection.ToCanonicalLocation(
            _editingSession.Document,
            CaretPosition);
        if (SelectionAnchor.HasValue)
        {
            DocumentLocation anchor = _legacyProjection.ToCanonicalLocation(
                _editingSession.Document,
                SelectionAnchor.Value);
            _editingSession.Selection.SetSelection(anchor, caret);
            return;
        }

        _editingSession.Selection.MoveCaret(caret);
    }

    private bool IsCanonicalTextSelection()
    {
        (DocumentPosition start, DocumentPosition end) = HasSelection
            ? GetNormalizedSelection()
            : (CaretPosition, CaretPosition);
        EnsureCanonicalMatchesLegacy(Enumerable.Range(
            start.ParagraphIndex,
            end.ParagraphIndex - start.ParagraphIndex + 1).ToArray());
        return _legacyProjection.IsTextRange(
            _editingSession.Document,
            start.ParagraphIndex,
            end.ParagraphIndex);
    }

    private DocumentLocation ToCanonicalLocation(DocumentPosition position) =>
        _legacyProjection.ToCanonicalLocation(
            _editingSession.Document,
            position);

    private TextStyle GetCanonicalTypingStyle()
    {
        if (_typingStyleOverride is not null)
        {
            return _typingStyleOverride.Clone();
        }

        DocumentLocation caret = _editingSession.Selection.Caret;
        ParagraphBlock paragraph = (ParagraphBlock)
            _editingSession.Document.FindBlock(caret.BlockId)!;
        int current = 0;
        TextStyle? previous = null;
        foreach (InlineNode inline in paragraph.Inlines)
        {
            TextStyle? style = inline switch
            {
                TextInline text => text.Style,
                LinkInline link => link.Style,
                _ => null
            };
            int end = current + inline.TextLength;
            if (style is not null &&
                (caret.Offset < end ||
                 caret.Offset == current && inline.TextLength == 0))
            {
                return style.Clone();
            }

            previous = style ?? previous;
            current = end;
        }

        return previous?.Clone() ?? new TextStyle();
    }

    private void ChangeTypingStyle(Action<TextStyle> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ExecuteCanonicalEdit(
            () => _editingSession.Formatting.ChangeTypingStyle(
                _editingSession.Selection.Caret,
                change),
            CaretPosition.ParagraphIndex);
        _typingStyleOverride = GetCanonicalTypingStyle();
    }

    private static IReadOnlyList<string> SplitPastedText(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private DocumentRange ToCanonicalRange(
        DocumentPosition start,
        DocumentPosition end) =>
        new(ToCanonicalLocation(start), ToCanonicalLocation(end));

    private void ApplyCanonicalParagraphStyle(
        DocumentPosition legacyStart,
        DocumentPosition legacyEnd,
        ParagraphStyle style)
    {
        DocumentLocation start = ToCanonicalLocation(new DocumentPosition(
            legacyStart.ParagraphIndex,
            0));
        ParagraphBlock endParagraph = (ParagraphBlock)
            _editingSession.Document.FindBlock(
                ToCanonicalLocation(legacyEnd).BlockId)!;
        DocumentRange fullParagraphRange = new(
            start,
            new DocumentLocation(endParagraph.Id, endParagraph.TextLength));
        _editingSession.Formatting.ApplyFormatting(
            fullParagraphRange,
            textStyle =>
            {
                switch (style)
                {
                    case ParagraphStyle.Heading1:
                        textStyle.IsBold = true;
                        textStyle.FontSize = 16;
                        break;
                    case ParagraphStyle.Heading2:
                        textStyle.IsBold = true;
                        textStyle.FontSize = 14;
                        break;
                    case ParagraphStyle.Normal:
                        textStyle.IsBold = false;
                        textStyle.FontSize = 14;
                        break;
                }
            },
            properties =>
            {
                properties.Style = style;
                switch (style)
                {
                    case ParagraphStyle.Heading1:
                        properties.Alignment = GostAlignment.Center;
                        properties.FirstLineIndent = 0;
                        properties.PageBreakBefore = true;
                        break;
                    case ParagraphStyle.Heading2:
                        properties.Alignment = GostAlignment.Center;
                        properties.FirstLineIndent = 0;
                        properties.PageBreakBefore = false;
                        break;
                    case ParagraphStyle.Normal:
                        properties.Alignment = GostAlignment.Justify;
                        properties.FirstLineIndent = 47.0;
                        properties.PageBreakBefore = false;
                        break;
                }

                return properties;
            });
    }

    private void OnCanonicalDocumentChanged(
        object? sender,
        OperationHistoryChangedEventArgs args)
    {
        _typingStyleOverride = null;
        int startIndex = GetLegacyMutationStart(args.Operation);
        _legacyProjection.Project(_editingSession.Document);
        if (args.Operation is null)
        {
            return;
        }

        _caretPosition = _legacyProjection.ToLegacyPosition(
            _editingSession.Document,
            _editingSession.Selection.Caret);
        _selectionAnchor = _editingSession.Selection.Anchor.HasValue
            ? _legacyProjection.ToLegacyPosition(
                _editingSession.Document,
                _editingSession.Selection.Anchor.Value)
            : null;
        NotifyDocumentChanged(
            startIndex,
            DocumentChangeKind.Structure |
            DocumentChangeKind.Metrics |
            DocumentChangeKind.Paint);
    }

    private int GetLegacyMutationStart(IEditOperation? operation)
    {
        if (operation is null || operation.AffectedBlocks.Count == 0)
        {
            return 0;
        }

        int start = int.MaxValue;
        foreach (DocumentNodeId id in operation.AffectedBlocks)
        {
            int index = _legacyProjection.GetLegacyIndex(
                _editingSession.Document,
                id);
            if (index >= 0)
            {
                start = Math.Min(start, index);
            }
        }

        return start == int.MaxValue ? 0 : start;
    }

    private static bool MatchesLegacy(BlockNode block, Paragraph legacy)
    {
        if (block is FigureBlock figure && legacy.IsImage)
        {
            return figure.ImageId == legacy.ImageId;
        }

        if (block is PageBreakBlock && !legacy.IsImage)
        {
            return legacy.PageBreakBefore &&
                string.IsNullOrEmpty(legacy.GetPlainText());
        }

        if (block is CodeBlock code && !legacy.IsImage)
        {
            return legacy.Style == ParagraphStyle.Code &&
                string.Equals(
                    code.Code,
                    legacy.GetPlainText(),
                    StringComparison.Ordinal);
        }

        if (block is not ParagraphBlock paragraph || legacy.IsImage)
        {
            return false;
        }

        if (!string.Equals(
                paragraph.GetPlainText(),
                legacy.GetPlainText(),
                StringComparison.Ordinal) ||
            paragraph.Properties.Alignment != legacy.Alignment ||
            !paragraph.Properties.FirstLineIndent.Equals(
                legacy.FirstLineIndent) ||
            !paragraph.Properties.LineSpacing.Equals(legacy.LineSpacing) ||
            paragraph.Properties.Style != legacy.Style ||
            paragraph.Properties.PageBreakBefore != legacy.PageBreakBefore)
        {
            return false;
        }

        List<(string Text, TextStyle Style)> canonicalRuns = paragraph.Inlines
            .Select(inline => inline switch
            {
                TextInline text => (text.Text, text.Style),
                LinkInline link => (link.Text, link.Style),
                LineBreakInline => ("\n", new TextStyle()),
                _ => (string.Empty, new TextStyle())
            })
            .ToList();
        if (canonicalRuns.Count != legacy.Runs.Count)
        {
            return false;
        }

        for (int index = 0; index < canonicalRuns.Count; index++)
        {
            (string text, TextStyle style) = canonicalRuns[index];
            TextRun run = legacy.Runs[index];
            if (!string.Equals(text, run.Text, StringComparison.Ordinal) ||
                style.IsBold != run.IsBold ||
                style.IsItalic != run.IsItalic ||
                style.Color != run.Color ||
                !style.FontSize.Equals(run.FontSize))
            {
                return false;
            }
        }

        return true;
    }
}
