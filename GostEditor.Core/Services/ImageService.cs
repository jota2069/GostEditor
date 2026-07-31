using System;
using System.Collections.Generic;
using System.IO;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.Services;

public sealed class ImageService : IImageService
{
    private readonly IImageContentProbe _contentProbe;

    public ImageService()
        : this(new ImageContentProbe())
    {
    }

    internal ImageService(IImageContentProbe contentProbe)
    {
        _contentProbe = contentProbe ?? throw new ArgumentNullException(nameof(contentProbe));
    }

    public ImageResult<ImagePlacementInfo> InsertPlacement(
        GostDocument document,
        int insertionIndex,
        CreateImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        if (insertionIndex < 0 || insertionIndex > document.Paragraphs.Count)
        {
            return ImageResult<ImagePlacementInfo>.Failure(
                ImageErrorCode.ParagraphIndexOutOfRange,
                "Индекс вставки изображения находится вне документа.",
                paragraphIndex: insertionIndex);
        }

        ImageResult<ImageContentMetadata> inspected = ValidateNewContent(request.Data, request.Size);
        if (!inspected.IsSuccess)
        {
            return ForwardError<ImageContentMetadata, ImagePlacementInfo>(inspected);
        }

        ImageResult<bool> documentState = ValidateAttachmentIds(document);
        if (!documentState.IsSuccess)
        {
            return ForwardError<bool, ImagePlacementInfo>(documentState);
        }

        Guid imageId = CreateUniqueId(document);
        ImageAttachment attachment = new ImageAttachment(
            imageId,
            NormalizeFileName(request.FileName),
            inspected.Value!.MediaType,
            request.Data);
        Paragraph paragraph = CreateImageParagraph(
            imageId,
            request.Size,
            request.Caption);

        document.MutableImages.Add(attachment);
        document.Paragraphs.Insert(insertionIndex, paragraph);
        UpdateImageCount(document);

        return ImageResult<ImagePlacementInfo>.Success(
            new ImagePlacementInfo(insertionIndex, imageId, request.Size));
    }

    public ImageResult<ImagePlacementInfo> InsertExistingPlacement(
        GostDocument document,
        int insertionIndex,
        Guid imageId,
        ImagePlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        if (insertionIndex < 0 || insertionIndex > document.Paragraphs.Count)
        {
            return ImageResult<ImagePlacementInfo>.Failure(
                ImageErrorCode.ParagraphIndexOutOfRange,
                "Индекс вставки изображения находится вне документа.",
                imageId,
                insertionIndex);
        }

        if (!IsValidSize(request.Size))
        {
            return InvalidSize<ImagePlacementInfo>(request.Size, insertionIndex);
        }

        ImageResult<ImageContentView> resolved = ResolveContent(document, imageId);
        if (!resolved.IsSuccess)
        {
            return ForwardError<ImageContentView, ImagePlacementInfo>(resolved);
        }

        document.Paragraphs.Insert(
            insertionIndex,
            CreateImageParagraph(imageId, request.Size, request.Caption));
        UpdateImageCount(document);

        return ImageResult<ImagePlacementInfo>.Success(
            new ImagePlacementInfo(insertionIndex, imageId, request.Size));
    }

    public ImageResult<ImagePlacementInfo> ReplacePlacementContent(
        GostDocument document,
        int paragraphIndex,
        ReplaceImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        ImageResult<Guid> currentReference = GetPlacementImageId(document, paragraphIndex);
        if (!currentReference.IsSuccess)
        {
            return ForwardError<Guid, ImagePlacementInfo>(currentReference);
        }

        ImageResult<ImageContentMetadata> inspected = ValidateNewContent(request.Data, request.Size);
        if (!inspected.IsSuccess)
        {
            return ForwardError<ImageContentMetadata, ImagePlacementInfo>(inspected);
        }

        Guid oldImageId = currentReference.Value;
        Guid newImageId = CreateUniqueId(document);
        string? currentFileName = null;
        int currentAttachmentMatches = 0;
        foreach (ImageAttachment attachment in document.Images)
        {
            if (attachment.Id == oldImageId)
            {
                currentFileName = attachment.FileName;
                currentAttachmentMatches++;
            }
        }

        if (currentAttachmentMatches > 1)
        {
            return ImageResult<ImagePlacementInfo>.Failure(
                ImageErrorCode.DuplicateImageId,
                $"ID изображения {oldImageId} не уникален.",
                oldImageId,
                paragraphIndex);
        }

        string fileName = request.FileName is null
            ? currentFileName ?? string.Empty
            : NormalizeFileName(request.FileName);
        ImageAttachment replacement = new ImageAttachment(
            newImageId,
            fileName,
            inspected.Value!.MediaType,
            request.Data);

        Paragraph paragraph = document.Paragraphs[paragraphIndex];
        document.MutableImages.Add(replacement);
        paragraph.ImageId = newImageId;
        paragraph.ImageWidth = request.Size.Width;
        paragraph.ImageHeight = request.Size.Height;
        bool removed = RemoveAttachmentIfUnreferenced(document, oldImageId);
        _ = removed;
        UpdateImageCount(document);

        return ImageResult<ImagePlacementInfo>.Success(
            new ImagePlacementInfo(paragraphIndex, newImageId, request.Size));
    }

