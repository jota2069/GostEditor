using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GostEditor.UI.Views;

public enum CropHandle { None, TopLeft, TopRight, BottomLeft, BottomRight, Top, Bottom, Left, Right, Center }

public partial class ImageEditorWindow : Window
{
    private TaskCompletionSource<byte[]?>? _tcs;
    private byte[]? _currentImageData;

    // Рисование
    private bool _isDrawingMode;
    private bool _isDrawing;
    private Point _lastPoint;
    private Color _drawColor = Color.Parse("#FF5555");
    private Canvas? _currentDrawingGroup; // <--- ГРУППА ДЛЯ НЕПРЕРЫВНОЙ ЛИНИИ

    // Обрезка
    private bool _isCropMode;
    private Rect _cropRect;
    private CropHandle _currentCropHandle = CropHandle.None;
    private Point _cropDragStartPoint;
    private Rect _cropRectStart;

    public ImageEditorWindow()
    {
        InitializeComponent();

        CancelBtn.Click += OnCancelClick;
        ApplyBtn.Click += OnApplyClick;
        RotateBtn.Click += OnRotateClick;
        CropBtn.Click += OnCropClick;
        DrawBtn.Click += OnDrawClick;
        UndoDrawBtn.Click += OnUndoDrawClick;
    }

    public Task<byte[]?> ShowDialogAsync(Window owner, byte[] imageData)
    {
        _tcs = new TaskCompletionSource<byte[]?>();
        _currentImageData = imageData;
        LoadImageToUi(imageData);
        ShowDialog(owner);
        return _tcs.Task;
    }

    private void LoadImageToUi(byte[] data)
    {
        try
        {
            using MemoryStream ms = new MemoryStream(data);
            PreviewImage.Source = new Bitmap(ms);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки: {ex.Message}");
        }
    }

    private void BakeDrawingToImage()
    {
        if (DrawingCanvas.Children.Count == 0 || PreviewImage.Source == null) return;

        Size bmpSize = PreviewImage.Source.Size;
        PixelSize pixelSize = new PixelSize((int)bmpSize.Width, (int)bmpSize.Height);

        using RenderTargetBitmap rtb = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        rtb.Render(ImageGrid);

        using MemoryStream ms = new MemoryStream();
        rtb.Save(ms);

        _currentImageData = ms.ToArray();
        DrawingCanvas.Children.Clear();
        LoadImageToUi(_currentImageData);
    }

