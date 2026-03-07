#pragma warning disable CS0618

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

using GostDocument = GostEditor.Core.Models.GostDocument;
using DOMTextRun = GostEditor.Core.TextEngine.DOM.TextRun;

namespace GostEditor.UI.Views;

public enum ResizeDirection
{
    None, TopLeft, TopCenter, TopRight, RightCenter, BottomRight, BottomCenter, BottomLeft, LeftCenter
}

public class CaretStyleChangedEventArgs : EventArgs
{
    public bool IsBold { get; init; }
    public bool IsItalic { get; init; }
    public double FontSize { get; init; }
    public GostAlignment Alignment { get; init; }
}

public partial class DocumentEngineView : UserControl
{
    private static readonly Typeface DefaultTypeface = new Typeface("Times New Roman");

    private DocumentEditor? _editor;
    private PageLayoutManager? _layoutManager;
    private double? _desiredX;

    private bool _isDragging;
    private List<RenderedPage> _currentPages = [];

    private ResizeDirection _currentResizeDirection = ResizeDirection.None;
    private Point _resizeStartPoint;
    private double _initialImageWidth;
    private double _initialImageHeight;
    private double _initialImageX;
    private double _initialImageY;
    private Paragraph? _resizingParagraph;
    private GostPageControl? _resizingPageControl;
    private double _finalResizeWidth;
    private double _finalResizeHeight;

    public event EventHandler<CaretStyleChangedEventArgs>? CaretStyleChanged;
    public event Action? ContentChanged;

    public DocumentEngineView()
    {
        InitializeComponent();
        Focusable = true;
        TextInput += OnTextInput;
        KeyDown += OnKeyDown;

        AddHandler(ContextRequestedEvent, (s, e) => e.Handled = true, RoutingStrategies.Tunnel);

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
        GostDocument document = new GostDocument();
        _editor = new DocumentEditor(document);
        _layoutManager = new PageLayoutManager();

        Paragraph p1 = new Paragraph();
        DOMTextRun welcomeRun = new DOMTextRun("Добро пожаловать в новый движок! Жми Ctrl+I для импорта кода.");
        p1.Runs.Add(welcomeRun);

        _editor.Document.Paragraphs.Clear();
        _editor.Document.Paragraphs.Add(p1);

        _editor.CaretPosition = new DocumentPosition(0, welcomeRun.Text.Length);

        RefreshView();
    }

    private ResizeDirection GetResizeHandleHit(RenderedPage page, Point localPoint, int selectedImageIndex)
    {
        foreach (ImagePlacement img in page.Images)
        {
            if (img.ParagraphIndex == selectedImageIndex)
            {
                double markerSize = 8.0;
                double halfSize = markerSize / 2.0;
                double padding = 15.0;

                Point[] centers =
                [
                    new Point(img.Bounds.Left, img.Bounds.Top),
                    new Point(img.Bounds.Center.X, img.Bounds.Top),
                    new Point(img.Bounds.Right, img.Bounds.Top),
                    new Point(img.Bounds.Right, img.Bounds.Center.Y),
                    new Point(img.Bounds.Right, img.Bounds.Bottom),
                    new Point(img.Bounds.Center.X, img.Bounds.Bottom),
                    new Point(img.Bounds.Left, img.Bounds.Bottom),
                    new Point(img.Bounds.Left, img.Bounds.Center.Y)
                ];

                ResizeDirection[] dirs =
                [
                    ResizeDirection.TopLeft, ResizeDirection.TopCenter, ResizeDirection.TopRight,
                    ResizeDirection.RightCenter, ResizeDirection.BottomRight, ResizeDirection.BottomCenter,
                    ResizeDirection.BottomLeft, ResizeDirection.LeftCenter
                ];

                for (int i = 0; i < centers.Length; i++)
                {
                    Rect hitArea = new Rect(centers[i].X - halfSize - padding, centers[i].Y - halfSize - padding,
                                            markerSize + padding * 2, markerSize + padding * 2);
                    if (hitArea.Contains(localPoint))
                    {
                        return dirs[i];
                    }
                }
            }
        }
        return ResizeDirection.None;
    }

    public void ApplyBold() { if (_editor == null) return; _editor.ToggleBold(); RefreshView(); Focus(); }
    public void ApplyFontSize(double fontSize) { if (_editor == null) return; _editor.SetFontSize(fontSize); RefreshView(); Focus(); }
    public void ApplyItalic() { if (_editor == null) return; _editor.ToggleItalic(); RefreshView(); Focus(); }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (_editor == null || string.IsNullOrEmpty(e.Text)) return;

