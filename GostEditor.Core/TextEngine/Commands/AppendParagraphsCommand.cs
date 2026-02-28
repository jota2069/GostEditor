using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine.Commands;

/// <summary>
/// Команда массового добавления абзацев в конец документа (используется для листингов кода).
/// </summary>
public class AppendParagraphsCommand : IEditorCommand
{
    private readonly DocumentEditor _editor;
    private readonly List<Paragraph> _paragraphsToAdd;

    private int _insertedIndex;
    private DocumentPosition _oldPosition;

    public AppendParagraphsCommand(DocumentEditor editor, List<Paragraph> paragraphsToAdd)
    {
        // ЯВНАЯ ТИПИЗАЦИЯ
        _editor = editor;
        _paragraphsToAdd = paragraphsToAdd;
    }

    public void Execute()
    {
        // 1. Запоминаем, где была каретка и с какого индекса начинаем вставку
        _oldPosition = _editor.CaretPosition;
        _insertedIndex = _editor.Document.Paragraphs.Count;

        // 2. Физически добавляем все абзацы в конец документа
        _editor.Document.Paragraphs.AddRange(_paragraphsToAdd);

        // 3. Перемещаем каретку в самый конец вставленного текста
        int lastIndex = _editor.Document.Paragraphs.Count - 1;
        int lastOffset = _editor.Document.Paragraphs[lastIndex].GetPlainText().Length;

        _editor.CaretPosition = new DocumentPosition(lastIndex, lastOffset);
        _editor.SelectionAnchor = null;
    }

    public void Undo()
    {
        // 1. Удаляем ровно то количество абзацев, которое вставили, начиная с сохраненного индекса
        int countToRemove = _paragraphsToAdd.Count;
        for (int i = 0; i < countToRemove; i++)
        {
            _editor.Document.Paragraphs.RemoveAt(_insertedIndex);
        }

        // 2. Возвращаем каретку на место
        _editor.CaretPosition = _oldPosition;
    }
}