    // --- ЛОГИКА ПОВОРОТА ---
    private void OnRotateClick(object? sender, RoutedEventArgs e)
    {
        if (_currentImageData == null || _isCropMode) return;
        BakeDrawingToImage();

        try
        {
            using MemoryStream ms = new MemoryStream(_currentImageData);
            using Bitmap oldBmp = new Bitmap(ms);

            int oldW = (int)oldBmp.Size.Width;
            int oldH = (int)oldBmp.Size.Height;

            using RenderTargetBitmap rtb = new RenderTargetBitmap(new PixelSize(oldH, oldW), new Vector(96, 96));
            using (DrawingContext ctx = rtb.CreateDrawingContext())
            {
                Matrix rotation = Matrix.CreateRotation(Math.PI / 2);
                Matrix translation = Matrix.CreateTranslation(oldH, 0);

                using (ctx.PushTransform(rotation * translation))
                {
                    ctx.DrawImage(oldBmp, new Rect(0, 0, oldW, oldH));
                }
            }

            using MemoryStream outMs = new MemoryStream();
            rtb.Save(outMs);
            _currentImageData = outMs.ToArray();
            LoadImageToUi(_currentImageData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка поворота: {ex.Message}");
        }
    }

    // --- ЛОГИКА РИСОВАНИЯ ---
    private void OnDrawClick(object? sender, RoutedEventArgs e)
    {
        if (_isCropMode) return;

        _isDrawingMode = !_isDrawingMode;

        DrawBtn.Background = _isDrawingMode
            ? new SolidColorBrush(Color.Parse("#007ACC"))
            : new SolidColorBrush(Color.Parse("#3E3E42"));

        DrawingCanvas.Cursor = _isDrawingMode ? new Cursor(StandardCursorType.Cross) : new Cursor(StandardCursorType.Arrow);

        BrushColorCombo.IsVisible = _isDrawingMode;
        BrushThicknessSlider.IsVisible = _isDrawingMode;
        UndoDrawBtn.IsVisible = _isDrawingMode;
    }

    private void OnColorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            _drawColor = combo.SelectedIndex switch
            {
                0 => Color.Parse("#FF5555"),
                1 => Color.Parse("#5555FF"),
                2 => Color.Parse("#55FF55"),
                3 => Colors.Black,
                4 => Colors.White,
                _ => Colors.Black
            };
        }
    }

    private void OnUndoDrawClick(object? sender, RoutedEventArgs e)
    {
        // Удаляем последнюю добавленную ГРУППУ (всю линию целиком)
        if (DrawingCanvas.Children.Count > 0)
        {
            DrawingCanvas.Children.RemoveAt(DrawingCanvas.Children.Count - 1);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool isCtrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

        if (isCtrl && e.Key == Key.Z)
        {
            if (DrawingCanvas.Children.Count > 0)
            {
                DrawingCanvas.Children.RemoveAt(DrawingCanvas.Children.Count - 1);
                e.Handled = true;
            }
        }
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isDrawingMode) return;
        PointerPointProperties props = e.GetCurrentPoint(DrawingCanvas).Properties;

        if (props.IsLeftButtonPressed)
        {
            _isDrawing = true;
            _lastPoint = e.GetPosition(DrawingCanvas);

            // Создаем отдельную группу (холст) для текущей непрерывной линии
            _currentDrawingGroup = new Canvas();
            DrawingCanvas.Children.Add(_currentDrawingGroup);

            e.Pointer.Capture(DrawingCanvas);
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDrawing || !_isDrawingMode || _currentDrawingGroup == null) return;

        Point currentPoint = e.GetPosition(DrawingCanvas);
        Line line = new Line
        {
            StartPoint = _lastPoint,
            EndPoint = currentPoint,
            Stroke = new SolidColorBrush(_drawColor),
            StrokeThickness = BrushThicknessSlider.Value,
            StrokeLineCap = PenLineCap.Round
        };

        // Добавляем мелкий отрезок внутрь группы
        _currentDrawingGroup.Children.Add(line);
        _lastPoint = currentPoint;
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDrawing)
        {
            _isDrawing = false;
            _currentDrawingGroup = null; // Завершили линию
            e.Pointer.Capture(null);
        }
    }

    // --- ЛОГИКА ОБРЕЗКИ (CROP) ---
    private void OnCropClick(object? sender, RoutedEventArgs e)
    {
        if (PreviewImage.Source == null) return;

        if (!_isCropMode)
        {
            BakeDrawingToImage();

            _isDrawingMode = false;
            DrawBtn.Background = new SolidColorBrush(Color.Parse("#3E3E42"));
            DrawingCanvas.Cursor = new Cursor(StandardCursorType.Arrow);
            BrushColorCombo.IsVisible = false;
            BrushThicknessSlider.IsVisible = false;
            UndoDrawBtn.IsVisible = false;

            _isCropMode = true;
            CropCanvas.IsVisible = true;
            CropBtn.Content = "Применить обрезку ✔";
            CropBtn.Background = new SolidColorBrush(Color.Parse("#28A745"));

            Size imgSize = PreviewImage.Source.Size;
            double paddingX = imgSize.Width * 0.1;
            double paddingY = imgSize.Height * 0.1;
            _cropRect = new Rect(paddingX, paddingY, imgSize.Width - paddingX * 2, imgSize.Height - paddingY * 2);

            UpdateCropUi(imgSize);
        }
        else
        {
            ApplyCrop();

            _isCropMode = false;
            CropCanvas.IsVisible = false;
            CropBtn.Content = "Обрезать ✂";
            CropBtn.Background = new SolidColorBrush(Color.Parse("#3E3E42"));
        }
    }

    private void UpdateCropUi(Size imageSize)
    {
        Canvas.SetLeft(CropBorder, _cropRect.X);
        Canvas.SetTop(CropBorder, _cropRect.Y);
        CropBorder.Width = _cropRect.Width;
        CropBorder.Height = _cropRect.Height;

        Canvas.SetLeft(CropTopRect, 0); Canvas.SetTop(CropTopRect, 0);
        CropTopRect.Width = imageSize.Width; CropTopRect.Height = _cropRect.Top;

        Canvas.SetLeft(CropBottomRect, 0); Canvas.SetTop(CropBottomRect, _cropRect.Bottom);
        CropBottomRect.Width = imageSize.Width; CropBottomRect.Height = imageSize.Height - _cropRect.Bottom;

        Canvas.SetLeft(CropLeftRect, 0); Canvas.SetTop(CropLeftRect, _cropRect.Top);
        CropLeftRect.Width = _cropRect.Left; CropLeftRect.Height = _cropRect.Height;

        Canvas.SetLeft(CropRightRect, _cropRect.Right); Canvas.SetTop(CropRightRect, _cropRect.Top);
        CropRightRect.Width = imageSize.Width - _cropRect.Right; CropRightRect.Height = _cropRect.Height;
    }

    private CropHandle GetCropHandle(Point p)
    {
        double margin = 15.0;

        bool left = Math.Abs(p.X - _cropRect.Left) < margin;
        bool right = Math.Abs(p.X - _cropRect.Right) < margin;
        bool top = Math.Abs(p.Y - _cropRect.Top) < margin;
        bool bottom = Math.Abs(p.Y - _cropRect.Bottom) < margin;

        if (left && top) return CropHandle.TopLeft;
        if (right && top) return CropHandle.TopRight;
        if (left && bottom) return CropHandle.BottomLeft;
        if (right && bottom) return CropHandle.BottomRight;

        if (left) return CropHandle.Left;
        if (right) return CropHandle.Right;
        if (top) return CropHandle.Top;
        if (bottom) return CropHandle.Bottom;

        if (_cropRect.Contains(p)) return CropHandle.Center;

        return CropHandle.None;
    }

    private void OnCropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isCropMode) return;
        Point p = e.GetPosition(CropCanvas);

        _currentCropHandle = GetCropHandle(p);
        if (_currentCropHandle != CropHandle.None)
        {
            _cropDragStartPoint = p;
            _cropRectStart = _cropRect;
            e.Pointer.Capture(CropCanvas);
            e.Handled = true;
        }
    }

    private void OnCropPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isCropMode || PreviewImage.Source == null) return;
        Point p = e.GetPosition(CropCanvas);

        if (_currentCropHandle == CropHandle.None)
        {
            CropHandle hoverHandle = GetCropHandle(p);
            CropCanvas.Cursor = hoverHandle switch
            {
                CropHandle.TopLeft or CropHandle.BottomRight => new Cursor(StandardCursorType.TopLeftCorner),
                CropHandle.TopRight or CropHandle.BottomLeft => new Cursor(StandardCursorType.TopRightCorner),
                CropHandle.Top or CropHandle.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
                CropHandle.Left or CropHandle.Right => new Cursor(StandardCursorType.SizeWestEast),
                CropHandle.Center => new Cursor(StandardCursorType.SizeAll),
                _ => new Cursor(StandardCursorType.Arrow)
            };
            return;
        }

        double dx = p.X - _cropDragStartPoint.X;
        double dy = p.Y - _cropDragStartPoint.Y;

        double newX = _cropRectStart.X;
        double newY = _cropRectStart.Y;
        double newW = _cropRectStart.Width;
        double newH = _cropRectStart.Height;

        if (_currentCropHandle == CropHandle.Center)
        {
            newX += dx;
            newY += dy;
        }
        else
        {
            if (_currentCropHandle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft) { newX += dx; newW -= dx; }
            if (_currentCropHandle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight) { newW += dx; }
            if (_currentCropHandle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight) { newY += dy; newH -= dy; }
            if (_currentCropHandle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight) { newH += dy; }
        }

        Size imgSize = PreviewImage.Source.Size;
        if (newW < 20) newW = 20;
        if (newH < 20) newH = 20;
        if (newX < 0) newX = 0;
        if (newY < 0) newY = 0;
        if (newX + newW > imgSize.Width) { if (_currentCropHandle == CropHandle.Center) newX = imgSize.Width - newW; else newW = imgSize.Width - newX; }
        if (newY + newH > imgSize.Height) { if (_currentCropHandle == CropHandle.Center) newY = imgSize.Height - newH; else newH = imgSize.Height - newY; }

        _cropRect = new Rect(newX, newY, newW, newH);
        UpdateCropUi(imgSize);
    }

    private void OnCropPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_currentCropHandle != CropHandle.None)
        {
            _currentCropHandle = CropHandle.None;
            e.Pointer.Capture(null);
        }
    }

    private void ApplyCrop()
    {
        if (_currentImageData == null || PreviewImage.Source == null) return;

        try
        {
            using MemoryStream ms = new MemoryStream(_currentImageData);
            using Bitmap oldBmp = new Bitmap(ms);

            PixelSize cropPixelSize = new PixelSize((int)_cropRect.Width, (int)_cropRect.Height);
            using RenderTargetBitmap rtb = new RenderTargetBitmap(cropPixelSize, new Vector(96, 96));

            using (DrawingContext ctx = rtb.CreateDrawingContext())
            {
                Matrix translation = Matrix.CreateTranslation(-_cropRect.X, -_cropRect.Y);
                using (ctx.PushTransform(translation))
                {
                    ctx.DrawImage(oldBmp, new Rect(0, 0, oldBmp.Size.Width, oldBmp.Size.Height));
                }
            }

            using MemoryStream outMs = new MemoryStream();
            rtb.Save(outMs);
            _currentImageData = outMs.ToArray();
            LoadImageToUi(_currentImageData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обрезке: {ex.Message}");
        }
    }

    // --- ФИНАЛИЗАЦИЯ ---
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _tcs?.TrySetResult(null);
        Close();
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_isCropMode) ApplyCrop();
        else BakeDrawingToImage();

        _tcs?.TrySetResult(_currentImageData);
        Close();
    }
}
