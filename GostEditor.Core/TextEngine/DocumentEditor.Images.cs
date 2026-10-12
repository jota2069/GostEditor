using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Editing;
using GostEditor.Core.Editing.Operations;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine;

public partial class DocumentEditor
{
    private bool DeleteCanonicalStructuralSelection(
        IReadOnlyList<string>? replacementParagraphs = null)
    {
        if (!HasSelection)
        {
            return false;
        }

        (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();
        IReadOnlyList<BlockNode> blocks = _editingSession.Document
            .EnumerateBlocks()
            .ToList();
        if (start.ParagraphIndex < 0 || end.ParagraphIndex >= blocks.Count ||
            start.ParagraphIndex > end.ParagraphIndex)
        {
            return false;
        }

        DocumentSection? section = _editingSession.Document.FindSectionContaining(
            blocks[start.ParagraphIndex].Id);
        if (section is null ||
            _editingSession.Document.FindSectionContaining(
                blocks[end.ParagraphIndex].Id)?.Id != section.Id)
        {
            return false;
        }

        EditTransaction transaction = new("Удаление структурного диапазона");
        HashSet<DocumentNodeId> removed = new();
        BlockNode startBlock = blocks[start.ParagraphIndex];
        BlockNode endBlock = blocks[end.ParagraphIndex];
        DocumentLocation? resultLocation = null;
        IReadOnlyList<string>? pendingReplacement = replacementParagraphs;

        if (startBlock.Id == endBlock.Id)
        {
            if (startBlock is not FigureBlock singleFigure)
            {
                return false;
            }

            int from = Math.Clamp(start.Offset, 0, singleFigure.Caption.Length);
            int to = Math.Clamp(end.Offset, from, singleFigure.Caption.Length);
            string inserted = pendingReplacement is { Count: 1 }
                ? pendingReplacement[0]
                : string.Empty;
            transaction.Add(new ChangeFigureCaptionOperation(
                singleFigure.Id,
                singleFigure.Caption[..from] + inserted +
                singleFigure.Caption[to..]));
            if (pendingReplacement is { Count: 1 })
            {
                pendingReplacement = null;
            }
        }
        else
        {
            bool retainStart = AddStartBoundaryDeletion(
                transaction,
                startBlock,
                start.Offset,
                pendingReplacement is { Count: 1 }
                    ? pendingReplacement[0]
                    : string.Empty,
                removed,
                out DocumentLocation? startLocation,
                out bool replacementConsumed);
            if (replacementConsumed)
            {
                pendingReplacement = null;
            }
            bool retainEnd = AddEndBoundaryDeletion(
                transaction,
                endBlock,
                end.Offset,
                removed,
                out DocumentLocation? endLocation);

            for (int index = start.ParagraphIndex + 1;
                 index < end.ParagraphIndex;
                 index++)
            {
                transaction.Add(new RemoveBlockOperation(blocks[index].Id));
                removed.Add(blocks[index].Id);
            }

            if (retainStart && retainEnd &&
                startBlock is ParagraphBlock startParagraph &&
                endBlock is ParagraphBlock endParagraph)
            {
                transaction.Add(new MergeParagraphsOperation(
                    startParagraph.Id,
                    endParagraph.Id));
                resultLocation = startLocation;
            }
            else
            {
                resultLocation = startLocation ?? endLocation;
            }
        }

        foreach (IGrouping<Guid, FigureBlock> references in
                 blocks.OfType<FigureBlock>().GroupBy(figure => figure.ImageId))
        {
            if (references.All(figure => removed.Contains(figure.Id)))
            {
                transaction.Add(new RemoveImageResourceOperation(references.Key));
            }
        }

        int remainingBlocks = blocks.Count - removed.Count -
            (startBlock is ParagraphBlock && endBlock is ParagraphBlock &&
             startBlock.Id != endBlock.Id &&
             !removed.Contains(startBlock.Id) && !removed.Contains(endBlock.Id)
                ? 1
                : 0);
        if (remainingBlocks == 0)
        {
            ParagraphBlock empty = new();
            empty.EnsureEditableInline();
            transaction.Add(new InsertBlockOperation(section.Id, 0, empty));
            resultLocation = new DocumentLocation(empty.Id, 0);
        }

        resultLocation ??= FindStructuralReplacementLocation(
            blocks,
            removed,
            start.ParagraphIndex,
            end.ParagraphIndex);
        if (pendingReplacement is { Count: > 0 })
        {
            if (!resultLocation.HasValue)
            {
                throw new InvalidOperationException(
                    "Не найден текстовый блок для замены диапазона.");
            }

            TextStyle style = GetCanonicalTypingStyle();
            DocumentLocation insertion = resultLocation.Value;
            string first = pendingReplacement[0];
            if (first.Length > 0)
            {
                transaction.Add(new InsertTextOperation(
                    insertion.BlockId,
                    insertion.Offset,
                    first,
                    style));
                insertion = new DocumentLocation(
                    insertion.BlockId,
                    insertion.Offset + first.Length);
            }

            for (int index = 1; index < pendingReplacement.Count; index++)
            {
                DocumentNodeId nextParagraphId = DocumentNodeId.New();
                transaction.Add(new SplitParagraphOperation(
                    insertion.BlockId,
                    insertion.Offset,
                    nextParagraphId));
                string paragraphText = pendingReplacement[index];
                if (paragraphText.Length > 0)
                {
                    transaction.Add(new InsertTextOperation(
                        nextParagraphId,
                        0,
                        paragraphText,
                        style));
                }

                insertion = new DocumentLocation(
                    nextParagraphId,
                    paragraphText.Length);
            }

            resultLocation = insertion;
        }

        ExecuteCanonicalStructuralEdit(() =>
            _editingSession.ExecuteWithSelection(
                transaction,
                () => _editingSession.Selection.MoveCaret(
                    resultLocation ?? GetNearestCanonicalTextLocation(
                        new DocumentPosition(
                            Math.Min(start.ParagraphIndex,
                                Math.Max(0, _editingSession.Document
                                    .EnumerateBlocks().Count() - 1)),
                            0)))));
        return true;
    }

    private static DocumentLocation? FindStructuralReplacementLocation(
        IReadOnlyList<BlockNode> blocks,
        ISet<DocumentNodeId> removed,
        int startIndex,
        int endIndex)
    {
        for (int index = startIndex - 1; index >= 0; index--)
        {
            if (blocks[index] is ParagraphBlock paragraph &&
                !removed.Contains(paragraph.Id))
            {
                return new DocumentLocation(paragraph.Id, paragraph.TextLength);
            }
        }

        for (int index = endIndex + 1; index < blocks.Count; index++)
        {
            if (blocks[index] is ParagraphBlock paragraph &&
                !removed.Contains(paragraph.Id))
            {
                return new DocumentLocation(paragraph.Id, 0);
            }
        }

        return null;
    }

    private static bool AddStartBoundaryDeletion(
        EditTransaction transaction,
        BlockNode block,
        int offset,
        string captionReplacement,
        ISet<DocumentNodeId> removed,
        out DocumentLocation? location,
        out bool replacementConsumed)
    {
        location = null;
        replacementConsumed = false;
        if (block is ParagraphBlock paragraph)
        {
            int normalized = Math.Clamp(offset, 0, paragraph.TextLength);
            int length = paragraph.TextLength - normalized;
            if (length > 0)
            {
                transaction.Add(new DeleteTextOperation(
                    paragraph.Id,
                    normalized,
                    length));
            }

            location = new DocumentLocation(paragraph.Id, normalized);
            return true;
        }

        if (block is FigureBlock figure && offset > 0)
        {
            int normalized = Math.Clamp(offset, 0, figure.Caption.Length);
            transaction.Add(new ChangeFigureCaptionOperation(
                figure.Id,
                figure.Caption[..normalized] + captionReplacement));
            replacementConsumed = captionReplacement.Length > 0;
            return true;
        }

        transaction.Add(new RemoveBlockOperation(block.Id));
        removed.Add(block.Id);
        return false;
    }

    private static bool AddEndBoundaryDeletion(
        EditTransaction transaction,
        BlockNode block,
        int offset,
        ISet<DocumentNodeId> removed,
        out DocumentLocation? location)
    {
        location = null;
        if (block is ParagraphBlock paragraph)
        {
            int normalized = Math.Clamp(offset, 0, paragraph.TextLength);
            if (normalized > 0)
            {
                transaction.Add(new DeleteTextOperation(
                    paragraph.Id,
                    0,
                    normalized));
            }

            location = new DocumentLocation(paragraph.Id, 0);
            return true;
        }

        if (block is FigureBlock figure && offset < figure.Caption.Length)
        {
            int normalized = Math.Clamp(offset, 0, figure.Caption.Length);
            transaction.Add(new ChangeFigureCaptionOperation(
                figure.Id,
                figure.Caption[normalized..]));
            return true;
        }

        transaction.Add(new RemoveBlockOperation(block.Id));
        removed.Add(block.Id);
        return false;
    }

    private bool SelectionContainsImage()
    {
        if (!HasSelection)
        {
            return false;
        }

        (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();
        int first = Math.Clamp(start.ParagraphIndex, 0, Document.Paragraphs.Count - 1);
        int last = Math.Clamp(end.ParagraphIndex, first, Document.Paragraphs.Count - 1);
        return Document.Paragraphs
            .Skip(first)
            .Take(last - first + 1)
            .Any(paragraph => paragraph.IsImage);
    }

    private HashSet<Guid> GetReferencedImageIds() => Document.Paragraphs
        .Where(paragraph => paragraph.ImageId.HasValue)
        .Select(paragraph => paragraph.ImageId!.Value)
        .ToHashSet();

    private void CleanupLostImageReferences(
        HashSet<Guid> previouslyReferencedImages)
    {
        HashSet<Guid> current = GetReferencedImageIds();
        Guid[] orphaned = previouslyReferencedImages
            .Where(id => !current.Contains(id) &&
                Document.Images.Any(image => image.Id == id))
            .ToArray();
        if (orphaned.Length > 0)
        {
            ImageService.RemoveOrphans(Document, orphaned);
        }
    }

    public void InsertImage(byte[] imageBytes, double width, double height)
    {
        ImageResult<ImagePlacementInfo> result = InsertImage(
            new CreateImageRequest(imageBytes, new ImageSize(width, height)));
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error?.Message);
        }
    }

