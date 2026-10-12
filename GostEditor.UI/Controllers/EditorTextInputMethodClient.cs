using System;
using Avalonia;
using Avalonia.Input.TextInput;

namespace GostEditor.UI.Controllers;

internal sealed class EditorTextInputMethodClient : TextInputMethodClient
{
    private readonly Visual _visual;
    private readonly Action<string?> _preeditChanged;

    public EditorTextInputMethodClient(
        Visual visual,
        Action<string?> preeditChanged)
    {
        _visual = visual ?? throw new ArgumentNullException(nameof(visual));
        _preeditChanged = preeditChanged ??
            throw new ArgumentNullException(nameof(preeditChanged));
    }

    public override Visual TextViewVisual => _visual;

    public override bool SupportsPreedit => true;

    public override bool SupportsSurroundingText => false;

    public override string SurroundingText => string.Empty;

    public override Rect CursorRectangle => default;

    public override TextSelection Selection
    {
        get => new(0, 0);
        set { }
    }

    public override void SetPreeditText(string? text) =>
        _preeditChanged(text);

    public override void SetPreeditText(string? text, int? cursorOffset) =>
        _preeditChanged(text);
}