        _editor.SelectedImageParagraphIndex = null;
        _editor.InsertText(e.Text);
        RefreshView();
        ContentChanged?.Invoke();
        e.Handled = true;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_editor == null) return;

        try
        {
            if ((e.Key == Key.Back || e.Key == Key.Delete) && _editor.SelectedImageParagraphIndex.HasValue)
            {
                _editor.ExecuteWithSnapshot(() =>
                {
                    int pIndex = _editor.SelectedImageParagraphIndex.Value;
                    _editor.Document.Paragraphs.RemoveAt(pIndex);
                    _editor.ClearSelection();

                    if (pIndex < _editor.Document.Paragraphs.Count)
                        _editor.CaretPosition = new DocumentPosition(pIndex, 0);
                    else if (pIndex - 1 >= 0)
                        _editor.CaretPosition = new DocumentPosition(pIndex - 1, _editor.Document.Paragraphs[pIndex - 1].GetPlainText().Length);
                    else
                        _editor.CaretPosition = new DocumentPosition(0, 0);
                });

                RefreshView();
                if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Enter || e.Key == Key.V || e.Key == Key.X)
                {
                    ContentChanged?.Invoke();
                }
                e.Handled = true;
                return;
            }

            bool isShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            bool isCtrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

            DocumentPosition? oldAnchor = _editor.SelectionAnchor;

            if (isCtrl)
            {
                TopLevel? topLevel = TopLevel.GetTopLevel(this);

                if (e.Key == Key.A) { SelectAll(); e.Handled = true; return; }
                if (e.Key == Key.Z) { Undo(); e.Handled = true; return; }
                if (e.Key == Key.Y) { Redo(); e.Handled = true; return; }

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
                if (e.Key != Key.Back && e.Key != Key.Delete)
                {
                    _editor.SelectedImageParagraphIndex = null;
                }

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
                pageControl.SetPageData(_currentPages[i], _editor.Document.PageWidth, _editor.Document.PageHeight, StartPageNumber, _editor.SelectedImageParagraphIndex);
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
            PointerPointProperties pointerProps = e.GetCurrentPoint(pageControl).Properties;

            if (pointerProps.IsRightButtonPressed)
            {
                int pageIndex = PagesStackPanel.Children.IndexOf(pageControl);
                if (pageIndex >= 0 && pageIndex < _currentPages.Count)
                {
                    RenderedPage pageData = _currentPages[pageIndex];
                    Point clickPoint = e.GetPosition(pageControl);
                    DocumentHitResult? hit = _layoutManager.GetPositionFromPoint(pageData, clickPoint);

                    if (hit is { IsImageHit: true, ImageParagraphIndex: not null })
                    {
                        _editor.SelectionAnchor = null;
                        _editor.SelectedImageParagraphIndex = hit.ImageParagraphIndex;
                        RefreshView();
                    }
                }

                ShowContextMenu(pageControl);
                e.Handled = true;
                return;
            }

            if (!pointerProps.IsLeftButtonPressed) return;

            Focus();
            _isDragging = true;
            _desiredX = null;

            int pageIndexForLeftClick = PagesStackPanel.Children.IndexOf(pageControl);
            if (pageIndexForLeftClick >= 0 && pageIndexForLeftClick < _currentPages.Count)
            {
                RenderedPage pageData = _currentPages[pageIndexForLeftClick];
                Point clickPoint = e.GetPosition(pageControl);

                if (_editor.SelectedImageParagraphIndex.HasValue)
                {
                    ResizeDirection hitDir = GetResizeHandleHit(pageData, clickPoint, _editor.SelectedImageParagraphIndex.Value);
                    if (hitDir != ResizeDirection.None)
                    {
                        _currentResizeDirection = hitDir;
                        _resizeStartPoint = e.GetPosition(PagesStackPanel);
                        _resizingParagraph = _editor.Document.Paragraphs[_editor.SelectedImageParagraphIndex.Value];
                        _resizingPageControl = pageControl;

                        ImagePlacement? imgPl = pageData.Images.Find(img => img.ParagraphIndex == _editor.SelectedImageParagraphIndex.Value);
                        if (imgPl != null)
                        {
                            _initialImageWidth = imgPl.Bounds.Width;
                            _initialImageHeight = imgPl.Bounds.Height;
                            _initialImageX = imgPl.Bounds.X;
                            _initialImageY = imgPl.Bounds.Y;
                        }
                        else
                        {
                            _initialImageWidth = _resizingParagraph.ImageWidth;
                            _initialImageHeight = _resizingParagraph.ImageHeight;
                            _initialImageX = 0;
                            _initialImageY = 0;
                        }

                        _finalResizeWidth = _initialImageWidth;
                        _finalResizeHeight = _initialImageHeight;

                        e.Pointer.Capture(pageControl);
                        return;
                    }
                }

                DocumentHitResult? hit = _layoutManager.GetPositionFromPoint(pageData, clickPoint);

                if (hit != null)
                {
                    if (hit is { IsImageHit: true, ImageParagraphIndex: not null })
                    {
                        _editor.SelectionAnchor = null;
                        _editor.SelectedImageParagraphIndex = hit.ImageParagraphIndex;
                    }
                    else if (hit.TextPosition.HasValue)
                    {
                        _editor.SelectedImageParagraphIndex = null;
                        _editor.SelectionAnchor = hit.TextPosition.Value;
                        _editor.CaretPosition = hit.TextPosition.Value;
                    }
                    RefreshView();
                }
            }

            e.Pointer.Capture(pageControl);
        }
    }

    private void OnPagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_layoutManager == null || _editor == null) return;

        if (!_isDragging && _currentResizeDirection == ResizeDirection.None && _editor.SelectedImageParagraphIndex.HasValue)
        {
            Point globalP = e.GetPosition(PagesStackPanel);
            bool cursorSet = false;

            for (int i = 0; i < PagesStackPanel.Children.Count; i++)
            {
                Control child = PagesStackPanel.Children[i];
                if (globalP.Y >= child.Bounds.Top && globalP.Y <= child.Bounds.Bottom && i < _currentPages.Count)
                {
                    Point localP = new Point(globalP.X, globalP.Y - child.Bounds.Top);
                    if (GetResizeHandleHit(_currentPages[i], localP, _editor.SelectedImageParagraphIndex.Value) != ResizeDirection.None)
                    {
                        Cursor = new Cursor(StandardCursorType.Hand);
                        cursorSet = true;
                        break;
                    }
                }
            }
            if (!cursorSet) Cursor = new Cursor(StandardCursorType.Ibeam);
        }

        if (_currentResizeDirection != ResizeDirection.None && _resizingPageControl != null)
        {
            Point currentGlobal = e.GetPosition(PagesStackPanel);
            double handleDeltaX = currentGlobal.X - _resizeStartPoint.X;
            double handleDeltaY = currentGlobal.Y - _resizeStartPoint.Y;

            double ratio = _initialImageWidth / _initialImageHeight;
            double newW = _initialImageWidth;
            double newH = _initialImageHeight;
            double newX = _initialImageX;
            double newY = _initialImageY;

            double rightEdge = _initialImageX + _initialImageWidth;
            double bottomEdge = _initialImageY + _initialImageHeight;

            switch (_currentResizeDirection)
            {
                case ResizeDirection.RightCenter:
                    newW = _initialImageWidth + handleDeltaX;
                    break;
                case ResizeDirection.LeftCenter:
                    newW = _initialImageWidth - handleDeltaX;
                    break;
                case ResizeDirection.BottomCenter:
                    newH = _initialImageHeight + handleDeltaY;
                    break;
                case ResizeDirection.TopCenter:
                    newH = _initialImageHeight - handleDeltaY;
                    break;
                case ResizeDirection.BottomRight:
                    newW = _initialImageWidth + handleDeltaX;
                    newH = newW / ratio;
                    break;
                case ResizeDirection.BottomLeft:
                    newW = _initialImageWidth - handleDeltaX;
                    newH = newW / ratio;
                    break;
                case ResizeDirection.TopRight:
                    newW = _initialImageWidth + handleDeltaX;
                    newH = newW / ratio;
                    break;
                case ResizeDirection.TopLeft:
                    newW = _initialImageWidth - handleDeltaX;
                    newH = newW / ratio;
                    break;
            }

            double minSize = 20.0;
            double contentWidth = _editor.Document.PageWidth - _editor.Document.MarginLeft - _editor.Document.MarginRight;

            if (newW < minSize)
            {
                newW = minSize;
                if (_currentResizeDirection != ResizeDirection.BottomCenter && _currentResizeDirection != ResizeDirection.TopCenter)
                    newH = newW / ratio;
            }
            if (newW > contentWidth)
            {
                newW = contentWidth;
                if (_currentResizeDirection != ResizeDirection.BottomCenter && _currentResizeDirection != ResizeDirection.TopCenter)
                    newH = newW / ratio;
            }
            if (newH < minSize)
            {
                newH = minSize;
            }

            if (_currentResizeDirection == ResizeDirection.LeftCenter ||
                _currentResizeDirection == ResizeDirection.BottomLeft ||
                _currentResizeDirection == ResizeDirection.TopLeft)
            {
                newX = rightEdge - newW;
            }

            if (_currentResizeDirection == ResizeDirection.TopCenter ||
                _currentResizeDirection == ResizeDirection.TopRight ||
                _currentResizeDirection == ResizeDirection.TopLeft)
            {
                newY = bottomEdge - newH;
            }

            _finalResizeWidth = newW;
            _finalResizeHeight = newH;

            _resizingPageControl.TempResizeBounds = new Rect(newX, newY, newW, newH);
            _resizingPageControl.InvalidateVisual();

            return;
        }

        if (!_isDragging) return;

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

                DocumentHitResult? hit = _layoutManager.GetPositionFromPoint(targetPageData, localPoint);

                if (hit != null && !hit.IsImageHit && hit.TextPosition.HasValue)
                {
                    if (hit.TextPosition.Value.CompareTo(_editor.CaretPosition) != 0)
                    {
                        _editor.CaretPosition = hit.TextPosition.Value;
                        RefreshView();
                    }
                }
            }
        }
    }

    private void OnPagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            e.Handled = true;
            return;
        }

        if (_currentResizeDirection != ResizeDirection.None && _resizingParagraph != null && _editor != null)
        {
            _editor.ExecuteWithSnapshot(() =>
            {
                _resizingParagraph.ImageWidth = _finalResizeWidth;
                _resizingParagraph.ImageHeight = _finalResizeHeight;
            });

            if (_resizingPageControl != null)
            {
                _resizingPageControl.TempResizeBounds = null;
                _resizingPageControl = null;
            }

            _currentResizeDirection = ResizeDirection.None;
            _resizingParagraph = null;

            if (sender is GostPageControl) e.Pointer.Capture(null);

            RefreshView();
            return;
        }

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
    public void AppendParagraphs(List<Paragraph> paragraphs) { if (_editor == null) return; _editor.AppendParagraphs(paragraphs); RefreshView(); }
    public void ApplyParagraphStyle(ParagraphStyle style) { if (_editor == null) return; _editor.SetParagraphStyle(style); RefreshView(); Focus(); }

    public bool HasSelection => _editor != null && _editor.HasSelection;
    private void SelectAll() { if (_editor == null) return; _editor.SelectAll(); RefreshView(); Focus(); }

    public void Undo() { if (_editor == null) return; _editor.History.Undo(); RefreshView(); Focus(); }
    public void Redo() { if (_editor == null) return; _editor.History.Redo(); RefreshView(); Focus(); }
    public string GetSelectedText() { return _editor != null ? _editor.GetSelectedText() : string.Empty; }
    public void DeleteSelection() { if (_editor == null) return; _editor.DeleteSelection(); RefreshView(); Focus(); }
    public void PasteText(string text) { if (_editor == null) return; _editor.PasteText(text); RefreshView(); Focus(); }

    // --- НОВЫЕ МЕТОДЫ ДЛЯ ЛЕВОЙ ПАНЕЛИ ---

    public void ScrollToParagraph(int paragraphIndex)
    {
        if (_editor == null || paragraphIndex < 0 || paragraphIndex >= _editor.Document.Paragraphs.Count) return;

        // Ставим каретку в самое начало выбранного абзаца
        _editor.CaretPosition = new DocumentPosition(paragraphIndex, 0);
        _editor.ClearSelection();

        // RefreshView сам вызовет метод ScrollToCaret() и прокрутит экран к этому месту!
        RefreshView();
        Focus();
    }

    public void InsertHeading(int level, string text)
    {
        if (_editor == null) return;

        _editor.ExecuteWithSnapshot(() =>
        {
            // Создаем сам заголовок
            Paragraph headingPara = new Paragraph
            {
                Style = level == 1 ? ParagraphStyle.Heading1 : ParagraphStyle.Heading2,
                Alignment = GostAlignment.Center,
                PageBreakBefore = level == 1, // Если Глава - начинаем с новой страницы
                FirstLineIndent = 0
            };

            // Если Глава - 16 шрифт, если подраздел - 14
            headingPara.Runs.Add(new DOMTextRun(text, true, false) { FontSize = level == 1 ? 16 : 14 });
            _editor.Document.Paragraphs.Add(headingPara);

            // Создаем ОБЫЧНЫЙ абзац сразу после заголовка (чтобы пользователь мог сразу печатать)
            Paragraph emptyPara = new Paragraph
            {
                Style = ParagraphStyle.Normal,
                Alignment = GostAlignment.Justify, // По ширине
                FirstLineIndent = 47.0 // Абзацный отступ
            };
            emptyPara.Runs.Add(new DOMTextRun("", false, false) { FontSize = 14 }); // 14 шрифт
            _editor.Document.Paragraphs.Add(emptyPara);

            // Ставим каретку в этот новый пустой абзац
            _editor.CaretPosition = new DocumentPosition(_editor.Document.Paragraphs.Count - 1, 0);
            _editor.ClearSelection();
        });

        RefreshView();
        ContentChanged?.Invoke();
        Focus();
    }

    // ------------------------------------

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_editor != null && _editor.HasSelection)
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(_editor.GetSelectedText());
                }
            }
            Focus();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private async void OnCutClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_editor != null && _editor.HasSelection)
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(_editor.GetSelectedText());
                    _editor.DeleteSelection();
                    RefreshView();
                }
            }
            Focus();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private async void OnPasteClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await PasteFromClipboardAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e) { SelectAll(); }
    private void OnUndoClick(object? sender, RoutedEventArgs e) { Undo(); }

    public async Task InsertImageFromFileAsync()
    {
        try
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите изображение",
                AllowMultiple = false,
                FileTypeFilter = [ FilePickerFileTypes.ImageAll ]
            });

            if (files.Count > 0)
            {
                await using Stream stream = await files[0].OpenReadAsync();
                using MemoryStream ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                byte[] bytes = ms.ToArray();

                using Bitmap bmp = new Bitmap(new MemoryStream(bytes));
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
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;

        try
        {
            string[] formats = await topLevel.Clipboard.GetFormatsAsync();
            foreach (string format in formats)
            {
                if (format.Contains("png", StringComparison.OrdinalIgnoreCase) || format.Contains("jpg", StringComparison.OrdinalIgnoreCase))
                {
                    object? imgData = await topLevel.Clipboard.GetDataAsync(format);
                    if (imgData is byte[] bytes)
                    {
                        using Bitmap bmp = new Bitmap(new MemoryStream(bytes));
                        _editor.InsertImage(bytes, bmp.Size.Width, bmp.Size.Height);
                        RefreshView();
                        Focus();
                        return;
                    }
                }
            }

            object? data = await topLevel.Clipboard.GetDataAsync(DataFormats.Files);
            if (data is IEnumerable<IStorageItem> items)
            {
                foreach (IStorageItem item in items)
                {
                    if (item is IStorageFile storageFile && (storageFile.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || storageFile.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || storageFile.Name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
                    {
                        await using Stream stream = await storageFile.OpenReadAsync();
                        using MemoryStream ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        byte[] bytes = ms.ToArray();

                        using Bitmap bmp = new Bitmap(new MemoryStream(bytes));
                        _editor.InsertImage(bytes, bmp.Size.Width, bmp.Size.Height);
                        RefreshView();
                        Focus();
                        return;
                    }
                }
            }

            string? pastedText = await topLevel.Clipboard.GetTextAsync();
            if (!string.IsNullOrEmpty(pastedText))
            {
                _editor.PasteText(pastedText);
                RefreshView();
                Focus();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка вставки: {ex.Message}");
        }
        Focus();
    }

    public GostDocument? CurrentDocument => _editor?.Document;

    public void LoadDocument(GostDocument newDocument)
    {
        _editor = new DocumentEditor(newDocument);
        RefreshView();
    }

    private void ShowContextMenu(Control target)
    {
        if (_editor == null) return;

        ContextMenu menu = new ContextMenu();
        List<MenuItem> items = [];

        if (_editor.SelectedImageParagraphIndex.HasValue)
        {
            int pIndex = _editor.SelectedImageParagraphIndex.Value;

            MenuItem copyItem = new MenuItem { Header = "Копировать" };
            copyItem.Click += (_, _) => { };

            MenuItem replaceItem = new MenuItem { Header = "Заменить" };
            replaceItem.Click += async (_, _) =>
            {
                await ReplaceImageAsync(pIndex);
            };

            MenuItem cutItem = new MenuItem { Header = "Вырезать" };
            cutItem.Click += (_, _) =>
            {
                _editor.ExecuteWithSnapshot(() => _editor.Document.Paragraphs.RemoveAt(pIndex));
                _editor.SelectedImageParagraphIndex = null;
                RefreshView();
            };

            MenuItem editItem = new MenuItem { Header = "Редактировать" };
            editItem.Click += async (_, _) =>
            {
                Window? mainWindow = TopLevel.GetTopLevel(this) as Window;
                if (mainWindow == null) return;

                Paragraph p = _editor.Document.Paragraphs[pIndex];
                byte[]? originalBytes = p.ImageData;
                if (originalBytes == null) return;

                ImageEditorWindow editorWindow = new ImageEditorWindow();
                byte[]? newImageBytes = await editorWindow.ShowDialogAsync(mainWindow, originalBytes);

                if (newImageBytes != null)
                {
                    using MemoryStream ms = new MemoryStream(newImageBytes);
                    using Bitmap bmp = new Bitmap(ms);
                    double newWidth = bmp.Size.Width;
                    double newHeight = bmp.Size.Height;

                    _editor.ExecuteWithSnapshot(() =>
                    {
                        p.ImageData = newImageBytes;
                        p.ImageWidth = newWidth;
                        p.ImageHeight = newHeight;
                    });

                    RefreshView();
                }
            };

            MenuItem deleteItem = new MenuItem { Header = "Удалить" };
            deleteItem.Click += (_, _) =>
            {
                _editor.ExecuteWithSnapshot(() => _editor.Document.Paragraphs.RemoveAt(pIndex));
                _editor.SelectedImageParagraphIndex = null;
                RefreshView();
            };

            items.Add(copyItem);
            items.Add(replaceItem);
            items.Add(cutItem);
            items.Add(editItem);
            items.Add(deleteItem);
        }
        else
        {
            MenuItem heading1Item = new MenuItem { Header = "Сделать Главой (Уровень 1)", FontWeight = FontWeight.Bold };
            heading1Item.Click += (_, _) => { ApplyParagraphStyle(ParagraphStyle.Heading1); ContentChanged?.Invoke(); };

            MenuItem heading2Item = new MenuItem { Header = "Сделать Подразделом (Уровень 2)", FontWeight = FontWeight.SemiBold };
            heading2Item.Click += (_, _) => { ApplyParagraphStyle(ParagraphStyle.Heading2); ContentChanged?.Invoke(); };

            MenuItem normalItem = new MenuItem { Header = "Сделать обычным текстом" };
            normalItem.Click += (_, _) => { ApplyParagraphStyle(ParagraphStyle.Normal); ContentChanged?.Invoke(); };

            MenuItem separator = new MenuItem { Header = "-" }; // Разделитель

            MenuItem copyTextItem = new MenuItem { Header = "Копировать текст" };
            copyTextItem.Click += OnCopyClick;

            MenuItem pasteTextItem = new MenuItem { Header = "Вставить текст" };
            pasteTextItem.Click += OnPasteClick;

            items.Add(heading1Item);
            items.Add(heading2Item);
            items.Add(normalItem);
            items.Add(separator);
            items.Add(copyTextItem);
            items.Add(pasteTextItem);
        }

        menu.ItemsSource = items;
        menu.Open(target);
    }

    private async Task ReplaceImageAsync(int paragraphIndex)
    {
        try
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null || _editor == null) return;

            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите новое изображение",
                AllowMultiple = false,
                FileTypeFilter = [ FilePickerFileTypes.ImageAll ]
            });

            if (files.Count > 0)
            {
                await using Stream stream = await files[0].OpenReadAsync();
                using MemoryStream ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                byte[] bytes = ms.ToArray();

                using Bitmap bmp = new Bitmap(new MemoryStream(bytes));
                double newWidth = bmp.Size.Width;
                double newHeight = bmp.Size.Height;

                _editor.ExecuteWithSnapshot(() =>
                {
                    Paragraph p = _editor.Document.Paragraphs[paragraphIndex];
                    p.ImageData = bytes;
                    p.ImageWidth = newWidth;
                    p.ImageHeight = newHeight;
                });

                RefreshView();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка замены файла: {ex.Message}");
        }
    }
}