    public ImageResult<ImagePlacementInfo> InsertImage(CreateImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ImageResult<(ImageResource Resource, FigureBlock Figure)> prepared =
            PrepareImage(request);
        if (!prepared.IsSuccess)
        {
            return ForwardImageError<
                (ImageResource Resource, FigureBlock Figure),
                ImagePlacementInfo>(prepared);
        }

        (DocumentSection section, int sectionIndex, int legacyIndex) =
            GetInsertionPointAfterCaret();
        (ImageResource resource, FigureBlock figure) = prepared.Value;
        ExecuteCanonicalStructuralEdit(() =>
        {
            EditTransaction transaction = new("Добавление изображения");
            transaction.Add(new AddImageResourceOperation(resource));
            transaction.Add(new InsertBlockOperation(section.Id, sectionIndex, figure));
            _editingSession.Execute(transaction);
        });
        CaretPosition = new DocumentPosition(legacyIndex, 0);
        ClearSelection();
        return ImageResult<ImagePlacementInfo>.Success(new ImagePlacementInfo(
            legacyIndex,
            resource.Id,
            request.Size));
    }

    public ImageResult<ImagePlacementInfo> InsertExistingImage(
        Guid imageId,
        ImagePlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidImageSize(request.Size))
        {
            return InvalidImageSize<ImagePlacementInfo>(request.Size);
        }

