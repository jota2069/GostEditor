using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;
using GostDocument = GostEditor.Core.Models.GostDocument;
using DomTextRun = GostEditor.Core.TextEngine.DOM.TextRun;

namespace GostEditor.UI.Layout;

public class PageLayoutManager
{
    private readonly IImageService _imageService;
    private readonly Dictionary<int, CachedParagraphLayout>
        _paragraphLayoutCache = new();
    private string? _globalLayoutKey;

    public PageLayoutManager(IImageService imageService)
    {
        _imageService = imageService
            ?? throw new ArgumentNullException(nameof(imageService));
    }

    public LayoutBuildStatistics LastStatistics { get; private set; } =
        LayoutBuildStatistics.Empty;

    public int CachedParagraphCount => _paragraphLayoutCache.Count;

    public void InvalidateFrom(
        int startParagraphIndex,
        DocumentChangeKind kind)
    {
        if (!kind.HasFlag(DocumentChangeKind.Metrics) &&
            !kind.HasFlag(DocumentChangeKind.Structure))
        {
            return;
        }

        int start = Math.Max(0, startParagraphIndex);
        foreach (int index in _paragraphLayoutCache.Keys
                     .Where(index => index >= start)
                     .ToArray())
        {
            _paragraphLayoutCache.Remove(index);
        }
    }

    public void ResetCache()
    {
        _paragraphLayoutCache.Clear();
        _globalLayoutKey = null;
        LastStatistics = LayoutBuildStatistics.Empty;
    }

