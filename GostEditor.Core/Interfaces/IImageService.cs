using System;
using System.Collections.Generic;
using GostEditor.Core.Models;

namespace GostEditor.Core.Interfaces;

public interface IImageService
{
    ImageResult<ImagePlacementInfo> InsertPlacement(
        GostDocument document,
        int insertionIndex,
        CreateImageRequest request);

    ImageResult<ImagePlacementInfo> InsertExistingPlacement(
        GostDocument document,
        int insertionIndex,
        Guid imageId,
        ImagePlacementRequest request);

    ImageResult<ImagePlacementInfo> ReplacePlacementContent(
        GostDocument document,
        int paragraphIndex,
        ReplaceImageRequest request);

    ImageResult<ImagePlacementInfo> ResizePlacement(
        GostDocument document,
        int paragraphIndex,
        ImageSize size);

    ImageResult<ImageRemovalInfo> RemovePlacement(
        GostDocument document,
        int paragraphIndex);

    ImageResult<ResolvedImagePlacement> ResolvePlacement(
        GostDocument document,
        int paragraphIndex);

    ImageResult<ImageContentView> ResolveContent(
        GostDocument document,
        Guid imageId);

    ImageIntegrityReport Inspect(GostDocument document);

    ImageResult<OrphanCleanupResult> RemoveOrphans(
        GostDocument document,
        IReadOnlyCollection<Guid> confirmedImageIds);
}
