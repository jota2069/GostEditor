#pragma warning disable CS0618 // Отключаем назойливые предупреждения буфера обмена для всего файла

using System;
using System.Globalization;
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
    private Key? _directTextKey;
    private bool _compositionPending;

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

        bool isCompositionCommit = _compositionPending ||
            !_directTextKey.HasValue ||
            !IsSingleTextElement(text) ||
            _directTextKey == Key.Space && text != " ";
        _directTextKey = null;
        _compositionPending = false;
        ResetVerticalNavigation();
        _editor.SelectedImageParagraphIndex = null;
        if (isCompositionCommit)
        {
            _editor.InsertCompositionText(text);
        }
        else
        {
            _editor.InsertText(text);
        }
        _renderController.RefreshView();

        await Task.CompletedTask;
    }

    internal void NotifyPreeditText(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _compositionPending = true;
        }

        _editor.BreakHistoryCoalescing();
    }

    public async Task HandleKeyDownAsync(KeyEventArgs e, IClipboard? clipboard)
    {
        if (_editor is null) return;

        bool isShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        bool isCtrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool isAlt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
        _directTextKey = !isCtrl && !isAlt && IsTextProducingKey(e.Key)
            ? e.Key
            : null;

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

    private static bool IsTextProducingKey(Key key) =>
        key is >= Key.A and <= Key.Z or
            >= Key.D0 and <= Key.D9 or
            >= Key.NumPad0 and <= Key.NumPad9 or
            Key.Space or
            Key.Oem1 or Key.Oem2 or Key.Oem3 or Key.Oem4 or Key.Oem5 or
            Key.Oem6 or Key.Oem7 or Key.Oem8 or Key.Oem102 or
            Key.OemComma or Key.OemMinus or Key.OemPeriod or Key.OemPlus;

    private static bool IsSingleTextElement(string text) =>
        StringInfo.GetNextTextElementLength(text) == text.Length;

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