    public ImageResult<ImagePlacementInfo> ResizePlacement(
        GostDocument document,
        int paragraphIndex,
        ImageSize size)
    {
        ArgumentNullException.ThrowIfNull(document);

        ImageResult<Guid> currentReference = GetPlacementImageId(document, paragraphIndex);
        if (!currentReference.IsSuccess)
        {
            return ForwardError<Guid, ImagePlacementInfo>(currentReference);
        }

        if (!IsValidSize(size))
        {
            return InvalidSize<ImagePlacementInfo>(size, paragraphIndex);
        }

        Paragraph paragraph = document.Paragraphs[paragraphIndex];
        paragraph.ImageWidth = size.Width;
        paragraph.ImageHeight = size.Height;

        return ImageResult<ImagePlacementInfo>.Success(
            new ImagePlacementInfo(paragraphIndex, currentReference.Value, size));
    }

    public ImageResult<ImageRemovalInfo> RemovePlacement(
        GostDocument document,
        int paragraphIndex)
    {
        ArgumentNullException.ThrowIfNull(document);

        ImageResult<Guid> currentReference = GetPlacementImageId(document, paragraphIndex);
        if (!currentReference.IsSuccess)
        {
            return ForwardError<Guid, ImageRemovalInfo>(currentReference);
        }

        Guid imageId = currentReference.Value;
        document.Paragraphs.RemoveAt(paragraphIndex);
        bool attachmentRemoved = RemoveAttachmentIfUnreferenced(document, imageId);

        if (document.Paragraphs.Count == 0)
        {
            document.Paragraphs.Add(new Paragraph());
        }

        UpdateImageCount(document);
        return ImageResult<ImageRemovalInfo>.Success(
            new ImageRemovalInfo(paragraphIndex, imageId, attachmentRemoved));
    }

    public ImageResult<ResolvedImagePlacement> ResolvePlacement(
        GostDocument document,
        int paragraphIndex)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (paragraphIndex < 0 || paragraphIndex >= document.Paragraphs.Count)
        {
            return ImageResult<ResolvedImagePlacement>.Failure(
                ImageErrorCode.ParagraphIndexOutOfRange,
                "Индекс абзаца находится вне документа.",
                paragraphIndex: paragraphIndex);
        }

        Paragraph paragraph = document.Paragraphs[paragraphIndex];
        if (!paragraph.ImageId.HasValue)
        {
            return ImageResult<ResolvedImagePlacement>.Failure(
                ImageErrorCode.ParagraphIsNotImage,
                "Абзац не содержит изображения.",
                paragraphIndex: paragraphIndex);
        }

        ImageSize size = new ImageSize(paragraph.ImageWidth, paragraph.ImageHeight);
        if (!IsValidSize(size))
        {
            return InvalidSize<ResolvedImagePlacement>(size, paragraphIndex);
        }

        ImageResult<ImageContentView> content = ResolveContent(document, paragraph.ImageId.Value);
        if (!content.IsSuccess)
        {
            return ForwardError<ImageContentView, ResolvedImagePlacement>(
                content,
                paragraphIndex);
        }

