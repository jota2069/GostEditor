using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.Core.Services;
using GostEditor.Core.Models;
using GostEditor.UI.Controls;
using GostEditor.UI.Layout;

// ПСЕВДОНИМЫ ДЛЯ РАЗРЕШЕНИЯ КОНФЛИКТОВ
using DOMDocument = GostEditor.Core.TextEngine.DOM.GostDocument;
using DOMTextRun = GostEditor.Core.TextEngine.DOM.TextRun;

namespace GostEditor.UI.Views;

public partial class DocumentEngineView : UserControl
{
    private static readonly Typeface DefaultTypeface = new Typeface("Times New Roman");

    private DocumentEditor? _editor;
    private PageLayoutManager? _layoutManager;
    private double? _desiredX;

    private bool _isDragging;

    // Используем современный синтаксис коллекций (C# 12)
    private List<RenderedPage> _currentPages = [];

    public DocumentEngineView()
    {
        InitializeComponent();
        Focusable = true;
        TextInput += OnTextInput;
        KeyDown += OnKeyDown;

        InitEngine();
    }

    // ЯВНАЯ ТИПИЗАЦИЯ
    public int StartPageNumber { get; private set; } = 1;

    public void SetStartPageNumber(int startPage)
    {
        StartPageNumber = startPage;
        RefreshView();
    }

    private void InitEngine()
    {
        // Используем наши псевдонимы (DOMDocument и DOMTextRun)
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
                    _editor.SelectAll();
                    RefreshView();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Z)
                {
                    _editor.History.Undo();
                    RefreshView();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Y)
                {
                    _editor.History.Redo();
                    RefreshView();
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

                if (e.Key == Key.V && topLevel?.Clipboard != null)
                {
                    string? pastedText = await topLevel.Clipboard.GetTextAsync();
                    if (!string.IsNullOrEmpty(pastedText))
                    {
                        _editor.PasteText(pastedText);
                        RefreshView();
                    }
                    e.Handled = true;
                    return;
                }
#pragma warning restore CS0618
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

    // ==========================================
    // ОБРАБОТЧИКИ КОНТЕКСТНОГО МЕНЮ (ПКМ)
    // ==========================================

    private async void OnCopyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_editor == null || !_editor.HasSelection) return;

            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
#pragma warning disable CS0618
                string textToCopy = _editor.GetSelectedText();
                await topLevel.Clipboard.SetTextAsync(textToCopy);
#pragma warning restore CS0618
            }
        }
        catch (Exception ex) { Console.WriteLine($"Ошибка буфера обмена: {ex.Message}"); }
        finally { Focus(); }
    }

    private async void OnCutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_editor == null || !_editor.HasSelection) return;

            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
#pragma warning disable CS0618
                string textToCut = _editor.GetSelectedText();
                await topLevel.Clipboard.SetTextAsync(textToCut);
#pragma warning restore CS0618

                _editor.DeleteSelection();
                RefreshView();
            }
        }
        catch (Exception ex) { Console.WriteLine($"Ошибка буфера обмена: {ex.Message}"); }
        finally { Focus(); }
    }

    private async void OnPasteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_editor == null) return;

            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
#pragma warning disable CS0618
                string? pastedText = await topLevel.Clipboard.GetTextAsync();
                if (!string.IsNullOrEmpty(pastedText))
                {
                    _editor.PasteText(pastedText);
                    RefreshView();
                }
#pragma warning restore CS0618
            }
        }
        catch (Exception ex) { Console.WriteLine($"Ошибка буфера обмена: {ex.Message}"); }
        finally { Focus(); }
    }

    private void OnSelectAllClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_editor == null) return;
        _editor.SelectAll();
        RefreshView();
        Focus();
    }

    private void OnUndoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_editor == null) return;
        _editor.History.Undo();
        RefreshView();
        Focus();
    }

    // ==========================================
    // РЕНДЕР И НАВИГАЦИЯ
    // ==========================================

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

        if (sender is GostPageControl)
        {
            e.Pointer.Capture(null);
        }
    }

    private void MoveVertical(bool up)
    {
        if (_layoutManager == null || _editor == null || _currentPages.Count == 0) return;

        int pageIndex = -1;
        Rect caretRect = default;

        for (int i = 0; i < _currentPages.Count; i++)
        {
            if (_currentPages[i].CaretBounds.HasValue)
            {
                pageIndex = i;
                caretRect = _currentPages[i].CaretBounds.Value;
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
            if (_currentPages[i].CaretBounds.HasValue)
            {
                Rect caretRect = _currentPages[i].CaretBounds.Value;
                Control pageControl = PagesStackPanel.Children[i];

                double safeY = caretRect.Y - 50;
                if (safeY < 0)
                {
                    safeY = 0;
                }

                Rect viewRect = new Rect(
                    caretRect.X,
                    safeY,
                    caretRect.Width,
                    caretRect.Height + 100
                );

                pageControl.BringIntoView(viewRect);
                break;
            }
        }
    }

    public void AlignLeft()
    {
        if (_editor == null) return;
        _editor.SetAlignment(GostAlignment.Left);
        RefreshView();
        Focus();
    }

    public void AlignCenter()
    {
        if (_editor == null) return;
        _editor.SetAlignment(GostAlignment.Center);
        RefreshView();
        Focus();
    }

    public void AlignRight()
    {
        if (_editor == null) return;
        _editor.SetAlignment(GostAlignment.Right);
        RefreshView();
        Focus();
    }

    public void AlignJustify()
    {
        if (_editor == null) return;
        _editor.SetAlignment(GostAlignment.Justify);
        RefreshView();
        Focus();
    }

    /// <summary>
    /// Принимает готовые абзацы извне (например, листинги кода) и вставляет их в документ через систему команд.
    /// </summary>
    public void AppendParagraphs(System.Collections.Generic.List<GostEditor.Core.TextEngine.DOM.Paragraph> paragraphs)
    {
        if (_editor == null) return;

        _editor.AppendParagraphs(paragraphs);
        RefreshView();
    }

    /// <summary>
    /// Применяет стиль к выделенным абзацам и обновляет экран.
    /// </summary>
    public void ApplyParagraphStyle(GostEditor.Core.TextEngine.DOM.ParagraphStyle style)
    {
        if (_editor == null) return;

        // Передаем команду ядру
        _editor.SetParagraphStyle(style);

        RefreshView();
        Focus();
    }



}
