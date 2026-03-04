using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Controls;
using GostEditor.UI.Layout;

using DOMDocument = GostEditor.Core.TextEngine.DOM.GostDocument;
using DOMTextRun = GostEditor.Core.TextEngine.DOM.TextRun;

namespace GostEditor.UI.Views;

public class CaretStyleChangedEventArgs : EventArgs
{
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public double FontSize { get; set; }
    public GostAlignment Alignment { get; set; }
}

public partial class DocumentEngineView : UserControl
{
    private static readonly Typeface DefaultTypeface = new Typeface("Times New Roman");

    private DocumentEditor? _editor;
    private PageLayoutManager? _layoutManager;
    private double? _desiredX;

    private bool _isDragging;
    private List<RenderedPage> _currentPages = [];

    public event EventHandler<CaretStyleChangedEventArgs>? CaretStyleChanged;

    public DocumentEngineView()
    {
        InitializeComponent();
        Focusable = true;
        TextInput += OnTextInput;
        KeyDown += OnKeyDown;

        InitEngine();
    }

    private int StartPageNumber { get; set; } = 1;

    public void SetStartPageNumber(int startPage)
    {
        StartPageNumber = startPage;
        RefreshView();
    }

    private void InitEngine()
    {
        DOMDocument document = new DOMDocument();
        Paragraph p1 = new Paragraph();

        DOMTextRun welcomeRun = new DOMTextRun("Добро пожаловать в новый движок! Жми Ctrl+I для импорта кода.");

        p1.Runs.Add(welcomeRun);
        document.Paragraphs.Add(p1);

        _editor = new DocumentEditor(document);
        _layoutManager = new PageLayoutManager();

        _editor.CaretPosition = new DocumentPosition(0, welcomeRun.Text.Length);

        RefreshView();
    }

    public void ApplyBold()
    {
        if (_editor == null) return;
        _editor.ToggleBold();
        RefreshView();
        Focus();
    }

    public void ApplyFontSize(double fontSize)
    {
        if (_editor == null) return;
        _editor.SetFontSize(fontSize);
        RefreshView();
        Focus();
    }

    public void ApplyItalic()
    {
        if (_editor == null) return;
        _editor.ToggleItalic();
        RefreshView();
        Focus();
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (_editor == null || string.IsNullOrEmpty(e.Text)) return;
        _editor.InsertText(e.Text);
        RefreshView();
        e.Handled = true;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_editor == null) return;

        try
        {
            bool isShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            bool isCtrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

            DocumentPosition? oldAnchor = _editor.SelectionAnchor;

            if (isCtrl)
            {
                TopLevel? topLevel = TopLevel.GetTopLevel(this);

                if (e.Key == Key.A)
                {
                    SelectAll();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Z)
                {
                    Undo();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Y)
                {
                    Redo();
                    e.Handled = true;
                    return;
                }

#pragma warning disable CS0618
                if (e.Key == Key.C && _editor.HasSelection && topLevel?.Clipboard != null)
                {
                    string textToCopy = _editor.GetSelectedText();
                    await topLevel.Clipboard.SetTextAsync(textToCopy);
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.X && _editor.HasSelection && topLevel?.Clipboard != null)
                {
                    string textToCut = _editor.GetSelectedText();
                    await topLevel.Clipboard.SetTextAsync(textToCut);
                    _editor.DeleteSelection();
                    RefreshView();
                    e.Handled = true;
                    return;
                }
#pragma warning restore CS0618

                if (e.Key == Key.V && topLevel?.Clipboard != null)
                {
                    await PasteFromClipboardAsync();
                    e.Handled = true;
                    return;
                }
            }

            bool isHandled = true;
            switch (e.Key)
            {
                case Key.Back: _editor.Backspace(); _desiredX = null; break;
                case Key.Enter: _editor.InsertNewLine(); _desiredX = null; break;
                case Key.Left: _editor.MoveLeft(); _desiredX = null; break;
                case Key.Right: _editor.MoveRight(); _desiredX = null; break;
                case Key.Up: MoveVertical(true); break;
                case Key.Down: MoveVertical(false); break;
                default: isHandled = false; break;
            }

            if (isHandled)
            {
                if (isShift)
                {
                    _editor.SelectionAnchor = oldAnchor;
                }
                else
                {
                    if (e.Key != Key.Up && e.Key != Key.Down) _desiredX = null;
                    _editor.SelectionAnchor = _editor.CaretPosition;
                }

                RefreshView();
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка в OnKeyDown: {ex.Message}");
        }
    }

