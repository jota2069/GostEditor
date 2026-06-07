using System;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.UI.Controllers;

/// <summary>
/// Контроллер для управления текстовым выделением в документе.
/// Отвечает за логику выделения, очистки и получения выделенного текста.
/// </summary>
public class SelectionController
{
    private readonly DocumentEditor _editor;

    public SelectionController(DocumentEditor editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
    }

    /// <summary>
    /// Проверяет, выделен ли сейчас какой-либо текст в документе
    /// </summary>
    public bool HasSelection => _editor.HasSelection;

    /// <summary>
    /// Снимает выделение текста
    /// </summary>
    public void ClearSelection()
    {
        _editor.ClearSelection();
    }

    /// <summary>
    /// Выделяет весь текст в документе (Ctrl+A)
    /// </summary>
    public void SelectAll()
    {
        _editor.SelectAll();
    }

    /// <summary>
    /// Возвращает текст, который сейчас выделен
    /// </summary>
    public string GetSelectedText()
    {
        return _editor.GetSelectedText();
    }

    /// <summary>
    /// Устанавливает выделение текста от указанного якоря до текущей позиции каретки
    /// </summary>
    /// <param name="anchor">Точка начала выделения</param>
    /// <param name="caret">Текущая позиция курсора</param>
    public void SetSelection(DocumentPosition anchor, DocumentPosition caret)
    {
        _editor.SelectionAnchor = anchor;
        _editor.CaretPosition = caret;
    }
}
