using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Services;

namespace GostEditor.UI.ViewModels;

/// <summary>
/// Главная модель представления для управления состоянием окна редактора.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IArchiveService _archiveService;
    private readonly DocumentExportService _documentExportService;
    private readonly ICodeParserService _codeParserService;
    private readonly DocumentSaveService _documentSaveService;

    private readonly HashSet<CodeListingViewModel> _trackedCodeListings = new();
    private readonly HashSet<BibliographySourceViewModel> _trackedBibliographySources = new();
    private bool _isSynchronizingDocument;

    public IArchiveService ArchiveService => _archiveService;
    public DocumentExportService DocumentExportService =>
        _documentExportService;
    public ICodeParserService CodeParserService => _codeParserService;
    public DocumentSaveService DocumentSaveService => _documentSaveService;

    public DocumentSessionState Session { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Готово";

    private double _zoomLevel = 1.0;

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            if (SetProperty(ref _zoomLevel, value))
            {
                OnPropertyChanged(nameof(ZoomPercentage));
            }
        }
    }

    public string ZoomPercentage => $"{(int)(ZoomLevel * 100)}%";

    [ObservableProperty]
    private GostDocument _currentDocument;

    // События для взаимодействия с редактором
    public event Action<List<Paragraph>>? OnInsertParagraphsRequested;
    public event Action<int>? OnScrollToParagraphRequested;
    public event Action<int, string>? OnInsertHeadingRequested;
    public event Action? OnPasteNormalizedRequested;
    public event Func<GostDocument>? GetEditorDocument;

    // === МЕТАДАННЫЕ ТИТУЛЬНОГО ЛИСТА ===
    [ObservableProperty]
    private string _university = string.Empty;

    [ObservableProperty]
    private string _department = string.Empty;

    [ObservableProperty]
    private string _discipline = string.Empty;

    [ObservableProperty]
    private string _workType = string.Empty;

    [ObservableProperty]
    private string _workTitle = string.Empty;

    [ObservableProperty]
    private string _studentName = string.Empty;

    [ObservableProperty]
    private string _groupNumber = string.Empty;

    [ObservableProperty]
    private string _teacherName = string.Empty;

    [ObservableProperty]
    private string _city = string.Empty;

    [ObservableProperty]
    private int _year = DateTime.Now.Year;

    // === МОДУЛЬНАЯ СИСТЕМА ===

    [ObservableProperty]
    private int _selectedModuleIndex = 0;

    [ObservableProperty]
    private bool _hasTitlePage = true;

    [ObservableProperty]
    private bool _hasTableOfContents = true;

    [ObservableProperty]
    private bool _hasBibliography = false;

    [ObservableProperty]
    private bool _hasAppendix = true;

    [ObservableProperty]
    private int _contentStartPage = 3;

    // === НАВИГАЦИЯ ПО ДОКУМЕНТУ ===

    [ObservableProperty]
    private ObservableCollection<NavigationItem> _navigationItems = new ObservableCollection<NavigationItem>();

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    // === ЛИСТИНГИ КОДА ===

    [ObservableProperty]
    private ObservableCollection<CodeListingViewModel> _codeListings = new ObservableCollection<CodeListingViewModel>();

    // === СПИСОК ЛИТЕРАТУРЫ ===

    [ObservableProperty]
    private ObservableCollection<BibliographySourceViewModel> _bibliographySources = new ObservableCollection<BibliographySourceViewModel>();

    [ObservableProperty]
    private BibliographySourceViewModel? _selectedBibliographySource;

    [ObservableProperty]
    private string _newBibliographySource = string.Empty;

    // === КОНСТРУКТОР ===

    public MainWindowViewModel(
        IArchiveService archiveService,
        DocumentExportService documentExportService,
        ICodeParserService codeParserService,
        DocumentSessionState session,
        DocumentSaveService documentSaveService)
    {
        _archiveService = archiveService
            ?? throw new ArgumentNullException(nameof(archiveService));

        _documentExportService = documentExportService
            ?? throw new ArgumentNullException(
                nameof(documentExportService));

        _codeParserService = codeParserService
            ?? throw new ArgumentNullException(nameof(codeParserService));

        Session = session
            ?? throw new ArgumentNullException(nameof(session));

        _documentSaveService = documentSaveService
            ?? throw new ArgumentNullException(nameof(documentSaveService));

        _currentDocument = new GostDocument();

        CodeListings.CollectionChanged += OnCodeListingsCollectionChanged;
        BibliographySources.CollectionChanged += OnBibliographySourcesCollectionChanged;

        SynchronizeCodeListingSubscriptions();
        SynchronizeBibliographySubscriptions();

        Debug.WriteLine("[VM] MainWindowViewModel инициализирован");
    }

    // === МЕТОДЫ ===

    /// <summary>
    /// Синхронизирует навигацию с текущим документом (строит дерево заголовков)
    /// </summary>
    public void SyncNavigation()
    {
        if (CurrentDocument == null)
        {
            Debug.WriteLine("[VM] SyncNavigation: CurrentDocument is null");
            return;
        }

        NavigationItems.Clear();

        for (int i = 0; i < CurrentDocument.Paragraphs.Count; i++)
        {
            Paragraph paragraph = CurrentDocument.Paragraphs[i];

            if (paragraph.Style == ParagraphStyle.Heading1 ||
                paragraph.Style == ParagraphStyle.Heading2)
            {
                int level = paragraph.Style == ParagraphStyle.Heading1 ? 1 : 2;
                string title = paragraph.GetPlainText();

                NavigationItems.Add(new NavigationItem
                {
                    Title = title,
                    ParagraphIndex = i,
                    Level = level
                });

                Debug.WriteLine($"[VM] Добавлен заголовок: '{title}' (уровень {level})");
            }
        }

        Debug.WriteLine($"[VM] SyncNavigation завершена: найдено {NavigationItems.Count} заголовков");
    }

    /// <summary>
    /// Вызывается автоматически при изменении выбранного элемента навигации
    /// </summary>
    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        if (value != null)
        {
            Debug.WriteLine($"[VM] Навигация к параграфу {value.ParagraphIndex}: '{value.Title}'");
            OnScrollToParagraphRequested?.Invoke(value.ParagraphIndex);
        }
    }

    // === КОМАНДЫ ===

    /// <summary>
    /// Экспорт документа в формат DOCX
    /// </summary>
    [RelayCommand]
    private async Task ExportToDocxAsync()
    {
        IsBusy = true;
        StatusMessage = "Экспорт в DOCX...";

        Debug.WriteLine("[VM] Начало экспорта DOCX");

        try
        {
            // Получаем актуальный документ из редактора
            GostDocument editorDocument = GetEditorDocument?.Invoke() ?? CurrentDocument;

            Debug.WriteLine($"[VM] Документ для экспорта получен:");
            Debug.WriteLine($"[VM]   Параграфов: {editorDocument.Paragraphs.Count}");
            Debug.WriteLine($"[VM]   Листингов: {editorDocument.CodeListings.Count}");
            Debug.WriteLine($"[VM]   Изображений: {editorDocument.Images.Count}");

            // ВАЖНО: Экспорт вызывается из MainWindow.axaml.cs
            await Task.CompletedTask;

            StatusMessage = "Готово к экспорту";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка подготовки к экспорту: {ex.Message}";
            Debug.WriteLine($"[VM] Ошибка экспорта: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Добавить главу (заголовок 1 уровня)
    /// </summary>
    [RelayCommand]
    private void AddChapter()
    {
        Debug.WriteLine("[VM] Добавление главы");
        OnInsertHeadingRequested?.Invoke(1, "Новая глава");
    }

    /// <summary>
    /// Добавить подраздел (заголовок 2 уровня)
    /// </summary>
    [RelayCommand]
    private void AddSubChapter()
    {
        Debug.WriteLine("[VM] Добавление подраздела");
        OnInsertHeadingRequested?.Invoke(2, "Новый подраздел");
    }

    /// <summary>
    /// Сброс всех полей титульного листа
    /// </summary>
    [RelayCommand]
    private void ResetTitlePage()
    {
        Debug.WriteLine("[VM] Сброс титульного листа");

        University = string.Empty;
        Department = string.Empty;
        Discipline = string.Empty;
        WorkType = string.Empty;
        WorkTitle = string.Empty;
        StudentName = string.Empty;
        GroupNumber = string.Empty;
        TeacherName = string.Empty;
        City = string.Empty;
        Year = DateTime.Now.Year;

        StatusMessage = "Титульный лист сброшен";
    }

    /// <summary>
    /// Очистить список листингов кода
    /// </summary>
    [RelayCommand]
    private void ClearCodeListings()
    {
        Debug.WriteLine("[VM] Очистка списка листингов");

        CodeListings.Clear();
        StatusMessage = "Список листингов очищен";
    }

    /// <summary>
    /// Вставить выбранные листинги в редактор
    /// ИСПРАВЛЕНО: Используем CodeParserService.GenerateAppendixParagraphs для правильного формата
    /// </summary>
    [RelayCommand]
    private void InsertCodeToEditor()
    {
        Debug.WriteLine("[VM] Вставка кода в редактор");

        if (!CodeListings.Any(l => l.IsSelected))
        {
            StatusMessage = "Не выбрано ни одного листинга";
            Debug.WriteLine("[VM] Нет выбранных листингов");
            return;
        }

        try
        {
            List<Paragraph> paragraphs = _codeParserService.GenerateAppendixParagraphs(
                CodeListings.Where(l => l.IsSelected).Select(vm => vm.Listing)
            );

            Debug.WriteLine($"[VM] Сгенерировано параграфов: {paragraphs.Count}");

            if (paragraphs.Count > 0)
            {
                OnInsertParagraphsRequested?.Invoke(paragraphs);

                int selectedCount = CodeListings.Count(l => l.IsSelected);
                StatusMessage = $"Вставлено листингов: {selectedCount}";

                Debug.WriteLine($"[VM] Вставлено листингов в редактор: {selectedCount}");
            }
            else
            {
                StatusMessage = "Ошибка генерации листингов";
                Debug.WriteLine("[VM] GenerateAppendixParagraphs вернул пустой список");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка вставки кода: {ex.Message}";
            Debug.WriteLine($"[VM] Ошибка вставки кода: {ex}");
        }
    }

    /// <summary>
    /// Вставка нормализованного текста (очищенного от форматирования)
    /// </summary>
    [RelayCommand]
    private void PasteNormalized()
    {
        Debug.WriteLine("[VM] Вставка нормализованного текста");
        OnPasteNormalizedRequested?.Invoke();
        StatusMessage = "Вставлен очищенный текст";
    }

    /// <summary>
    /// Добавить соавтора/студента
    /// </summary>
    [RelayCommand]
    private void AddStudent()
    {
        Debug.WriteLine("[VM] Добавление соавтора");

        if (string.IsNullOrWhiteSpace(StudentName))
        {
            StudentName = "Новый соавтор";
        }
        else
        {
            StudentName += Environment.NewLine + "Новый соавтор";
        }

        StatusMessage = "Соавтор добавлен";
    }

    [RelayCommand]
    private void AddBibliographySource()
    {
        string description = NewBibliographySource.Trim();
        if (string.IsNullOrEmpty(description))
        {
            StatusMessage = "Введите описание источника";
            return;
        }

        BibliographySource source = new BibliographySource
        {
            Description = description,
            Order = BibliographySources.Count
        };

        BibliographySources.Add(new BibliographySourceViewModel
        {
            Source = source,
            IsSelected = true
        });

        NewBibliographySource = string.Empty;
        HasBibliography = true;
        StatusMessage = "Источник добавлен";
    }

    [RelayCommand]
    private void RemoveBibliographySource()
    {
        if (SelectedBibliographySource == null)
        {
            StatusMessage = "Выберите источник для удаления";
            return;
        }

        BibliographySources.Remove(SelectedBibliographySource);
        SelectedBibliographySource = null;
        RenumberBibliographySources();
        StatusMessage = "Источник удалён";
    }

    [RelayCommand]
    private void ClearBibliographySources()
    {
        BibliographySources.Clear();
        StatusMessage = "Список литературы очищен";
    }

    private void RenumberBibliographySources()
    {
        for (int i = 0; i < BibliographySources.Count; i++)
        {
            BibliographySources[i].Source.Order = i;
            BibliographySources[i].RefreshDisplayNumber();
        }
    }

    private void OnCodeListingsCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        SynchronizeCodeListingSubscriptions();

        if (!_isSynchronizingDocument)
        {
            if (CodeListings.Count == 0 &&
                CurrentDocument.CodeListings.Count == 0)
            {
                return;
            }

            SynchronizeCodeListingsToDocument();
            Session.RecordMutation();
        }
    }

    private void OnBibliographySourcesCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        SynchronizeBibliographySubscriptions();

        if (!_isSynchronizingDocument)
        {
            if (BibliographySources.Count == 0 &&
                CurrentDocument.BibliographySources.Count == 0)
            {
                return;
            }

            SynchronizeBibliographyToDocument();
            Session.RecordMutation();
        }
    }

    private void SynchronizeCodeListingSubscriptions()
    {
        foreach (CodeListingViewModel item in _trackedCodeListings.ToArray())
        {
            if (CodeListings.Contains(item))
            {
                continue;
            }

            item.PropertyChanged -= OnTrackedItemPropertyChanged;
            _trackedCodeListings.Remove(item);
        }

        foreach (CodeListingViewModel item in CodeListings)
        {
            if (!_trackedCodeListings.Add(item))
            {
                continue;
            }

            item.PropertyChanged += OnTrackedItemPropertyChanged;
        }
    }

    private void SynchronizeBibliographySubscriptions()
    {
        foreach (BibliographySourceViewModel item in _trackedBibliographySources.ToArray())
        {
            if (BibliographySources.Contains(item))
            {
                continue;
            }

            item.PropertyChanged -= OnTrackedItemPropertyChanged;
            _trackedBibliographySources.Remove(item);
        }

        foreach (BibliographySourceViewModel item in BibliographySources)
        {
            if (!_trackedBibliographySources.Add(item))
            {
                continue;
            }

            item.PropertyChanged += OnTrackedItemPropertyChanged;
        }
    }

    private void OnTrackedItemPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_isSynchronizingDocument)
        {
            return;
        }

        switch (sender)
        {
            case CodeListingViewModel when
                string.IsNullOrEmpty(e.PropertyName) ||
                e.PropertyName is
                    nameof(CodeListingViewModel.IsSelected) or
                    nameof(CodeListingViewModel.Listing):
                SynchronizeCodeListingsToDocument();
                break;

            case BibliographySourceViewModel when
                string.IsNullOrEmpty(e.PropertyName) ||
                e.PropertyName is
                    nameof(BibliographySourceViewModel.IsSelected) or
                    nameof(BibliographySourceViewModel.Source) or
                    nameof(BibliographySourceViewModel.Description):
                SynchronizeBibliographyToDocument();
                break;

            default:
                return;
        }

        Session.RecordMutation();
    }

    // === ОТСЛЕЖИВАНИЕ ИЗМЕНЕНИЙ ДОКУМЕНТА ===

    partial void OnUniversityChanged(string value)
    {
        RecordTitlePageMutation(titlePage => titlePage.University = value);
    }

    partial void OnDepartmentChanged(string value)
    {
        RecordTitlePageMutation(titlePage => titlePage.Department = value);
    }

    partial void OnDisciplineChanged(string value)
    {
        RecordTitlePageMutation(titlePage => titlePage.Discipline = value);
    }

    partial void OnWorkTypeChanged(string value)
    {
        RecordTitlePageMutation(titlePage => titlePage.WorkType = value);
    }

    partial void OnWorkTitleChanged(string value)
    {
        RecordTitlePageMutation(titlePage => titlePage.WorkTitle = value);
    }

    partial void OnStudentNameChanged(string value)
    {
        RecordTitlePageMutation(titlePage => titlePage.StudentName = value);
    }

    partial void OnGroupNumberChanged(string value)
    {
        RecordTitlePageMutation(titlePage => titlePage.GroupNumber = value);
    }

    partial void OnTeacherNameChanged(string value)
    {
        RecordTitlePageMutation(titlePage => titlePage.TeacherName = value);
    }

    partial void OnCityChanged(string value)
    {
        RecordTitlePageMutation(titlePage => titlePage.City = value);
    }

    partial void OnYearChanged(int value)
    {
        RecordTitlePageMutation(titlePage => titlePage.Year = value);
    }

    // === СИНХРОНИЗАЦИЯ МОДУЛЕЙ С ДОКУМЕНТОМ ===

    partial void OnHasTitlePageChanged(bool value)
    {
        RecordModuleMutation(modules => modules.HasTitlePage = value);
    }

    partial void OnHasTableOfContentsChanged(bool value)
    {
        RecordModuleMutation(modules => modules.HasTableOfContents = value);
    }

    partial void OnHasBibliographyChanged(bool value)
    {
        RecordModuleMutation(modules => modules.HasBibliography = value);
    }

    partial void OnHasAppendixChanged(bool value)
    {
        RecordModuleMutation(modules => modules.HasAppendix = value);
    }

    partial void OnContentStartPageChanged(int value)
    {
        RecordModuleMutation(modules => modules.ContentStartPage = value);
    }

    private void RecordTitlePageMutation(Action<TitlePageInfo> mutation)
    {
        if (_isSynchronizingDocument)
        {
            return;
        }

        mutation(CurrentDocument.TitlePage);
        Session.RecordMutation();
    }

    private void RecordModuleMutation(Action<DocumentModules> mutation)
    {
        if (_isSynchronizingDocument)
        {
            return;
        }

        mutation(CurrentDocument.Modules);
        Session.RecordMutation();
    }

    private void SynchronizeCodeListingsToDocument()
    {
        CurrentDocument.CodeListings.Clear();
        foreach (CodeListingViewModel viewModel in CodeListings)
        {
            viewModel.Listing.IsSelected = viewModel.IsSelected;
            CurrentDocument.CodeListings.Add(viewModel.Listing);
        }
    }

    private void SynchronizeBibliographyToDocument()
    {
        CurrentDocument.BibliographySources.Clear();
        for (int index = 0; index < BibliographySources.Count; index++)
        {
            BibliographySourceViewModel viewModel = BibliographySources[index];
            viewModel.Source.Order = index;
            viewModel.Source.IsSelected = viewModel.IsSelected;
            CurrentDocument.BibliographySources.Add(viewModel.Source);
        }

        CurrentDocument.Counters.SourcesCount =
            CurrentDocument.BibliographySources.Count(
                source => source.IsSelected);
    }

    /// <summary>
    /// Вызывается автоматически при изменении CurrentDocument
    /// Синхронизирует настройки модулей из документа в UI
    /// </summary>
    partial void OnCurrentDocumentChanged(GostDocument value)
    {
        _isSynchronizingDocument = true;

        try
        {
            if (value?.Modules == null)
            {
                return;
            }

            _university = value.TitlePage.University;
            _department = value.TitlePage.Department;
            _discipline = value.TitlePage.Discipline;
            _workType = value.TitlePage.WorkType;
            _workTitle = value.TitlePage.WorkTitle;
            _studentName = value.TitlePage.StudentName;
            _groupNumber = value.TitlePage.GroupNumber;
            _teacherName = value.TitlePage.TeacherName;
            _city = value.TitlePage.City;
            _year = value.TitlePage.Year;

            // Загружаем настройки модулей из документа в UI
            // Используем backing fields для избежания вызова OnChanged методов
            _hasTitlePage = value.Modules.HasTitlePage;
            _hasTableOfContents = value.Modules.HasTableOfContents;
            _hasBibliography = value.Modules.HasBibliography;
            _hasAppendix = value.Modules.HasAppendix;
            _contentStartPage = value.Modules.ContentStartPage;

            // Уведомляем UI об изменениях
            OnPropertyChanged(nameof(University));
            OnPropertyChanged(nameof(Department));
            OnPropertyChanged(nameof(Discipline));
            OnPropertyChanged(nameof(WorkType));
            OnPropertyChanged(nameof(WorkTitle));
            OnPropertyChanged(nameof(StudentName));
            OnPropertyChanged(nameof(GroupNumber));
            OnPropertyChanged(nameof(TeacherName));
            OnPropertyChanged(nameof(City));
            OnPropertyChanged(nameof(Year));
            OnPropertyChanged(nameof(HasTitlePage));
            OnPropertyChanged(nameof(HasTableOfContents));
            OnPropertyChanged(nameof(HasBibliography));
            OnPropertyChanged(nameof(HasAppendix));
            OnPropertyChanged(nameof(ContentStartPage));

            CodeListings.Clear();
            foreach (CodeListing listing in value.CodeListings)
            {
                CodeListings.Add(new CodeListingViewModel
                {
                    Listing = listing,
                    IsSelected = listing.IsSelected
                });
            }

            BibliographySources.Clear();
            foreach (BibliographySource source in value.BibliographySources)
            {
                BibliographySources.Add(new BibliographySourceViewModel
                {
                    Source = source,
                    IsSelected = source.IsSelected
                });
            }

            Debug.WriteLine($"[VM] Синхронизированы настройки модулей из документа");
            Debug.WriteLine($"[VM]   TitlePage: {_hasTitlePage}");
            Debug.WriteLine($"[VM]   TOC: {_hasTableOfContents}");
            Debug.WriteLine($"[VM]   Bibliography: {_hasBibliography}");
            Debug.WriteLine($"[VM]   Appendix: {_hasAppendix}");
            Debug.WriteLine($"[VM]   ContentStartPage: {_contentStartPage}");
        }
        finally
        {
            _isSynchronizingDocument = false;
        }
    }
}

/// <summary>
/// ViewModel для отображения листинга кода с чекбоксом выбора
/// </summary>
public partial class CodeListingViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private CodeListing _listing = new CodeListing();

    public string DisplayName =>
        !string.IsNullOrEmpty(Listing.RelativePath)
            ? Listing.RelativePath
            : Listing.FileName;

    public string Language => Listing.Language;

    public int LinesCount =>
        string.IsNullOrEmpty(Listing.Content)
            ? 0
            : Listing.Content.Split('\n').Length;

    partial void OnIsSelectedChanged(bool value)
    {
        Listing.IsSelected = value;
    }
}

public partial class BibliographySourceViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private BibliographySource _source = new BibliographySource();

    public int DisplayNumber => Source.Order + 1;

    public string Description
    {
        get => Source.Description;
        set
        {
            if (Source.Description != value)
            {
                Source.Description = value;
                OnPropertyChanged();
            }
        }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        Source.IsSelected = value;
    }

    public void RefreshDisplayNumber()
    {
        OnPropertyChanged(nameof(DisplayNumber));
    }
}