        return ImageResult<ResolvedImagePlacement>.Success(
            new ResolvedImagePlacement(
                paragraphIndex,
                paragraph.ImageId.Value,
                size,
                content.Value!));
    }

    public ImageResult<ImageContentView> ResolveContent(
        GostDocument document,
        Guid imageId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (imageId == Guid.Empty)
        {
            return ImageResult<ImageContentView>.Failure(
                ImageErrorCode.ImageNotFound,
                "Пустой идентификатор изображения недопустим.",
                imageId);
        }

        ImageAttachment? match = null;
        foreach (ImageAttachment attachment in document.Images)
        {
            if (attachment.Id != imageId)
            {
                continue;
            }

            if (match is not null)
            {
                return ImageResult<ImageContentView>.Failure(
                    ImageErrorCode.DuplicateImageId,
                    $"В документе найдено несколько изображений с ID {imageId}.",
                    imageId);
            }

            match = attachment;
        }

        if (match is null)
        {
            return ImageResult<ImageContentView>.Failure(
                ImageErrorCode.ImageNotFound,
                $"Изображение с ID {imageId} не найдено.",
                imageId);
        }

        if (match.Data.IsEmpty)
        {
            return ImageResult<ImageContentView>.Failure(
                ImageErrorCode.EmptyData,
                $"Изображение с ID {imageId} не содержит данных.",
                imageId);
        }

        return ImageResult<ImageContentView>.Success(
            new ImageContentView(
                match.Id,
                match.Data,
                match.FileName,
                match.MediaType));
    }

    public ImageIntegrityReport Inspect(GostDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<ImageIntegrityIssue> issues = new List<ImageIntegrityIssue>();
        Dictionary<Guid, int> attachmentCounts = new Dictionary<Guid, int>();
        HashSet<Guid> referencedIds = new HashSet<Guid>();

        foreach (ImageAttachment attachment in document.Images)
        {
            if (attachment.Id == Guid.Empty)
            {
                issues.Add(new ImageIntegrityIssue(
                    ImageIntegrityIssueCode.EmptyImageId,
                    ImageIntegritySeverity.Error,
                    "Attachment содержит пустой ID.",
                    attachment.Id));
            }

            attachmentCounts.TryGetValue(attachment.Id, out int count);
            attachmentCounts[attachment.Id] = count + 1;

            if (attachment.Data.IsEmpty)
            {
                issues.Add(new ImageIntegrityIssue(
                    ImageIntegrityIssueCode.EmptyImageData,
                    ImageIntegritySeverity.Warning,
                    $"Attachment {attachment.Id} не содержит данных.",
                    attachment.Id));
            }
        }

        foreach ((Guid id, int count) in attachmentCounts)
        {
            if (count > 1)
            {
                issues.Add(new ImageIntegrityIssue(
                    ImageIntegrityIssueCode.DuplicateImageId,
                    ImageIntegritySeverity.Error,
                    $"ID изображения {id} встречается {count} раз.",
                    id));
            }
        }

        for (int paragraphIndex = 0; paragraphIndex < document.Paragraphs.Count; paragraphIndex++)
        {
            Paragraph paragraph = document.Paragraphs[paragraphIndex];
            if (!paragraph.ImageId.HasValue)
            {
                continue;
            }

            Guid imageId = paragraph.ImageId.Value;
            referencedIds.Add(imageId);

            if (!attachmentCounts.ContainsKey(imageId))
            {
                issues.Add(new ImageIntegrityIssue(
                    ImageIntegrityIssueCode.DanglingImageReference,
                    ImageIntegritySeverity.Error,
                    $"Абзац {paragraphIndex} ссылается на отсутствующее изображение {imageId}.",
                    imageId,
                    paragraphIndex));
            }

            if (!IsValidSize(new ImageSize(paragraph.ImageWidth, paragraph.ImageHeight)))
            {
                issues.Add(new ImageIntegrityIssue(
                    ImageIntegrityIssueCode.InvalidDimensions,
                    ImageIntegritySeverity.Error,
                    $"Абзац {paragraphIndex} содержит недопустимый размер изображения.",
                    imageId,
                    paragraphIndex));
            }
        }

        foreach (ImageAttachment attachment in document.Images)
        {
            if (!referencedIds.Contains(attachment.Id))
            {
                issues.Add(new ImageIntegrityIssue(
                    ImageIntegrityIssueCode.OrphanAttachment,
                    ImageIntegritySeverity.Warning,
                    $"Изображение {attachment.Id} не используется ни одним абзацем.",
                    attachment.Id));
            }
        }

        return new ImageIntegrityReport(issues);
    }

    public ImageResult<OrphanCleanupResult> RemoveOrphans(
        GostDocument document,
        IReadOnlyCollection<Guid> confirmedImageIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(confirmedImageIds);

        HashSet<Guid> ids = new HashSet<Guid>(confirmedImageIds);

        foreach (Guid imageId in ids)
        {
            int matches = 0;
            foreach (ImageAttachment attachment in document.Images)
            {
                if (attachment.Id == imageId)
                {
                    matches++;
                }
            }

            if (matches == 0)
            {
                return ImageResult<OrphanCleanupResult>.Failure(
                    ImageErrorCode.ImageNotFound,
                    $"Изображение {imageId} не найдено.",
                    imageId);
            }

            if (matches > 1)
            {
                return ImageResult<OrphanCleanupResult>.Failure(
                    ImageErrorCode.DuplicateImageId,
                    $"ID изображения {imageId} не уникален.",
                    imageId);
            }

            foreach (Paragraph paragraph in document.Paragraphs)
            {
                if (paragraph.ImageId == imageId)
                {
                    return ImageResult<OrphanCleanupResult>.Failure(
                        ImageErrorCode.ImageStillReferenced,
                        $"Изображение {imageId} всё ещё используется.",
                        imageId);
                }
            }
        }

        document.MutableImages.RemoveAll(attachment => ids.Contains(attachment.Id));
        return ImageResult<OrphanCleanupResult>.Success(
            new OrphanCleanupResult(new List<Guid>(ids)));
    }

    private ImageResult<ImageContentMetadata> ValidateNewContent(
        ReadOnlyMemory<byte> data,
        ImageSize size)
    {
        if (!IsValidSize(size))
        {
            return ImageResult<ImageContentMetadata>.Failure(
                ImageErrorCode.InvalidDimensions,
                $"Недопустимый размер изображения: {size.Width} × {size.Height}.");
        }

        return _contentProbe.Inspect(data);
    }

    private static ImageResult<Guid> GetPlacementImageId(
        GostDocument document,
        int paragraphIndex)
    {
        if (paragraphIndex < 0 || paragraphIndex >= document.Paragraphs.Count)
        {
            return ImageResult<Guid>.Failure(
                ImageErrorCode.ParagraphIndexOutOfRange,
                "Индекс абзаца находится вне документа.",
                paragraphIndex: paragraphIndex);
        }

        Guid? imageId = document.Paragraphs[paragraphIndex].ImageId;
        if (!imageId.HasValue)
        {
            return ImageResult<Guid>.Failure(
                ImageErrorCode.ParagraphIsNotImage,
                "Абзац не содержит изображения.",
                paragraphIndex: paragraphIndex);
        }

        return ImageResult<Guid>.Success(imageId.Value);
    }

    private static ImageResult<bool> ValidateAttachmentIds(GostDocument document)
    {
        HashSet<Guid> ids = new HashSet<Guid>();
        foreach (ImageAttachment attachment in document.Images)
        {
            if (attachment.Id == Guid.Empty)
            {
                return ImageResult<bool>.Failure(
                    ImageErrorCode.InvalidDocumentState,
                    "Документ содержит attachment с пустым ID.");
            }

            if (!ids.Add(attachment.Id))
            {
                return ImageResult<bool>.Failure(
                    ImageErrorCode.DuplicateImageId,
                    $"ID изображения {attachment.Id} не уникален.",
                    attachment.Id);
            }
        }

        return ImageResult<bool>.Success(true);
    }

    private static Paragraph CreateImageParagraph(
        Guid imageId,
        ImageSize size,
        string caption)
    {
        Paragraph paragraph = new Paragraph
        {
            Alignment = GostAlignment.Center,
            FirstLineIndent = 0,
            ImageId = imageId,
            ImageWidth = size.Width,
            ImageHeight = size.Height
        };
        paragraph.Runs.Add(new TextRun(caption ?? string.Empty, false, false)
        {
            FontSize = 14
        });
        return paragraph;
    }

    private static bool RemoveAttachmentIfUnreferenced(
        GostDocument document,
        Guid imageId)
    {
        foreach (Paragraph paragraph in document.Paragraphs)
        {
            if (paragraph.ImageId == imageId)
            {
                return false;
            }
        }

        return document.MutableImages.RemoveAll(attachment => attachment.Id == imageId) > 0;
    }

    private static Guid CreateUniqueId(GostDocument document)
    {
        Guid id;
        do
        {
            id = Guid.NewGuid();
        }
        while (ContainsImageId(document, id));

        return id;
    }

    private static bool ContainsImageId(GostDocument document, Guid imageId)
    {
        foreach (ImageAttachment attachment in document.Images)
        {
            if (attachment.Id == imageId)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeFileName(string? fileName)
    {
        return string.IsNullOrWhiteSpace(fileName)
            ? string.Empty
            : Path.GetFileName(fileName);
    }

    private static bool IsValidSize(ImageSize size)
    {
        return double.IsFinite(size.Width)
            && double.IsFinite(size.Height)
            && size.Width > 0
            && size.Height > 0;
    }

    private static ImageResult<T> InvalidSize<T>(
        ImageSize size,
        int? paragraphIndex = null)
    {
        return ImageResult<T>.Failure(
            ImageErrorCode.InvalidDimensions,
            $"Недопустимый размер изображения: {size.Width} × {size.Height}.",
            paragraphIndex: paragraphIndex);
    }

    private static ImageResult<TTarget> ForwardError<TSource, TTarget>(
        ImageResult<TSource> source,
        int? paragraphIndex = null)
    {
        ImageError error = source.Error
            ?? throw new InvalidOperationException("Не удалось перенаправить успешный результат.");
        return ImageResult<TTarget>.Failure(
            error.Code,
            error.Message,
            error.ImageId,
            paragraphIndex ?? error.ParagraphIndex);
    }

    private static void UpdateImageCount(GostDocument document)
    {
        int count = 0;
        foreach (Paragraph paragraph in document.Paragraphs)
        {
            if (paragraph.ImageId.HasValue)
            {
                count++;
            }
        }

        document.Counters.ImagesCount = count;
    }
}
