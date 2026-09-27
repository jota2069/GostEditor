using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine.Commands;

/// <summary>
/// Stores only the paragraph range and optional image catalog changed by one
/// editor operation. This replaces whole-document snapshots in the active
/// editor history while preserving the existing .gost v2 runtime model.
/// </summary>
public sealed class DocumentMutationCommand : IEditorCommand
{
    private readonly DocumentEditor _editor;
    private readonly Action _action;
    private readonly Func<DocumentMutationRange> _beforeRangeProvider;
    private readonly Func<DocumentMutationRange> _afterRangeProvider;
    private readonly bool _captureImages;
    private readonly Func<bool>? _shouldCommit;
    private readonly DocumentChangeKind _changeKind;

    private MutationState? _before;
    private MutationState? _after;
    private bool _isFirstExecution = true;
    private bool _isCommitted;

    public DocumentMutationCommand(
        DocumentEditor editor,
        Func<DocumentMutationRange> beforeRangeProvider,
        Func<DocumentMutationRange> afterRangeProvider,
        Action action,
        DocumentChangeKind changeKind,
        bool captureImages = false,
        Func<bool>? shouldCommit = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _beforeRangeProvider = beforeRangeProvider
            ?? throw new ArgumentNullException(nameof(beforeRangeProvider));
        _afterRangeProvider = afterRangeProvider
            ?? throw new ArgumentNullException(nameof(afterRangeProvider));
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _changeKind = changeKind;
        _captureImages = captureImages;
        _shouldCommit = shouldCommit;
    }

    public bool IsCommitted => _isCommitted;

    public void Execute()
    {
        if (_isFirstExecution)
        {
            ExecuteFirstTime();
            return;
        }

        if (!_isCommitted || _after is null || _before is null)
        {
            return;
        }

        Restore(_after, _before.Range.Count);
        _editor.NotifyDocumentChanged(
            Math.Min(_before.Range.Start, _after.Range.Start),
            _changeKind);
    }

    public void Undo()
    {
        if (!_isCommitted || _before is null || _after is null)
        {
            return;
        }

        Restore(_before, _after.Range.Count);
        _editor.NotifyDocumentChanged(
            Math.Min(_before.Range.Start, _after.Range.Start),
            _changeKind);
    }

    private void ExecuteFirstTime()
    {
        DocumentMutationRange beforeRange =
            _beforeRangeProvider().Normalize(_editor.Document.Paragraphs.Count);
        _before = Capture(beforeRange);

        try
        {
            _action();
        }
        catch
        {
            Restore(_before, beforeRange.Count);
            throw;
        }

        DocumentMutationRange afterRange =
            _afterRangeProvider().Normalize(_editor.Document.Paragraphs.Count);
        _after = Capture(afterRange);
        CompactParagraphRanges();
        _isCommitted = _shouldCommit?.Invoke() ?? HasMeaningfulChange();
        _isFirstExecution = false;

        if (_isCommitted)
        {
            _editor.NotifyDocumentChanged(
                Math.Min(beforeRange.Start, afterRange.Start),
                _changeKind);
        }
    }


    private void CompactParagraphRanges()
    {
        if (_before is null || _after is null ||
            _before.Range.Start != _after.Range.Start)
        {
            return;
        }

        int prefix = 0;
        int comparable = Math.Min(
            _before.Paragraphs.Count,
            _after.Paragraphs.Count);
        while (prefix < comparable &&
               ParagraphEquals(
                   _before.Paragraphs[prefix],
                   _after.Paragraphs[prefix]))
        {
            prefix++;
        }

        int suffix = 0;
        int beforeRemaining = _before.Paragraphs.Count - prefix;
        int afterRemaining = _after.Paragraphs.Count - prefix;
        while (suffix < beforeRemaining &&
               suffix < afterRemaining &&
               ParagraphEquals(
                   _before.Paragraphs[_before.Paragraphs.Count - 1 - suffix],
                   _after.Paragraphs[_after.Paragraphs.Count - 1 - suffix]))
        {
            suffix++;
        }

        if (prefix == 0 && suffix == 0)
        {
            return;
        }

        _before = _before with
        {
            Range = new DocumentMutationRange(
                _before.Range.Start + prefix,
                _before.Paragraphs.Count - prefix - suffix),
            Paragraphs = _before.Paragraphs
                .Skip(prefix)
                .Take(_before.Paragraphs.Count - prefix - suffix)
                .ToList()
        };

        _after = _after with
        {
            Range = new DocumentMutationRange(
                _after.Range.Start + prefix,
                _after.Paragraphs.Count - prefix - suffix),
            Paragraphs = _after.Paragraphs
                .Skip(prefix)
                .Take(_after.Paragraphs.Count - prefix - suffix)
                .ToList()
        };
    }

