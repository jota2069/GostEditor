using System;
using Avalonia;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Layout;

namespace GostEditor.UI.Controllers;

public class MouseController
{
    private readonly DocumentEditor _editor;
    private readonly RenderController _renderController;

    private bool _isSelecting = false;

    public MouseController(
        DocumentEditor editor,
        RenderController renderController)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _renderController = renderController ?? throw new ArgumentNullException(nameof(renderController));
    }

    public void HandlePointerPressed(int pageIndex, Point point, bool isShift)
    {
        if (pageIndex < 0 || pageIndex >= _renderController.CurrentPages.Count)
        {
            return;
        }

        RenderedPage page = _renderController.CurrentPages[pageIndex];

        DocumentHitResult? hit = _renderController.HitTest(
            page,
            point);
        if (hit is null)
        {
            return;
        }

        if (hit.IsImageHit)
        {
            _editor.SelectedImageParagraphIndex = hit.ImageParagraphIndex;
            _editor.ClearSelection();
            _renderController.RefreshView();
            return;
        }

        if (hit.TextPosition is not DocumentPosition position)
        {
            return;
        }

        _editor.SelectedImageParagraphIndex = null;
        _editor.CaretPosition = position;

        if (!isShift)
        {
            _editor.SelectionAnchor = position;
        }

        _isSelecting = true;
        _renderController.RefreshView();
    }

    public void HandlePointerMoved(int pageIndex, Point point)
    {
        if (!_isSelecting || pageIndex < 0 || pageIndex >= _renderController.CurrentPages.Count)
        {
            return;
        }

        RenderedPage page = _renderController.CurrentPages[pageIndex];
        DocumentPosition? currentPos = _renderController.HitTestText(
            page,
            point);
        if (!currentPos.HasValue)
        {
            return;
        }

        _editor.CaretPosition = currentPos.Value;
        _renderController.RefreshView();
    }

    public void HandlePointerReleased()
    {
        _isSelecting = false;
    }
}
