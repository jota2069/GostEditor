using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.ViewModels;
using GostDocument = GostEditor.Core.Models.GostDocument;

namespace GostEditor.UI.Views;

public partial class MainWindow : Window
{
    private bool _isUpdatingUi;

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
            // === ИСПРАВЛЕНИЕ: ЖЕНИМ РЕДАКТОР И ЛЕВУЮ ПАНЕЛЬ ===
            if (MainEditor != null)
            {
                MainEditor.LoadDocument(vm.CurrentDocument);
            }

            vm.OnInsertParagraphsRequested -= InsertParagraphsToEditor;
            vm.OnScrollToParagraphRequested -= ScrollToParagraph;
            vm.OnInsertHeadingRequested -= InsertHeading;

            if (MainEditor != null)
                MainEditor.ContentChanged -= vm.SyncNavigation;

            vm.OnInsertParagraphsRequested += InsertParagraphsToEditor;
            vm.OnScrollToParagraphRequested += ScrollToParagraph;
            vm.OnInsertHeadingRequested += InsertHeading;

            if (MainEditor != null)
                MainEditor.ContentChanged += vm.SyncNavigation;
        }
    }

    private void ScrollToParagraph(int index)
    {
        if (MainEditor != null)
        {
            MainEditor.ScrollToParagraph(index);
        }
    }

    private void InsertHeading(int level, string text)
    {
        if (MainEditor != null)
        {
            MainEditor.InsertHeading(level, text);
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SyncNavigation(); // Обновляем левую панель после добавления
            }
        }
    }

    private void MainEditor_CaretStyleChanged(object? sender, CaretStyleChangedEventArgs e)
    {
        _isUpdatingUi = true;

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

        _isUpdatingUi = false;
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

    private void OnBoldClick(object? sender, RoutedEventArgs e) { MainEditor?.ApplyBold(); MainEditor?.Focus(); }
    private void OnItalicClick(object? sender, RoutedEventArgs e) { MainEditor?.ApplyItalic(); MainEditor?.Focus(); }
    private void OnAlignLeftClick(object? sender, RoutedEventArgs e) { MainEditor?.AlignLeft(); MainEditor?.Focus(); }
    private void OnAlignCenterClick(object? sender, RoutedEventArgs e) { MainEditor?.AlignCenter(); MainEditor?.Focus(); }
    private void OnAlignRightClick(object? sender, RoutedEventArgs e) { MainEditor?.AlignRight(); MainEditor?.Focus(); }
    private void OnAlignJustifyClick(object? sender, RoutedEventArgs e) { MainEditor?.AlignJustify(); MainEditor?.Focus(); }
    private void OnClearFormattingClick(object? sender, RoutedEventArgs e) { MainEditor?.Focus(); }
    private void OnUndoToolbarClick(object? sender, RoutedEventArgs e) { MainEditor?.Undo(); MainEditor?.Focus(); }
    private void OnRedoToolbarClick(object? sender, RoutedEventArgs e) { MainEditor?.Redo(); MainEditor?.Focus(); }

    private void OnStartPageNumberChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (e.NewValue.HasValue && CurrentStartPageLabel != null)
        {
            int newStartPage = (int)e.NewValue.Value;
            CurrentStartPageLabel.Text = $"(Сейчас: {newStartPage})";
            if (MainEditor != null) MainEditor.SetStartPageNumber(newStartPage);
        }
    }

    private void OnFontSizeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || MainEditor == null || sender == null) return;

        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem { Content: not null } selectedItem)
        {
            if (double.TryParse(selectedItem.Content.ToString(), out double newSize))
            {
                MainEditor.ApplyFontSize(newSize);
            }
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

    private async void OnGlobalPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.S)
            {
                await SaveDocumentToFileAsync();
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка сохранения по хоткею: {ex.Message}");
        }
    }

    public async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await SaveDocumentToFileAsync();
    }

    public async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (MainEditor == null) return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Открыть документ",
                AllowMultiple = false,
                FileTypeFilter = [ new FilePickerFileType("GOST Document") { Patterns = ["*.gost"] } ]
            });

            if (files.Count > 0)
            {
                await using Stream stream = await files[0].OpenReadAsync();
                GostDocument loadedDoc = await GostArchiveManager.LoadAsync(stream);

                // === ИСПРАВЛЕНИЕ: Обновляем документ и в UI, и во ViewModel ===
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.CurrentDocument = loadedDoc;
                    MainEditor.LoadDocument(loadedDoc);
                    vm.SyncNavigation();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при открытии файла: {ex.Message}");
        }
    }

    private async Task SaveDocumentToFileAsync()
    {
        try
        {
            if (MainEditor == null || MainEditor.CurrentDocument == null) return;

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Сохранить документ",
                DefaultExtension = ".gost",
                FileTypeChoices = [ new FilePickerFileType("GOST Document") { Patterns = ["*.gost"] } ]
            });

            if (file != null)
            {
                await using Stream stream = await file.OpenWriteAsync();
                await GostArchiveManager.SaveAsync(MainEditor.CurrentDocument, stream);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка сохранения: {ex.Message}");
        }
    }

    private void OnNewDocumentClick(object? sender, RoutedEventArgs e)
    {
        // === ИСПРАВЛЕНИЕ: При создании нового файла синхронизируем их ===
        if (DataContext is MainWindowViewModel vm && MainEditor != null)
        {
            GostDocument newDoc = new GostDocument();
            vm.CurrentDocument = newDoc;
            MainEditor.LoadDocument(newDoc);
            vm.SyncNavigation();
        }
    }

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
