using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace GostEditor.UI.Views;

public partial class DocumentPageView : UserControl
{
    private bool _isUpdateQueued = false;
    private bool _isDragging = false;
    private bool _isProcessingOverflow = false;
    private int _dragStart = -1;
    private const double IndentSize = 47.0;

    public DocumentPageView(int pageNumber, string initialText = "")
    {
        InitializeComponent();

        PageNumberText.Text = pageNumber.ToString();
        PageTextBox.Text = initialText;

        PageTextBox.TextChanged += OnTextChanged;
        PageTextBox.KeyDown += OnTextBoxKeyDown;
        PageTextBox.PropertyChanged += PageTextBox_PropertyChanged;

        MouseHitLayer.PointerPressed += OnMouseHitLayerPointerPressed;
        MouseHitLayer.PointerMoved += OnMouseHitLayerPointerMoved;
        MouseHitLayer.PointerReleased += OnMouseHitLayerPointerReleased;
    }

    public event Action<DocumentPageView, string, int>? PageOverflow;
    public event Action<string>? TextChanged;
    public event Action<DocumentPageView, int>? RequestPageChange;
    public event Action<DocumentPageView>? PageInteraction;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(QueueCaretAndSelectionUpdate, DispatcherPriority.Loaded);
    }

    public void SelectAllText()
    {
        string text = PageTextBox.Text ?? string.Empty;
        PageTextBox.SelectionStart = 0;
        PageTextBox.SelectionEnd = text.Length;
        PageTextBox.CaretIndex = text.Length;
        QueueCaretAndSelectionUpdate();
    }

    public void ClearSelectionVisually()
    {
        PageTextBox.SelectionStart = PageTextBox.CaretIndex;
        PageTextBox.SelectionEnd = PageTextBox.CaretIndex;
        QueueCaretAndSelectionUpdate();
    }

    public void ClearFormatting()
    {
        string text = PageTextBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        int start = Math.Min(PageTextBox.SelectionStart, PageTextBox.SelectionEnd);
        int end = Math.Max(PageTextBox.SelectionStart, PageTextBox.SelectionEnd);

        if (start == end)
        {
            PageTextBox.Text = text.Replace("\uFEFF", "").Replace("\u2060", "");
            PageTextBox.Focus();
            return;
        }

        while (start > 0 && (text[start - 1] == '\uFEFF' || text[start - 1] == '\u2060')) start--;
        while (end < text.Length && (text[end] == '\uFEFF' || text[end] == '\u2060')) end++;

        string selected = text.Substring(start, end - start);
        string clean = selected.Replace("\uFEFF", "").Replace("\u2060", "");

        string newText = text.Remove(start, end - start).Insert(start, clean);
        PageTextBox.Text = newText;
        PageTextBox.SelectionStart = start;
        PageTextBox.SelectionEnd = start + clean.Length;
        PageTextBox.CaretIndex = PageTextBox.SelectionEnd;
        PageTextBox.Focus();

        QueueCaretAndSelectionUpdate();
    }

    public void WrapSelectedText(string marker)
    {
        string text = PageTextBox.Text ?? string.Empty;
        int start = Math.Min(PageTextBox.SelectionStart, PageTextBox.SelectionEnd);
        int end = Math.Max(PageTextBox.SelectionStart, PageTextBox.SelectionEnd);

        if (start == end)
        {
            string newText = text.Insert(start, $"{marker}{marker}");
            PageTextBox.Text = newText;
            PageTextBox.CaretIndex = start + marker.Length;
            PageTextBox.SelectionStart = PageTextBox.CaretIndex;
            PageTextBox.SelectionEnd = PageTextBox.CaretIndex;
        }
        else
        {
            string selected = text.Substring(start, end - start);
            string newText = text.Remove(start, end - start).Insert(start, $"{marker}{selected}{marker}");
            PageTextBox.Text = newText;
            PageTextBox.SelectionStart = start;
            PageTextBox.SelectionEnd = start + selected.Length + marker.Length * 2;
        }

        PageTextBox.Focus();
        QueueCaretAndSelectionUpdate();
    }

    public void InsertTextAtCaret(string textToInsert)
    {
        string text = PageTextBox.Text ?? string.Empty;
        int start = Math.Min(PageTextBox.SelectionStart, PageTextBox.SelectionEnd);
        int end = Math.Max(PageTextBox.SelectionStart, PageTextBox.SelectionEnd);

        string newText = text.Remove(start, end - start).Insert(start, textToInsert);
        PageTextBox.Text = newText;
        PageTextBox.SelectionStart = start + textToInsert.Length;
        PageTextBox.SelectionEnd = start + textToInsert.Length;
        PageTextBox.CaretIndex = PageTextBox.SelectionEnd;

        PageTextBox.Focus();
        QueueCaretAndSelectionUpdate();
    }

    private void OnMouseHitLayerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PageInteraction?.Invoke(this);

        PageTextBox.Focus();
        Point point = e.GetPosition(RichTextDisplay);
        TextLayout? layout = RichTextDisplay.TextLayout;

        if (layout != null)
        {
            TextHitTestResult hit = layout.HitTestPoint(point);
            _dragStart = hit.TextPosition;
            PageTextBox.SelectionStart = _dragStart;
            PageTextBox.SelectionEnd = _dragStart;
            PageTextBox.CaretIndex = _dragStart;
            _isDragging = true;
            QueueCaretAndSelectionUpdate();
        }
    }

    private void OnMouseHitLayerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging && RichTextDisplay.TextLayout != null)
        {
            Point point = e.GetPosition(RichTextDisplay);
            TextHitTestResult hit = RichTextDisplay.TextLayout.HitTestPoint(point);

            PageTextBox.SelectionStart = _dragStart;
            PageTextBox.SelectionEnd = hit.TextPosition;

            QueueCaretAndSelectionUpdate();
        }
    }

    private void OnMouseHitLayerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
    }

    private void PageTextBox_PropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.CaretIndexProperty ||
            e.Property == TextBox.SelectionStartProperty ||
            e.Property == TextBox.SelectionEndProperty)
        {
            QueueCaretAndSelectionUpdate();
        }
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isProcessingOverflow) return;

        string currentText = PageTextBox.Text ?? string.Empty;
        TextChanged?.Invoke(currentText);

        UpdateRichText(currentText);
        CheckOverflow();
    }

    private void UpdateRichText(string text)
    {
        RichTextDisplay.Inlines?.Clear();
        if (string.IsNullOrEmpty(text))
        {
            QueueCaretAndSelectionUpdate();
            return;
        }

        bool isBold = false;
        bool isItalic = false;
        System.Text.StringBuilder currentText = new System.Text.StringBuilder();

        void FlushText()
        {
            if (currentText.Length > 0)
            {
                RichTextDisplay.Inlines!.Add(new Run(currentText.ToString())
                {
                    FontWeight = isBold ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = isItalic ? FontStyle.Italic : FontStyle.Normal,
                    Foreground = Brushes.Black,
                    FontFamily = PageTextBox.FontFamily,
                    FontSize = PageTextBox.FontSize
                });
                currentText.Clear();
            }
        }

        foreach (char c in text)
        {
            if (c == '\uFEFF')
            {
                FlushText();
                isBold = !isBold;
                RichTextDisplay.Inlines!.Add(new Run("\uFEFF") { Foreground = Brushes.Transparent, FontSize = 0.01 });
            }
            else if (c == '\u2060')
            {
                FlushText();
                isItalic = !isItalic;
                RichTextDisplay.Inlines!.Add(new Run("\u2060") { Foreground = Brushes.Transparent, FontSize = 0.01 });
            }
            else
            {
                currentText.Append(c);
            }
        }

        FlushText();
        QueueCaretAndSelectionUpdate();
    }

    private void QueueCaretAndSelectionUpdate()
    {
        if (_isUpdateQueued || _isProcessingOverflow) return;
        _isUpdateQueued = true;
        Dispatcher.UIThread.Post(PerformCaretAndSelectionUpdate, DispatcherPriority.Render);
    }

    private void PerformCaretAndSelectionUpdate()
    {
        _isUpdateQueued = false;

        TextLayout? layout = RichTextDisplay.TextLayout;
        if (layout == null) return;

        string text = PageTextBox.Text ?? string.Empty;
        int caretIndex = Math.Min(PageTextBox.CaretIndex, text.Length);

        Rect hitTest = layout.HitTestTextPosition(caretIndex);
        double caretHeight = hitTest.Height > 0 ? hitTest.Height : 28.0;

        CustomCaret.Margin = new Thickness(hitTest.X, hitTest.Y, 0, 0);
        CustomCaret.Height = caretHeight;

        int start = PageTextBox.SelectionStart;
        int end = PageTextBox.SelectionEnd;

        if (start != end)
        {
            int min = Math.Min(start, end);
            int length = Math.Abs(start - end);

            IEnumerable<Rect> rects = layout.HitTestTextRange(min, length);
            IBrush selectionBrush = Brush.Parse("#440078D7");

            int rectIndex = 0;
            foreach (Rect rect in rects)
            {
                Rectangle highlight;
                if (rectIndex < SelectionCanvas.Children.Count)
                {
                    highlight = (Rectangle)SelectionCanvas.Children[rectIndex];
                    highlight.Fill = selectionBrush;
                    highlight.IsVisible = true;
                }
                else
                {
                    highlight = new Rectangle { Fill = selectionBrush };
                    SelectionCanvas.Children.Add(highlight);
                }

                highlight.Width = rect.Width;
                highlight.Height = rect.Height;
                Canvas.SetLeft(highlight, rect.X);
                Canvas.SetTop(highlight, rect.Y);
                rectIndex++;
            }

            for (int i = rectIndex; i < SelectionCanvas.Children.Count; i++)
            {
                SelectionCanvas.Children[i].IsVisible = false;
            }
        }
        else
        {
            foreach (Control child in SelectionCanvas.Children)
            {
                child.IsVisible = false;
            }
        }
    }

    private void CheckOverflow()
    {
        if (_isProcessingOverflow) return;

        string text = PageTextBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        int pageBreakIndex = text.IndexOf('\f');
        if (pageBreakIndex >= 0)
        {
            PerformSplit(pageBreakIndex, 1);
            return;
        }

        RichTextDisplay.Measure(new Size(643, double.PositiveInfinity));

        TextLayout? layout = RichTextDisplay.TextLayout;
        if (layout == null) return;

        if (layout.TextLines.Count <= 34) return;

        int splitIndex = 0;
        for (int i = 0; i < 34; i++)
        {
            splitIndex += layout.TextLines[i].Length;
        }

        if (splitIndex < text.Length && splitIndex > 0)
        {
            int lastSpace = text.LastIndexOfAny(new[] { ' ', '\n', '\r' }, splitIndex);
            if (lastSpace > 0 && (splitIndex - lastSpace) < 50)
            {
                splitIndex = lastSpace;
            }
        }

        if (splitIndex <= 0) splitIndex = 1;
        if (splitIndex >= text.Length) return;

        PerformSplit(splitIndex, 0);
    }

    private void PerformSplit(int splitIndex, int skipChars)
    {
        _isProcessingOverflow = true;

        string text = PageTextBox.Text ?? string.Empty;
        PageTextBox.TextChanged -= OnTextChanged;

        int originalCaret = PageTextBox.CaretIndex;

        string pageText = text.Substring(0, splitIndex);
        string overflowText = text.Substring(splitIndex + skipChars);

        PageTextBox.Text = pageText;
        UpdateRichText(pageText);

        int caretOffset = originalCaret - (splitIndex + skipChars);
        if (caretOffset < 0)
        {
            PageTextBox.CaretIndex = Math.Min(originalCaret, pageText.Length);
        }

        PageTextBox.TextChanged += OnTextChanged;
        _isProcessingOverflow = false;

        PageOverflow?.Invoke(this, overflowText, caretOffset);
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        bool isModifier = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0;
        if (!isModifier && e.Key != Key.LeftShift && e.Key != Key.RightShift)
        {
            PageInteraction?.Invoke(this);
        }

        string currentText = PageTextBox.Text ?? string.Empty;
        int length = currentText.Length;
        bool isShiftPressed = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (e.Key == Key.Right)
        {
            if (PageTextBox.CaretIndex >= length && !isShiftPressed)
            {
                RequestPageChange?.Invoke(this, 1);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Left)
        {
            if (PageTextBox.CaretIndex <= 0 && !isShiftPressed)
            {
                RequestPageChange?.Invoke(this, -1);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Up || e.Key == Key.Down)
        {
            TextLayout? layout = RichTextDisplay.TextLayout;
            if (layout != null)
            {
                int currentIndex = isShiftPressed ? PageTextBox.SelectionEnd : PageTextBox.CaretIndex;

                if (e.Key == Key.Up && currentIndex <= 0)
                {
                    if (!isShiftPressed) RequestPageChange?.Invoke(this, -1);
                    e.Handled = true;
                    return;
                }

                Rect currentHit = layout.HitTestTextPosition(currentIndex);
                double targetY = currentHit.Y + (e.Key == Key.Down ? 28.0 : -28.0);

                if (targetY >= layout.Height)
                {
                    if (!isShiftPressed) RequestPageChange?.Invoke(this, 1);
                }
                else if (targetY < 0)
                {
                    if (!isShiftPressed) RequestPageChange?.Invoke(this, -1);
                }
                else
                {
                    TextHitTestResult newHit = layout.HitTestPoint(new Point(currentHit.X, targetY));
                    if (isShiftPressed)
                    {
                        PageTextBox.SelectionEnd = newHit.TextPosition;
                    }
                    else
                    {
                        PageTextBox.CaretIndex = newHit.TextPosition;
                    }
                }
                e.Handled = true;
            }
        }
    }

    public string GetText() => PageTextBox.Text ?? string.Empty;
    public void SetPageNumber(int number) => PageNumberText.Text = number.ToString();

    public void FocusEditor(int index = -1)
    {
        PageTextBox.Focus();
        if (index >= 0 && index <= (PageTextBox.Text?.Length ?? 0))
        {
            PageTextBox.CaretIndex = index;
        }
        else
        {
            PageTextBox.CaretIndex = PageTextBox.Text?.Length ?? 0;
        }
        QueueCaretAndSelectionUpdate();
    }

    public void SetText(string text)
    {
        PageTextBox.TextChanged -= OnTextChanged;
        PageTextBox.Text = text;
        UpdateRichText(text);
        PageTextBox.TextChanged += OnTextChanged;

        Dispatcher.UIThread.Post(CheckOverflow, DispatcherPriority.Background);
    }
}
