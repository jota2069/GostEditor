using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Services;
using GostDocument = GostEditor.Core.Models.GostDocument;

namespace GostEditor.UI.ViewModels;

public partial class SelectableCodeListing : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public CodeListing Listing { get; }

    public SelectableCodeListing(CodeListing listing)
    {
        Listing = listing;
    }
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDocumentService _documentService;
    private readonly IExportService _exportService;
    private readonly ICodeParserService _codeParserService;
    private readonly ITextNormalizerService _textNormalizerService;
    private readonly IValidationService _validationService;
    private readonly DialogService _dialogService;

    private string? _currentFilePath;

    public event Action<List<Paragraph>>? OnInsertParagraphsRequested;

    // Новые события для связи с редактором
    public event Action<int>? OnScrollToParagraphRequested;
    public event Action<int, string>? OnInsertHeadingRequested;

    [ObservableProperty]
    private GostDocument _currentDocument = new GostDocument();

    // НОВОЕ: Наш список "Умного оглавления"
    [ObservableProperty]
    private ObservableCollection<NavigationItem> _navigationItems = new ObservableCollection<NavigationItem>();

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private ObservableCollection<SelectableCodeListing> _codeListings = new ObservableCollection<SelectableCodeListing>();

    [ObservableProperty] private string _university = "«Тверской государственный технический университет»";
    [ObservableProperty] private string _department = "Кафедра [Название]";
    [ObservableProperty] private string _discipline = "[Дисциплина]";
    [ObservableProperty] private string _workType = "[Тип работы]";
    [ObservableProperty] private string _workTitle = "[Тема работы]";
    [ObservableProperty] private string _groupNumber = "[Группа]";
    [ObservableProperty] private string _studentName = "[ФИО студента]";
    [ObservableProperty] private string _teacherName = "[ФИО преподавателя]";
    [ObservableProperty] private string _city = "Тверь";
    [ObservableProperty] private int _year = DateTime.Now.Year;

    [ObservableProperty]
    private string _windowTitle = "Новый документ — GostEditor";

    [ObservableProperty]
    private string _statusMessage = "Готово";

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private bool _hasUnsavedChanges = false;

    [ObservableProperty]
    private bool _isBusy = false;

    public string ZoomPercentage => $"{Math.Round(ZoomLevel * 100)}%";

    partial void OnZoomLevelChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomPercentage));
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

        SyncNavigation();
    }

    // --- УМНАЯ НАВИГАЦИЯ (РАДАР) ---

    public void SyncNavigation()
    {
        NavigationItems.Clear();

        for (int i = 0; i < CurrentDocument.Paragraphs.Count; i++)
        {
            Paragraph p = CurrentDocument.Paragraphs[i];

            // Если находим заголовок - добавляем его в левую панель
            if (p.Style == ParagraphStyle.Heading1 || p.Style == ParagraphStyle.Heading2)
            {
                string text = p.GetPlainText().Trim();
                if (string.IsNullOrEmpty(text)) text = "[Пустой заголовок]";

                NavigationItems.Add(new NavigationItem
                {
                    Title = text,
                    ParagraphIndex = i,
                    Level = p.Style == ParagraphStyle.Heading1 ? 1 : 2
                });
            }
        }
    }

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        // Кликнули в левом меню? Командуем редактору: Скролль к этому абзацу!
        if (value != null)
        {
            OnScrollToParagraphRequested?.Invoke(value.ParagraphIndex);
        }
    }

    [RelayCommand]
    private void AddChapter()
    {
        // Уровень 1 - Глава (с новой страницы)
        OnInsertHeadingRequested?.Invoke(1, "Новая глава");
    }

    [RelayCommand]
    private void AddSubChapter()
    {
        // Уровень 2 - Подраздел (на текущей странице)
        OnInsertHeadingRequested?.Invoke(2, "Новый подраздел");
    }

    // --- ЛОГИКА ТИТУЛЬНОГО ЛИСТА И UI ---

    private void UpdateWindowTitle()
    {
        string fileName = _currentFilePath is not null
            ? System.IO.Path.GetFileName(_currentFilePath)
            : "Новый документ";

        string unsaved = HasUnsavedChanges ? " *" : "";
        WindowTitle = $"{fileName}{unsaved} — GostEditor";
    }

    [RelayCommand]
    private void AddStudent()
    {
        StudentName += "\n[ФИО студента]";
    }

    [RelayCommand]
    private void ResetTitlePage()
    {
        University = "«Тверской государственный технический университет»";
        Department = "Кафедра [Название]";
        Discipline = "[Дисциплина]";
        WorkType = "[Тип работы]";
        WorkTitle = "[Тема работы]";
        GroupNumber = "[Группа]";
        StudentName = "[ФИО студента]";
        TeacherName = "[ФИО преподавателя]";
        City = "Тверь";
        Year = DateTime.Now.Year;

        StatusMessage = "Настройки титульного листа сброшены.";
    }

    // --- ЛОГИКА КОМАНД ИЗ UI ---

    [RelayCommand]
    private async Task PasteNormalizedAsync()
    {
        StatusMessage = "Вставка текста...";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ExportToDocxAsync()
    {
        IsBusy = true;
        StatusMessage = "Экспорт в DOCX...";
        try
        {
            string? exportPath = await _dialogService.ShowSaveFileDialogAsync("Экспорт в DOCX", ".docx", "Word Document (*.docx)|*.docx");
            if (!string.IsNullOrEmpty(exportPath))
            {
                CurrentDocument.TitlePage.University = University;
                CurrentDocument.TitlePage.Department = Department;
                CurrentDocument.TitlePage.Discipline = Discipline;
                CurrentDocument.TitlePage.WorkType = WorkType;
                CurrentDocument.TitlePage.WorkTitle = WorkTitle;
                CurrentDocument.TitlePage.GroupNumber = GroupNumber;
                CurrentDocument.TitlePage.StudentName = StudentName;
                CurrentDocument.TitlePage.TeacherName = TeacherName;
                CurrentDocument.TitlePage.City = City;
                CurrentDocument.TitlePage.Year = Year;

                await _exportService.ExportToDocxAsync(CurrentDocument, exportPath);
                StatusMessage = "Успешно экспортировано в DOCX.";
            }
            else
            {
                StatusMessage = "Экспорт отменен.";
            }
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

    [RelayCommand]
    private async Task ParseCodeFolderAsync()
    {
        IsBusy = true;
        StatusMessage = "Для чтения кода используйте перетаскивание папки (Drag & Drop)";
        await Task.CompletedTask;
        IsBusy = false;
    }

    [RelayCommand]
    private void ClearCodeListings()
    {
        CodeListings.Clear();
        CurrentDocument.CodeListings.Clear();
        StatusMessage = "Список исходников очищен.";
    }

    [RelayCommand]
    private void InsertCodeToEditor()
    {
        if (!CodeListings.Any()) return;

        List<Paragraph> codeParagraphs = new List<Paragraph>();
        int counter = 1;

        foreach (SelectableCodeListing item in CodeListings.Where(l => l.IsSelected))
        {
            Paragraph titlePara = new Paragraph { Alignment = GostAlignment.Left };
            titlePara.Runs.Add(new TextRun($"Листинг {counter}. Файл {item.Listing.RelativePath}", false, false));
            codeParagraphs.Add(titlePara);

            string[] lines = item.Listing.Content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                Paragraph linePara = new Paragraph
                {
                    Alignment = GostAlignment.Left,
                    FirstLineIndent = 0,
                    Style = ParagraphStyle.Code
                };
                linePara.Runs.Add(new TextRun(line.Replace("\t", "    "), false, false));
                codeParagraphs.Add(linePara);
            }

            codeParagraphs.Add(new Paragraph());
            counter++;
        }

        OnInsertParagraphsRequested?.Invoke(codeParagraphs);
        StatusMessage = "Исходный код вставлен в редактор.";
    }
}
