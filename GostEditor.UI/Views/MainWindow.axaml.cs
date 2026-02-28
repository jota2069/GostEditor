using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GostEditor.Core.Services;
using GostEditor.UI.ViewModels;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.UI.Views;

public partial class MainWindow : Window
{
    private string _currentFilePath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnWindowPointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnGlobalPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += (object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
            {
                if (args.PropertyName == nameof(vm.SelectedSection))
                {

                }
            };
        }
    }

    private void OnBoldClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.ApplyBold();
        MainEditor?.Focus();
    }

    private void OnItalicClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.ApplyItalic();
        MainEditor?.Focus();
    }

    private void OnAlignLeftClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.AlignLeft();
        MainEditor?.Focus();
    }

    private void OnAlignCenterClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.AlignCenter();
        MainEditor?.Focus();
    }

    private void OnAlignRightClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.AlignRight();
        MainEditor?.Focus();
    }

    private void OnAlignJustifyClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.AlignJustify();
        MainEditor?.Focus();
    }

    private void OnClearFormattingClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.Focus();
    }

    // ==========================================
    // НОВЫЙ МЕТОД: ВСТАВКА ЛИСТИНГОВ КОДА
    // ==========================================
    private void OnInsertCodeListingsClick(object? sender, RoutedEventArgs e)
    {
        // Проверяем, что ViewModel доступна и в ней есть загруженные листинги
        if (DataContext is MainWindowViewModel vm && vm.CodeListings != null)
        {
            // ЯВНАЯ ТИПИЗАЦИЯ: берем наш сервис
            CodeParserService parser = new CodeParserService();

            // Генерируем ГОСТ-абзацы из отмеченных файлов
            List<Paragraph> paragraphs = parser.GenerateAppendixParagraphs(vm.CodeListings);

            if (paragraphs.Count > 0)
            {
                // Отправляем в редактор
                MainEditor?.AppendParagraphs(paragraphs);

                // Переключаем пользователя обратно на вкладку "Редактор" (индекс 0)
                if (MainTabs != null)
                {
                    MainTabs.SelectedIndex = 0;
                }

                // Возвращаем фокус редактору
                MainEditor?.Focus();
            }
        }
    }

    private void OnStartPageNumberChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
    {
        if (e.NewValue.HasValue && CurrentStartPageLabel != null)
        {
            // ЯВНАЯ ТИПИЗАЦИЯ: извлекаем число как int
            int newStartPage = (int)e.NewValue.Value;

            CurrentStartPageLabel.Text = $"(Сейчас: {newStartPage})";

            // Передаем новое число в наш движок
            if (MainEditor != null)
            {
                MainEditor.SetStartPageNumber(newStartPage);
            }
        }
    }

    private async void OnGlobalPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.S)
        {
            await SaveDocumentAsync();
            e.Handled = true;
        }
    }

    private void OnNewDocumentClick(object? sender, RoutedEventArgs e)
    {
        // Убран async, чтобы IDE не ругалась на отсутствие await
    }

    private async Task SaveDocumentAsync()
    {
        await Task.CompletedTask;
    }

    private void OnWindowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                double delta = e.Delta.Y > 0 ? 0.1 : -0.1;
                double newZoom = Math.Round(vm.ZoomLevel + delta, 1);
                if (newZoom >= 0.5 && newZoom <= 2.0) vm.ZoomLevel = newZoom;
                e.Handled = true;
            }
        }
    }

    // ==========================================
    // ИСПРАВЛЕНО: СМЕНА СТИЛЯ (ЧЕРЕЗ SENDER)
    // ==========================================
    private void OnStyleSelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (MainEditor == null || sender == null) return;

        // ЯВНАЯ ТИПИЗАЦИЯ: извлекаем ComboBox из sender
        Avalonia.Controls.ComboBox comboBox = (Avalonia.Controls.ComboBox)sender;
        int index = comboBox.SelectedIndex;

        GostEditor.Core.TextEngine.DOM.ParagraphStyle selectedStyle = GostEditor.Core.TextEngine.DOM.ParagraphStyle.Normal;

        switch (index)
        {
            case 0:
                selectedStyle = GostEditor.Core.TextEngine.DOM.ParagraphStyle.Normal;
                break;
            case 1:
                selectedStyle = GostEditor.Core.TextEngine.DOM.ParagraphStyle.Heading1;
                break;
            case 2:
                selectedStyle = GostEditor.Core.TextEngine.DOM.ParagraphStyle.Heading2;
                break;
            case 3:
                selectedStyle = GostEditor.Core.TextEngine.DOM.ParagraphStyle.Code;
                break;
        }

        MainEditor.ApplyParagraphStyle(selectedStyle);
    }

    // ==========================================
    // ИСПРАВЛЕНО: СМЕНА ШРИФТА (ЧЕРЕЗ SENDER)
    // ==========================================
    private void OnFontSizeSelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (MainEditor == null || sender == null) return;

        // ЯВНАЯ ТИПИЗАЦИЯ: извлекаем ComboBox из sender
        Avalonia.Controls.ComboBox comboBox = (Avalonia.Controls.ComboBox)sender;

        if (comboBox.SelectedItem == null) return;

        Avalonia.Controls.ComboBoxItem selectedItem = (Avalonia.Controls.ComboBoxItem)comboBox.SelectedItem;
        string? content = selectedItem.Content?.ToString();

        if (double.TryParse(content, out double newSize))
        {
            MainEditor.ApplyFontSize(newSize);
        }
    }
}
