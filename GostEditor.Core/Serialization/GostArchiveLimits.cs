namespace GostEditor.Core.Serialization;

/// <summary>
/// Hard safety limits applied while opening an untrusted .gost package.
/// They are intentionally well above ordinary documents but finite so a
/// malformed archive cannot request unbounded memory or work.
/// </summary>
public static class GostArchiveLimits
{
    public const long MaxArchiveBytes = 512L * 1024 * 1024;
    public const int MaxEntryCount = 2048;
    public const int MaxEntryNameLength = 1024;
    public const long MaxCentralDirectoryBytes = 16L * 1024 * 1024;
    public const long MaxUncompressedArchiveBytes = 512L * 1024 * 1024;
    public const long MaxManifestBytes = 16L * 1024 * 1024;
    public const long MaxImageBytes = 64L * 1024 * 1024;
    public const long MaxTotalImageBytes = 256L * 1024 * 1024;
    public const int MaxJsonDepth = 64;
    public const int MaxJsonTokens = 2_000_000;
    public const int MaxJsonStringCharacters = 16_000_000;
    public const long MaxTotalJsonStringCharacters = 24_000_000;
    public const int MaxJsonNumberBytes = 256;
    public const int MaxParagraphs = 100_000;
    public const int MaxRuns = 1_000_000;
    public const int MaxImages = 1_000;
    public const int MaxCodeListings = 10_000;
    public const int MaxBibliographySources = 100_000;
    public const double MaxLayoutDimension = 100_000;
    public const double MaxFontSize = 1_000;
    public const int MaxImagePixelDimension = 16_384;
    public const long MaxImagePixels = 40_000_000;
}
