using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Controllers;
using GostEditor.UI.Services;
using GostEditor.UI.ViewModels;

namespace GostEditor.UI.Views;

public partial class MainWindow : Window
{
    private bool _isUpdatingUi;
    private bool _isCloseConfirmed;
    private bool _isClosePromptActive;
    private readonly AutoSaveService? _autoSaveService;
    private readonly RecoveryStorageService? _recoveryStorageService;
    private readonly PersistenceShutdownService? _persistenceShutdownService;
    private readonly PersistenceIoCoordinator? _persistenceIoCoordinator;
    private Task? _closeWorkflow;

    public MainWindow()
        : this(new ImageService())
    {
    }

    public MainWindow(IImageService imageService)
    {
        ArgumentNullException.ThrowIfNull(imageService);
        InitializeComponent();
        MainEditor?.ConfigureImageService(imageService);
        AddHandler(PointerWheelChangedEvent, OnWindowPointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnGlobalPreviewKeyDown, RoutingStrategies.Tunnel);

        if (MainEditor != null)
        {
            MainEditor.CaretStyleChanged += MainEditor_CaretStyleChanged;
        }
    }

    public MainWindow(
        IImageService imageService,
        AutoSaveService autoSaveService,
        RecoveryStorageService recoveryStorageService,
        PersistenceShutdownService persistenceShutdownService,
        PersistenceIoCoordinator persistenceIoCoordinator)
        : this(imageService)
    {
        _autoSaveService = autoSaveService
            ?? throw new ArgumentNullException(nameof(autoSaveService));

        _recoveryStorageService = recoveryStorageService
            ?? throw new ArgumentNullException(nameof(recoveryStorageService));

        _persistenceShutdownService = persistenceShutdownService
            ?? throw new ArgumentNullException(
                nameof(persistenceShutdownService));

        _persistenceIoCoordinator = persistenceIoCoordinator
            ?? throw new ArgumentNullException(
                nameof(persistenceIoCoordinator));

        _autoSaveService.Failed += OnAutoSaveFailed;
        Opened += OnWindowOpened;
        Closed += OnWindowClosed;
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
                MainEditor.ContentChanged -= OnEditorContentChanged;
            }

            // Подписка на новые события
            viewModel.OnInsertParagraphsRequested += InsertParagraphsToEditor;
            viewModel.OnScrollToParagraphRequested += ScrollToParagraph;
            viewModel.OnInsertHeadingRequested += InsertHeading;
            viewModel.OnPasteNormalizedRequested += PasteNormalizedToEditor;
            viewModel.GetEditorDocument += GetDocumentFromEditor;

