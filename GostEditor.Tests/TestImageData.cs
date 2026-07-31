namespace GostEditor.Tests;

internal static class TestImageData
{
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    public static byte[] CreatePng()
    {
        return Convert.FromBase64String(OnePixelPngBase64);
    }
}
