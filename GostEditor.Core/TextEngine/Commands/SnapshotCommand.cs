using System;
using System.Collections.Generic;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;

namespace GostEditor.Core.TextEngine.Commands;

public class SnapshotCommand : IEditorCommand
{
    private readonly DocumentEditor _editor;
    private readonly Action _action;

    private DocumentSnapshot _oldState = new DocumentSnapshot();
    private DocumentSnapshot _newState = new DocumentSnapshot();

    private bool _isFirstExecution = true;

    public SnapshotCommand(DocumentEditor editor, Action action)
    {
        _editor = editor;
        _action = action;
    }

    public void Execute()
    {
        if (_isFirstExecution)
        {
            _oldState = CaptureState();

            _action.Invoke();

            _newState = CaptureState();

            _isFirstExecution = false;
        }
        else
        {
            RestoreState(_newState);
        }
    }

    public void Undo()
    {
        RestoreState(_oldState);
    }

    private DocumentSnapshot CaptureState()
    {
        return new DocumentSnapshot
        {
            Paragraphs = CloneParagraphs(_editor.Document.Paragraphs),
            Images = new List<ImageAttachment>(_editor.Document.Images),
            Caret = _editor.CaretPosition,
            Selection = _editor.SelectionAnchor,
            SelectedImageParagraphIndex = _editor.SelectedImageParagraphIndex,
            ImagesCount = _editor.Document.Counters.ImagesCount
        };
    }

    private void RestoreState(DocumentSnapshot state)
    {
        _editor.Document.Paragraphs.Clear();

        foreach (Paragraph p in CloneParagraphs(state.Paragraphs))
        {
            _editor.Document.Paragraphs.Add(p);
        }

        _editor.Document.MutableImages.Clear();
        _editor.Document.MutableImages.AddRange(state.Images);
        _editor.Document.Counters.ImagesCount = state.ImagesCount;
        _editor.CaretPosition = state.Caret;
        _editor.SelectionAnchor = state.Selection;
        _editor.SelectedImageParagraphIndex = state.SelectedImageParagraphIndex;
    }

    private static List<Paragraph> CloneParagraphs(List<Paragraph> source)
    {
        List<Paragraph> list = new List<Paragraph>(source.Count);

        foreach (Paragraph p in source)
        {
            Paragraph newP = new Paragraph
            {
                Alignment = p.Alignment,
                FirstLineIndent = p.FirstLineIndent,
                LineSpacing = p.LineSpacing,
                Style = p.Style,
                PageBreakBefore = p.PageBreakBefore,
                ImageId = p.ImageId,
                ImageWidth = p.ImageWidth,
                ImageHeight = p.ImageHeight
            };

            foreach (TextRun run in p.Runs)
            {
                newP.Runs.Add(new TextRun(run.Text, run.IsBold, run.IsItalic)
                {
                    FontSize = run.FontSize,
                    Color = run.Color
                });
            }

            list.Add(newP);
        }

        return list;
    }

    private sealed class DocumentSnapshot
    {
        public List<Paragraph> Paragraphs { get; init; } = new List<Paragraph>();
        public List<ImageAttachment> Images { get; init; } = new List<ImageAttachment>();
        public DocumentPosition Caret { get; init; }
        public DocumentPosition? Selection { get; init; }
        public int? SelectedImageParagraphIndex { get; init; }
        public int ImagesCount { get; init; }
    }
}
