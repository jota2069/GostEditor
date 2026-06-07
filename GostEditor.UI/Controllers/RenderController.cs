using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Layout;
using GostEditor.UI.Controls;

namespace GostEditor.UI.Controllers;

/// <summary>
/// Аргументы события изменения стиля текста под курсором.
/// </summary>
public class CaretStyleChangedEventArgs : EventArgs
{
    public required bool IsBold { get; init; }
    public required bool IsItalic { get; init; }
    public required double FontSize { get; init; }
    public required GostAlignment Alignment { get; init; }
}

/// <summary>
/// Контроллер, управляющий процессом отрисовки документа.
/// </summary>
public class RenderController
{
    private readonly DocumentEditor _editor;
    private readonly PageLayoutManager _layoutManager;
    private readonly Typeface _defaultTypeface;
    private StackPanel? _pagesPanel;

    public List<RenderedPage> CurrentPages { get; private set; } = new List<RenderedPage>();

    public event EventHandler<CaretStyleChangedEventArgs>? CaretStyleChanged;

    public RenderController(DocumentEditor editor, PageLayoutManager layoutManager, Typeface defaultTypeface)
    {
        _editor = editor;
        _layoutManager = layoutManager;
        _defaultTypeface = defaultTypeface;
    }

    public void AttachUi(StackPanel pagesPanel)
    {
        _pagesPanel = pagesPanel;
        RefreshView();
    }

    public void RefreshView()
    {
        if (_pagesPanel == null)
        {
            return;
        }

        // Строим layout документа
        CurrentPages = _layoutManager.BuildLayout(_editor.Document, _editor, _defaultTypeface);

        // Синхронизируем UI
        SyncPageControls();

        // Скроллим к каретке (отложенный вызов)
        Dispatcher.UIThread.Post(ScrollToCaret, DispatcherPriority.Normal);

        // Уведомляем об изменении стиля
        NotifyCaretStyle();
    }

    private void SyncPageControls()
    {
        if (_pagesPanel == null)
        {
            return;
        }

        // Добавляем недостающие страницы
        while (_pagesPanel.Children.Count < CurrentPages.Count)
        {
            GostPageControl pageControl = new GostPageControl();
            _pagesPanel.Children.Add(pageControl);
        }

        // Удаляем лишние страницы
        while (_pagesPanel.Children.Count > CurrentPages.Count)
        {
            _pagesPanel.Children.RemoveAt(_pagesPanel.Children.Count - 1);
        }

        // Обновляем данные каждой страницы
        for (int i = 0; i < CurrentPages.Count; i++)
        {
            if (_pagesPanel.Children[i] is GostPageControl pageControl)
            {
                pageControl.SetPageData(
                    CurrentPages[i],
                    _editor.Document.PageWidth,
                    _editor.Document.PageHeight,
                    1, // startPageNumber (пока захардкожено)
                    _editor.SelectedImageParagraphIndex);
            }
        }
    }

    public void ScrollToCaret()
    {
        if (CurrentPages.Count == 0 || _pagesPanel == null)
        {
            return;
        }

        // Находим страницу с кареткой
        for (int i = 0; i < CurrentPages.Count; i++)
        {
            Rect? caretRect = CurrentPages[i].CaretBounds;

            if (caretRect.HasValue)
            {
                Control pageControl = _pagesPanel.Children[i];
                double safeY = caretRect.Value.Y - 50;

                if (safeY < 0)
                {
                    safeY = 0;
                }

                Rect viewRect = new Rect(
                    caretRect.Value.X,
                    safeY,
                    caretRect.Value.Width,
                    caretRect.Value.Height + 100);

                pageControl.BringIntoView(viewRect);
                break;
            }
        }
    }

    private void NotifyCaretStyle()
    {
        if (_editor.CaretPosition.ParagraphIndex >= _editor.Document.Paragraphs.Count)
        {
            return;
        }

        Paragraph paragraph = _editor.Document.Paragraphs[_editor.CaretPosition.ParagraphIndex];
        GostAlignment alignment = paragraph.Alignment;

        bool isBold = false;
        bool isItalic = false;
        double fontSize = 14;

        // Поиск текущего TextRun под кареткой
        int currentOffset = 0;
        TextRun? targetRun = null;

        if (paragraph.Runs.Count > 0)
        {
            targetRun = paragraph.Runs[0];

            foreach (TextRun run in paragraph.Runs)
            {
                if (_editor.CaretPosition.Offset > currentOffset &&
                    _editor.CaretPosition.Offset <= currentOffset + run.Text.Length)
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
}
