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
    private double? _preferredHorizontalX;
    private DocumentPosition? _lastVerticalCaret;

    public TextInputController(DocumentEditor editor, RenderController renderController)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _renderController = renderController ?? throw new ArgumentNullException(nameof(renderController));
    }

    public async Task HandleTextInputAsync(string text, IClipboard? clipboard)
    {
        if (string.IsNullOrEmpty(text) ||
            !_editor.TextBoundaries.IsValidUtf16(text))
        {
            return;
        }

        // Avalonia TextInput payloads are the composition boundary here: this
        // path accepts committed text only and does not emulate platform IME
        // pre-edit state. Separate committed chunks are re-segmented by Core.
        ResetVerticalNavigation();
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
                    ResetVerticalNavigation();
                    _editor.History.Undo();
                    _renderController.RefreshView();
                    e.Handled = true;
                    return;
                case Key.Y:
                    ResetVerticalNavigation();
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

        bool handled = await HandleNavigationKeyAsync(e.Key, isShift);
        if (handled)
        {
            _renderController.RefreshView();
            e.Handled = true;
        }
    }

    internal Task<bool> HandleNavigationKeyAsync(
        Key key,
        bool isShift)
    {
        bool handled = true;

        switch (key)
        {
            case Key.Back:
                ResetVerticalNavigation();
                _editor.Backspace();
                break;
            case Key.Delete:
                ResetVerticalNavigation();
                if (_editor.SelectedImageParagraphIndex.HasValue)
                {
                    _editor.RemoveImage(_editor.SelectedImageParagraphIndex.Value);
                }
                else
                {
                    _editor.DeleteForward();
                }
                break;
            case Key.Enter:
                ResetVerticalNavigation();
                _editor.InsertNewLine();
                break;
            case Key.Left:
                ResetVerticalNavigation();
                _editor.MoveLeft(isShift);
                break;
            case Key.Right:
                ResetVerticalNavigation();
                _editor.MoveRight(isShift);
                break;
            case Key.Up:
                MoveVertical(-1, isShift);
                break;
            case Key.Down:
                MoveVertical(1, isShift);
                break;
            default: handled = false; break;
        }
        return Task.FromResult(handled);
    }

    private void MoveVertical(int lineDelta, bool extendSelection)
    {
        if (!_lastVerticalCaret.HasValue ||
            !_lastVerticalCaret.Value.Equals(_editor.CaretPosition))
        {
            _preferredHorizontalX = null;
        }

        if (_renderController.TryGetVerticalCaretPosition(
                lineDelta,
                _preferredHorizontalX,
                out DocumentPosition target,
                out double resolvedPreferredX))
        {
            _preferredHorizontalX = resolvedPreferredX;
            _editor.MoveCaret(target, extendSelection);
        }

        _lastVerticalCaret = _editor.CaretPosition;
    }

    private void ResetVerticalNavigation()
    {
        _preferredHorizontalX = null;
        _lastVerticalCaret = null;
    }

    private async Task HandlePasteAsync(IClipboard clipboard)
    {
        string? text = await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text) &&
            _editor.TextBoundaries.IsValidUtf16(text))
        {
            ResetVerticalNavigation();
            _editor.SelectedImageParagraphIndex = null;
            _editor.PasteText(text);
            _renderController.RefreshView();
        }
    }
}