    public List<RenderedPage> BuildLayout(
        GostDocument document,
        DocumentEditor editor,
        Typeface typeface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(editor);
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch stopwatch = Stopwatch.StartNew();
        string globalLayoutKey = CreateGlobalLayoutKey(
            document,
            typeface);
        if (!string.Equals(
                _globalLayoutKey,
                globalLayoutKey,
                StringComparison.Ordinal))
        {
            _paragraphLayoutCache.Clear();
            _globalLayoutKey = globalLayoutKey;
        }

        List<RenderedPage> pages = new();
        RenderedPage currentPage = new() { PageNumber = 1 };

        double currentY = document.MarginTop;
        double maxBottom = document.PageHeight - document.MarginBottom;
        double contentWidth = document.ContentWidth;

        (DocumentPosition selStart, DocumentPosition selEnd) =
            editor.GetNormalizedSelection();

        int figureCounter = 1;
        int cacheHits = 0;
        int cacheMisses = 0;

        for (int pIndex = 0;
             pIndex < editor.Document.Paragraphs.Count;
             pIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paragraph paragraph = editor.Document.Paragraphs[pIndex];
            int paragraphTextLength = paragraph.GetPlainText().Length;

            if (paragraph.PageBreakBefore &&
                (currentPage.Lines.Count > 0 ||
                 currentPage.Images.Count > 0))
            {
                pages.Add(currentPage);
                currentPage = new RenderedPage
                {
                    PageNumber = pages.Count + 1
                };
                currentY = document.MarginTop;
            }

            CachedParagraphLayout cached = GetOrCreateParagraphLayout(
                paragraph,
                pIndex,
                figureCounter,
                typeface,
                contentWidth,
                out bool cacheHit);
            if (cacheHit)
            {
                cacheHits++;
            }
            else
            {
                cacheMisses++;
            }

            TextLayout layout = cached.Layout;
            int prefixCharsCount = cached.PrefixCharsCount;

            if (paragraph.IsImage)
            {
                figureCounter++;
            }

            if (paragraph.ImageId is Guid imageId)
            {
                ImageResult<ResolvedImagePlacement> resolved =
                    _imageService.ResolvePlacement(document, pIndex);
                bool hasContent = resolved.IsSuccess;
                ReadOnlyMemory<byte> imageBytes =
                    ReadOnlyMemory<byte>.Empty;
                ImageSize imageSize = GetSafeImageSize(paragraph);

                if (resolved.IsSuccess)
                {
                    ResolvedImagePlacement placement = resolved.Value!;
                    imageBytes = placement.Content.Data;
                    imageSize = placement.Size;
                }
                else
                {
                    ImageResult<ImageContentView> content =
                        _imageService.ResolveContent(document, imageId);
                    if (content.IsSuccess)
                    {
                        imageBytes = content.Value!.Data;
                        hasContent = true;
                    }
                }

                double imageWidth = imageSize.Width;
                double imageHeight = imageSize.Height;

                if (imageWidth > contentWidth)
                {
                    double scale = contentWidth / imageWidth;
                    imageWidth = contentWidth;
                    imageHeight *= scale;
                }

                if (currentY + imageHeight > maxBottom)
                {
                    pages.Add(currentPage);
                    currentPage = new RenderedPage
                    {
                        PageNumber = pages.Count + 1
                    };
                    currentY = document.MarginTop;
                }

                double imageX =
                    document.MarginLeft +
                    (contentWidth - imageWidth) / 2;

                currentPage.Images.Add(
                    new ImagePlacement(
                        imageId,
                        imageBytes,
                        hasContent,
                        new Rect(
                            imageX,
                            currentY,
                            imageWidth,
                            imageHeight),
                        pIndex));

                currentY += imageHeight + 10;
            }

            List<Rect> selectionRects = new();
            if (editor.HasSelection &&
                pIndex >= selStart.ParagraphIndex &&
                pIndex <= selEnd.ParagraphIndex)
            {
                int start = pIndex == selStart.ParagraphIndex
                    ? selStart.Offset
                    : 0;
                int end = pIndex == selEnd.ParagraphIndex
                    ? selEnd.Offset
                    : paragraph.GetPlainText().Length;

                if (end > start)
                {
                    int adjustedStart = start + prefixCharsCount;
                    int adjustedLength = end - start;
                    selectionRects = layout
                        .HitTestTextRange(
                            adjustedStart,
                            adjustedLength)
                        .ToList();
                }
            }

            double textLayoutInternalY = 0;

            foreach (TextLine textLine in layout.TextLines)
            {
                if (currentY + textLine.Height > maxBottom)
                {
                    pages.Add(currentPage);
                    currentPage = new RenderedPage
                    {
                        PageNumber = pages.Count + 1
                    };
                    currentY = document.MarginTop;
                }

                Point location = new(
                    document.MarginLeft,
                    currentY);

                currentPage.Lines.Add(
                    new TextLinePlacement(
                        textLine,
                        location,
                        pIndex,
                        textLayoutInternalY,
                        layout,
                        prefixCharsCount,
                        paragraphTextLength));

                foreach (Rect rectangle in selectionRects)
                {
                    if (rectangle.Y >= textLayoutInternalY - 0.1 &&
                        rectangle.Y <
                        textLayoutInternalY + textLine.Height - 0.1)
                    {
                        double selectionX =
                            document.MarginLeft + rectangle.X;
                        currentPage.SelectionBounds.Add(
                            new Rect(
                                selectionX,
                                currentY,
                                rectangle.Width,
                                rectangle.Height));
                    }
                }

                if (pIndex == editor.CaretPosition.ParagraphIndex)
                {
                    int adjustedCaretOffset =
                        editor.CaretPosition.Offset + prefixCharsCount;
                    Rect characterRect =
                        layout.HitTestTextPosition(
                            adjustedCaretOffset);

                    if (characterRect.Y >=
                            textLayoutInternalY - 0.1 &&
                        characterRect.Y <
                            textLayoutInternalY +
                            textLine.Height - 0.1)
                    {
                        double caretX =
                            document.MarginLeft + characterRect.X;
                        currentPage.CaretBounds = new Rect(
                            caretX,
                            currentY,
                            1.5,
                            textLine.Height);
                    }
                }

                textLayoutInternalY += textLine.Height;
                currentY += textLine.Height * paragraph.LineSpacing;
            }
        }

        if (currentPage.Lines.Count > 0 ||
            pages.Count == 0 ||
            currentPage.Images.Count > 0)
        {
            pages.Add(currentPage);
        }

        RetainLiveParagraphEntries(
            editor.Document.Paragraphs.Count);

        stopwatch.Stop();
        LastStatistics = new LayoutBuildStatistics(
            editor.Document.Paragraphs.Count,
            cacheHits,
            cacheMisses,
            pages.Count,
            stopwatch.Elapsed);

        return pages;
    }

    private CachedParagraphLayout GetOrCreateParagraphLayout(
        Paragraph paragraph,
        int paragraphIndex,
        int figureNumber,
        Typeface typeface,
        double contentWidth,
        out bool cacheHit)
    {
        int signature = ComputeParagraphSignature(
            paragraph,
            figureNumber,
            typeface,
            contentWidth);

        if (_paragraphLayoutCache.TryGetValue(
                paragraphIndex,
                out CachedParagraphLayout? cached) &&
            cached.Signature == signature)
        {
            cacheHit = true;
            return cached;
        }

        cacheHit = false;
        CachedParagraphLayout created = CreateParagraphLayout(
            paragraph,
            figureNumber,
            typeface,
            contentWidth,
            signature);
        _paragraphLayoutCache[paragraphIndex] = created;
        return created;
    }

