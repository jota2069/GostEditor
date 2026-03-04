using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.ViewModels;

namespace GostEditor.UI.Views;

public partial class MainWindow : Window
{
    private bool _isUpdatingUI = false;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnWindowPointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnGlobalPreviewKeyDown, RoutingStrategies.Tunnel);

        if (MainEditor != null)
        {
            MainEditor.CaretStyleChanged += MainEditor_CaretStyleChanged;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.OnInsertParagraphsRequested -= InsertParagraphsToEditor;
            vm.OnInsertParagraphsRequested += InsertParagraphsToEditor;
        }
    }

    private void MainEditor_CaretStyleChanged(object? sender, CaretStyleChangedEventArgs e)
    {
        _isUpdatingUI = true;

        if (BtnBold != null) BtnBold.IsChecked = e.IsBold;
        if (BtnItalic != null) BtnItalic.IsChecked = e.IsItalic;

        if (BtnAlignLeft != null) BtnAlignLeft.IsChecked = e.Alignment == GostAlignment.Left;
        if (BtnAlignCenter != null) BtnAlignCenter.IsChecked = e.Alignment == GostAlignment.Center;
        if (BtnAlignRight != null) BtnAlignRight.IsChecked = e.Alignment == GostAlignment.Right;
        if (BtnAlignJustify != null) BtnAlignJustify.IsChecked = e.Alignment == GostAlignment.Justify;

        if (FontSizeComboBox != null)
        {
            foreach (object itemObj in FontSizeComboBox.Items)
            {
                // Изящная проверка на null через pattern matching
                if (itemObj is ComboBoxItem { Content: not null } item)
                {
                    if (double.TryParse(item.Content.ToString(), out double size))
                    {
                        if (Math.Abs(size - e.FontSize) < 0.1)
                        {
                            FontSizeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
        }

        _isUpdatingUI = false;
    }

    private void InsertParagraphsToEditor(List<Paragraph> paragraphs)
    {
        if (paragraphs.Count > 0 && MainEditor != null)
        {
            MainEditor.AppendParagraphs(paragraphs);
            if (MainTabs != null) MainTabs.SelectedIndex = 0;
            MainEditor.Focus();
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

    private void OnUndoToolbarClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.Undo();
        MainEditor?.Focus();
    }

    private void OnRedoToolbarClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.Redo();
        MainEditor?.Focus();
    }

    private void OnStartPageNumberChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (e.NewValue.HasValue && CurrentStartPageLabel != null)
        {
            int newStartPage = (int)e.NewValue.Value;
            CurrentStartPageLabel.Text = $"(Сейчас: {newStartPage})";
            if (MainEditor != null) MainEditor.SetStartPageNumber(newStartPage);
        }
    }

    private async void OnGlobalPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.S)
            {
                await SaveDocumentAsync();
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка сохранения по хоткею: {ex.Message}");
        }
    }

    private async Task SaveDocumentAsync()
    {
        if (DataContext is MainWindowViewModel vm && vm.SaveDocumentCommand.CanExecute(null))
        {
            await vm.SaveDocumentCommand.ExecuteAsync(null);
        }
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

    private void OnFontSizeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUI) return;
        if (MainEditor == null || sender == null) return;

        ComboBox comboBox = (ComboBox)sender;
        if (comboBox.SelectedItem == null) return;

        if (comboBox.SelectedItem is ComboBoxItem { Content: not null } selectedItem)
        {
            string? content = selectedItem.Content.ToString();

            if (double.TryParse(content, out double newSize))
            {
                MainEditor.ApplyFontSize(newSize);
            }
        }
    }

    private void OnNewDocumentClick(object? sender, RoutedEventArgs e) { }

    private async void OnInsertImageClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (MainEditor != null)
            {
                await MainEditor.InsertImageFromFileAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при вставке рисунка: {ex.Message}");
        }
    }
}
