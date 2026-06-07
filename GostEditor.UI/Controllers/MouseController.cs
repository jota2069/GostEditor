using System;
using Avalonia;
using Avalonia.Media.TextFormatting;
using Avalonia.Media;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Layout;

namespace GostEditor.UI.Controllers;

public class MouseController
{
    private readonly DocumentEditor _editor;
    private readonly RenderController _renderController;

    private bool _isSelecting = false;

    public MouseController(DocumentEditor editor, RenderController renderController)
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

        if (page.Images != null)
        {
            foreach (ImagePlacement img in page.Images)
            {
                if (img.Bounds.Contains(point))
                {
                    _editor.SelectedImageParagraphIndex = img.ParagraphIndex;
                    _editor.ClearSelection();
                    _renderController.RefreshView();
                    return;
                }
            }
        }

        DocumentPosition position = FindPositionAtPoint(page, point);

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
        DocumentPosition currentPos = FindPositionAtPoint(page, point);

        _editor.CaretPosition = currentPos;
        _renderController.RefreshView();
    }

    public void HandlePointerReleased()
    {
        _isSelecting = false;
    }

    private DocumentPosition FindPositionAtPoint(RenderedPage page, Point point)
    {
        if (page.Lines.Count == 0)
        {
            return new DocumentPosition(0, 0);
        }

        TextLinePlacement closestLine = page.Lines[0];
        double minDistance = double.MaxValue;

        foreach (TextLinePlacement linePlacement in page.Lines)
        {
            double lineCenterY = linePlacement.Location.Y + (linePlacement.Line.Height / 2.0);
            double distance = Math.Abs(point.Y - lineCenterY);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestLine = linePlacement;
            }
        }

        double relativeX = point.X - closestLine.Location.X;
        if (relativeX < 0)
        {
            relativeX = 0;
        }

        // ИСПРАВЛЕНИЕ: Актуальный метод Avalonia 11
        CharacterHit hit = closestLine.Line.GetCharacterHitFromDistance(relativeX);

        int offset = hit.FirstCharacterIndex + hit.TrailingLength;
        int finalOffset = Math.Max(0, offset - closestLine.PrefixLength);

        return new DocumentPosition(closestLine.ParagraphIndex, finalOffset);
    }
}
