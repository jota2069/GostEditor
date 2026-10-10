#pragma warning disable CS0618

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Controllers;
using GostEditor.UI.Layout;

namespace GostEditor.UI.Views;

public partial class DocumentEngineView : UserControl
{
    private DocumentEditor _editor = null!;
    private RenderController _renderController = null!;
    private TextInputController _textInputController = null!;
    private SelectionController _selectionController = null!;
    private MouseController _mouseController = null!;
    private ImageController _imageController = null!;
    private PageLayoutManager _layoutManager = null!;
    private IImageService _imageService = null!;
    private bool _isConfigured;
    private bool _inputFocusRequested;

    public event EventHandler<CaretStyleChangedEventArgs>? CaretStyleChanged;
    public event Action? ContentChanged;

    public GostDocument CurrentDocument => _editor.Document;

    public DocumentEngineView()
    {
        InitializeComponent();
    }

    public void ConfigureImageService(IImageService imageService)
    {
        ArgumentNullException.ThrowIfNull(imageService);
        if (_isConfigured)
        {
            if (!ReferenceEquals(_imageService, imageService))
            {
                throw new InvalidOperationException(
                    "DocumentEngineView уже настроен с другим IImageService.");
            }

            return;
        }

        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        _editor = new DocumentEditor(new GostDocument(), _imageService);
        _layoutManager = new PageLayoutManager(_imageService);
        Typeface defaultTypeface = new Typeface("Times New Roman");

        _renderController = new RenderController(_editor, _layoutManager, defaultTypeface);
        _selectionController = new SelectionController(_editor);
        _textInputController = new TextInputController(_editor, _renderController);
        _mouseController = new MouseController(
            _editor,
            _renderController);
        _imageController = new ImageController(_editor, _renderController, _layoutManager);

        _editor.DocumentChanged += (_, _) => ContentChanged?.Invoke();
        _renderController.CaretStyleChanged += (s, e) => CaretStyleChanged?.Invoke(this, e);
        _isConfigured = true;

        AddHandler(ContextRequestedEvent, (s, e) => e.Handled = true, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnGlobalPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnGlobalPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnTextInputAsync, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDownAsync, RoutingStrategies.Tunnel);

        if (this.FindControl<StackPanel>("PagesStackPanel") is { } pagesPanel)
        {
            _renderController.AttachUi(pagesPanel);
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_isConfigured &&
            this.FindControl<StackPanel>("PagesStackPanel") is { } pagesPanel)
            _renderController.AttachUi(pagesPanel);
    }

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TryApplyRequestedInputFocus();
        Dispatcher.UIThread.Post(
            TryApplyRequestedInputFocus,
            DispatcherPriority.Loaded);
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _inputFocusRequested = false;
    }

    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Visual? visual = e.Source as Visual;
        while (visual != null && !(visual is GostEditor.UI.Controls.GostPageControl))
            visual = (Visual?)visual.GetVisualParent();

        if (visual is GostEditor.UI.Controls.GostPageControl pageControl && pageControl.Parent is StackPanel stack)
        {
            int pageIndex = stack.Children.IndexOf(pageControl);
            Point point = e.GetPosition(pageControl);
            PointerPointProperties props = e.GetCurrentPoint(this).Properties;

            if (pageIndex >= 0 && pageIndex < _renderController.CurrentPages.Count)
            {
                RenderedPage pageData = _renderController.CurrentPages[pageIndex];

                if (props.IsRightButtonPressed)
                {
                    _imageController.TryHandleRightClick(pageData, point);
                    ShowContextMenu(pageControl);
                    e.Handled = true;
                    return;
                }

                if (props.IsLeftButtonPressed)
                {
                    RequestInputFocus();

                    // Обрабатываем клик по картинке (выделение или ресайз)
                    if (_imageController.TryHandleLeftClick(pageData, point, e, stack, pageControl))
                    {
                        e.Handled = true;
                        return;
                    }

                    // Иначе обычное выделение текста
                    bool isShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
                    _mouseController.HandlePointerPressed(pageIndex, point, isShift);
                    e.Pointer.Capture(pageControl);
                    e.Handled = true;
                }
            }
        }
    }

    private void OnGlobalPointerMoved(object? sender, PointerEventArgs e)
    {
        if (this.FindControl<StackPanel>("PagesStackPanel") is not { } stack) return;

        // Обновляем курсор при наведении на рамку картинки
        Cursor? hoverCursor = _imageController.GetHoverCursor(e.GetPosition(stack), stack);
        if (hoverCursor != null) Cursor = hoverCursor;

        // Передаем движение в ImageController (для ресайза)
        _imageController.HandlePointerMoved(e, stack);

        // Передаем движение в MouseController (для выделения текста)
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Visual? visual = e.Source as Visual;
            while (visual != null && !(visual is GostEditor.UI.Controls.GostPageControl))
                visual = (Visual?)visual.GetVisualParent();

            if (visual is GostEditor.UI.Controls.GostPageControl pageControl)
            {
                int pageIndex = stack.Children.IndexOf(pageControl);
                _mouseController.HandlePointerMoved(pageIndex, e.GetPosition(pageControl));
            }
        }
    }

    private void OnGlobalPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right) return;

        // Кто-то из контроллеров должен завершить работу
        if (_imageController.HandlePointerReleased(e)) return;

        _mouseController.HandlePointerReleased();
        e.Pointer.Capture(null);
    }

    public void LoadDocument(GostDocument document)
    {
        _renderController.ResetDocumentVisualState();
        _editor.LoadDocument(document);
        _renderController.RefreshView();
        RequestInputFocus();
    }

    internal bool IsInputFocusRequested => _inputFocusRequested;
    internal DocumentPosition CurrentCaretPosition => _editor.CaretPosition;

    private void RequestInputFocus()
    {
        _inputFocusRequested = true;
        TryApplyRequestedInputFocus();
        Dispatcher.UIThread.Post(
            TryApplyRequestedInputFocus,
            DispatcherPriority.Loaded);
    }

    private void TryApplyRequestedInputFocus()
    {
        if (_inputFocusRequested && Focus())
        {
            _inputFocusRequested = false;
        }
    }

    private async void OnTextInputAsync(object? sender, TextInputEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text))
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            await _textInputController.HandleTextInputAsync(e.Text, topLevel?.Clipboard);
            e.Handled = true;
        }
    }

    private async void OnKeyDownAsync(object? sender, KeyEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        await _textInputController.HandleKeyDownAsync(e, topLevel?.Clipboard);
    }

    // === КОНТЕКСТНОЕ МЕНЮ ===
    private void ShowContextMenu(Control target)
    {
        if (_editor == null) return;

        ContextMenu menu = new ContextMenu();
        List<MenuItem> items = [];

        if (_editor.SelectedImageParagraphIndex.HasValue)
        {
            int pIndex = _editor.SelectedImageParagraphIndex.Value;

            MenuItem copyItem = new MenuItem { Header = "Копировать" };
            copyItem.Click += OnCopyClick;

            MenuItem replaceItem = new MenuItem { Header = "Заменить" };
            replaceItem.Click += async (_, _) => { if (TopLevel.GetTopLevel(this) is { } tl) await _imageController.ReplaceImageAsync(tl, pIndex); };

            MenuItem cutItem = new MenuItem { Header = "Вырезать" };
            cutItem.Click += OnCutClick;

            MenuItem editItem = new MenuItem { Header = "Редактировать" };
            editItem.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is not Window mainWindow) return;

                ImageResult<ResolvedImagePlacement> resolved =
                    _imageService.ResolvePlacement(_editor.Document, pIndex);
                if (!resolved.IsSuccess) return;

                ImageEditorWindow editorWindow = new ImageEditorWindow();
                byte[]? newImageBytes = await editorWindow.ShowDialogAsync(
                    mainWindow,
                    resolved.Value!.Content.Data.ToArray());

                if (newImageBytes != null)
                {
                    using MemoryStream ms = new MemoryStream(newImageBytes);
                    using Bitmap bmp = new Bitmap(ms);
                    _editor.ReplaceImage(
                        pIndex,
                        new ReplaceImageRequest(
                            newImageBytes,
                            new ImageSize(bmp.Size.Width, bmp.Size.Height)));
                    _renderController.RefreshView();
                }
            };

            MenuItem deleteItem = new MenuItem { Header = "Удалить" };
            deleteItem.Click += (_, _) =>
            {
                _editor.RemoveImage(pIndex);
                _renderController.RefreshView();
            };

            items.Add(copyItem); items.Add(replaceItem); items.Add(cutItem); items.Add(editItem); items.Add(deleteItem);
        }
        else
        {
            MenuItem h1 = new MenuItem { Header = "Сделать Главой (Уровень 1)", FontWeight = FontWeight.Bold }; h1.Click += (_, _) => ApplyParagraphStyle(ParagraphStyle.Heading1);
            MenuItem h2 = new MenuItem { Header = "Сделать Подразделом (Уровень 2)", FontWeight = FontWeight.SemiBold }; h2.Click += (_, _) => ApplyParagraphStyle(ParagraphStyle.Heading2);
            MenuItem norm = new MenuItem { Header = "Сделать обычным текстом" }; norm.Click += (_, _) => ApplyParagraphStyle(ParagraphStyle.Normal);
            MenuItem copy = new MenuItem { Header = "Копировать текст" }; copy.Click += OnCopyClick;
            MenuItem paste = new MenuItem { Header = "Вставить текст" }; paste.Click += OnPasteClick;

            items.Add(h1); items.Add(h2); items.Add(norm); items.Add(new MenuItem { Header = "-" }); items.Add(copy); items.Add(paste);
        }

        menu.ItemsSource = items;
        menu.Open(target);
    }

    public async Task InsertImageFromFileAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        await _imageController.InsertImageFromFileAsync(topLevel);
        Focus();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e) { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb && _editor.HasSelection) await cb.SetTextAsync(_editor.GetSelectedText()); }
    private async void OnCutClick(object? sender, RoutedEventArgs e) { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb && _editor.HasSelection) { await cb.SetTextAsync(_editor.GetSelectedText()); _editor.DeleteSelection(); _renderController.RefreshView(); } }
    private async void OnPasteClick(object? sender, RoutedEventArgs e) { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { string? text = await cb.GetTextAsync(); if (!string.IsNullOrEmpty(text)) { _editor.PasteText(text); _renderController.RefreshView(); } } }
    private void OnSelectAllClick(object? sender, RoutedEventArgs e) { _editor.SelectAll(); _renderController.RefreshView(); }
    private void OnUndoClick(object? sender, RoutedEventArgs e) => Undo();

    public void ApplyBold() { _editor.ApplyBold(); _renderController.RefreshView(); }
    public void ApplyItalic() { _editor.ApplyItalic(); _renderController.RefreshView(); }
    public void ApplyFontSize(double size) { _editor.ApplyFontSize(size); _renderController.RefreshView(); }
    public void AlignLeft() { _editor.AlignLeft(); _renderController.RefreshView(); }
    public void AlignCenter() { _editor.AlignCenter(); _renderController.RefreshView(); }
    public void AlignRight() { _editor.AlignRight(); _renderController.RefreshView(); }
    public void AlignJustify() { _editor.AlignJustify(); _renderController.RefreshView(); }
    public void ClearFormatting() { _editor.ClearFormatting(); _renderController.RefreshView(); Focus(); }
    public void InsertTextBlock() { _editor.InsertTextBlock(); _renderController.RefreshView(); Focus(); }
    public void InsertTablePlaceholder() { _editor.InsertTablePlaceholder(); _renderController.RefreshView(); Focus(); }
    public bool FindNext(string searchText) { bool found = _editor.FindNext(searchText); _renderController.RefreshView(); if (found) _renderController.ScrollToCaret(); Focus(); return found; }
    public void PasteText(string text) { _editor.PasteText(text); _renderController.RefreshView(); Focus(); }
    public void ApplyParagraphStyle(ParagraphStyle style) { _editor.SetParagraphStyle(style); _renderController.RefreshView(); Focus(); }
    public void Undo() { _editor.History.Undo(); _renderController.RefreshView(); }
    public void Redo() { _editor.History.Redo(); _renderController.RefreshView(); }
    public void AppendParagraphs(List<Paragraph> paragraphs) { _editor.AppendParagraphs(paragraphs); _renderController.RefreshView(); }
    public void InsertHeading(int level, string text) { _editor.InsertHeading(level, text); _renderController.RefreshView(); }
    public void ScrollToParagraph(int index) { _editor.ScrollToParagraph(index); _renderController.RefreshView(); _renderController.ScrollToCaret(); }
    public void SetStartPageNumber(int pageNumber)
    {
        _editor.Document.Modules.ContentStartPage = Math.Max(1, pageNumber);
        _renderController.RefreshView();
    }

    public async Task PasteNormalizedFromClipboardAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        string? text = await clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        TextNormalizerService normalizer = new TextNormalizerService();
        PasteText(normalizer.Normalize(text));
    }
}