            if (MainEditor != null)
            {
                MainEditor.ContentChanged += OnEditorContentChanged;
            }

        }
    }

    private async void OnWindowOpened(
        object? sender,
        EventArgs e)
    {
        Opened -= OnWindowOpened;
        await HandleStartupRecoveryAsync();
    }

    private async Task HandleStartupRecoveryAsync()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (_autoSaveService is null ||
            _recoveryStorageService is null)
        {
            return;
        }

        RecoveryStartupResult inspection;
        try
        {
            inspection =
                await _recoveryStorageService.InspectStartupAsync();
        }
        catch (Exception exception)
        {
            await HandleCorruptedRecoveryAsync(
                viewModel,
                "Не удалось проверить каталог аварийного восстановления.",
                exception);
            return;
        }

        if (inspection.State == RecoveryStartupState.None)
        {
            StartAutoSave(viewModel);
            return;
        }

        if (inspection.State == RecoveryStartupState.Corrupted)
        {
            await HandleCorruptedRecoveryAsync(
                viewModel,
                "Не найдено ни одной согласованной и читаемой " +
                "аварийной копии.",
                inspection.Issues.FirstOrDefault()?.Exception ??
                new InvalidDataException(
                    "Recovery содержит только повреждённые данные."));
            return;
        }

        RecoveryMetadata? metadata = inspection.Metadata;
        GostDocument? recoveredDocument = inspection.Document;
        if (recoveredDocument is null)
        {
            await HandleCorruptedRecoveryAsync(
                viewModel,
                "Проверка recovery не вернула документ.",
                new InvalidDataException(
                    "Recoverable state не содержит документа."));
            return;
        }

        RecoveryPromptDialog dialog = new(metadata);

        RecoveryDecision decision =
            await dialog.ShowDialog<RecoveryDecision>(this);

        switch (decision)
        {
            case RecoveryDecision.Restore:
                RestoreRecovery(
                    viewModel,
                    metadata,
                    recoveredDocument,
                    inspection.Issues.Count);
                break;

            case RecoveryDecision.Discard:
                try
                {
                    await _autoSaveService.ResetAsync();
                    viewModel.Session.StartNew();
                    viewModel.StatusMessage =
                        "Аварийная копия удалена";
                    StartAutoSave(viewModel);
                }
                catch (Exception exception)
                {
                    await HandleCorruptedRecoveryAsync(
                        viewModel,
                        "Не удалось удалить аварийную копию. " +
                        "Она не будет перезаписана автоматически.",
                        exception);
                }
                break;

            default:
                CloseApplicationFromStartup();
                break;
        }
    }

    private void RestoreRecovery(
        MainWindowViewModel viewModel,
        RecoveryMetadata? metadata,
        GostDocument recoveredDocument,
        int ignoredDamagedArtifacts)
    {
        if (MainEditor is null)
        {
            return;
        }

        try
        {
            viewModel.CurrentDocument =
                recoveredDocument;

            MainEditor.LoadDocument(
                recoveredDocument);

            viewModel.SyncNavigation();

            viewModel.Session.MarkRecovered(
                metadata?.OriginalFilePath);

            viewModel.StatusMessage = ignoredDamagedArtifacts == 0
                ? "Аварийная копия восстановлена"
                : "Аварийная копия восстановлена; " +
                  "повреждённые варианты пропущены";

            StartAutoSave(viewModel);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"[RECOVERY] Ошибка восстановления: {exception}");

            viewModel.StatusMessage =
                "Не удалось восстановить аварийную копию";

            CloseApplicationFromStartup();
        }
    }

    private async Task HandleCorruptedRecoveryAsync(
        MainWindowViewModel viewModel,
        string problemDescription,
        Exception exception)
    {
        Debug.WriteLine(
            $"[RECOVERY] Повреждённая аварийная копия: {exception}");

        string currentDescription = problemDescription;

        while (true)
        {
            CorruptedRecoveryPromptDialog dialog =
                new(currentDescription);

            CorruptedRecoveryDecision decision =
                await dialog.ShowDialog<CorruptedRecoveryDecision>(this);

            if (decision != CorruptedRecoveryDecision.Delete)
            {
                CloseApplicationFromStartup();
                return;
            }

            try
            {
                await _autoSaveService!.ResetAsync();

                viewModel.Session.StartNew();
                viewModel.StatusMessage =
                    "Повреждённая аварийная копия удалена";

                StartAutoSave(viewModel);
                return;
            }
            catch (Exception deleteException)
            {
                Debug.WriteLine(
                    $"[RECOVERY] Не удалось удалить повреждённую копию: " +
                    $"{deleteException}");

                currentDescription =
                    "Не удалось удалить повреждённую аварийную копию. " +
                    "Проверьте права доступа к каталогу Recovery и " +
                    "повторите попытку либо закройте программу.";
            }
        }
    }

    private void CloseApplicationFromStartup()
    {
        _isCloseConfirmed = true;
        Close();
    }

    private void StartAutoSave(
        MainWindowViewModel viewModel)
    {
        _autoSaveService?.Start(
            () => SyncDocumentFromViewModel(viewModel));
    }

    private void OnEditorContentChanged()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.Session.MarkDirty();
        viewModel.SyncNavigation();
    }

    private void OnAutoSaveFailed(Exception exception)
    {
        Debug.WriteLine(
            $"[AUTOSAVE] Ошибка автоматического сохранения: {exception}");
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_isCloseConfirmed)
        {
            base.OnClosing(e);
            return;
        }

        if (_isClosePromptActive)
        {
            e.Cancel = true;
            base.OnClosing(e);
            return;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            if (_persistenceShutdownService is null &&
                !viewModel.Session.IsDirty)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            _isClosePromptActive = true;
            _closeWorkflow = ObserveWindowCloseAsync(viewModel);
        }

        base.OnClosing(e);
    }

    private async Task ObserveWindowCloseAsync(
        MainWindowViewModel viewModel)
    {
        try
        {
            if (_persistenceShutdownService is not null)
            {
                await _persistenceShutdownService.SuspendAndDrainAsync();

                if (viewModel.Session.IsDirty)
                {
                    _persistenceShutdownService.Resume();
                }
            }

            CloseConfirmation confirmation =
                await ConfirmUnsavedChangesAsync(viewModel);

            if (!confirmation.CanClose)
            {
                return;
            }

            if (MainEditor is not null)
            {
                MainEditor.IsEnabled = false;
            }

            if (confirmation.DiscardChanges)
            {
                await ClearAutoSaveRecoveryAsync();
            }
            else if (viewModel.Session.IsDirty)
            {
                viewModel.StatusMessage =
                    "Появились новые изменения; закрытие отменено";
                return;
            }

            if (_persistenceShutdownService is not null &&
                !_persistenceShutdownService.IsSuspended)
            {
                await _persistenceShutdownService.SuspendAndDrainAsync();
            }

            if (!confirmation.DiscardChanges &&
                viewModel.Session.IsDirty)
            {
                viewModel.StatusMessage =
                    "Появились новые изменения; закрытие отменено";
                return;
            }

            _isCloseConfirmed = true;
            Close();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"[MAINWINDOW] Ошибка подтверждения закрытия: {exception}");

            viewModel.StatusMessage =
                $"Не удалось закрыть документ: {exception.Message}";
        }
        finally
        {
            if (!_isCloseConfirmed)
            {
                if (MainEditor is not null)
                {
                    MainEditor.IsEnabled = true;
                }

                TryResumePersistence(viewModel);
            }

            _isClosePromptActive = false;
            _closeWorkflow = null;
        }
    }

    private void TryResumePersistence(MainWindowViewModel viewModel)
    {
        try
        {
            if (_persistenceShutdownService?.IsSuspended == true)
            {
                _persistenceShutdownService.Resume();
            }

            StartAutoSave(viewModel);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                "[MAINWINDOW] Не удалось возобновить persistence после " +
                $"отмены закрытия: {exception}");

            viewModel.StatusMessage =
                $"Не удалось возобновить автосохранение: {exception.Message}";
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_autoSaveService is null)
        {
            return;
        }

        _autoSaveService.Stop();
        _autoSaveService.Failed -= OnAutoSaveFailed;
        Opened -= OnWindowOpened;
        Closed -= OnWindowClosed;
    }

    private async Task ClearAutoSaveRecoveryAsync(
        long? expectedCleanRevision = null)
    {
        if (_autoSaveService is null)
        {
            return;
        }

        try
        {
            await _autoSaveService.ClearRecoveryAsync(
                expectedCleanRevision);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"[AUTOSAVE] Не удалось удалить аварийную копию: {exception}");
        }
    }

    private async Task ResetAutoSaveRecoveryAsync()
    {
        if (_autoSaveService is null)
        {
            return;
        }

        try
        {
            await _autoSaveService.ResetAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"[AUTOSAVE] Не удалось сбросить аварийную копию: {exception}");
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
        return MainEditor?.CurrentDocument ?? viewModel.CurrentDocument;
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
        if ((e.KeyModifiers & KeyModifiers.Control) == 0 ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        ScrollViewer? scrollViewer =
            this.FindControl<ScrollViewer>("EditorScrollViewer");

        if (scrollViewer is null)
        {
            return;
        }

        Point pointerPosition = e.GetPosition(scrollViewer);

        if (pointerPosition.X < 0 ||
            pointerPosition.Y < 0 ||
            pointerPosition.X > scrollViewer.Bounds.Width ||
            pointerPosition.Y > scrollViewer.Bounds.Height)
        {
            return;
        }

        double oldZoom = viewModel.ZoomLevel;
        double zoomStep = e.Delta.Y > 0 ? 0.1 : -0.1;

        double newZoom = Math.Clamp(
            Math.Round(oldZoom + zoomStep, 1),
            0.5,
            2.0);

        if (Math.Abs(newZoom - oldZoom) < 0.001)
        {
            e.Handled = true;
            return;
        }

        Vector oldOffset = scrollViewer.Offset;
        double ratio = newZoom / oldZoom;

        double targetX =
            (oldOffset.X + pointerPosition.X) * ratio - pointerPosition.X;

        double targetY =
            (oldOffset.Y + pointerPosition.Y) * ratio - pointerPosition.Y;

        viewModel.ZoomLevel = newZoom;

        Dispatcher.UIThread.Post(
            () =>
            {
                double maxX = Math.Max(
                    0,
                    scrollViewer.Extent.Width - scrollViewer.Viewport.Width);

                double maxY = Math.Max(
                    0,
                    scrollViewer.Extent.Height - scrollViewer.Viewport.Height);

                scrollViewer.Offset = new Vector(
                    Math.Clamp(targetX, 0, maxX),
                    Math.Clamp(targetY, 0, maxY));
            },
            DispatcherPriority.Loaded);

        e.Handled = true;
    }

    private void OnContentStartPageValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (MainEditor == null || e.NewValue is not decimal value)
        {
            return;
        }

        int pageNumber = Math.Max(1, decimal.ToInt32(value));
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ContentStartPage = pageNumber;
        }

        MainEditor.SetStartPageNumber(pageNumber);
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
        if (DataContext is not MainWindowViewModel viewModel ||
            MainEditor is null)
        {
            return;
        }

        CloseConfirmation confirmation =
            await ConfirmUnsavedChangesAsync(viewModel);

        if (!confirmation.CanClose)
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
                IStorageFile selectedFile = files[0];

                string? filePath = selectedFile.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    throw new InvalidOperationException(
                        "Не удалось определить путь открытого документа.");
                }

                await LoadDocumentForOpenAsync(
                    viewModel.ArchiveService,
                    selectedFile.OpenReadAsync,
                    _persistenceIoCoordinator,
                    (loadedDocument, fingerprint) => PublishOpenedDocument(
                        viewModel,
                        loadedDocument,
                        filePath,
                        fingerprint));

                await ResetAutoSaveRecoveryAsync();

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

    private void PublishOpenedDocument(
        MainWindowViewModel viewModel,
        GostDocument loadedDocument,
        string filePath,
        FileContentFingerprint fileFingerprint)
    {
        viewModel.CurrentDocument = loadedDocument;
        MainEditor!.LoadDocument(loadedDocument);
        viewModel.SyncNavigation();
        viewModel.Session.MarkOpened(filePath, fileFingerprint);
    }

    internal static async Task LoadDocumentForOpenAsync(
        IArchiveService archiveService,
        Func<Task<Stream>> openStreamAsync,
        PersistenceIoCoordinator? ioCoordinator,
        Action<GostDocument> publishDocument,
        CancellationToken cancellationToken = default)
    {
        await LoadDocumentForOpenAsync(
            archiveService,
            openStreamAsync,
            ioCoordinator,
            (document, _) => publishDocument(document),
            cancellationToken);
    }

    internal static async Task LoadDocumentForOpenAsync(
        IArchiveService archiveService,
        Func<Task<Stream>> openStreamAsync,
        PersistenceIoCoordinator? ioCoordinator,
        Action<GostDocument, FileContentFingerprint> publishDocument,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveService);
        ArgumentNullException.ThrowIfNull(openStreamAsync);
        ArgumentNullException.ThrowIfNull(publishDocument);

        if (ioCoordinator is null)
        {
            await using Stream uncoordinatedStream =
                await openStreamAsync();
            (GostDocument document, FileContentFingerprint fingerprint) =
                await LoadDocumentAndFingerprintAsync(
                    archiveService,
                    uncoordinatedStream,
                    cancellationToken);
            publishDocument(document, fingerprint);
            return;
        }

        using PersistenceIoCoordinator.PersistenceIoLease ownership =
            await ioCoordinator.AcquireAsync(
                PersistenceIoOperation.Open,
                cancellationToken);

        ownership.CancellationToken.ThrowIfCancellationRequested();

        await using Stream stream = await openStreamAsync();

        (GostDocument loadedDocument,
            FileContentFingerprint coordinatedFingerprint) =
            await LoadDocumentAndFingerprintAsync(
                archiveService,
                stream,
                ownership.CancellationToken);

        ownership.CancellationToken.ThrowIfCancellationRequested();
        publishDocument(loadedDocument, coordinatedFingerprint);
    }

    private static async Task<(
        GostDocument Document,
        FileContentFingerprint Fingerprint)> LoadDocumentAndFingerprintAsync(
            IArchiveService archiveService,
            Stream source,
            CancellationToken cancellationToken)
    {
        await using MemoryStream buffered = new();
        await source.CopyToAsync(buffered, cancellationToken);

        buffered.Position = 0;
        FileContentFingerprint fingerprint =
            await new FileContentFingerprintService().CaptureAsync(
                buffered,
                cancellationToken);

        buffered.Position = 0;
        GostDocument document =
            await archiveService.LoadAsync(buffered);
        return (document, fingerprint);
    }

    private async Task<bool> SaveDocumentToFileAsync(
        bool forceSaveAs = false,
        bool overwriteExternalChanges = false)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            MainEditor?.CurrentDocument is null)
        {
            return false;
        }

        viewModel.IsBusy = true;
        viewModel.StatusMessage = "Сохранение...";

        try
        {
            string? filePath = viewModel.Session.CurrentFilePath;

            if (forceSaveAs || string.IsNullOrWhiteSpace(filePath))
            {
                IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
                    new FilePickerSaveOptions
                    {
                        Title = "Сохранить документ",
                        DefaultExtension = ".gost",
                        FileTypeChoices =
                        [
                            new FilePickerFileType("GOST Document")
                            {
                                Patterns = ["*.gost"]
                            }
                        ]
                    });

                if (file is null)
                {
                    viewModel.StatusMessage = "Сохранение отменено";
                    return false;
                }

                filePath = file.TryGetLocalPath();

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    throw new InvalidOperationException(
                        "Не удалось определить путь сохранённого документа.");
                }
            }

            GostDocument documentToSave =
                SyncDocumentFromViewModel(viewModel);

            DocumentSaveResult saveResult =
                await viewModel.DocumentSaveService.SaveAsync(
                    documentToSave,
                    filePath,
                    overwriteExternalChanges: overwriteExternalChanges);

            if (saveResult.IsCurrentRevision)
            {
                await ClearAutoSaveRecoveryAsync(
                    saveResult.SavedRevision);
            }

            viewModel.StatusMessage = viewModel.Session.IsDirty
                ? "Сохранена предыдущая версия; есть новые изменения"
                : "Документ сохранён";
            return CanCloseAfterSave(saveResult, viewModel.Session);
        }
        catch (ExternalFileChangedException exception)
        {
            ExternalFileConflictDialog dialog = new(
                Path.GetFileName(exception.FilePath));
            ExternalFileConflictDecision decision =
                await dialog.ShowDialog<ExternalFileConflictDecision>(this);

            return decision switch
            {
                ExternalFileConflictDecision.Overwrite =>
                    await SaveDocumentToFileAsync(
                        overwriteExternalChanges: true),
                ExternalFileConflictDecision.SaveCopy =>
                    await SaveDocumentToFileAsync(forceSaveAs: true),
                _ => false
            };
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage =
                $"Ошибка сохранения: {ex.Message}";

            Debug.WriteLine(
                $"[MAINWINDOW] Ошибка сохранения: {ex}");

            return false;
        }
        finally
        {
            viewModel.IsBusy = false;
        }
    }

    private async Task<CloseConfirmation> ConfirmUnsavedChangesAsync(
        MainWindowViewModel viewModel)
    {
        if (!viewModel.Session.IsDirty)
        {
            return new CloseConfirmation(
                CanClose: true,
                DiscardChanges: false);
        }

        UnsavedChangesPromptDialog dialog =
            new(viewModel.Session.DocumentName);

        UnsavedChangesDecision decision =
            await dialog.ShowDialog<UnsavedChangesDecision>(this);

        switch (decision)
        {
            case UnsavedChangesDecision.Save:
                return new CloseConfirmation(
                    CanClose: await SaveDocumentToFileAsync(),
                    DiscardChanges: false);

            case UnsavedChangesDecision.Discard:
                return new CloseConfirmation(
                    CanClose: true,
                    DiscardChanges: true);

            default:
                return new CloseConfirmation(
                    CanClose: false,
                    DiscardChanges: false);
        }
    }

    internal static bool CanCloseAfterSave(
        DocumentSaveResult saveResult,
        DocumentSessionState session)
    {
        ArgumentNullException.ThrowIfNull(saveResult);
        ArgumentNullException.ThrowIfNull(session);

        return saveResult.IsCurrentRevision &&
               !session.IsDirty &&
               session.ChangeVersion == saveResult.SavedRevision &&
               session.SavedRevision == saveResult.SavedRevision;
    }

    private readonly record struct CloseConfirmation(
        bool CanClose,
        bool DiscardChanges);

    private async void OnNewDocumentClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            MainEditor is null)
        {
            return;
        }

        CloseConfirmation confirmation =
            await ConfirmUnsavedChangesAsync(viewModel);

        if (!confirmation.CanClose)
        {
            return;
        }

        GostDocument newDocument = new();

        viewModel.CurrentDocument = newDocument;
        MainEditor.LoadDocument(newDocument);
        viewModel.SyncNavigation();
        viewModel.Session.StartNew();

        await ResetAutoSaveRecoveryAsync();

        viewModel.StatusMessage = "Создан новый документ";
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
            await viewModel.DocumentExportService.ExportToDocxAsync(
                documentToExport,
                outputPath);

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
