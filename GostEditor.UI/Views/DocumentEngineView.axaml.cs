using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Controls;
using GostEditor.UI.Layout;
using System.Collections.Generic;

namespace GostEditor.UI.Views;

public partial class DocumentEngineView : UserControl
{
    private DocumentEditor _editor;
    private PageLayoutManager _layoutManager;

    // Флаги для мыши и кэш текущих страниц
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
        p1.Runs.Add(new TextRun("Добро пожаловать в новый движок! Попробуй выделить этот текст мышкой или напечатать что-то свое. "));
        document.Paragraphs.Add(p1);

        _editor = new DocumentEditor(document);
        _layoutManager = new PageLayoutManager();
        _editor.CaretPosition = new DocumentPosition(0, p1.GetPlainText().Length);

        RefreshView();
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        _editor.InsertText(e.Text);
        RefreshView();
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool isHandled = true;
        switch (e.Key)
        {
            case Key.Back: _editor.Backspace(); RefreshView(); break;
            case Key.Enter: _editor.InsertNewLine(); RefreshView(); break;
            case Key.Left: _editor.MoveLeft(); RefreshView(); break;
            case Key.Right: _editor.MoveRight(); RefreshView(); break;
            default: isHandled = false; break;
        }
        e.Handled = isHandled;
    }

    private void RefreshView()
    {
        _currentPages = _layoutManager.BuildLayout(_editor.Document, _editor);

        // 1. Умное обновление: добавляем листы только если документ вырос
        while (PagesStackPanel.Children.Count < _currentPages.Count)
        {
            GostPageControl pageControl = new GostPageControl();

            // Подписываемся на события мыши ОДИН РАЗ при рождении листа
            pageControl.PointerPressed += OnPagePointerPressed;
            pageControl.PointerMoved += OnPagePointerMoved;
            pageControl.PointerReleased += OnPagePointerReleased;

            PagesStackPanel.Children.Add(pageControl);
        }

        // 2. Удаляем лишние листы, если текст стерли
        while (PagesStackPanel.Children.Count > _currentPages.Count)
        {
            PagesStackPanel.Children.RemoveAt(PagesStackPanel.Children.Count - 1);
        }

        // 3. Раздаем новые данные существующим листам (БОЛЬШЕ НИКАКОГО ПЕРЕСОЗДАНИЯ!)
        for (int i = 0; i < _currentPages.Count; i++)
        {
            GostPageControl pageControl = (GostPageControl)PagesStackPanel.Children[i];
            pageControl.SetPageData(_currentPages[i], _editor.Document.PageWidth, _editor.Document.PageHeight);
        }
    }

    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is GostPageControl pageControl)
        {
            // Строго реагируем только на левую кнопку мыши
            if (!e.GetCurrentPoint(pageControl).Properties.IsLeftButtonPressed) return;

            Focus(); // Забираем фокус для клавиатуры

            int pageIndex = PagesStackPanel.Children.IndexOf(pageControl);
            if (pageIndex >= 0 && pageIndex < _currentPages.Count)
            {
                RenderedPage pageData = _currentPages[pageIndex];
                Point clickPoint = e.GetPosition(pageControl);
                DocumentPosition? pos = _layoutManager.GetPositionFromPoint(pageData, clickPoint);

                if (pos.HasValue)
                {
                    _editor.SelectionAnchor = pos.Value; // Ставим якорь
                    _editor.CaretPosition = pos.Value;   // Каретка прыгает к якорю (выделение мгновенно сбрасывается)
                    _isDragging = true;                  // Начинаем следить за тягой
                    RefreshView();
                }
            }
        }
    }

    private void OnPagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging && sender is GostPageControl pageControl)
        {
            // Если мышь движется, но левая кнопка уже отпущена (страховка)
            if (!e.GetCurrentPoint(pageControl).Properties.IsLeftButtonPressed)
            {
                _isDragging = false;
                return;
            }

            int pageIndex = PagesStackPanel.Children.IndexOf(pageControl);
            if (pageIndex >= 0 && pageIndex < _currentPages.Count)
            {
                RenderedPage pageData = _currentPages[pageIndex];
                Point movePoint = e.GetPosition(pageControl);
                DocumentPosition? pos = _layoutManager.GetPositionFromPoint(pageData, movePoint);

                // Избегаем лишних перерисовок, если каретка не сдвинулась с прошлой буквы
                if (pos.HasValue && pos.Value.CompareTo(_editor.CaretPosition) != 0)
                {
                    _editor.CaretPosition = pos.Value; // Каретка тянется за мышью, образуя выделение
                    RefreshView();
                }
            }
        }
    }

    private void OnPagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false; // Отпустили левую кнопку мыши — зафиксировали выделение
    }
}
