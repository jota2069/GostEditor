namespace GostEditor.Core.Serialization.Format;

internal static class GostFormatPaths
{
    public const string Manifest = "document.json";
    public const string ImageDirectory = "media/images/";

    public static string GetImagePath(Guid imageId)
    {
        return $"{ImageDirectory}{imageId:N}.dat";
    }
}
