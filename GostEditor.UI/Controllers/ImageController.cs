using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Controls;
using GostEditor.UI.Layout;

namespace GostEditor.UI.Controllers;

public enum ResizeDirection
{
    None, TopLeft, TopCenter, TopRight, RightCenter, BottomRight, BottomCenter, BottomLeft, LeftCenter
}

public class ImageController
{
    private readonly DocumentEditor _editor;
    private readonly RenderController _renderController;
    private readonly PageLayoutManager _layoutManager;

    private bool _isDraggingImage;
    private ResizeDirection _currentResizeDirection = ResizeDirection.None;
    private Point _resizeStartPoint;
    private double _initialImageWidth, _initialImageHeight, _initialImageX, _initialImageY;
    private Paragraph? _resizingParagraph;
    private GostPageControl? _resizingPageControl;
    private double _finalResizeWidth, _finalResizeHeight;

    public ImageController(DocumentEditor editor, RenderController renderController, PageLayoutManager layoutManager)
    {
        _editor = editor;
        _renderController = renderController;
        _layoutManager = layoutManager;
    }

    public bool TryHandleRightClick(RenderedPage pageData, Point point)
    {
        DocumentHitResult? hit = _layoutManager.GetPositionFromPoint(pageData, point);
        if (hit is { IsImageHit: true, ImageParagraphIndex: not null })
        {
            _editor.SelectionAnchor = null;
            _editor.SelectedImageParagraphIndex = hit.ImageParagraphIndex;
            _renderController.RefreshView();
            return true;
        }
        return false;
    }

    public bool TryHandleLeftClick(RenderedPage pageData, Point localPoint, PointerPressedEventArgs e, StackPanel stack, GostPageControl pageControl)
    {
        // 1. Проверяем, потянули ли мы за квадратик УЖЕ выделенной картинки
        if (_editor.SelectedImageParagraphIndex.HasValue)
        {
            ResizeDirection hitDir = GetResizeHandleHit(pageData, localPoint, _editor.SelectedImageParagraphIndex.Value);
            if (hitDir != ResizeDirection.None)
            {
                _currentResizeDirection = hitDir;
                _resizeStartPoint = e.GetPosition(stack);
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
                }

                _finalResizeWidth = _initialImageWidth;
                _finalResizeHeight = _initialImageHeight;

                _isDraggingImage = true;
                e.Pointer.Capture(pageControl);
                return true;
            }
        }

        // 2. Если квадратик не задет, проверяем, кликнули ли мы просто ПО самой картинке
        DocumentHitResult? hit = _layoutManager.GetPositionFromPoint(pageData, localPoint);
        if (hit is { IsImageHit: true, ImageParagraphIndex: not null })
        {
            _editor.SelectionAnchor = null;
            _editor.SelectedImageParagraphIndex = hit.ImageParagraphIndex;
            _renderController.RefreshView();
            return true;
        }

        // 3. Кликнули мимо картинки — снимаем с нее выделение
        if (_editor.SelectedImageParagraphIndex.HasValue)
        {
            _editor.SelectedImageParagraphIndex = null;
            _renderController.RefreshView();
        }

