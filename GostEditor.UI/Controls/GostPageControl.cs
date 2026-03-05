using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GostEditor.UI.Layout;
using System;
using System.Collections.Generic;
using System.IO;

namespace GostEditor.UI.Controls;

public class GostPageControl : Control
{
    private RenderedPage? _pageToRender;
    private double _pageWidth = 794.0;
    private double _pageHeight = 1123.0;

    private readonly DispatcherTimer _caretTimer;
    private bool _isCaretVisible = true;

    private int _startPageNumber = 1;
    private int? _selectedImageParagraphIndex;

    // НОВОЕ: Временные координаты рамки во время перетаскивания
    public Rect? TempResizeBounds { get; set; }

    private readonly Dictionary<byte[], Bitmap> _imageCache = new Dictionary<byte[], Bitmap>();

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

    public void SetPageData(RenderedPage page, double width, double height, int startPageNumber, int? selectedImageIndex = null)
    {
        _pageToRender = page;
        _pageWidth = width;
        _pageHeight = height;
        _startPageNumber = startPageNumber;
        _selectedImageParagraphIndex = selectedImageIndex;

        // Сбрасываем призрачную рамку при пересчете документа
        TempResizeBounds = null;
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

        SolidColorBrush selectionBrush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));
        foreach (Rect selRect in _pageToRender.SelectionBounds)
        {
            context.FillRectangle(selectionBrush, selRect);
        }

        if (_pageToRender.Images != null)
        {
            foreach (ImagePlacement img in _pageToRender.Images)
            {
                if (!_imageCache.TryGetValue(img.ImageData, out Bitmap? bmp))
                {
                    using MemoryStream ms = new MemoryStream((byte[])img.ImageData);
                    bmp = new Bitmap(ms);
                    _imageCache[img.ImageData] = bmp;
                }

                if (bmp != null)
                {
                    // ЛОГИКА ПРИЗРАЧНОЙ РАМКИ:
                    // Если картинка выделена и мы её тянем, рисуем её по временным координатам
                    bool isResizingThisImage = _selectedImageParagraphIndex.HasValue &&
                                               img.ParagraphIndex == _selectedImageParagraphIndex.Value &&
                                               TempResizeBounds.HasValue;

                    Rect drawBounds = isResizingThisImage ? TempResizeBounds!.Value : img.Bounds;

                    // Отрисовка самой картинки (видеокарта сама мгновенно её растянет)
                    context.DrawImage(bmp, drawBounds);

                    // Отрисовка синей рамки выделения поверх картинки
                    if (_selectedImageParagraphIndex.HasValue && img.ParagraphIndex == _selectedImageParagraphIndex.Value)
                    {
                        Pen borderPen = new Pen(new SolidColorBrush(Color.Parse("#1565C0")), 1.5);
                        context.DrawRectangle(null, borderPen, drawBounds);

                        double markerSize = 8.0;
                        double halfSize = markerSize / 2.0;
                        ISolidColorBrush markerFill = Brushes.White;
                        Pen markerPen = new Pen(new SolidColorBrush(Color.Parse("#1565C0")), 1);

                        Point[] markerCenters = new Point[]
                        {
                            new Point(drawBounds.Left, drawBounds.Top),
                            new Point(drawBounds.Center.X, drawBounds.Top),
                            new Point(drawBounds.Right, drawBounds.Top),
                            new Point(drawBounds.Right, drawBounds.Center.Y),
                            new Point(drawBounds.Right, drawBounds.Bottom),
                            new Point(drawBounds.Center.X, drawBounds.Bottom),
                            new Point(drawBounds.Left, drawBounds.Bottom),
                            new Point(drawBounds.Left, drawBounds.Center.Y)
                        };

                        foreach (Point center in markerCenters)
                        {
                            Rect markerRect = new Rect(center.X - halfSize, center.Y - halfSize, markerSize, markerSize);
                            context.FillRectangle(markerFill, markerRect);
                            context.DrawRectangle(markerPen, markerRect);
                        }
                    }
                }
            }
        }

        foreach (TextLinePlacement placement in _pageToRender.Lines)
        {
            placement.Line.Draw(context, placement.Location);
        }

        if (_pageToRender.CaretBounds.HasValue && _isCaretVisible && !_selectedImageParagraphIndex.HasValue)
        {
            context.FillRectangle(Brushes.Black, _pageToRender.CaretBounds.Value);
        }

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

        double xPosition = (bounds.Width / 2.0) - (pageNumberText.Width / 2.0);
        double yPosition = bounds.Height - 60.0;

        Point textPosition = new Point(xPosition, yPosition);

        context.DrawText(pageNumberText, textPosition);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PageClicked?.Invoke(this, e.GetPosition(this));
    }
}