    private MutationState Capture(DocumentMutationRange range)
    {
        List<Paragraph> paragraphs = new(range.Count);
        for (int index = 0; index < range.Count; index++)
        {
            paragraphs.Add(CloneParagraph(
                _editor.Document.Paragraphs[range.Start + index]));
        }

        List<ImageAttachment>? images = _captureImages
            ? new List<ImageAttachment>(_editor.Document.Images)
            : null;

        return new MutationState(
            range,
            paragraphs,
            images,
            CloneCounters(_editor.Document.Counters),
            _editor.CaretPosition,
            _editor.SelectionAnchor,
            _editor.SelectedImageParagraphIndex);
    }

    private void Restore(MutationState state, int currentRangeCount)
    {
        int safeStart = Math.Clamp(
            state.Range.Start,
            0,
            _editor.Document.Paragraphs.Count);
        int removableCount = Math.Clamp(
            currentRangeCount,
            0,
            _editor.Document.Paragraphs.Count - safeStart);

        if (removableCount > 0)
        {
            _editor.Document.Paragraphs.RemoveRange(
                safeStart,
                removableCount);
        }

        if (state.Paragraphs.Count > 0)
        {
            _editor.Document.Paragraphs.InsertRange(
                safeStart,
                state.Paragraphs.Select(CloneParagraph));
        }

        if (_editor.Document.Paragraphs.Count == 0)
        {
            _editor.Document.Paragraphs.Add(new Paragraph());
        }

        if (_captureImages && state.Images is not null)
        {
            _editor.Document.MutableImages.Clear();
            _editor.Document.MutableImages.AddRange(state.Images);
        }

        RestoreCounters(_editor.Document.Counters, state.Counters);
        _editor.CaretPosition = ClampPosition(state.Caret);
        _editor.SelectionAnchor = state.Selection.HasValue
            ? ClampPosition(state.Selection.Value)
            : null;
        _editor.SelectedImageParagraphIndex =
            state.SelectedImageParagraphIndex.HasValue
                ? Math.Clamp(
                    state.SelectedImageParagraphIndex.Value,
                    0,
                    _editor.Document.Paragraphs.Count - 1)
                : null;
    }

    private DocumentPosition ClampPosition(DocumentPosition position)
    {
        int paragraphIndex = Math.Clamp(
            position.ParagraphIndex,
            0,
            _editor.Document.Paragraphs.Count - 1);
        int offset = Math.Clamp(
            position.Offset,
            0,
            _editor.Document.Paragraphs[paragraphIndex]
                .GetPlainText()
                .Length);
        return new DocumentPosition(paragraphIndex, offset);
    }

    private bool HasMeaningfulChange()
    {
        if (_before is null || _after is null)
        {
            return false;
        }

        if (_before.Range != _after.Range ||
    _before.Paragraphs.Count != _after.Paragraphs.Count ||
    !_before.Caret.Equals(_after.Caret) ||
    !Nullable.Equals(
        _before.Selection,
        _after.Selection) ||
    _before.SelectedImageParagraphIndex !=
        _after.SelectedImageParagraphIndex)
        {
            return true;
        }

        for (int index = 0; index < _before.Paragraphs.Count; index++)
        {
            if (!ParagraphEquals(
                    _before.Paragraphs[index],
                    _after.Paragraphs[index]))
            {
                return true;
            }
        }

        if (_captureImages)
        {
            if (!ImageListsEqual(_before.Images, _after.Images))
            {
                return true;
            }
        }

        return !CountersEqual(_before.Counters, _after.Counters);
    }

