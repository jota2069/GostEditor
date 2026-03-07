using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine.Commands;

public class AppendParagraphsCommand : IEditorCommand
{
    private readonly DocumentEditor _editor;
    private readonly List<Paragraph> _paragraphsToAdd;

    private int _insertedIndex;
    private DocumentPosition _oldPosition;

    public AppendParagraphsCommand(DocumentEditor editor, List<Paragraph> paragraphsToAdd)
    {
        _editor = editor;
        _paragraphsToAdd = paragraphsToAdd;
    }

    public void Execute()
    {
        _oldPosition = _editor.CaretPosition;
        _insertedIndex = _editor.Document.Paragraphs.Count;

        _editor.Document.Paragraphs.AddRange(_paragraphsToAdd);

        int lastIndex = _editor.Document.Paragraphs.Count - 1;
        int lastOffset = _editor.Document.Paragraphs[lastIndex].GetPlainText().Length;

        _editor.CaretPosition = new DocumentPosition(lastIndex, lastOffset);
        _editor.SelectionAnchor = null;
    }

    public void Undo()
    {
        int countToRemove = _paragraphsToAdd.Count;
        for (int i = 0; i < countToRemove; i++)
        {
            _editor.Document.Paragraphs.RemoveAt(_insertedIndex);
        }

        _editor.CaretPosition = _oldPosition;
    }
}
