#pragma warning disable CS0618 // Отключаем назойливые предупреждения буфера обмена для всего файла

using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.UI.Controllers;

public class TextInputController
{
    private readonly DocumentEditor _editor;
    private readonly RenderController _renderController;

    public TextInputController(DocumentEditor editor, RenderController renderController)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _renderController = renderController ?? throw new ArgumentNullException(nameof(renderController));
    }

    public async Task HandleTextInputAsync(string text, IClipboard? clipboard)
    {
        if (string.IsNullOrEmpty(text)) return;

        _editor.SelectedImageParagraphIndex = null;
        _editor.InsertText(text);
        _renderController.RefreshView();

        await Task.CompletedTask;
    }

    public async Task HandleKeyDownAsync(KeyEventArgs e, IClipboard? clipboard)
    {
        if (_editor is null) return;

        bool isShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        bool isCtrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

        DocumentPosition? oldAnchor = _editor.SelectionAnchor;

        if (isCtrl)
        {
            switch (e.Key)
            {
                case Key.A:
                    _editor.SelectAll();
                    _renderController.RefreshView();
                    e.Handled = true;
                    return;
                case Key.Z:
                    _editor.History.Undo();
                    _renderController.RefreshView();
                    e.Handled = true;
                    return;
                case Key.Y:
                    _editor.History.Redo();
                    _renderController.RefreshView();
                    e.Handled = true;
                    return;
                case Key.C:
                    if (_editor.HasSelection && clipboard != null)
                        await clipboard.SetTextAsync(_editor.GetSelectedText());
                    e.Handled = true;
                    return;
                case Key.X:
                    if (_editor.HasSelection && clipboard != null)
                    {
                        await clipboard.SetTextAsync(_editor.GetSelectedText());
                        _editor.DeleteSelection();
                        _renderController.RefreshView();
                    }
                    e.Handled = true;
                    return;
                case Key.V:
                    if (clipboard != null) await HandlePasteAsync(clipboard);
                    e.Handled = true;
                    return;
            }
        }

        bool handled = await HandleNavigationKeyAsync(e.Key, oldAnchor, isShift);
        if (handled)
        {
            _renderController.RefreshView();
            e.Handled = true;
        }
    }

    private Task<bool> HandleNavigationKeyAsync(Key key, DocumentPosition? oldAnchor, bool isShift)
    {
        bool handled = true;

        DocumentPosition? anchorToKeep = isShift ? (oldAnchor ?? _editor.CaretPosition) : null;

        switch (key)
        {
            case Key.Back: _editor.Backspace(); break;
            case Key.Delete:
                if (_editor.SelectedImageParagraphIndex.HasValue)
                {
                    _editor.ExecuteWithSnapshot(() =>
                    {
                        if (_editor.Document != null && _editor.SelectedImageParagraphIndex.Value < _editor.Document.Paragraphs.Count)
                            _editor.Document.Paragraphs.RemoveAt(_editor.SelectedImageParagraphIndex.Value);
                        _editor.ClearSelection();
                    });
                }
                else if (_editor.HasSelection)
                {
                    _editor.DeleteSelection();
                }
                else
                {
                    DocumentPosition oldPosition = _editor.CaretPosition;
                    _editor.MoveRight();
                    if (_editor.CaretPosition.CompareTo(oldPosition) != 0)
                    {
                        _editor.Backspace();
                    }
                }
                break;
            case Key.Enter: _editor.InsertNewLine(); break;
            case Key.Left:
                _editor.MoveLeft();
                _editor.SelectionAnchor = isShift ? anchorToKeep : _editor.CaretPosition;
                break;
            case Key.Right:
                _editor.MoveRight();
                _editor.SelectionAnchor = isShift ? anchorToKeep : _editor.CaretPosition;
                break;
            case Key.Up:
                _editor.MoveLeft();
                _editor.SelectionAnchor = isShift ? anchorToKeep : _editor.CaretPosition;
                break;
            case Key.Down:
                _editor.MoveRight();
                _editor.SelectionAnchor = isShift ? anchorToKeep : _editor.CaretPosition;
                break;
            default: handled = false; break;
        }
        return Task.FromResult(handled);
    }

    private async Task HandlePasteAsync(IClipboard clipboard)
    {
        string? text = await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            _editor.SelectedImageParagraphIndex = null;
            _editor.PasteText(text);
            _renderController.RefreshView();
        }
    }
}
