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
using System.Linq;

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

    // Временные координаты рамки во время перетаскивания
    public Rect? TempResizeBounds { get; set; }

    private readonly Dictionary<Guid, Bitmap> _imageCache = new Dictionary<Guid, Bitmap>();
    private readonly HashSet<Guid> _invalidImageIds = new HashSet<Guid>();

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

    public void ClearImageCache()
    {
        foreach (Bitmap bitmap in _imageCache.Values)
        {
            bitmap.Dispose();
        }

        _imageCache.Clear();
        _invalidImageIds.Clear();
    }

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _caretTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        _caretTimer.Stop();
        ClearImageCache();
        base.OnDetachedFromVisualTree(e);
    }

    public void SetPageData(RenderedPage page, double width, double height, int startPageNumber, int? selectedImageIndex = null)
    {
        _pageToRender = page;
        _pageWidth = width;
        _pageHeight = height;
        _startPageNumber = startPageNumber;
        _selectedImageParagraphIndex = selectedImageIndex;

        HashSet<Guid> activeImageIds = page.Images.Select(image => image.ImageId).ToHashSet();
        foreach (Guid imageId in _imageCache.Keys.Where(imageId => !activeImageIds.Contains(imageId)).ToList())
        {
            _imageCache[imageId].Dispose();
            _imageCache.Remove(imageId);
            _invalidImageIds.Remove(imageId);
        }

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

        if (_pageToRender is null) return;

        SolidColorBrush selectionBrush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));
        foreach (Rect selRect in _pageToRender.SelectionBounds)
        {
            context.FillRectangle(selectionBrush, selRect);
        }

        if (_pageToRender.Images != null)
        {
            foreach (ImagePlacement img in _pageToRender.Images)
            {
                Bitmap? bmp = null;
                if (img.HasContent && !_invalidImageIds.Contains(img.ImageId) &&
                    !_imageCache.TryGetValue(img.ImageId, out bmp))
                {
                    try
                    {
                        using MemoryStream ms = new MemoryStream(img.ImageBytes.ToArray());
                        bmp = new Bitmap(ms);
                        _imageCache[img.ImageId] = bmp;
                    }
                    catch
                    {
                        _invalidImageIds.Add(img.ImageId);
                    }
                }

                bool isResizingThisImage = _selectedImageParagraphIndex.HasValue &&
                                           img.ParagraphIndex == _selectedImageParagraphIndex.Value &&
                                           TempResizeBounds.HasValue;
                Rect drawBounds = isResizingThisImage ? TempResizeBounds!.Value : img.Bounds;

                if (bmp != null)
                {
                    context.DrawImage(bmp, drawBounds);
                }
                else
                {
                    DrawMissingImagePlaceholder(context, drawBounds);
                }

                if (_selectedImageParagraphIndex.HasValue &&
                    img.ParagraphIndex == _selectedImageParagraphIndex.Value)
                {
                    DrawImageSelection(context, drawBounds);
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

    private static void DrawMissingImagePlaceholder(DrawingContext context, Rect bounds)
    {
        Rect safeBounds = bounds.Width > 0 && bounds.Height > 0
            ? bounds
            : new Rect(bounds.X, bounds.Y, 450, 300);
        Pen borderPen = new Pen(Brushes.Gray, 1.5);

        context.FillRectangle(Brushes.LightGray, safeBounds);
        context.DrawRectangle(null, borderPen, safeBounds);
        context.DrawLine(borderPen, safeBounds.TopLeft, safeBounds.BottomRight);
        context.DrawLine(borderPen, safeBounds.TopRight, safeBounds.BottomLeft);
    }

    private static void DrawImageSelection(DrawingContext context, Rect bounds)
    {
        Pen borderPen = new Pen(new SolidColorBrush(Color.Parse("#1565C0")), 1.5);
        context.DrawRectangle(null, borderPen, bounds);

        const double markerSize = 8.0;
        const double halfSize = markerSize / 2.0;
        ISolidColorBrush markerFill = Brushes.White;
        Pen markerPen = new Pen(new SolidColorBrush(Color.Parse("#1565C0")), 1);

        Point[] markerCenters =
        [
            new Point(bounds.Left, bounds.Top),
            new Point(bounds.Center.X, bounds.Top),
            new Point(bounds.Right, bounds.Top),
            new Point(bounds.Right, bounds.Center.Y),
            new Point(bounds.Right, bounds.Bottom),
            new Point(bounds.Center.X, bounds.Bottom),
            new Point(bounds.Left, bounds.Bottom),
            new Point(bounds.Left, bounds.Center.Y)
        ];

        foreach (Point center in markerCenters)
        {
            Rect markerRect = new Rect(
                center.X - halfSize,
                center.Y - halfSize,
                markerSize,
                markerSize);
            context.FillRectangle(markerFill, markerRect);
            context.DrawRectangle(markerPen, markerRect);
        }
    }
}
