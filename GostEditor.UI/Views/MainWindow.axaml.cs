using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.ViewModels;
using GostEditor.UI.Controllers;

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

        if (DataContext is MainWindowViewModel viewModel)
        {
            if (MainEditor != null)
            {
                MainEditor.LoadDocument(viewModel.CurrentDocument);
            }

            // Отписка от старых событий
            viewModel.OnInsertParagraphsRequested -= InsertParagraphsToEditor;
            viewModel.OnScrollToParagraphRequested -= ScrollToParagraph;
            viewModel.OnInsertHeadingRequested -= InsertHeading;
            viewModel.OnPasteNormalizedRequested -= PasteNormalizedToEditor;
            viewModel.GetEditorDocument -= GetDocumentFromEditor;

            if (MainEditor != null)
            {
                MainEditor.ContentChanged -= viewModel.SyncNavigation;
            }

            // Подписка на новые события
            viewModel.OnInsertParagraphsRequested += InsertParagraphsToEditor;
            viewModel.OnScrollToParagraphRequested += ScrollToParagraph;
            viewModel.OnInsertHeadingRequested += InsertHeading;
            viewModel.OnPasteNormalizedRequested += PasteNormalizedToEditor;
            viewModel.GetEditorDocument += GetDocumentFromEditor;

            if (MainEditor != null)
            {
                MainEditor.ContentChanged += viewModel.SyncNavigation;
            }
        }
    }

    private GostDocument GetDocumentFromEditor()
    {
        if (MainEditor == null || MainEditor.CurrentDocument == null)
        {
            Debug.WriteLine("[MAINWINDOW] Редактор не инициализирован или документ пуст!");
            return new GostDocument();
        }

        GostDocument document = MainEditor.CurrentDocument;

        Debug.WriteLine($"[MAINWINDOW] Получен документ из редактора:");
        Debug.WriteLine($"[MAINWINDOW]   Параграфов: {document.Paragraphs.Count}");
        Debug.WriteLine($"[MAINWINDOW]   Листингов: {document.CodeListings.Count}");
        Debug.WriteLine($"[MAINWINDOW]   Изображений: {document.Images.Count}");

        return document;
    }

    private GostDocument SyncDocumentFromViewModel(MainWindowViewModel viewModel)
    {
        GostDocument document = MainEditor?.CurrentDocument ?? viewModel.CurrentDocument;

        document.TitlePage.University = viewModel.University;
        document.TitlePage.Department = viewModel.Department;
        document.TitlePage.Discipline = viewModel.Discipline;
        document.TitlePage.WorkType = viewModel.WorkType;
        document.TitlePage.WorkTitle = viewModel.WorkTitle;
        document.TitlePage.GroupNumber = viewModel.GroupNumber;
        document.TitlePage.StudentName = viewModel.StudentName;
        document.TitlePage.TeacherName = viewModel.TeacherName;
        document.TitlePage.City = viewModel.City;
        document.TitlePage.Year = viewModel.Year;

        document.Modules.HasTitlePage = viewModel.HasTitlePage;
        document.Modules.HasTableOfContents = viewModel.HasTableOfContents;
        document.Modules.HasBibliography = viewModel.HasBibliography;
        document.Modules.HasAppendix = viewModel.HasAppendix;
        document.Modules.ContentStartPage = viewModel.ContentStartPage;

        document.CodeListings.Clear();

        foreach (CodeListingViewModel listingViewModel in viewModel.CodeListings)
        {
            listingViewModel.Listing.IsSelected = listingViewModel.IsSelected;
            document.CodeListings.Add(listingViewModel.Listing);
        }

        document.BibliographySources.Clear();

        for (int i = 0; i < viewModel.BibliographySources.Count; i++)
        {
            BibliographySourceViewModel sourceViewModel = viewModel.BibliographySources[i];
            sourceViewModel.Source.Order = i;
            sourceViewModel.Source.IsSelected = sourceViewModel.IsSelected;
            document.BibliographySources.Add(sourceViewModel.Source);
        }

        document.Counters.SourcesCount = document.BibliographySources.Count(source => source.IsSelected);

        return document;
    }

    private async void PasteNormalizedToEditor()
    {
        if (MainEditor == null)
        {
            return;
        }

        await MainEditor.PasteNormalizedFromClipboardAsync();
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

            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SyncNavigation();
            }
        }
    }

    private void MainEditor_CaretStyleChanged(object? sender, CaretStyleChangedEventArgs e)
    {
        _isUpdatingUi = true;

        if (BtnBold != null)
        {
            BtnBold.IsChecked = e.IsBold;
        }

        if (BtnItalic != null)
        {
            BtnItalic.IsChecked = e.IsItalic;
        }

        if (BtnAlignLeft != null)
        {
            BtnAlignLeft.IsChecked = e.Alignment == GostAlignment.Left;
        }

        if (BtnAlignCenter != null)
        {
            BtnAlignCenter.IsChecked = e.Alignment == GostAlignment.Center;
        }

        if (BtnAlignRight != null)
        {
            BtnAlignRight.IsChecked = e.Alignment == GostAlignment.Right;
        }

        if (BtnAlignJustify != null)
        {
            BtnAlignJustify.IsChecked = e.Alignment == GostAlignment.Justify;
        }

        if (FontSizeComboBox != null)
        {
            foreach (object? itemObj in FontSizeComboBox.Items)
            {
                if (itemObj is ComboBoxItem { Content: not null } item)
                {
                    string? contentString = item.Content.ToString();

                    if (contentString != null && double.TryParse(contentString, out double size))
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

            if (MainTabs != null)
            {
                MainTabs.SelectedIndex = 0;
            }

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
        MainEditor?.ClearFormatting();
    }

    private async void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        if (MainEditor == null)
        {
            return;
        }

        string? query = await ShowInputDialogAsync("Поиск", "Введите текст для поиска:");
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        bool found = MainEditor.FindNext(query);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.StatusMessage = found ? $"Найдено: {query}" : $"Не найдено: {query}";
        }
    }

    private void OnInsertTextBlockClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.InsertTextBlock();
    }

    private void OnInsertTableClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.InsertTablePlaceholder();
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


    private void OnFontSizeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || MainEditor == null || sender == null)
        {
            return;
        }

        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem { Content: not null } selectedItem)
        {
            string? contentString = selectedItem.Content.ToString();

            if (contentString != null && double.TryParse(contentString, out double newSize))
            {
                MainEditor.ApplyFontSize(newSize);
            }
        }
    }

    private void OnWindowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                double delta = e.Delta.Y > 0 ? 0.1 : -0.1;
                double newZoom = Math.Round(viewModel.ZoomLevel + delta, 1);

                if (newZoom >= 0.5 && newZoom <= 2.0)
                {
                    viewModel.ZoomLevel = newZoom;
                }

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
            else if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.F)
            {
                OnSearchClick(sender, e);
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MAINWINDOW] Ошибка сохранения по хоткею: {ex.Message}");
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await SaveDocumentToFileAsync();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || MainEditor == null)
        {
            return;
        }

        viewModel.IsBusy = true;
        viewModel.StatusMessage = "Загрузка...";

        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Открыть документ",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("GOST Document")
                        {
                            Patterns = new[] { "*.gost" }
                        }
                    }
                });

            if (files.Count > 0)
            {
                await using Stream stream = await files[0].OpenReadAsync();
                GostDocument loadedDocument = await viewModel.ArchiveService.LoadAsync(stream);

                viewModel.CurrentDocument = loadedDocument;
                MainEditor.LoadDocument(loadedDocument);
                viewModel.SyncNavigation();

                viewModel.StatusMessage = "Документ загружен";
            }
            else
            {
                viewModel.StatusMessage = "Готово";
            }
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"Ошибка загрузки: {ex.Message}";
            Debug.WriteLine($"[MAINWINDOW] Ошибка загрузки: {ex}");
        }
        finally
        {
            viewModel.IsBusy = false;
        }
    }

    private async Task SaveDocumentToFileAsync()
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            MainEditor == null ||
            MainEditor.CurrentDocument == null)
        {
            return;
        }

        viewModel.IsBusy = true;
        viewModel.StatusMessage = "Сохранение...";

        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Сохранить документ",
                    DefaultExtension = ".gost",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("GOST Document")
                        {
                            Patterns = new[] { "*.gost" }
                        }
                    }
                });

            if (file != null)
            {
                GostDocument documentToSave = SyncDocumentFromViewModel(viewModel);
                await using Stream stream = await file.OpenWriteAsync();
                await viewModel.ArchiveService.SaveAsync(documentToSave, stream);
                viewModel.StatusMessage = "Документ сохранен";
            }
            else
            {
                viewModel.StatusMessage = "Сохранение отменено";
            }
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"Ошибка сохранения: {ex.Message}";
            Debug.WriteLine($"[MAINWINDOW] Ошибка сохранения: {ex}");
        }
        finally
        {
            viewModel.IsBusy = false;
        }
    }

    private void OnNewDocumentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && MainEditor != null)
        {
            GostDocument newDocument = new GostDocument();
            viewModel.CurrentDocument = newDocument;
            MainEditor.LoadDocument(newDocument);
            viewModel.SyncNavigation();
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
            Debug.WriteLine($"[MAINWINDOW] Ошибка при вставке рисунка: {ex}");
        }
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            MainEditor == null ||
            MainEditor.CurrentDocument == null)
        {
            Debug.WriteLine("[MAINWINDOW] Не удалось экспортировать: нет ViewModel или документа");
            return;
        }

        viewModel.IsBusy = true;
        viewModel.StatusMessage = "Экспорт в DOCX...";

        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Экспорт в формат Word (ГОСТ)",
                    DefaultExtension = ".docx",
                    SuggestedFileName = "Документ_ГОСТ",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Word Document")
                        {
                            Patterns = new[] { "*.docx" }
                        }
                    }
                });

            if (file == null)
            {
                viewModel.StatusMessage = "Экспорт отменен";
                return;
            }

            // КРИТИЧНО: Получаем локальный путь безопасно
            string outputPath = file.Path.LocalPath;

            if (string.IsNullOrEmpty(outputPath))
            {
                viewModel.StatusMessage = "Ошибка: невозможно получить путь к файлу";
                Debug.WriteLine("[MAINWINDOW] file.Path.LocalPath вернул null или пустую строку!");
                return;
            }

            Debug.WriteLine($"[MAINWINDOW] Экспорт в: {outputPath}");
            Debug.WriteLine($"[MAINWINDOW] Параграфов в документе: {MainEditor.CurrentDocument.Paragraphs.Count}");

            GostDocument documentToExport = SyncDocumentFromViewModel(viewModel);

            Debug.WriteLine($"[MAINWINDOW] Подготовлено листингов: {documentToExport.CodeListings.Count}");

            // ИСПОЛЬЗУЕМ СЕРВИС ИЗ DI!
            await viewModel.ExportService.ExportToDocxAsync(documentToExport, outputPath);

            viewModel.StatusMessage = "Успешно экспортировано!";
            Debug.WriteLine("[MAINWINDOW] Экспорт завершён успешно");
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"Ошибка экспорта: {ex.Message}";
            Debug.WriteLine($"[MAINWINDOW] Ошибка экспорта: {ex}");
        }
        finally
        {
            viewModel.IsBusy = false;
        }
    }

    private async void OnParseFolderClick(object? sender, RoutedEventArgs e)
{
    if (DataContext is not MainWindowViewModel viewModel)
    {
        return;
    }

    viewModel.IsBusy = true;
    viewModel.StatusMessage = "Выбор папки...";

    try
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Выберите папку с исходным кодом проекта",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            IStorageFolder selectedFolder = folders[0];
            string folderPath = selectedFolder.Path.LocalPath;

            Debug.WriteLine($"[MAINWINDOW] Выбрана папка: {folderPath}");

            viewModel.StatusMessage = "Парсинг файлов...";

            IReadOnlyList<CodeListing> listings = await viewModel.CodeParserService.ParseDirectoryAsync(folderPath);

            viewModel.CodeListings.Clear();

            foreach (CodeListing listing in listings)
            {
                viewModel.CodeListings.Add(new CodeListingViewModel
                {
                    Listing = listing,
                    IsSelected = true
                });

                Debug.WriteLine($"[MAINWINDOW] Добавлен файл: {listing.RelativePath} ({listing.Language})");
            }

            viewModel.StatusMessage = $"Найдено файлов: {listings.Count}";
            Debug.WriteLine($"[MAINWINDOW] Всего загружено: {listings.Count} файлов");

            // Переключаемся на вкладку "Структура документа" -> "Приложения"
            if (MainTabs != null)
            {
                MainTabs.SelectedIndex = 1; // Вкладка "Структура документа"
            }

            viewModel.SelectedModuleIndex = 3; // Подвкладка "Приложения (Код)"
        }
        else
        {
            viewModel.StatusMessage = "Выбор папки отменён";
        }
    }
    catch (Exception ex)
    {
        viewModel.StatusMessage = $"Ошибка парсинга: {ex.Message}";
        Debug.WriteLine($"[MAINWINDOW] Ошибка парсинга: {ex}");
    }
    finally
    {
        viewModel.IsBusy = false;
    }
}

    private async Task<string?> ShowInputDialogAsync(string title, string message, string defaultText = "")
    {
        TextBox textBox = new TextBox
        {
            Text = defaultText,
            MinWidth = 360
        };

        Button okButton = new Button
        {
            Content = "Найти",
            Padding = new Thickness(18, 6),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button cancelButton = new Button
        {
            Content = "Отмена",
            Padding = new Thickness(18, 6),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Window dialog = new Window
        {
            Title = title,
            Width = 460,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    textBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, okButton }
                    }
                }
            }
        };

        string? result = null;
        okButton.Click += (_, _) => { result = textBox.Text; dialog.Close(); };
        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
        return result;
    }
}