        return false;
    }

    public void HandlePointerMoved(PointerEventArgs e, StackPanel stack)
    {
        if (!_isDraggingImage || _resizingPageControl == null) return;

        Point currentGlobal = e.GetPosition(stack);
        double handleDeltaX = currentGlobal.X - _resizeStartPoint.X;
        double handleDeltaY = currentGlobal.Y - _resizeStartPoint.Y;

        double ratio = _initialImageWidth / _initialImageHeight;
        double newW = _initialImageWidth, newH = _initialImageHeight;
        double newX = _initialImageX, newY = _initialImageY;
        double rightEdge = _initialImageX + _initialImageWidth;
        double bottomEdge = _initialImageY + _initialImageHeight;

        switch (_currentResizeDirection)
        {
            case ResizeDirection.RightCenter: newW = _initialImageWidth + handleDeltaX; break;
            case ResizeDirection.LeftCenter: newW = _initialImageWidth - handleDeltaX; break;
            case ResizeDirection.BottomCenter: newH = _initialImageHeight + handleDeltaY; break;
            case ResizeDirection.TopCenter: newH = _initialImageHeight - handleDeltaY; break;
            case ResizeDirection.BottomRight: newW = _initialImageWidth + handleDeltaX; newH = newW / ratio; break;
            case ResizeDirection.BottomLeft: newW = _initialImageWidth - handleDeltaX; newH = newW / ratio; break;
            case ResizeDirection.TopRight: newW = _initialImageWidth + handleDeltaX; newH = newW / ratio; break;
            case ResizeDirection.TopLeft: newW = _initialImageWidth - handleDeltaX; newH = newW / ratio; break;
        }

        double minSize = 20.0;
        double contentWidth = _editor.Document.PageWidth - _editor.Document.MarginLeft - _editor.Document.MarginRight;

        if (newW < minSize) { newW = minSize; if (_currentResizeDirection != ResizeDirection.BottomCenter && _currentResizeDirection != ResizeDirection.TopCenter) newH = newW / ratio; }
        if (newW > contentWidth) { newW = contentWidth; if (_currentResizeDirection != ResizeDirection.BottomCenter && _currentResizeDirection != ResizeDirection.TopCenter) newH = newW / ratio; }
        if (newH < minSize) newH = minSize;

        if (_currentResizeDirection == ResizeDirection.LeftCenter || _currentResizeDirection == ResizeDirection.BottomLeft || _currentResizeDirection == ResizeDirection.TopLeft) newX = rightEdge - newW;
        if (_currentResizeDirection == ResizeDirection.TopCenter || _currentResizeDirection == ResizeDirection.TopRight || _currentResizeDirection == ResizeDirection.TopLeft) newY = bottomEdge - newH;

        _finalResizeWidth = newW;
        _finalResizeHeight = newH;

        _resizingPageControl.TempResizeBounds = new Rect(newX, newY, newW, newH);
        _resizingPageControl.InvalidateVisual();
    }

    public bool HandlePointerReleased(PointerReleasedEventArgs e)
    {
        if (!_isDraggingImage || _resizingParagraph == null) return false;

        _editor.ExecuteWithSnapshot(() =>
        {
            _resizingParagraph.ImageWidth = _finalResizeWidth;
            _resizingParagraph.ImageHeight = _finalResizeHeight;
        });

        if (_resizingPageControl != null) _resizingPageControl.TempResizeBounds = null;

        _isDraggingImage = false;
        _currentResizeDirection = ResizeDirection.None;
        _resizingParagraph = null;
        _resizingPageControl = null;

        _renderController.RefreshView();
        e.Pointer.Capture(null);
        return true;
    }

    public Cursor? GetHoverCursor(Point globalP, StackPanel stack)
    {
        if (_isDraggingImage || !_editor.SelectedImageParagraphIndex.HasValue) return null;

        for (int i = 0; i < stack.Children.Count; i++)
        {
            Control child = stack.Children[i];
            if (globalP.Y >= child.Bounds.Top && globalP.Y <= child.Bounds.Bottom && i < _renderController.CurrentPages.Count)
            {
                Point localP = new Point(globalP.X, globalP.Y - child.Bounds.Top);
                if (GetResizeHandleHit(_renderController.CurrentPages[i], localP, _editor.SelectedImageParagraphIndex.Value) != ResizeDirection.None)
                {
                    return new Cursor(StandardCursorType.Hand);
                }
            }
        }
        return new Cursor(StandardCursorType.Ibeam);
    }

    private ResizeDirection GetResizeHandleHit(RenderedPage page, Point localPoint, int selectedImageIndex)
    {
        foreach (ImagePlacement img in page.Images)
        {
            if (img.ParagraphIndex == selectedImageIndex)
            {
                double markerSize = 8.0, halfSize = markerSize / 2.0, padding = 15.0;
                Point[] centers = [
                    new Point(img.Bounds.Left, img.Bounds.Top), new Point(img.Bounds.Center.X, img.Bounds.Top), new Point(img.Bounds.Right, img.Bounds.Top),
                    new Point(img.Bounds.Right, img.Bounds.Center.Y), new Point(img.Bounds.Right, img.Bounds.Bottom), new Point(img.Bounds.Center.X, img.Bounds.Bottom),
                    new Point(img.Bounds.Left, img.Bounds.Bottom), new Point(img.Bounds.Left, img.Bounds.Center.Y)
                ];
                ResizeDirection[] dirs = [
                    ResizeDirection.TopLeft, ResizeDirection.TopCenter, ResizeDirection.TopRight, ResizeDirection.RightCenter,
                    ResizeDirection.BottomRight, ResizeDirection.BottomCenter, ResizeDirection.BottomLeft, ResizeDirection.LeftCenter
                ];

                for (int i = 0; i < centers.Length; i++)
                {
                    Rect hitArea = new Rect(centers[i].X - halfSize - padding, centers[i].Y - halfSize - padding, markerSize + padding * 2, markerSize + padding * 2);
                    if (hitArea.Contains(localPoint)) return dirs[i];
                }
            }
        }
        return ResizeDirection.None;
    }

    public async Task InsertImageFromFileAsync(TopLevel topLevel)
    {
        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите изображение", AllowMultiple = false, FileTypeFilter = [ FilePickerFileTypes.ImageAll ]
        });

        if (files.Count > 0)
        {
            await using Stream stream = await files[0].OpenReadAsync();
            using MemoryStream ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            byte[] bytes = ms.ToArray();
            using Bitmap bmp = new Bitmap(new MemoryStream(bytes));

            Paragraph imgPara = new Paragraph { ImageData = bytes, ImageWidth = 450, ImageHeight = 300, Alignment = GostAlignment.Center };
            int insertIdx = _editor.CaretPosition.ParagraphIndex + 1;

            if (insertIdx >= _editor.Document.Paragraphs.Count) _editor.Document.Paragraphs.Add(imgPara);
            else _editor.Document.Paragraphs.Insert(insertIdx, imgPara);

            _renderController.RefreshView();
        }
    }

    public async Task ReplaceImageAsync(TopLevel topLevel, int paragraphIndex)
    {
        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите новое изображение", AllowMultiple = false, FileTypeFilter = [ FilePickerFileTypes.ImageAll ]
        });

        if (files.Count > 0)
        {
            await using Stream stream = await files[0].OpenReadAsync();
            using MemoryStream ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            byte[] bytes = ms.ToArray();
            using Bitmap bmp = new Bitmap(new MemoryStream(bytes));

            _editor.ExecuteWithSnapshot(() =>
            {
                Paragraph p = _editor.Document.Paragraphs[paragraphIndex];
                p.ImageData = bytes; p.ImageWidth = bmp.Size.Width; p.ImageHeight = bmp.Size.Height;
            });
            _renderController.RefreshView();
        }
    }
}
