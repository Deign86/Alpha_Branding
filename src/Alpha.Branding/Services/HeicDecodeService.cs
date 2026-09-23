using ImageMagick;
using System.IO;

namespace Alpha.Branding.Services;

public static class HeicDecodeService
{
    public static bool IsHeic(string path) =>
        Path.GetExtension(path).Equals(".heic", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".heif", StringComparison.OrdinalIgnoreCase);

    public static Task<byte[]> TranscodeToJpegBytesAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var image = new MagickImage(path);
                ct.ThrowIfCancellationRequested();
                image.Format = MagickFormat.Jpeg;
                image.Quality = 92;
                var bytes = image.ToByteArray();
                ct.ThrowIfCancellationRequested();
                return bytes;
            }
            catch (MagickException ex)
            {
                throw new InvalidDataException($"Could not decode HEIC image '{path}'.", ex);
            }
        }, ct);
}