    private static CachedParagraphLayout CreateParagraphLayout(
        Paragraph paragraph,
        int figureNumber,
        Typeface typeface,
        double contentWidth,
        int signature)
    {
        string plainText = paragraph.GetPlainText();
        bool hasImageCaption = paragraph.IsImage &&
                               !string.IsNullOrWhiteSpace(plainText);
        if (string.IsNullOrEmpty(plainText))
        {
            plainText = "\u200B";
        }

        double baseFontSize = 18.67;
        FontWeight baseWeight = FontWeight.Normal;
        FontFamily baseFontFamily = typeface.FontFamily;

        if (paragraph.Style == ParagraphStyle.Heading1)
        {
            baseFontSize = 21.33;
            baseWeight = FontWeight.Bold;
        }
        else if (paragraph.Style == ParagraphStyle.Heading2)
        {
            baseFontSize = 18.67;
            baseWeight = FontWeight.Normal;
        }
        else if (paragraph.Style == ParagraphStyle.Code)
        {
            baseFontSize = 16.0;
            baseFontFamily = new FontFamily("Consolas");
        }

        Typeface baseTypeface = new(
            baseFontFamily,
            FontStyle.Normal,
            baseWeight);

        int prefixCharsCount = 0;
        string prefixString = string.Empty;
        IBrush prefixBrush = Brushes.Black;

        if (paragraph.IsImage)
        {
            prefixString = hasImageCaption
                ? $"Рисунок {figureNumber} - "
                : $"Рисунок {figureNumber}";
            prefixCharsCount = prefixString.Length;
        }
        else if (paragraph.Alignment != GostAlignment.Center &&
                 paragraph.Alignment != GostAlignment.Right &&
                 paragraph.FirstLineIndent > 0)
        {
            prefixString = "\u2003\u2002";
            prefixCharsCount = prefixString.Length;
            prefixBrush = Brushes.Transparent;
        }

        if (prefixCharsCount > 0)
        {
            plainText = prefixString + plainText;
        }

        List<Avalonia.Utilities.ValueSpan<TextRunProperties>>
            styleOverrides = new();

        if (prefixCharsCount > 0)
        {
            TextRunProperties prefixProperties =
                new GenericTextRunProperties(
                    baseTypeface,
                    baseFontSize,
                    null,
                    prefixBrush);
            styleOverrides.Add(
                new Avalonia.Utilities.ValueSpan<TextRunProperties>(
                    0,
                    prefixCharsCount,
                    prefixProperties));
        }

        int currentPosition = prefixCharsCount;
        foreach (DomTextRun run in paragraph.Runs)
        {
            if (string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            double runFontSize =
                run.FontSize * 1.3333333333333333;
            FontWeight effectiveWeight =
                run.IsBold || baseWeight == FontWeight.Bold
                    ? FontWeight.Bold
                    : FontWeight.Normal;
            FontStyle effectiveStyle = run.IsItalic
                ? FontStyle.Italic
                : FontStyle.Normal;

            if (effectiveWeight != baseWeight ||
                effectiveStyle != FontStyle.Normal ||
                Math.Abs(runFontSize - baseFontSize) > 0.1)
            {
                Typeface runTypeface = new(
                    baseFontFamily,
                    effectiveStyle,
                    effectiveWeight);
                TextRunProperties properties =
                    new GenericTextRunProperties(
                        runTypeface,
                        runFontSize,
                        null,
                        Brushes.Black);

                styleOverrides.Add(
                    new Avalonia.Utilities.ValueSpan<TextRunProperties>(
                        currentPosition,
                        run.Text.Length,
                        properties));
            }

            currentPosition += run.Text.Length;
        }

        TextAlignment alignment = paragraph.Alignment switch
        {
            GostAlignment.Center => TextAlignment.Center,
            GostAlignment.Right => TextAlignment.Right,
            GostAlignment.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left
        };

        TextLayout layout = new(
            plainText,
            baseTypeface,
            baseFontSize,
            Brushes.Black,
            alignment,
            TextWrapping.Wrap,
            maxWidth: contentWidth,
            textStyleOverrides: styleOverrides);

        return new CachedParagraphLayout(
            signature,
            prefixCharsCount,
            layout);
    }

    private static int ComputeParagraphSignature(
        Paragraph paragraph,
        int figureNumber,
        Typeface typeface,
        double contentWidth)
    {
        HashCode hash = new();
        hash.Add(contentWidth);
        hash.Add(typeface.FontFamily.ToString(), StringComparer.Ordinal);
        hash.Add(paragraph.Alignment);
        hash.Add(paragraph.FirstLineIndent);
        hash.Add(paragraph.LineSpacing);
        hash.Add(paragraph.Style);
        hash.Add(paragraph.PageBreakBefore);
        hash.Add(paragraph.ImageId);
        hash.Add(paragraph.ImageWidth);
        hash.Add(paragraph.ImageHeight);
        if (paragraph.IsImage)
        {
            hash.Add(figureNumber);
        }

        foreach (DomTextRun run in paragraph.Runs)
        {
            hash.Add(run.Text, StringComparer.Ordinal);
            hash.Add(run.IsBold);
            hash.Add(run.IsItalic);
            hash.Add(run.FontSize);
            hash.Add(run.Color);
        }

        return hash.ToHashCode();
    }

    private static string CreateGlobalLayoutKey(
        GostDocument document,
        Typeface typeface) =>
        string.Join(
            "|",
            document.PageWidth,
            document.PageHeight,
            document.MarginLeft,
            document.MarginRight,
            document.MarginTop,
            document.MarginBottom,
            typeface.FontFamily.ToString());

    private void RetainLiveParagraphEntries(int paragraphCount)
    {
        foreach (int index in _paragraphLayoutCache.Keys
                     .Where(index => index >= paragraphCount)
                     .ToArray())
        {
            _paragraphLayoutCache.Remove(index);
        }
    }

    private static ImageSize GetSafeImageSize(Paragraph paragraph)
    {
        if (double.IsFinite(paragraph.ImageWidth) &&
            double.IsFinite(paragraph.ImageHeight) &&
            paragraph.ImageWidth > 0 &&
            paragraph.ImageHeight > 0)
        {
            return new ImageSize(
                paragraph.ImageWidth,
                paragraph.ImageHeight);
        }

        return new ImageSize(450, 300);
    }

    public DocumentHitResult? GetPositionFromPoint(
        RenderedPage page,
        Point clickPoint)
    {
        ArgumentNullException.ThrowIfNull(page);

        foreach (ImagePlacement image in page.Images)
        {
            if (image.Bounds.Contains(clickPoint))
            {
                return new DocumentHitResult(image.ParagraphIndex);
            }
        }

        DocumentPosition? textPosition = GetTextPositionFromPoint(
            page,
            clickPoint);
        return textPosition.HasValue
            ? new DocumentHitResult(textPosition.Value)
            : null;
    }

    internal DocumentPosition? GetTextPositionFromPoint(
        RenderedPage page,
        Point clickPoint)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.Lines.Count == 0)
        {
            return null;
        }

