using System;
using Avalonia; // Важно для работы OnAttachedToVisualTree
using Avalonia.Controls;

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

    public event Action<string, bool>? PageOverflow;
    public event Action<string>? TextChanged;

    // ОБНОВЛЕНО: Когда создается новая страница (например, при массовой вставке),
    // мы даем ей команду тоже проверить свои границы после отрисовки.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(
            CheckOverflow,
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        TextChanged?.Invoke(PageTextBox.Text ?? string.Empty);

        Avalonia.Threading.Dispatcher.UIThread.Post(
            CheckOverflow,
            Avalonia.Threading.DispatcherPriority.Background);
    }

    private void CheckOverflow()
    {
        const double maxHeight = 1007;
        double width = PageTextBox.Bounds.Width > 0 ? PageTextBox.Bounds.Width : 643;

        // Измеряем реальную высоту текста
        PageTextBox.Measure(new Avalonia.Size(width, double.PositiveInfinity));
        double currentHeight = PageTextBox.DesiredSize.Height;

        if (currentHeight <= maxHeight)
        {
            return;
        }

        string text = PageTextBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        PageTextBox.TextChanged -= OnTextChanged;
        int originalCaret = PageTextBox.CaretIndex;

        // --- МАТЕМАТИЧЕСКИЙ РАСЧЕТ БЕЗ ЗАВИСАНИЙ ---
        // Вычисляем долю текста, которая поместится на страницу.
        // Умножаем на 0.90 (берем с запасом 10%), чтобы точно влезло и не было дерганий.
        double ratio = maxHeight / currentHeight;
        int estimatedLength = (int)(text.Length * ratio * 0.90);

        int splitIndex = estimatedLength;

        // Ищем ближайший пробел, чтобы не резать слово пополам
        if (splitIndex > 0 && splitIndex < text.Length)
        {
            int lastSpace = text.LastIndexOfAny([' ', '\n', '\r'], splitIndex);
            if (lastSpace > 0)
            {
                splitIndex = lastSpace;
            }
        }

        // Защита от пустых страниц
        if (splitIndex <= 0) splitIndex = 1;

        string pageText = text[..splitIndex];
        string overflowText = text[splitIndex..].TrimStart();

        bool moveCursor = originalCaret >= splitIndex;

        PageTextBox.Text = pageText;
        PageTextBox.TextChanged += OnTextChanged;

        if (!moveCursor && originalCaret <= pageText.Length)
        {
            PageTextBox.CaretIndex = originalCaret;
        }

        if (!string.IsNullOrEmpty(overflowText))
        {
            PageOverflow?.Invoke(overflowText, moveCursor);
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

        Avalonia.Threading.Dispatcher.UIThread.Post(
            CheckOverflow,
            Avalonia.Threading.DispatcherPriority.Background);
    }
}
