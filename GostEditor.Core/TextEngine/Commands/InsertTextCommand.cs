using GostEditor.Core.Interfaces;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine.Commands;

/// <summary>
/// Команда вставки текста в документ. Поддерживает отмену (удаление вставленного).
/// </summary>
public class InsertTextCommand : IEditorCommand
{
    // ЯВНАЯ ТИПИЗАЦИЯ
    private readonly DocumentEditor _editor;
    private readonly string _textToInsert;
    private readonly DocumentPosition _startPosition;

    // Сохраняем позицию, где оказалась каретка ПОСЛЕ вставки,
    // чтобы знать, какой диапазон удалять при Undo
    private DocumentPosition _endPosition;

    public InsertTextCommand(DocumentEditor editor, string textToInsert, DocumentPosition startPosition)
    {
        _editor = editor;
        _textToInsert = textToInsert;
        _startPosition = startPosition;
    }

    public void Execute()
    {
        // 1. Вставляем текст в DOM-модель (используем твой существующий внутренний метод вставки)
        // Примечание: предполагается, что у тебя есть метод вроде InsertTextInternal,
        // который делает физическую вставку и возвращает новую позицию каретки.
        _endPosition = _editor.InsertTextInternal(_startPosition, _textToInsert);

        // 2. Обновляем каретку в UI
        _editor.CaretPosition = _endPosition;

        // Сбрасываем выделение, так как после ввода текста оно пропадает
        _editor.SelectionAnchor = null;
    }

    public void Undo()
    {
        // 1. Физически удаляем тот кусок текста, который только что вставили.
        // Передаем начальную и конечную позицию вставки.
        _editor.DeleteRangeInternal(_startPosition, _endPosition);

        // 2. Возвращаем каретку на исходное место (до ввода текста)
        _editor.CaretPosition = _startPosition;
        _editor.SelectionAnchor = null;
    }
}
