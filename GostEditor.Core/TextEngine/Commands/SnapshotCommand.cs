using System;
using System.Collections.Generic;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Core.Interfaces;

namespace GostEditor.Core.TextEngine.Commands;

public class SnapshotCommand : IEditorCommand
{
    private readonly DocumentEditor _editor;
    private readonly Action _action;

    private List<Paragraph> _oldParagraphs = new List<Paragraph>();
    private DocumentPosition _oldCaret;
    private DocumentPosition? _oldSelection;

    private List<Paragraph> _newParagraphs = new List<Paragraph>();
    private DocumentPosition _newCaret;
    private DocumentPosition? _newSelection;

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
            _oldParagraphs = CloneDocument(_editor.Document.Paragraphs);
            _oldCaret = _editor.CaretPosition;
            _oldSelection = _editor.SelectionAnchor;

            _action.Invoke();

            _newParagraphs = CloneDocument(_editor.Document.Paragraphs);
            _newCaret = _editor.CaretPosition;
            _newSelection = _editor.SelectionAnchor;

            _isFirstExecution = false;
        }
        else
        {
            RestoreState(_newParagraphs, _newCaret, _newSelection);
        }
    }

    public void Undo()
    {
        RestoreState(_oldParagraphs, _oldCaret, _oldSelection);
    }

    private void RestoreState(List<Paragraph> paragraphs, DocumentPosition caret, DocumentPosition? selection)
    {
        _editor.Document.Paragraphs.Clear();

        foreach (Paragraph p in CloneDocument(paragraphs))
        {
            _editor.Document.Paragraphs.Add(p);
        }

        _editor.CaretPosition = caret;
        _editor.SelectionAnchor = selection;
    }

    private List<Paragraph> CloneDocument(List<Paragraph> source)
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
                ImageData = p.ImageData,
                ImageWidth = p.ImageWidth,
                ImageHeight = p.ImageHeight
            };

            foreach (TextRun run in p.Runs)
            {
                newP.Runs.Add(new TextRun(run.Text, run.IsBold, run.IsItalic) { FontSize = run.FontSize });
            }

            list.Add(newP);
        }

        return list;
    }
}