        if (!_editingSession.Document.Resources.TryGetImage(imageId, out _))
        {
            return ImageResult<ImagePlacementInfo>.Failure(
                ImageErrorCode.ImageNotFound,
                $"Изображение с ID {imageId} не найдено.",
                imageId);
        }

        (DocumentSection section, int sectionIndex, int legacyIndex) =
            GetInsertionPointAfterCaret();
        FigureBlock figure = new(imageId, request.Size.Width, request.Size.Height)
        {
            Caption = request.Caption ?? string.Empty
        };
        ExecuteCanonicalStructuralEdit(() =>
            _editingSession.Execute(new InsertBlockOperation(
                section.Id,
                sectionIndex,
                figure)));
        CaretPosition = new DocumentPosition(legacyIndex, 0);
        ClearSelection();
        return ImageResult<ImagePlacementInfo>.Success(new ImagePlacementInfo(
            legacyIndex,
            imageId,
            request.Size));
    }

    public ImageResult<ImagePlacementInfo> ReplaceImage(
        int paragraphIndex,
        ReplaceImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ImageResult<FigureBlock> currentResult = GetCanonicalFigure(paragraphIndex);
        if (!currentResult.IsSuccess)
        {
            return ForwardImageError<FigureBlock, ImagePlacementInfo>(currentResult);
        }

        FigureBlock current = currentResult.Value!;
        _editingSession.Document.Resources.TryGetImage(
            current.ImageId,
            out ImageResource? oldResource);
        bool removeOldResource = CountFigureReferences(current.ImageId) == 1;
        string fileName = request.FileName ?? oldResource?.FileName ?? string.Empty;
        ImageResult<(ImageResource Resource, FigureBlock Figure)> prepared =
            PrepareImage(new CreateImageRequest(
                request.Data,
                request.Size,
                fileName,
                current.Caption),
                removeOldResource ? current.ImageId : null);
        if (!prepared.IsSuccess)
        {
            return ForwardImageError<
                (ImageResource Resource, FigureBlock Figure),
                ImagePlacementInfo>(prepared, paragraphIndex);
        }

        (DocumentSection section, int index) =
            _editingSession.Context.GetRequiredBlockLocation(current.Id);
        (ImageResource resource, FigureBlock candidate) = prepared.Value;
        FigureBlock replacement = new(
            current.Id,
            candidate.ImageId,
            candidate.Width,
            candidate.Height)
        {
            Caption = candidate.Caption
        };
        ExecuteCanonicalStructuralEdit(() =>
        {
            EditTransaction transaction = new("Замена изображения");
            transaction.Add(new RemoveBlockOperation(current.Id));
            if (removeOldResource)
            {
                transaction.Add(new RemoveImageResourceOperation(current.ImageId));
            }

            transaction.Add(new AddImageResourceOperation(resource));
            transaction.Add(new InsertBlockOperation(section.Id, index, replacement));
            _editingSession.Execute(transaction);
        });
        return ImageResult<ImagePlacementInfo>.Success(new ImagePlacementInfo(
            paragraphIndex,
            resource.Id,
            request.Size));
    }

    public ImageResult<ImagePlacementInfo> ResizeImage(
        int paragraphIndex,
        ImageSize size)
    {
        if (!IsValidImageSize(size))
        {
            return InvalidImageSize<ImagePlacementInfo>(size, paragraphIndex);
        }

        ImageResult<FigureBlock> result = GetCanonicalFigure(paragraphIndex);
        if (!result.IsSuccess)
        {
            return ForwardImageError<FigureBlock, ImagePlacementInfo>(
                result,
                paragraphIndex);
        }

        FigureBlock figure = result.Value!;
        ExecuteCanonicalStructuralEdit(() =>
            _editingSession.Execute(new ResizeFigureOperation(
                figure.Id,
                size.Width,
                size.Height)));
        return ImageResult<ImagePlacementInfo>.Success(new ImagePlacementInfo(
            paragraphIndex,
            figure.ImageId,
            size));
    }

    public ImageResult<ImageRemovalInfo> RemoveImage(int paragraphIndex)
    {
        ImageResult<FigureBlock> result = GetCanonicalFigure(paragraphIndex);
        if (!result.IsSuccess)
        {
            return ForwardImageError<FigureBlock, ImageRemovalInfo>(
                result,
                paragraphIndex);
        }

        FigureBlock figure = result.Value!;
        bool removeResource = CountFigureReferences(figure.ImageId) == 1;
        ExecuteCanonicalStructuralEdit(() =>
        {
            EditTransaction transaction = new("Удаление изображения");
            transaction.Add(new RemoveBlockOperation(figure.Id));
            if (removeResource)
            {
                transaction.Add(new RemoveImageResourceOperation(figure.ImageId));
            }

            _editingSession.Execute(transaction);
        });
        CaretPosition = new DocumentPosition(
            Math.Clamp(paragraphIndex, 0, Document.Paragraphs.Count - 1),
            0);
        ClearSelection();
        return ImageResult<ImageRemovalInfo>.Success(new ImageRemovalInfo(
            paragraphIndex,
            figure.ImageId,
            removeResource));
    }

    public ImageResult<OrphanCleanupResult> RemoveOrphanImages(
        IReadOnlyCollection<Guid> confirmedImageIds)
    {
        ArgumentNullException.ThrowIfNull(confirmedImageIds);
        HashSet<Guid> referenced = _editingSession.Document.EnumerateBlocks()
            .OfType<FigureBlock>()
            .Select(figure => figure.ImageId)
            .ToHashSet();
        List<Guid> removable = new();
        foreach (Guid id in confirmedImageIds.Distinct())
        {
            if (referenced.Contains(id))
            {
                return ImageResult<OrphanCleanupResult>.Failure(
                    ImageErrorCode.ImageStillReferenced,
                    $"Изображение {id} всё ещё используется.",
                    id);
            }

            if (!_editingSession.Document.Resources.Images.ContainsKey(id) &&
                !Document.Images.Any(image => image.Id == id))
            {
                return ImageResult<OrphanCleanupResult>.Failure(
                    ImageErrorCode.ImageNotFound,
                    $"Изображение с ID {id} не найдено.",
                    id);
            }

            removable.Add(id);
        }

        if (removable.Count > 0)
        {
            ExecuteCanonicalStructuralEdit(() =>
            {
                EditTransaction transaction = new("Удаление неиспользуемых изображений");
                foreach (Guid id in removable)
                {
                    if (_editingSession.Document.Resources.Images.ContainsKey(id))
                    {
                        transaction.Add(new RemoveImageResourceOperation(id));
                        continue;
                    }

                    ImageAttachment legacy = Document.Images.Single(
                        image => image.Id == id);
                    transaction.Add(new RemoveImageResourceOperation(
                        new ImageResource(
                            legacy.Id,
                            legacy.FileName,
                            legacy.MediaType,
                            legacy.Data,
                            legacy.Caption,
                            legacy.Order)));
                }

                _editingSession.Execute(transaction);
            });
        }

        return ImageResult<OrphanCleanupResult>.Success(
            new OrphanCleanupResult(removable));
    }

    public ImageResult<ResolvedImagePlacement> ResolveImage(int paragraphIndex)
    {
        ImageResult<FigureBlock> result = GetCanonicalFigure(paragraphIndex);
        if (!result.IsSuccess)
        {
            return ForwardImageError<FigureBlock, ResolvedImagePlacement>(
                result,
                paragraphIndex);
        }

        FigureBlock figure = result.Value!;
        if (!_editingSession.Document.Resources.TryGetImage(
                figure.ImageId,
                out ImageResource? resource) || resource is null)
        {
            return ImageResult<ResolvedImagePlacement>.Failure(
                ImageErrorCode.ImageNotFound,
                $"Изображение с ID {figure.ImageId} не найдено.",
                figure.ImageId,
                paragraphIndex);
        }

        return ImageResult<ResolvedImagePlacement>.Success(
            new ResolvedImagePlacement(
                paragraphIndex,
                figure.ImageId,
                new ImageSize(figure.Width, figure.Height),
                new ImageContentView(
                    resource.Id,
                    resource.Data,
                    resource.FileName,
                    resource.MediaType)));
    }

    private ImageResult<(ImageResource Resource, FigureBlock Figure)> PrepareImage(
        CreateImageRequest request,
        Guid? replacingResourceId = null)
    {
        if (request.Data.Length > GostArchiveLimits.MaxImageBytes)
        {
            return ImageResult<(ImageResource, FigureBlock)>.Failure(
                ImageErrorCode.InvalidDocumentState,
                $"Размер изображения превышает {GostArchiveLimits.MaxImageBytes} байт.");
        }

        int retainedResourceCount = _editingSession.Document.Resources.Images.Count -
            (replacingResourceId.HasValue ? 1 : 0);
        if (retainedResourceCount >=
            GostArchiveLimits.MaxImages)
        {
            return ImageResult<(ImageResource, FigureBlock)>.Failure(
                ImageErrorCode.InvalidDocumentState,
                $"Документ не может содержать более {GostArchiveLimits.MaxImages} изображений.");
        }

        long currentBytes = _editingSession.Document.Resources.ImagesInOrder
            .Where(resource => resource.Id != replacingResourceId)
            .Sum(resource => (long)resource.Data.Length);
        if (request.Data.Length >
            GostArchiveLimits.MaxTotalImageBytes - currentBytes)
        {
            return ImageResult<(ImageResource, FigureBlock)>.Failure(
                ImageErrorCode.InvalidDocumentState,
                $"Суммарный размер изображений превышает " +
                $"{GostArchiveLimits.MaxTotalImageBytes} байт.");
        }

        try
        {
            GostImageInputValidator.ValidateDecodableImage(
                request.Data,
                "Добавляемое изображение");
        }
        catch (InvalidDataException exception)
        {
            return ImageResult<(ImageResource, FigureBlock)>.Failure(
                ImageErrorCode.UnsupportedOrInvalidImage,
                exception.Message);
        }

        GostDocument scratch = new();
        ImageResult<ImagePlacementInfo> validated = ImageService.InsertPlacement(
            scratch,
            0,
            request);
        if (!validated.IsSuccess)
        {
            ImageError error = validated.Error!;
            return ImageResult<(ImageResource, FigureBlock)>.Failure(
                error.Code,
                error.Message,
                error.ImageId,
                error.ParagraphIndex);
        }

        ImageAttachment attachment = scratch.Images.Single();
        ImageResource resource = new(
            attachment.Id,
            attachment.FileName,
            attachment.MediaType,
            attachment.Data,
            attachment.Caption,
            attachment.Order);
        FigureBlock figure = new(
            attachment.Id,
            request.Size.Width,
            request.Size.Height)
        {
            Caption = request.Caption ?? string.Empty
        };
        return ImageResult<(ImageResource, FigureBlock)>.Success((resource, figure));
    }

    private (DocumentSection Section, int SectionIndex, int LegacyIndex)
        GetInsertionPointAfterCaret()
    {
        IReadOnlyList<BlockNode> blocks = _editingSession.Document
            .EnumerateBlocks()
            .ToList();
        int legacyIndex = Math.Clamp(CaretPosition.ParagraphIndex + 1, 0, blocks.Count);
        BlockNode current = blocks[Math.Clamp(
            CaretPosition.ParagraphIndex,
            0,
            blocks.Count - 1)];
        DocumentSection section = _editingSession.Document.FindSectionContaining(current.Id)
            ?? throw new InvalidOperationException("Секция caret не найдена.");
        int sectionIndex = section.Blocks.FindIndex(block => block.Id == current.Id) + 1;
        return (section, sectionIndex, legacyIndex);
    }

    private ImageResult<FigureBlock> GetCanonicalFigure(int paragraphIndex)
    {
        IReadOnlyList<BlockNode> blocks = _editingSession.Document
            .EnumerateBlocks()
            .ToList();
        if (paragraphIndex < 0 || paragraphIndex >= blocks.Count)
        {
            return ImageResult<FigureBlock>.Failure(
                ImageErrorCode.ParagraphIndexOutOfRange,
                "Индекс абзаца находится вне документа.",
                paragraphIndex: paragraphIndex);
        }

        if (blocks[paragraphIndex] is not FigureBlock figure)
        {
            return ImageResult<FigureBlock>.Failure(
                ImageErrorCode.ParagraphIsNotImage,
                "Абзац не содержит изображения.",
                paragraphIndex: paragraphIndex);
        }

        return ImageResult<FigureBlock>.Success(figure);
    }

    private int CountFigureReferences(Guid imageId) =>
        _editingSession.Document.EnumerateBlocks()
            .OfType<FigureBlock>()
            .Count(figure => figure.ImageId == imageId);

    private static bool IsValidImageSize(ImageSize size) =>
        double.IsFinite(size.Width) && double.IsFinite(size.Height) &&
        size.Width > 0 && size.Height > 0;

    private static ImageResult<T> InvalidImageSize<T>(
        ImageSize size,
        int? paragraphIndex = null) =>
        ImageResult<T>.Failure(
            ImageErrorCode.InvalidDimensions,
            $"Недопустимый размер изображения: {size.Width} × {size.Height}.",
            paragraphIndex: paragraphIndex);

    private static ImageResult<TTarget> ForwardImageError<TSource, TTarget>(
        ImageResult<TSource> source,
        int? paragraphIndex = null)
    {
        ImageError error = source.Error
            ?? throw new InvalidOperationException("Ожидалась ошибка изображения.");
        return ImageResult<TTarget>.Failure(
            error.Code,
            error.Message,
            error.ImageId,
            paragraphIndex ?? error.ParagraphIndex);
    }
}
