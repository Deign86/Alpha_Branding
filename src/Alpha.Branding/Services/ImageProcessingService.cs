using Alpha.Branding.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Windows.Media.Imaging;

namespace Alpha.Branding.Services;

public sealed class ImageProcessingService
{
    public const int TargetWidth = 1200;
    public const int TargetHeight = 1000;

    public async Task<BrandedImage> ProcessAsync(string inputPath, string overlayPath, string? prefix, int index, int total, CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(inputPath);
        await using var overlay = File.OpenRead(overlayPath);
        using var photo = await Image.LoadAsync<Rgba32>(input, cancellationToken);
        using var frame = await Image.LoadAsync<Rgba32>(overlay, cancellationToken);
        photo.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(TargetWidth, TargetHeight), Mode = ResizeMode.Stretch }));
        frame.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(TargetWidth, TargetHeight), Mode = ResizeMode.Stretch }));
        photo.Mutate(context => context.DrawImage(frame, new Point(0, 0), 1f));

        await using var output = new MemoryStream();
        await photo.SaveAsWebpAsync(output, new WebpEncoder { Quality = 80 }, cancellationToken);
        return new BrandedImage
        {
            FileName = FileNameGenerator.Generate(prefix, index, total),
            WebpBytes = output.ToArray(),
            Preview = CreatePreview(photo),
            SequenceIndex = index,
            BatchSize = total
        };
    }

    private static BitmapImage CreatePreview(Image<Rgba32> image)
    {
        using var preview = new MemoryStream();
        image.SaveAsPng(preview);
        preview.Position = 0;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = preview;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