    private static Paragraph CloneParagraph(Paragraph source)
    {
        Paragraph clone = new()
        {
            Alignment = source.Alignment,
            FirstLineIndent = source.FirstLineIndent,
            LineSpacing = source.LineSpacing,
            Style = source.Style,
            PageBreakBefore = source.PageBreakBefore,
            ImageId = source.ImageId,
            ImageWidth = source.ImageWidth,
            ImageHeight = source.ImageHeight
        };

        foreach (TextRun run in source.Runs)
        {
            clone.Runs.Add(new TextRun(
                run.Text,
                run.IsBold,
                run.IsItalic)
            {
                FontSize = run.FontSize,
                Color = run.Color
            });
        }

        return clone;
    }

    private static bool ParagraphEquals(Paragraph left, Paragraph right)
    {
        if (left.Alignment != right.Alignment ||
            !left.FirstLineIndent.Equals(right.FirstLineIndent) ||
            !left.LineSpacing.Equals(right.LineSpacing) ||
            left.Style != right.Style ||
            left.PageBreakBefore != right.PageBreakBefore ||
            left.ImageId != right.ImageId ||
            !left.ImageWidth.Equals(right.ImageWidth) ||
            !left.ImageHeight.Equals(right.ImageHeight) ||
            left.Runs.Count != right.Runs.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Runs.Count; index++)
        {
            TextRun leftRun = left.Runs[index];
            TextRun rightRun = right.Runs[index];
            if (leftRun.Text != rightRun.Text ||
                leftRun.IsBold != rightRun.IsBold ||
                leftRun.IsItalic != rightRun.IsItalic ||
                !leftRun.FontSize.Equals(rightRun.FontSize) ||
                leftRun.Color != rightRun.Color)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ImageListsEqual(
        IReadOnlyList<ImageAttachment>? left,
        IReadOnlyList<ImageAttachment>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            ImageAttachment a = left[index];
            ImageAttachment b = right[index];
            if (a.Id != b.Id ||
                a.FileName != b.FileName ||
                a.MediaType != b.MediaType ||
                a.Caption != b.Caption ||
                a.Order != b.Order ||
                !a.Data.Span.SequenceEqual(b.Data.Span))
            {
                return false;
            }
        }

        return true;
    }

    private static DocumentCounters CloneCounters(DocumentCounters source) =>
        new()
        {
            ImagesCount = source.ImagesCount,
            TablesCount = source.TablesCount,
            SourcesCount = source.SourcesCount,
            PagesCount = source.PagesCount,
            ApplicationsCount = source.ApplicationsCount
        };

    private static void RestoreCounters(
        DocumentCounters target,
        DocumentCounters source)
    {
        target.ImagesCount = source.ImagesCount;
        target.TablesCount = source.TablesCount;
        target.SourcesCount = source.SourcesCount;
        target.PagesCount = source.PagesCount;
        target.ApplicationsCount = source.ApplicationsCount;
    }

    private static bool CountersEqual(
        DocumentCounters left,
        DocumentCounters right) =>
        left.ImagesCount == right.ImagesCount &&
        left.TablesCount == right.TablesCount &&
        left.SourcesCount == right.SourcesCount &&
        left.PagesCount == right.PagesCount &&
        left.ApplicationsCount == right.ApplicationsCount;

    private sealed record MutationState(
        DocumentMutationRange Range,
        List<Paragraph> Paragraphs,
        List<ImageAttachment>? Images,
        DocumentCounters Counters,
        DocumentPosition Caret,
        DocumentPosition? Selection,
        int? SelectedImageParagraphIndex);
}
