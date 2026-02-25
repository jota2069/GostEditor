using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GostEditor.UI.Layout;
using System;

namespace GostEditor.UI.Controls;

public class GostPageControl : Control
{
    private RenderedPage? _pageToRender;
    private double _pageWidth = 794.0;
    private double _pageHeight = 1123.0;

    private readonly DispatcherTimer _caretTimer;
    private bool _isCaretVisible = true;

    public event EventHandler<Point>? PageClicked;

    public GostPageControl()
    {
        _caretTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _caretTimer.Tick += (s, e) => { _isCaretVisible = !_isCaretVisible; InvalidateVisual(); };
        _caretTimer.Start();
    }

    public void SetPageData(RenderedPage page, double width, double height)
    {
        _pageToRender = page;
        _pageWidth = width;
        _pageHeight = height;
        _isCaretVisible = true;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(_pageWidth, _pageHeight);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = this.Bounds;
        Rect backgroundRect = new Rect(0, 0, bounds.Width, bounds.Height);

        context.FillRectangle(Brushes.White, backgroundRect);
        context.DrawRectangle(new Pen(Brushes.LightGray, 1), backgroundRect);

        if (_pageToRender == null) return;

        // 1. Рисуем выделение (ПОД текстом)
        var selectionBrush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));
        foreach (var selRect in _pageToRender.SelectionBounds)
        {
            context.FillRectangle(selectionBrush, selRect);
        }

        // 2. Рисуем текст
        foreach (var placement in _pageToRender.Lines)
        {
            placement.Line.Draw(context, placement.Location);
        }

        // 3. Рисуем каретку (ПОВЕРХ текста)
        if (_pageToRender.CaretBounds.HasValue && _isCaretVisible)
        {
            context.FillRectangle(Brushes.Black, _pageToRender.CaretBounds.Value);
        }
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PageClicked?.Invoke(this, e.GetPosition(this));
    }
}
