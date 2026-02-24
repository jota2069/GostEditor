using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.UI.Services;

namespace GostEditor.UI.ViewModels;

/// <summary>
/// Главная ViewModel приложения. Связывает UI с сервисами Core.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    // Сервисы из Core — инжектируются через конструктор.
    private readonly IDocumentService _documentService;
    private readonly IExportService _exportService;
    private readonly ICodeParserService _codeParserService;
    private readonly ITextNormalizerService _textNormalizerService;

    // Диалоговый сервис — только в UI, Core о нём не знает.
    private readonly DialogService _dialogService;

    // ─── Свойства, связанные с документом ───────────────────────────────────

    /// <summary>
    /// Текущий открытый документ.
    /// </summary>
    [ObservableProperty]
    private GostDocument _currentDocument = new GostDocument();

    /// <summary>
    /// Флаг: есть ли несохранённые изменения.
    /// </summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges = false;

    /// <summary>
    /// Текст статусной строки внизу окна.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Готово.";

    /// <summary>
    /// Флаг: выполняется ли долгая операция (блокирует кнопки).
    /// </summary>
    [ObservableProperty]
    private bool _isBusy = false;

    /// <summary>
    /// Коллекция листингов кода для отображения в списке.
    /// При парсинге папки сюда попадают все найденные файлы.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<CodeListing> _codeListings = new ObservableCollection<CodeListing>();

    // ─── Свойства титульного листа (прокси к CurrentDocument.TitlePage) ─────
    // Биндим отдельно, чтобы поля формы работали без доп. конвертеров.

    public string University
    {
        get => CurrentDocument.TitlePage.University;
        set
        {
            if (CurrentDocument.TitlePage.University != value)
            {
                CurrentDocument.TitlePage.University = value;
                OnPropertyChanged();
                HasUnsavedChanges = true;
            }
        }
    }

    public string Department
    {
        get => CurrentDocument.TitlePage.Department;
        set
        {
            if (CurrentDocument.TitlePage.Department != value)
            {
                CurrentDocument.TitlePage.Department = value;
                OnPropertyChanged();
                HasUnsavedChanges = true;
            }
        }
    }

    public string WorkTitle
    {
        get => CurrentDocument.TitlePage.WorkTitle;
        set
        {
            if (CurrentDocument.TitlePage.WorkTitle != value)
            {
                CurrentDocument.TitlePage.WorkTitle = value;
                OnPropertyChanged();
                HasUnsavedChanges = true;
            }
        }
    }

    public string StudentName
    {
        get => CurrentDocument.TitlePage.StudentName;
        set
        {
            if (CurrentDocument.TitlePage.StudentName != value)
            {
                CurrentDocument.TitlePage.StudentName = value;
                OnPropertyChanged();
                HasUnsavedChanges = true;
            }
        }
    }

    public string GroupNumber
    {
        get => CurrentDocument.TitlePage.GroupNumber;
        set
        {
            if (CurrentDocument.TitlePage.GroupNumber != value)
            {
                CurrentDocument.TitlePage.GroupNumber = value;
                OnPropertyChanged();
                HasUnsavedChanges = true;
            }
        }
    }

    public string TeacherName
    {
        get => CurrentDocument.TitlePage.TeacherName;
        set
        {
            if (CurrentDocument.TitlePage.TeacherName != value)
            {
                CurrentDocument.TitlePage.TeacherName = value;
                OnPropertyChanged();
                HasUnsavedChanges = true;
            }
        }
    }

    // ─── Конструктор ────────────────────────────────────────────────────────

    public MainViewModel(
        IDocumentService documentService,
        IExportService exportService,
        ICodeParserService codeParserService,
        ITextNormalizerService textNormalizerService,
        DialogService dialogService)
    {
        _documentService = documentService;
        _exportService = exportService;
        _codeParserService = codeParserService;
        _textNormalizerService = textNormalizerService;
        _dialogService = dialogService;
    }

    // ─── Команды ────────────────────────────────────────────────────────────

    /// <summary>
    /// Создать новый пустой документ.
    /// </summary>
    [RelayCommand]
    private void NewDocument()
    {
        // IDocumentService.CreateNew() — синхронный, просто возвращает новый GostDocument.
        CurrentDocument = _documentService.CreateNew();
        CodeListings.Clear();
        HasUnsavedChanges = false;
        StatusMessage = "Создан новый документ.";

        // Уведомляем UI об обновлении всех прокси-свойств титульного листа.
        RefreshTitlePageBindings();
    }

    /// <summary>
    /// Сохранить текущий документ в файл .gost.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveDocumentAsync()
    {
        string? filePath = await _dialogService.ShowSaveFileDialogAsync(
            title: "Сохранить документ",
            defaultExtension: "gost",
            filter: "GostEditor Document (*.gost)|*.gost");

        if (filePath is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Сохранение...";

        try
        {
            // DocumentService.SaveAsync() — пакует JSON + картинки в ZIP-архив .gost.
            await _documentService.SaveAsync(CurrentDocument, filePath);
            HasUnsavedChanges = false;
            StatusMessage = $"Сохранено: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сохранения: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSave()
    {
        return !IsBusy;
    }

    /// <summary>
    /// Открыть существующий документ из файла .gost.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadDocumentAsync()
    {
        string? filePath = await _dialogService.ShowOpenFileDialogAsync(
            title: "Открыть документ",
            filter: "GostEditor Document (*.gost)|*.gost");

        if (filePath is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Загрузка...";

        try
        {
            // DocumentService.LoadAsync() — распаковывает ZIP, десериализует JSON,
            // восстанавливает байты картинок из entries архива.
            CurrentDocument = await _documentService.LoadAsync(filePath);

            // Синхронизируем ObservableCollection с загруженными листингами.
            CodeListings.Clear();
            foreach (CodeListing listing in CurrentDocument.CodeListings)
            {
                CodeListings.Add(listing);
            }

            HasUnsavedChanges = false;
            StatusMessage = $"Открыт: {filePath}";
            RefreshTitlePageBindings();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLoad()
    {
        return !IsBusy;
    }

    /// <summary>
    /// Экспортировать документ в Word (.docx) по ГОСТ 7.32.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportToDocxAsync()
    {
        string? outputPath = await _dialogService.ShowSaveFileDialogAsync(
            title: "Экспорт в Word",
            defaultExtension: "docx",
            filter: "Word Document (*.docx)|*.docx");

        if (outputPath is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Экспорт в DOCX...";

        try
        {
            // ExportService.ExportToDocxAsync() — строит .docx через Xceed,
            // применяет все правила ГОСТ 7.32-2017: шрифты, отступы, листинги.
            await _exportService.ExportToDocxAsync(CurrentDocument, outputPath);
            StatusMessage = $"Экспортировано: {outputPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка экспорта: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExport()
    {
        return !IsBusy;
    }

    /// <summary>
    /// Выбрать папку с исходным кодом и распарсить все файлы.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanParseCode))]
    private async Task ParseCodeFolderAsync()
    {
        string? folderPath = await _dialogService.ShowOpenFolderDialogAsync(
            title: "Выбрать папку с кодом");

        if (folderPath is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Парсинг кода...";

        try
        {
            // CodeParserService.ParseDirectoryAsync() — рекурсивно обходит папку,
            // находит .cs/.py/.js и т.д., очищает мусор (using, #pragma), возвращает список.
            IReadOnlyList<CodeListing> parsed =
                await _codeParserService.ParseDirectoryAsync(folderPath);

            CodeListings.Clear();
            CurrentDocument.CodeListings.Clear();

            foreach (CodeListing listing in parsed)
            {
                CodeListings.Add(listing);
                CurrentDocument.CodeListings.Add(listing);
            }

            HasUnsavedChanges = true;
            StatusMessage = $"Найдено файлов: {parsed.Count}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка парсинга: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanParseCode()
    {
        return !IsBusy;
    }

    /// <summary>
    /// Нормализовать текст из буфера обмена и вставить в нужное поле.
    /// Вызывается вручную — пользователь вставляет "грязный" текст с мусорными символами.
    /// </summary>
    [RelayCommand]
    private async Task PasteNormalizedAsync()
    {
        string? clipboardText = await _dialogService.GetClipboardTextAsync();

        if (string.IsNullOrEmpty(clipboardText))
        {
            StatusMessage = "Буфер обмена пуст.";
            return;
        }

        // TextNormalizerService.Normalize() — убирает мягкие дефисы, NBSP,
        // нулевые символы, нормализует переносы строк, trim каждой строки.
        string normalized = _textNormalizerService.Normalize(clipboardText);

        await _dialogService.SetClipboardTextAsync(normalized);
        StatusMessage = "Текст нормализован и скопирован в буфер.";
    }

    // ─── Вспомогательные методы ─────────────────────────────────────────────

    /// <summary>
    /// Уведомляет Avalonia об обновлении всех прокси-свойств титульного листа.
    /// Вызывается после загрузки или создания нового документа.
    /// </summary>
    private void RefreshTitlePageBindings()
    {
        OnPropertyChanged(nameof(University));
        OnPropertyChanged(nameof(Department));
        OnPropertyChanged(nameof(WorkTitle));
        OnPropertyChanged(nameof(StudentName));
        OnPropertyChanged(nameof(GroupNumber));
        OnPropertyChanged(nameof(TeacherName));
    }
}