    private void RefreshView()
    {
        if (_editor == null || _layoutManager == null) return;

        _currentPages = _layoutManager.BuildLayout(_editor.Document, _editor, DefaultTypeface);

        while (PagesStackPanel.Children.Count < _currentPages.Count)
        {
            GostPageControl pageControl = new GostPageControl();
            pageControl.PointerPressed += OnPagePointerPressed;
            pageControl.PointerMoved += OnPagePointerMoved;
            pageControl.PointerReleased += OnPagePointerReleased;
            PagesStackPanel.Children.Add(pageControl);
        }

        while (PagesStackPanel.Children.Count > _currentPages.Count)
        {
            PagesStackPanel.Children.RemoveAt(PagesStackPanel.Children.Count - 1);
        }

        for (int i = 0; i < _currentPages.Count; i++)
        {
            if (PagesStackPanel.Children[i] is GostPageControl pageControl)
            {
                pageControl.SetPageData(_currentPages[i], _editor.Document.PageWidth, _editor.Document.PageHeight, StartPageNumber);
            }
        }

        Dispatcher.UIThread.Post(ScrollToCaret, DispatcherPriority.Normal);

        NotifyCaretStyle();
    }

    private void NotifyCaretStyle()
    {
        if (_editor == null) return;
        if (_editor.CaretPosition.ParagraphIndex >= _editor.Document.Paragraphs.Count) return;

        Paragraph p = _editor.Document.Paragraphs[_editor.CaretPosition.ParagraphIndex];
        GostAlignment alignment = p.Alignment;

        bool isBold = false;
        bool isItalic = false;
        double fontSize = 14;

        int currentOffset = 0;
        DOMTextRun? targetRun = null;

        if (p.Runs.Count > 0)
        {
            targetRun = p.Runs[0];
            foreach (DOMTextRun run in p.Runs)
            {
                if (_editor.CaretPosition.Offset > currentOffset && _editor.CaretPosition.Offset <= currentOffset + run.Text.Length)
                {
                    targetRun = run;
                    break;
                }
                if (_editor.CaretPosition.Offset == currentOffset && run.Text.Length == 0)
                {
                    targetRun = run;
                    break;
                }
                currentOffset += run.Text.Length;
            }
        }

        if (targetRun != null)
        {
            isBold = targetRun.IsBold;
            isItalic = targetRun.IsItalic;
            fontSize = targetRun.FontSize;
        }

        CaretStyleChanged?.Invoke(this, new CaretStyleChangedEventArgs
        {
            IsBold = isBold,
            IsItalic = isItalic,
            FontSize = fontSize,
            Alignment = alignment
        });
    }

    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_layoutManager == null || _editor == null) return;

        if (sender is GostPageControl pageControl)
        {
            if (!e.GetCurrentPoint(pageControl).Properties.IsLeftButtonPressed) return;

            Focus();
            _isDragging = true;
            _desiredX = null;

            e.Pointer.Capture(pageControl);

            int pageIndex = PagesStackPanel.Children.IndexOf(pageControl);
            if (pageIndex >= 0 && pageIndex < _currentPages.Count)
            {
                RenderedPage pageData = _currentPages[pageIndex];
                Point clickPoint = e.GetPosition(pageControl);
                DocumentPosition? pos = _layoutManager.GetPositionFromPoint(pageData, clickPoint);

                if (pos.HasValue)
                {
                    _editor.SelectionAnchor = pos.Value;
                    _editor.CaretPosition = pos.Value;
                    RefreshView();
                }
            }
        }
    }

    private void OnPagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_layoutManager == null || _editor == null || !_isDragging) return;

        if (sender is GostPageControl capturedPage)
        {
            if (!e.GetCurrentPoint(capturedPage).Properties.IsLeftButtonPressed)
            {
                _isDragging = false;
                e.Pointer.Capture(null);
                return;
            }

            Point globalPoint = e.GetPosition(PagesStackPanel);
            int targetPageIndex = -1;
            Point localPoint = default;

            for (int i = 0; i < PagesStackPanel.Children.Count; i++)
            {
                Control child = PagesStackPanel.Children[i];

                if (globalPoint.Y >= child.Bounds.Top && globalPoint.Y <= child.Bounds.Bottom)
                {
                    targetPageIndex = i;
                    localPoint = new Point(globalPoint.X, globalPoint.Y - child.Bounds.Top);
                    break;
                }
            }

            if (globalPoint.Y < 0 && PagesStackPanel.Children.Count > 0)
            {
                targetPageIndex = 0;
                localPoint = new Point(globalPoint.X, 0);
            }
            else if (targetPageIndex == -1 && PagesStackPanel.Children.Count > 0 && globalPoint.Y > PagesStackPanel.Bounds.Bottom)
            {
                targetPageIndex = PagesStackPanel.Children.Count - 1;
                Control lastChild = PagesStackPanel.Children[^1];
                localPoint = new Point(globalPoint.X, lastChild.Bounds.Height);
            }

            if (targetPageIndex >= 0 && targetPageIndex < _currentPages.Count)
            {
                RenderedPage targetPageData = _currentPages[targetPageIndex];
                DocumentPosition? pos = _layoutManager.GetPositionFromPoint(targetPageData, localPoint);

                if (pos.HasValue && pos.Value.CompareTo(_editor.CaretPosition) != 0)
                {
                    _editor.CaretPosition = pos.Value;
                    RefreshView();
                }
            }
        }
    }

    private void OnPagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        if (sender is GostPageControl) e.Pointer.Capture(null);
    }

    private void MoveVertical(bool up)
    {
        if (_layoutManager == null || _editor == null || _currentPages.Count == 0) return;

        int pageIndex = -1;
        Rect caretRect = default;

        for (int i = 0; i < _currentPages.Count; i++)
        {
            if (_currentPages[i].CaretBounds is { } rect)
            {
                pageIndex = i;
                caretRect = rect;
                break;
            }
        }

        if (pageIndex == -1) return;

        double targetX = _desiredX ?? caretRect.X;
        _desiredX = targetX;

        List<TextLinePlacement> allLines = [];

        foreach (RenderedPage page in _currentPages)
        {
            allLines.AddRange(page.Lines);
        }

        int currentLineIndex = -1;
        for (int i = 0; i < allLines.Count; i++)
        {
            TextLinePlacement line = allLines[i];

            if (line.ParagraphIndex == _editor.CaretPosition.ParagraphIndex &&
                caretRect.Y >= line.Location.Y - 1 &&
                caretRect.Y <= line.Location.Y + line.Line.Height + 1)
            {
                currentLineIndex = i;
                break;
            }
        }

        if (currentLineIndex == -1) return;

        int targetLineIndex = up ? currentLineIndex - 1 : currentLineIndex + 1;

        if (targetLineIndex < 0 || targetLineIndex >= allLines.Count) return;

        TextLinePlacement targetLine = allLines[targetLineIndex];

        double layoutX = targetX - targetLine.Location.X;

        if (layoutX < 0) layoutX = 0;
        if (layoutX > targetLine.Line.Width) layoutX = targetLine.Line.Width;

        double layoutY = targetLine.InternalY + (targetLine.Line.Height / 2);

        TextHitTestResult hit = targetLine.ParentLayout.HitTestPoint(new Point(layoutX, layoutY));

        _editor.CaretPosition = new DocumentPosition(targetLine.ParagraphIndex, hit.TextPosition);
    }

    private void ScrollToCaret()
    {
        if (_currentPages.Count == 0) return;

        for (int i = 0; i < _currentPages.Count; i++)
        {
            if (_currentPages[i].CaretBounds is { } caretRect)
            {
                Control pageControl = PagesStackPanel.Children[i];

                double safeY = caretRect.Y - 50;
                if (safeY < 0) safeY = 0;

                Rect viewRect = new Rect(caretRect.X, safeY, caretRect.Width, caretRect.Height + 100);
                pageControl.BringIntoView(viewRect);
                break;
            }
        }
    }

    public void AlignLeft() { if (_editor == null) return; _editor.SetAlignment(GostAlignment.Left); RefreshView(); Focus(); }
    public void AlignCenter() { if (_editor == null) return; _editor.SetAlignment(GostAlignment.Center); RefreshView(); Focus(); }
    public void AlignRight() { if (_editor == null) return; _editor.SetAlignment(GostAlignment.Right); RefreshView(); Focus(); }
    public void AlignJustify() { if (_editor == null) return; _editor.SetAlignment(GostAlignment.Justify); RefreshView(); Focus(); }

    public void AppendParagraphs(List<Paragraph> paragraphs)
    {
        if (_editor == null) return;
        _editor.AppendParagraphs(paragraphs);
        RefreshView();
    }

    public void ApplyParagraphStyle(ParagraphStyle style)
    {
        if (_editor == null) return;
        _editor.SetParagraphStyle(style);
        RefreshView();
        Focus();
    }

    public bool HasSelection => _editor != null && _editor.HasSelection;

    public void SelectAll()
    {
        if (_editor == null) return;
        _editor.SelectAll();
        RefreshView();
        Focus();
    }

    public void Undo()
    {
        if (_editor == null) return;
        _editor.History.Undo();
        RefreshView();
        Focus();
    }

    public void Redo()
    {
        if (_editor == null) return;
        _editor.History.Redo();
        RefreshView();
        Focus();
    }

    public string GetSelectedText()
    {
        return _editor != null ? _editor.GetSelectedText() : string.Empty;
    }

    public void DeleteSelection()
    {
        if (_editor == null) return;
        _editor.DeleteSelection();
        RefreshView();
        Focus();
    }

    public void PasteText(string text)
    {
        if (_editor == null) return;
        _editor.PasteText(text);
        RefreshView();
        Focus();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_editor != null && _editor.HasSelection)
            {
                TopLevel? topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
#pragma warning disable CS0618
                    await topLevel.Clipboard.SetTextAsync(_editor.GetSelectedText());
#pragma warning restore CS0618
                }
            }
            Focus();
        }
        catch (Exception ex) { Console.WriteLine(ex); }
    }

    private async void OnCutClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_editor != null && _editor.HasSelection)
            {
                TopLevel? topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
#pragma warning disable CS0618
                    await topLevel.Clipboard.SetTextAsync(_editor.GetSelectedText());
#pragma warning restore CS0618
                    _editor.DeleteSelection();
                    RefreshView();
                }
            }
            Focus();
        }
        catch (Exception ex) { Console.WriteLine(ex); }
    }

    private async void OnPasteClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await PasteFromClipboardAsync();
        }
        catch (Exception ex) { Console.WriteLine(ex); }
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        SelectAll();
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e)
    {
        Undo();
    }

    public async Task InsertImageFromFileAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите изображение",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll]
            });

            if (files.Count > 0)
            {
                await using var stream = await files[0].OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                byte[] bytes = ms.ToArray();

                using var bmp = new Bitmap(new MemoryStream(bytes));
                _editor?.InsertImage(bytes, bmp.Size.Width, bmp.Size.Height);
                RefreshView();
                Focus();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка выбора файла: {ex.Message}");
        }
    }

    private async Task PasteFromClipboardAsync()
    {
        if (_editor == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;

        try
        {
#pragma warning disable CS0618
            var formats = await topLevel.Clipboard.GetFormatsAsync();

            // 1. Ищем картинку напрямую из буфера (скриншот ножницами)
            foreach (var format in formats)
            {
                if (format.Contains("png", StringComparison.OrdinalIgnoreCase) ||
                    format.Contains("jpg", StringComparison.OrdinalIgnoreCase))
                {
                    var imgData = await topLevel.Clipboard.GetDataAsync(format);
                    if (imgData is byte[] bytes)
                    {
                        using var bmp = new Bitmap(new MemoryStream(bytes));
                        _editor.InsertImage(bytes, bmp.Size.Width, bmp.Size.Height);
                        RefreshView();
                        Focus();
                        return;
                    }
                }
            }

            // 2. Ищем скопированный ФАЙЛ картинки (из проводника)
            var data = await topLevel.Clipboard.GetDataAsync(DataFormats.Files);
            if (data is IEnumerable<IStorageItem> items)
            {
                foreach (var item in items)
                {
                    if (item is IStorageFile storageFile &&
                       (storageFile.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        storageFile.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        storageFile.Name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
                    {
                        await using var stream = await storageFile.OpenReadAsync();
                        using var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        byte[] bytes = ms.ToArray();

                        using var bmp = new Bitmap(new MemoryStream(bytes));
                        _editor.InsertImage(bytes, bmp.Size.Width, bmp.Size.Height);
                        RefreshView();
                        Focus();
                        return;
                    }
                }
            }

            // 3. Вставляем как обычный текст
            string? pastedText = await topLevel.Clipboard.GetTextAsync();
            if (!string.IsNullOrEmpty(pastedText))
            {
                _editor.PasteText(pastedText);
                RefreshView();
                Focus();
            }
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка вставки: {ex.Message}");
        }
        Focus();
    }
}
