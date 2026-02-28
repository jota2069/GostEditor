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

    // Храним сдвиг нумерации, который приходит из MainWindow
    private int _startPageNumber = 1;

    public event EventHandler<Point>? PageClicked;

    public GostPageControl()
    {
        _caretTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _caretTimer.Tick += (object? s, EventArgs e) =>
        {
            _isCaretVisible = !_isCaretVisible;
            InvalidateVisual();
        };
        _caretTimer.Start();
    }

    // МЕТОД ОБНОВЛЕН: принимает int startPageNumber
    public void SetPageData(RenderedPage page, double width, double height, int startPageNumber)
    {
        _pageToRender = page;
        _pageWidth = width;
        _pageHeight = height;
        _startPageNumber = startPageNumber; // Сохраняем новое начало нумерации
        _isCaretVisible = true;

        // Принудительно заставляем холст перерисоваться с новыми цифрами
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

        SolidColorBrush selectionBrush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));
        foreach (Rect selRect in _pageToRender.SelectionBounds)
        {
            context.FillRectangle(selectionBrush, selRect);
        }

        foreach (TextLinePlacement placement in _pageToRender.Lines)
        {
            placement.Line.Draw(context, placement.Location);
        }

        if (_pageToRender.CaretBounds.HasValue && _isCaretVisible)
        {
            context.FillRectangle(Brushes.Black, _pageToRender.CaretBounds.Value);
        }

        // ==========================================
        // ОТРИСОВКА НОМЕРА СТРАНИЦЫ (ЯВНАЯ ТИПИЗАЦИЯ)
        // ==========================================

        // Формула: (Порядковый номер листа) + (Сдвиг из UI) - 1
        int actualPageNumber = _pageToRender.PageNumber + _startPageNumber - 1;
        string pageNumberString = actualPageNumber.ToString();

        Typeface typeface = new Typeface("Times New Roman");

        FormattedText pageNumberText = new FormattedText(
            pageNumberString,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            14.0,
            Brushes.Black
        );

        // Центрируем цифру и отступаем 60 пикселей от нижнего края белого листа
        double xPosition = (bounds.Width / 2.0) - (pageNumberText.Width / 2.0);
        double yPosition = bounds.Height - 60.0;

        Point textPosition = new Point(xPosition, yPosition);

        context.DrawText(pageNumberText, textPosition);
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PageClicked?.Invoke(this, e.GetPosition(this));
    }
}
