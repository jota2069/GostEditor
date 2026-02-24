using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace GostEditor.UI.Views;

public partial class DocumentPageView : UserControl
{
    // Флаг для защиты от спама расчетами (Debouncing)
    private bool _isUpdateQueued = false;

    public DocumentPageView(int pageNumber, string initialText = "")
    {
        InitializeComponent();

        PageNumberText.Text = pageNumber.ToString();
        PageTextBox.Text = initialText;

        PageTextBox.TextChanged += OnTextChanged;
        PageTextBox.KeyDown += OnTextBoxKeyDown;

        PageTextBox.PropertyChanged += PageTextBox_PropertyChanged;
    }

    public event Action<DocumentPageView, string, int>? PageOverflow;
    public event Action<string>? TextChanged;
    public event Action<DocumentPageView, int>? RequestPageChange;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(CheckOverflow, Avalonia.Threading.DispatcherPriority.Loaded);
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
        string currentText = PageTextBox.Text ?? string.Empty;

        TextChanged?.Invoke(currentText);
        UpdateRichText(currentText);

        Avalonia.Threading.Dispatcher.UIThread.Post(CheckOverflow, Avalonia.Threading.DispatcherPriority.Background);
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
                RichTextDisplay.Inlines!.Add(new Run("\uFEFF")
                {
                    Foreground = Brushes.Transparent,
                    FontFamily = PageTextBox.FontFamily,
                    FontSize = PageTextBox.FontSize
                });
            }
            else if (c == '\u2060')
            {
                FlushText();
                isItalic = !isItalic;
                RichTextDisplay.Inlines!.Add(new Run("\u2060")
                {
                    Foreground = Brushes.Transparent,
                    FontFamily = PageTextBox.FontFamily,
                    FontSize = PageTextBox.FontSize
                });
            }
            else
            {
                currentText.Append(c);
            }
        }

        FlushText();
        QueueCaretAndSelectionUpdate();
    }

    // Если прилетело 10 команд на обновление за миллисекунду, выполнится только 1!
    private void QueueCaretAndSelectionUpdate()
    {
        if (_isUpdateQueued) return;
        _isUpdateQueued = true;

        Avalonia.Threading.Dispatcher.UIThread.Post(PerformCaretAndSelectionUpdate, Avalonia.Threading.DispatcherPriority.Render);
    }

    //ОПТИМИЗАЦИЯ 2: Пул объектов (Object Pooling)
    private void PerformCaretAndSelectionUpdate()
    {
        _isUpdateQueued = false; // Сбрасываем флаг

        TextLayout layout = RichTextDisplay.TextLayout;
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

                // Переиспользуем старые квадраты вместо создания новых (БЕЗ CLEAR!)
                if (rectIndex < SelectionCanvas.Children.Count)
                {
                    highlight = (Rectangle)SelectionCanvas.Children[rectIndex];
                    highlight.IsVisible = true;
                }
                else
                {
                    highlight = new Rectangle { Fill = selectionBrush };
                    SelectionCanvas.Children.Add(highlight);
                }

                highlight.Width = rect.Width;
                highlight.Height = rect.Height;
                highlight.Margin = new Thickness(rect.X, rect.Y, 0, 0);
                rectIndex++;
            }

            // Прячем лишние квадраты, которые остались от прошлого выделения
            for (int i = rectIndex; i < SelectionCanvas.Children.Count; i++)
            {
                SelectionCanvas.Children[i].IsVisible = false;
            }
        }
        else
        {
            // Если ничего не выделено - просто прячем все квадраты
            foreach (Control child in SelectionCanvas.Children)
            {
                child.IsVisible = false;
            }
        }
    }

    private void CheckOverflow()
    {
        const double maxHeight = 952;

        TextLayout layout = RichTextDisplay.TextLayout;
        if (layout == null || layout.Height <= maxHeight) return;

        PageTextBox.TextChanged -= OnTextChanged;
        int originalCaret = PageTextBox.CaretIndex;
        string text = PageTextBox.Text ?? string.Empty;

        double currentHeight = 0;
        int splitIndex = 0;

        foreach (TextLine line in layout.TextLines)
        {
            double lineHeight = line.Height < 28 ? 28 : line.Height;
            if (currentHeight + lineHeight > maxHeight) break;
            currentHeight += lineHeight;
            splitIndex += line.Length;
        }

        if (splitIndex < text.Length && splitIndex > 0)
        {
            int lastSpace = text.LastIndexOfAny([' ', '\n', '\r'], splitIndex);
            if (lastSpace > 0 && (splitIndex - lastSpace) < 50)
            {
                splitIndex = lastSpace;
            }
        }

        if (splitIndex <= 0) splitIndex = 1;

        int caretOffset = originalCaret - splitIndex;
        string pageText = text[..splitIndex];
        string overflowText = text[splitIndex..];

        PageTextBox.Text = pageText;
        UpdateRichText(pageText);
        PageTextBox.TextChanged += OnTextChanged;

        if (caretOffset < 0)
        {
            PageTextBox.CaretIndex = Math.Min(originalCaret, pageText.Length);
        }

        if (!string.IsNullOrEmpty(overflowText))
        {
            PageOverflow?.Invoke(this, overflowText, caretOffset);
        }
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        string currentText = PageTextBox.Text ?? string.Empty;
        int length = currentText.Length;

        if (e.Key == Key.Right)
        {
            if (PageTextBox.CaretIndex >= length)
            {
                RequestPageChange?.Invoke(this, 1);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Down)
        {
            if (currentText.IndexOf('\n', PageTextBox.CaretIndex) == -1 &&
                PageTextBox.CaretIndex >= length - 60)
            {
                RequestPageChange?.Invoke(this, 1);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Up || e.Key == Key.Left)
        {
            if (PageTextBox.CaretIndex <= 0)
            {
                RequestPageChange?.Invoke(this, -1);
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
    }

    public void SetText(string text)
    {
        PageTextBox.TextChanged -= OnTextChanged;
        PageTextBox.Text = text;
        UpdateRichText(text);
        PageTextBox.TextChanged += OnTextChanged;
        Avalonia.Threading.Dispatcher.UIThread.Post(CheckOverflow, Avalonia.Threading.DispatcherPriority.Background);
    }
}
