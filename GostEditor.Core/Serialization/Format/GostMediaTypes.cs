namespace GostEditor.Core.Serialization.Format;

internal static class GostMediaTypes
{
    public const string Binary = "application/octet-stream";

    public static string Detect(ReadOnlySpan<byte> data, string? fileName = null)
    {
        if (data.StartsWith(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (data.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }

        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (data.StartsWith("BM"u8))
        {
            return "image/bmp";
        }

        if (data.Length >= 12 &&
            data[..4].SequenceEqual("RIFF"u8) &&
            data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return Path.GetExtension(fileName)?.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => Binary
        };
    }
}
