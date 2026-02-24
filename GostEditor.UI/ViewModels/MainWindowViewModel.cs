using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.UI.Services;

namespace GostEditor.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDocumentService _documentService;
    private readonly IExportService _exportService;
    private readonly ICodeParserService _codeParserService;
    private readonly ITextNormalizerService _textNormalizerService;
    private readonly IValidationService _validationService;
    private readonly DialogService _dialogService;

    // Путь к текущему открытому файлу.
    private string? _currentFilePath;

    // Таймер автосохранения.
    private System.Threading.Timer? _autoSaveTimer;

    [ObservableProperty]
    private GostDocument _currentDocument = new GostDocument();

    [ObservableProperty]
    private ObservableCollection<DocumentSection> _sections =
        new ObservableCollection<DocumentSection>();

    [ObservableProperty]
    private DocumentSection? _selectedSection;

    [ObservableProperty]
    private ObservableCollection<CodeListing> _codeListings =
        new ObservableCollection<CodeListing>();

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = "Готово.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    public string ZoomPercentage => $"{(int)(ZoomLevel * 100)}%";

    partial void OnZoomLevelChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomPercentage));
    }

    [ObservableProperty]
    private string _windowTitle = "GostEditor — ГОСТ 7.32-2017";

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
                UpdateWindowTitle();
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
                UpdateWindowTitle();
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
                UpdateWindowTitle();
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
                UpdateWindowTitle();
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
                UpdateWindowTitle();
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
                UpdateWindowTitle();
            }
        }
    }

    public MainWindowViewModel(
        IDocumentService documentService,
        IExportService exportService,
        ICodeParserService codeParserService,
        ITextNormalizerService textNormalizerService,
        IValidationService validationService,
        DialogService dialogService)
    {
        _documentService = documentService;
        _exportService = exportService;
        _codeParserService = codeParserService;
        _textNormalizerService = textNormalizerService;
        _validationService = validationService;
        _dialogService = dialogService;

        // Автосохранение каждые 3 минуты.
        _autoSaveTimer = new System.Threading.Timer(
            callback: _ => AutoSave(),
            state: null,
            dueTime: TimeSpan.FromMinutes(3),
            period: TimeSpan.FromMinutes(3));
    }

    [RelayCommand]
    private void NewDocument()
    {
        CurrentDocument = _documentService.CreateNew();
        CodeListings.Clear();
        Sections.Clear();
        SelectedSection = null;
        _currentFilePath = null;
        HasUnsavedChanges = false;
        StatusMessage = "Создан новый документ.";
        RefreshTitlePageBindings();
        UpdateWindowTitle();
    }

    [RelayCommand]
    private void AddSection()
    {
        DocumentSection section = new DocumentSection
        {
            Title = $"Раздел {Sections.Count + 1}",
            Order = Sections.Count
        };

        Sections.Add(section);
        CurrentDocument.Sections.Add(section);
        SelectedSection = section;
        HasUnsavedChanges = true;
        UpdateWindowTitle();
    }

    [RelayCommand]
    private void DeleteSection(DocumentSection section)
    {
        Sections.Remove(section);
        CurrentDocument.Sections.Remove(section);
        SelectedSection = Sections.Count > 0 ? Sections[0] : null;
        HasUnsavedChanges = true;
        UpdateWindowTitle();
    }

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
            await _documentService.SaveAsync(CurrentDocument, filePath);
            _currentFilePath = filePath;
            HasUnsavedChanges = false;
            StatusMessage = $"Сохранено: {filePath}";
            UpdateWindowTitle();
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
            CurrentDocument = await _documentService.LoadAsync(filePath);

            Sections.Clear();
            foreach (DocumentSection section in CurrentDocument.Sections)
            {
                Sections.Add(section);
            }

            CodeListings.Clear();
            foreach (CodeListing listing in CurrentDocument.CodeListings)
            {
                CodeListings.Add(listing);
            }

            SelectedSection = Sections.Count > 0 ? Sections[0] : null;
            _currentFilePath = filePath;
            HasUnsavedChanges = false;
            StatusMessage = $"Открыт: {filePath}";
            RefreshTitlePageBindings();
            UpdateWindowTitle();
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

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportToDocxAsync()
    {
        List<string> errors = _validationService.Validate(CurrentDocument);

        if (errors.Count > 0)
        {
            string errorText = string.Join("\n", errors.Select(e => $"• {e}"));
            StatusMessage = $"Ошибки: {errors.Count}. Исправьте перед экспортом.";
            await _dialogService.ShowMessageAsync("Документ не готов к экспорту", errorText);
            return;
        }

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
            StatusMessage = $"Найдено файлов: {parsed.Count.ToString()}";
            UpdateWindowTitle();
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

    [RelayCommand]
    private async Task PasteNormalizedAsync()
    {
        string? clipboardText = await _dialogService.GetClipboardTextAsync();

        if (string.IsNullOrEmpty(clipboardText))
        {
            StatusMessage = "Буфер обмена пуст.";
            return;
        }

        string normalized = _textNormalizerService.Normalize(clipboardText);
        await _dialogService.SetClipboardTextAsync(normalized);
        StatusMessage = "Текст нормализован и скопирован в буфер.";
    }

    private void AutoSave()
    {
        if (!HasUnsavedChanges || _currentFilePath is null)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await _documentService.SaveAsync(CurrentDocument, _currentFilePath);
                HasUnsavedChanges = false;
                StatusMessage = $"Автосохранено: {DateTime.Now:HH:mm}";
                UpdateWindowTitle();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка автосохранения: {ex.Message}";
            }
        });
    }

    private void UpdateWindowTitle()
    {
        string fileName = _currentFilePath is not null
            ? System.IO.Path.GetFileName(_currentFilePath)
            : "Новый документ";

        string unsaved = HasUnsavedChanges ? " *" : "";
        WindowTitle = $"{fileName}{unsaved} — GostEditor";
    }

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
