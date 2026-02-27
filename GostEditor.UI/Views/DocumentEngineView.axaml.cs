using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Controls;
using GostEditor.UI.Layout;

namespace GostEditor.UI.Views;

public partial class DocumentEngineView : UserControl
{
    public static readonly Typeface DefaultTypeface = new Typeface("Times New Roman");

    private DocumentEditor? _editor;
    private PageLayoutManager? _layoutManager;
    private double? _desiredX = null;

    private bool _isDragging = false;
    private List<RenderedPage> _currentPages = new List<RenderedPage>();

    public DocumentEngineView()
    {
        InitializeComponent();
        Focusable = true;
        TextInput += OnTextInput;
        KeyDown += OnKeyDown;

        InitEngine();
    }

    private void InitEngine()
    {
        GostDocument document = new GostDocument();
        Paragraph p1 = new Paragraph();

        GostEditor.Core.TextEngine.DOM.TextRun welcomeRun = new GostEditor.Core.TextEngine.DOM.TextRun("Добро пожаловать в новый движок! Выдели это слово и нажми 'Ж'. ");

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

        bool isShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        bool isCtrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

        DocumentPosition? oldAnchor = _editor.SelectionAnchor;

        if (isCtrl)
        {
            Avalonia.Controls.TopLevel? topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);

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

    public void RefreshView()
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
            GostPageControl pageControl = (GostPageControl)PagesStackPanel.Children[i];
            pageControl.SetPageData(_currentPages[i], _editor.Document.PageWidth, _editor.Document.PageHeight);
        }

        Dispatcher.UIThread.Post(ScrollToCaret, (DispatcherPriority)1);
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
                Avalonia.Controls.Control child = PagesStackPanel.Children[i];

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
                Avalonia.Controls.Control lastChild = PagesStackPanel.Children[^1];
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

        if (sender is GostPageControl pageControl)
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

        if (_desiredX == null) _desiredX = caretRect.X;

        List<TextLinePlacement> allLines = new List<TextLinePlacement>();

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

        double layoutX = _desiredX.Value - targetLine.Location.X;

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
                Avalonia.Rect caretRect = _currentPages[i].CaretBounds.Value;
                Avalonia.Controls.Control pageControl = PagesStackPanel.Children[i];

                double safeY = caretRect.Y - 50;
                if (safeY < 0)
                {
                    safeY = 0;
                }

                Avalonia.Rect viewRect = new Avalonia.Rect(
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
        _editor.SetAlignment(GostEditor.Core.TextEngine.DOM.GostAlignment.Left);
        RefreshView();
        Focus();
    }

    public void AlignCenter()
    {
        if (_editor == null) return;
        _editor.SetAlignment(GostEditor.Core.TextEngine.DOM.GostAlignment.Center);
        RefreshView();
        Focus();
    }

    public void AlignRight()
    {
        if (_editor == null) return;
        _editor.SetAlignment(GostEditor.Core.TextEngine.DOM.GostAlignment.Right);
        RefreshView();
        Focus();
    }

    public void AlignJustify()
    {
        if (_editor == null) return;
        _editor.SetAlignment(GostEditor.Core.TextEngine.DOM.GostAlignment.Justify);
        RefreshView();
        Focus();
    }
}
