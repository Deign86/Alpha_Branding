using ImageMagick;

namespace Alpha.Branding.Tests;

internal static class MagickImageTestExtensions
{
    public static IMagickColor<byte> PixelAt(this IMagickImage<byte> image, int x, int y)
    {
        using var pixels = image.GetPixels();
        return pixels.GetPixel(x, y).ToColor()!;
    }
}
