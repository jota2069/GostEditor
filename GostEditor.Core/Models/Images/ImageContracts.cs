using System;
using System.Collections.Generic;

namespace GostEditor.Core.Models;

public readonly record struct ImageSize(double Width, double Height);

public sealed record CreateImageRequest(
    ReadOnlyMemory<byte> Data,
    ImageSize Size,
    string FileName = "",
    string Caption = "");

public sealed record ImagePlacementRequest(
    ImageSize Size,
    string Caption = "");

public sealed record ReplaceImageRequest(
    ReadOnlyMemory<byte> Data,
    ImageSize Size,
    string? FileName = null);

public readonly record struct ImagePlacementInfo(
    int ParagraphIndex,
    Guid ImageId,
    ImageSize Size);

public readonly record struct ImageRemovalInfo(
    int ParagraphIndex,
    Guid ImageId,
    bool AttachmentRemoved);

public readonly record struct ImageContentView(
    Guid Id,
    ReadOnlyMemory<byte> Data,
    string FileName,
    string MediaType);

public readonly record struct ResolvedImagePlacement(
    int ParagraphIndex,
    Guid ImageId,
    ImageSize Size,
    ImageContentView Content);

public readonly record struct OrphanCleanupResult(
    IReadOnlyList<Guid> RemovedImageIds);

public enum ImageErrorCode
{
    EmptyData,
    UnsupportedOrInvalidImage,
    InvalidDimensions,
    ParagraphIndexOutOfRange,
    ParagraphIsNotImage,
    ImageNotFound,
    DuplicateImageId,
    ImageStillReferenced,
    InvalidDocumentState
}

public sealed record ImageError(
    ImageErrorCode Code,
    string Message,
    Guid? ImageId = null,
    int? ParagraphIndex = null);

public readonly record struct ImageResult<T>(T? Value, ImageError? Error)
{
    public bool IsSuccess => Error is null;

    public static ImageResult<T> Success(T value) => new ImageResult<T>(value, null);

    public static ImageResult<T> Failure(
        ImageErrorCode code,
        string message,
        Guid? imageId = null,
        int? paragraphIndex = null)
    {
        return new ImageResult<T>(
            default,
            new ImageError(code, message, imageId, paragraphIndex));
    }
}

public enum ImageIntegritySeverity
{
    Warning,
    Error
}

public enum ImageIntegrityIssueCode
{
    EmptyImageId,
    DuplicateImageId,
    DanglingImageReference,
    EmptyImageData,
    InvalidDimensions,
    OrphanAttachment
}

public sealed record ImageIntegrityIssue(
    ImageIntegrityIssueCode Code,
    ImageIntegritySeverity Severity,
    string Message,
    Guid? ImageId = null,
    int? ParagraphIndex = null);

public sealed class ImageIntegrityReport
{
    public ImageIntegrityReport(IReadOnlyList<ImageIntegrityIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<ImageIntegrityIssue> Issues { get; }

    public bool IsValid
    {
        get
        {
            foreach (ImageIntegrityIssue issue in Issues)
            {
                if (issue.Severity == ImageIntegritySeverity.Error)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

internal readonly record struct ImageContentMetadata(
    string MediaType,
    int PixelWidth,
    int PixelHeight);