        TextLinePlacement targetLine = page.Lines.First();
        double minimumDistance = double.MaxValue;

        foreach (TextLinePlacement line in page.Lines)
        {
            double lineTop = line.Location.Y;
            double lineBottom =
                line.Location.Y + line.Line.Height;

            if (clickPoint.Y >= lineTop &&
                clickPoint.Y < lineBottom)
            {
                targetLine = line;
                break;
            }

            double lineCenter = lineTop + line.Line.Height / 2;
            double distance = Math.Abs(clickPoint.Y - lineCenter);
            if (distance < minimumDistance)
            {
                minimumDistance = distance;
                targetLine = line;
            }
        }

        double layoutX = clickPoint.X - targetLine.Location.X;
        layoutX = Math.Clamp(
            layoutX,
            0,
            targetLine.ParentLayout.MaxWidth);

        double layoutY =
            targetLine.InternalY + targetLine.Line.Height / 2;
        TextHitTestResult hitTest =
            targetLine.ParentLayout.HitTestPoint(
                new Point(layoutX, layoutY));

        int clickedOffset = Math.Clamp(
            hitTest.TextPosition - targetLine.PrefixLength,
            0,
            targetLine.ParagraphTextLength);

        return new DocumentPosition(
            targetLine.ParagraphIndex,
            clickedOffset);
    }

    private sealed record CachedParagraphLayout(
        int Signature,
        int PrefixCharsCount,
        TextLayout Layout);
}
