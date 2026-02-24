using Avalonia.Controls;
using System;
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

    // Событие — вызывается когда текст переполняет страницу.
    public event Action<string>? PageOverflow;

    // Событие — вызывается при каждом изменении текста.
    public event Action<string>? TextChanged;

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        TextChanged?.Invoke(PageTextBox.Text ?? string.Empty);

        // Откладываем проверку — ждём пока Avalonia пересчитает layout.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            CheckOverflow,
            Avalonia.Threading.DispatcherPriority.Background);
    }

    private void CheckOverflow()
    {
        // Высота рабочей области: 1123 - 76 - 40 = 1007px.
        const double maxHeight = 1007;

        // Измеряем реальную высоту текста.
        PageTextBox.Measure(new Avalonia.Size(
            PageTextBox.Bounds.Width > 0 ? PageTextBox.Bounds.Width : 643,
            double.PositiveInfinity));

        double textHeight = PageTextBox.DesiredSize.Height;

        if (textHeight <= maxHeight)
        {
            return;
        }

        // Текст не влезает — отрезаем последнюю строку и отправляем на следующую страницу.
        string text = PageTextBox.Text ?? string.Empty;
        int lastNewLine = text.LastIndexOf('\n');

        if (lastNewLine < 0)
        {
            // Одна длинная строка — отрезаем по словам.
            int lastSpace = text.LastIndexOf(' ');
            if (lastSpace < 0)
            {
                return;
            }

            string overflow = text[lastSpace..].TrimStart();
            string pageText = text[..lastSpace];

            SetText(pageText);
            PageOverflow?.Invoke(overflow);
            return;
        }

        string overflowText = text[(lastNewLine + 1)..];
        string remainingText = text[..lastNewLine];

        SetText(remainingText);
        PageOverflow?.Invoke(overflowText);
    }

    public string GetText()
    {
        return PageTextBox.Text ?? string.Empty;
    }

    public void SetPageNumber(int number)
    {
        PageNumberText.Text = number.ToString();
    }

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
    }

}
