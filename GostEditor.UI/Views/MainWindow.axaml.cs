using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GostEditor.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnWindowPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is GostEditor.UI.ViewModels.MainWindowViewModel vm)
        {
            vm.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(vm.SelectedSection))
                {
                    if (vm.SelectedSection != null)
                    {
                        SectionEditor.LoadSection(vm.SelectedSection);
                    }
                }
            };

            if (vm.SelectedSection != null)
            {
                SectionEditor.LoadSection(vm.SelectedSection);
            }
        }
    }

    private void OnBoldClick(object? sender, RoutedEventArgs e)
    {
        WrapSelectedText("\uFEFF");
    }

    private void OnItalicClick(object? sender, RoutedEventArgs e)
    {
        WrapSelectedText("\u2060");
    }

    // Метод для оборачивания текста невидимыми маркерами (для жирного и курсива)
    private void WrapSelectedText(string marker)
    {
        IFocusManager? focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        IInputElement? focusedElement = focusManager?.GetFocusedElement();

        if (focusedElement is TextBox textBox && textBox.Name == "PageTextBox")
        {
            string text = textBox.Text ?? string.Empty;
            int start = textBox.SelectionStart;
            int end = textBox.SelectionEnd;

            if (start > end)
            {
                int temp = start;
                start = end;
                end = temp;
            }

            if (start == end)
            {
                string newText = text.Insert(start, $"{marker}{marker}");
                textBox.Text = newText;
                textBox.CaretIndex = start + marker.Length;
                textBox.Focus();
            }
            else
            {
                string selected = text.Substring(start, end - start);
                string newText = text.Remove(start, end - start).Insert(start, $"{marker}{selected}{marker}");
                textBox.Text = newText;
                textBox.SelectionStart = start;
                textBox.SelectionEnd = start + selected.Length + marker.Length * 2;
                textBox.Focus();
            }
        }
    }

    // НОВЫЙ МЕТОД: Для простой вставки одного символа (например, разрыва страницы)
    private void InsertText(string textToInsert)
    {
        IFocusManager? focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        IInputElement? focusedElement = focusManager?.GetFocusedElement();

        if (focusedElement is TextBox textBox && textBox.Name == "PageTextBox")
        {
            string text = textBox.Text ?? string.Empty;
            int start = textBox.SelectionStart;
            int end = textBox.SelectionEnd;

            if (start > end)
            {
                int temp = start;
                start = end;
                end = temp;
            }

            string newText = text.Remove(start, end - start).Insert(start, textToInsert);
            textBox.Text = newText;
            textBox.SelectionStart = start + textToInsert.Length;
            textBox.SelectionEnd = start + textToInsert.Length;
            textBox.Focus();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (e.Key == Key.B)
            {
                WrapSelectedText("\uFEFF");
                e.Handled = true;
            }
            else if (e.Key == Key.I)
            {
                WrapSelectedText("\u2060");
                e.Handled = true;
            }
            // ПУНКТ 3 и 4: Разрыв страницы по Ctrl + Enter
            else if (e.Key == Key.Enter)
            {
                InsertText("\f");
                e.Handled = true;
            }
        }
    }

    private void OnWindowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (DataContext is GostEditor.UI.ViewModels.MainWindowViewModel vm)
            {
                double delta = e.Delta.Y > 0 ? 0.1 : -0.1;
                double newZoom = Math.Round(vm.ZoomLevel + delta, 1);

                if (newZoom >= 0.5 && newZoom <= 2.0)
                {
                    vm.ZoomLevel = newZoom;
                }

                e.Handled = true;
            }
        }
    }
}
