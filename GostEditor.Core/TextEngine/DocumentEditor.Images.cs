using System;
using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.Commands;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine;

public partial class DocumentEditor
{
    private bool SelectionContainsImage()
    {
        if (!HasSelection)
        {
            return false;
        }

        (DocumentPosition start, DocumentPosition end) =
            GetNormalizedSelection();

        int first = Math.Clamp(
            start.ParagraphIndex,
            0,
            Document.Paragraphs.Count - 1);
        int last = Math.Clamp(
            end.ParagraphIndex,
            first,
            Document.Paragraphs.Count - 1);

        for (int index = first; index <= last; index++)
        {
            if (Document.Paragraphs[index].IsImage)
            {
                return true;
            }
        }

        return false;
    }

    private HashSet<Guid> GetReferencedImageIds()
    {
        HashSet<Guid> ids = new HashSet<Guid>();
        foreach (Paragraph paragraph in Document.Paragraphs)
        {
            if (paragraph.ImageId.HasValue)
            {
                ids.Add(paragraph.ImageId.Value);
            }
        }

        return ids;
    }

    private void CleanupLostImageReferences(HashSet<Guid> previouslyReferencedImages)
    {
        HashSet<Guid> currentlyReferencedImages = GetReferencedImageIds();
        foreach (Guid imageId in previouslyReferencedImages)
        {
            if (currentlyReferencedImages.Contains(imageId)
                || !Document.Images.Any(attachment => attachment.Id == imageId))
            {
                continue;
            }

            ImageService.RemoveOrphans(Document, new[] { imageId });
        }
    }

    private ImageResult<T> ExecuteImageCommand<T>(
        Func<ImageResult<T>> action,
        Func<DocumentMutationRange>? beforeRangeProvider = null,
        Func<DocumentMutationRange>? afterRangeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_isExecutingCommand)
        {
            return action();
        }

        _isExecutingCommand = true;
        try
        {
            ImageResult<T> result = default;
            DocumentMutationCommand command = new(
                this,
                beforeRangeProvider ?? (() => new DocumentMutationRange(
                    0,
                    Document.Paragraphs.Count)),
                afterRangeProvider ?? (() => new DocumentMutationRange(
                    0,
                    Document.Paragraphs.Count)),
                () => result = action(),
                DocumentChangeKind.Structure |
                DocumentChangeKind.Metrics |
                DocumentChangeKind.Paint |
                DocumentChangeKind.Resources,
                captureImages: true,
                shouldCommit: () => result.IsSuccess);
            History.TryExecuteCommand(
                command,
                () => result.IsSuccess);
            return result;
        }
        finally
        {
            _isExecutingCommand = false;
        }
    }

    public void InsertImage(byte[] imageBytes, double width, double height)
    {
        ImageResult<ImagePlacementInfo> result = InsertImage(
            new CreateImageRequest(
                imageBytes,
                new ImageSize(width, height)));

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error?.Message);
        }
    }

    public ImageResult<ImagePlacementInfo> InsertImage(CreateImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        int insertionIndex = Math.Min(
            CaretPosition.ParagraphIndex + 1,
            Document.Paragraphs.Count);

        return ExecuteImageCommand(() =>
        {
            ImageResult<ImagePlacementInfo> result = ImageService.InsertPlacement(
                Document,
                insertionIndex,
                request);
            if (result.IsSuccess)
            {
                CaretPosition = new DocumentPosition(insertionIndex, 0);
                ClearSelection();
            }

            return result;
        },
        () => DocumentMutationRange.EmptyAt(insertionIndex),
        () => new DocumentMutationRange(
            insertionIndex,
            Document.Paragraphs.Count > insertionIndex ? 1 : 0));
    }

    public ImageResult<ImagePlacementInfo> InsertExistingImage(
        Guid imageId,
        ImagePlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        int insertionIndex = Math.Min(
            CaretPosition.ParagraphIndex + 1,
            Document.Paragraphs.Count);

        return ExecuteImageCommand(() =>
        {
            ImageResult<ImagePlacementInfo> result = ImageService.InsertExistingPlacement(
                Document,
                insertionIndex,
                imageId,
                request);
            if (result.IsSuccess)
            {
                CaretPosition = new DocumentPosition(insertionIndex, 0);
                ClearSelection();
            }

            return result;
        },
        () => DocumentMutationRange.EmptyAt(insertionIndex),
        () => new DocumentMutationRange(
            insertionIndex,
            Document.Paragraphs.Count > insertionIndex ? 1 : 0));
    }

    public ImageResult<ImagePlacementInfo> ReplaceImage(
        int paragraphIndex,
        ReplaceImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteImageCommand(
            () => ImageService.ReplacePlacementContent(
                Document,
                paragraphIndex,
                request),
            () => new DocumentMutationRange(paragraphIndex, 1),
            () => new DocumentMutationRange(paragraphIndex, 1));
    }

    public ImageResult<ImagePlacementInfo> ResizeImage(
        int paragraphIndex,
        ImageSize size)
    {
        return ExecuteImageCommand(
            () => ImageService.ResizePlacement(
                Document,
                paragraphIndex,
                size),
            () => new DocumentMutationRange(paragraphIndex, 1),
            () => new DocumentMutationRange(paragraphIndex, 1));
    }

    public ImageResult<ImageRemovalInfo> RemoveImage(int paragraphIndex)
    {
        int originalParagraphCount = Document.Paragraphs.Count;

        return ExecuteImageCommand(() =>
        {
            ImageResult<ImageRemovalInfo> result = ImageService.RemovePlacement(
                Document,
                paragraphIndex);
            if (result.IsSuccess)
            {
                int caretParagraph = Math.Clamp(
                    paragraphIndex,
                    0,
                    Document.Paragraphs.Count - 1);
                CaretPosition = new DocumentPosition(caretParagraph, 0);
                ClearSelection();
            }

            return result;
        },
        () => new DocumentMutationRange(paragraphIndex, 1),
        () => new DocumentMutationRange(
            Math.Clamp(
                paragraphIndex,
                0,
                Document.Paragraphs.Count),
            Math.Max(
                0,
                Document.Paragraphs.Count -
                (originalParagraphCount - 1))));
    }

    public ImageResult<OrphanCleanupResult> RemoveOrphanImages(
        IReadOnlyCollection<Guid> confirmedImageIds)
    {
        ArgumentNullException.ThrowIfNull(confirmedImageIds);
        return ExecuteImageCommand(
            () => ImageService.RemoveOrphans(
                Document,
                confirmedImageIds),
            () => DocumentMutationRange.EmptyAt(0),
            () => DocumentMutationRange.EmptyAt(0));
    }

    public ImageResult<ResolvedImagePlacement> ResolveImage(int paragraphIndex)
    {
        return ImageService.ResolvePlacement(Document, paragraphIndex);
    }
}
