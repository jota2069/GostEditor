using System;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input; // ДОБАВЛЕНО для работы с клавиатурой
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace GostEditor.UI.Views;

public partial class DocumentPageView : UserControl
{
    public DocumentPageView(int pageNumber, string initialText = "")
    {
        InitializeComponent();

        PageNumberText.Text = pageNumber.ToString();
        PageTextBox.Text = initialText;
        PageTextBox.TextChanged += OnTextChanged;

        // ДОБАВЛЕНО: Слушаем нажатия стрелочек на клавиатуре
        PageTextBox.KeyDown += OnTextBoxKeyDown;
    }

    public event Action<DocumentPageView, string, int>? PageOverflow;
    public event Action<string>? TextChanged;

    // ДОБАВЛЕНО: Событие запроса перехода (direction: 1 = вперед, -1 = назад)
    public event Action<DocumentPageView, int>? RequestPageChange;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(CheckOverflow, Avalonia.Threading.DispatcherPriority.Loaded);
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
        if (string.IsNullOrEmpty(text)) return;

        Regex regex = new Regex(@"(\*\*.*?\*\*|\*.*?\*)");
        string[] parts = regex.Split(text);

        foreach (string part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if (part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4)
            {
                RichTextDisplay.Inlines!.Add(new Run("**") { Foreground = Brushes.Transparent });
                RichTextDisplay.Inlines!.Add(new Run(part.Substring(2, part.Length - 4))
                {
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.Black
                });
                RichTextDisplay.Inlines!.Add(new Run("**") { Foreground = Brushes.Transparent });
            }
            else if (part.StartsWith("*") && part.EndsWith("*") && part.Length >= 2)
            {
                RichTextDisplay.Inlines!.Add(new Run("*") { Foreground = Brushes.Transparent });
                RichTextDisplay.Inlines!.Add(new Run(part.Substring(1, part.Length - 2))
                {
                    FontStyle = FontStyle.Italic,
                    Foreground = Brushes.Black
                });
                RichTextDisplay.Inlines!.Add(new Run("*") { Foreground = Brushes.Transparent });
            }
            else
            {
                RichTextDisplay.Inlines!.Add(new Run(part) { Foreground = Brushes.Black });
            }
        }
    }

    private void CheckOverflow()
    {
        const double maxHeight = 952;
        double width = PageTextBox.Bounds.Width > 0 ? PageTextBox.Bounds.Width : 643;

        string text = PageTextBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        TextLayout textLayout = new TextLayout(
            text: text,
            typeface: new Typeface(PageTextBox.FontFamily),
            fontSize: PageTextBox.FontSize,
            foreground: null,
            textAlignment: TextAlignment.Left,
            textWrapping: TextWrapping.Wrap,
            maxWidth: width,
            maxHeight: double.PositiveInfinity,
            lineHeight: 28.0);

        if (textLayout.Height <= maxHeight) return;

        PageTextBox.TextChanged -= OnTextChanged;
        int originalCaret = PageTextBox.CaretIndex;

        double currentHeight = 0;
        int splitIndex = 0;

        foreach (TextLine line in textLayout.TextLines)
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

    // ДОБАВЛЕНО: Логика перехода между страницами по стрелочкам
    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        int length = PageTextBox.Text?.Length ?? 0;

        // Если нажали Вниз или Вправо, находясь в самом конце текста
        if (e.Key == Key.Down || e.Key == Key.Right)
        {
            if (PageTextBox.CaretIndex >= length)
            {
                RequestPageChange?.Invoke(this, 1);
                e.Handled = true; // Глушим стандартное поведение
            }
        }
        // Если нажали Вверх или Влево, находясь в самом начале текста
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
