namespace GostEditor.Core.Interfaces;

/// <summary>
/// Интерфейс для любой операции в редакторе, поддерживающей отмену.
/// </summary>
public interface IEditorCommand
{
    /// <summary>
    /// Выполнение действия.
    /// </summary>
    void Execute();

    /// <summary>
    /// Отмена действия (возврат в исходное состояние).
    /// </summary>
    void Undo();
}
