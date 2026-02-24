using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting; // Низкоуровневый быстрый движок

namespace GostEditor.UI.Views;

public partial class DocumentPageView : UserControl
{
    public DocumentPageView(int pageNumber, string initialText = "")
    {
        InitializeComponent();

        PageNumberText.Text = pageNumber.ToString();
        PageTextBox.Text = initialText;
        PageTextBox.TextChanged += OnTextChanged;
    }

    // 🔥 ИСПРАВЛЕНИЕ 1: Теперь мы передаем саму страницу (DocumentPageView sender)
    public event Action<DocumentPageView, string, bool>? PageOverflow;
    public event Action<string>? TextChanged;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(CheckOverflow, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        TextChanged?.Invoke(PageTextBox.Text ?? string.Empty);
        Avalonia.Threading.Dispatcher.UIThread.Post(CheckOverflow, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void CheckOverflow()
    {
        const double maxHeight = 1007;
        double width = PageTextBox.Bounds.Width > 0 ? PageTextBox.Bounds.Width : 643;

        string text = PageTextBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        // Мгновенный расчет строк в оперативной памяти (без зависаний)
        var textLayout = new TextLayout(
            text,
            new Typeface(PageTextBox.FontFamily),
            PageTextBox.FontSize,
            null,
            TextAlignment.Left,
            TextWrapping.Wrap,
            maxWidth: width,
            maxHeight: double.PositiveInfinity);

        // Если влезло - ничего не делаем
        if (textLayout.Height <= maxHeight) return;

        PageTextBox.TextChanged -= OnTextChanged;
        int originalCaret = PageTextBox.CaretIndex;

        double currentHeight = 0;
        int splitIndex = 0;

        foreach (var line in textLayout.TextLines)
        {
            // Принудительно учитываем LineHeight="28" из XAML
            double lineHeight = line.Height < 28 ? 28 : line.Height;

            if (currentHeight + lineHeight > maxHeight) break;
            currentHeight += lineHeight;
            splitIndex += line.Length;
        }

        // Защита от разрыва слов пополам
        if (splitIndex < text.Length && splitIndex > 0)
        {
            int lastSpace = text.LastIndexOfAny([' ', '\n', '\r'], splitIndex);
            // Если пробел близко - режем по нему. Иначе рубим жестко (абракадабра)
            if (lastSpace > 0 && (splitIndex - lastSpace) < 50)
            {
                splitIndex = lastSpace;
            }
        }

        if (splitIndex <= 0) splitIndex = 1;

        string pageText = text[..splitIndex];
        string overflowText = text[splitIndex..].TrimStart();

        bool moveCursor = originalCaret >= splitIndex;

        PageTextBox.Text = pageText;
        PageTextBox.TextChanged += OnTextChanged;

        if (!moveCursor)
        {
            PageTextBox.CaretIndex = Math.Min(originalCaret, pageText.Length);
        }

        if (!string.IsNullOrEmpty(overflowText))
        {
            // 🔥 Вызываем событие и передаем THIS (саму страницу)
            PageOverflow?.Invoke(this, overflowText, moveCursor);
        }
    }

    public string GetText() => PageTextBox.Text ?? string.Empty;
    public void SetPageNumber(int number) => PageNumberText.Text = number.ToString();

    public void FocusEditor()
    {
        PageTextBox.Focus();
        PageTextBox.CaretIndex = PageTextBox.Text?.Length ?? 0;
    }

    public void SetText(string text)
    {
        PageTextBox.TextChanged -= OnTextChanged;
        PageTextBox.Text = text;
        PageTextBox.TextChanged += OnTextChanged;
        Avalonia.Threading.Dispatcher.UIThread.Post(CheckOverflow, Avalonia.Threading.DispatcherPriority.Background);
    }
}
