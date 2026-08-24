using Alpha.Branding.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Alpha.Branding.Services;

public class VideoProcessingService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".wmv", ".avi", ".m4v", ".mkv", ".webm"
    };

    public static bool IsVideoFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var ext = Path.GetExtension(filePath);
        return VideoExtensions.Contains(ext);
    }

    public static async Task<(BitmapImage? Thumbnail, string DurationText, TimeSpan Duration)> GetVideoMetadataAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath)).AsTask(cancellationToken);
            var videoProps = await storageFile.Properties.GetVideoPropertiesAsync().AsTask(cancellationToken);
            var duration = videoProps.Duration;

            var durationText = duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : duration.ToString(@"m\:ss");

            BitmapImage? thumbnail = null;
            try
            {
                using var thumbStream = await storageFile.GetThumbnailAsync(ThumbnailMode.VideosView, 320).AsTask(cancellationToken);
                if (thumbStream != null && thumbStream.Size > 0)
                {
                    using var netStream = thumbStream.AsStreamForRead();
                    using var ms = new MemoryStream();
                    await netStream.CopyToAsync(ms, cancellationToken);
                    ms.Position = 0;

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    thumbnail = bmp;
                }
            }
            catch
            {
                // Fallback thumbnail if extraction fails
            }

            return (thumbnail, durationText, duration);
        }
        catch
        {
            return (null, string.Empty, TimeSpan.Zero);
        }
    }

    public static async Task<string> CreateAdaptedOverlayAsync(
        string overlaySourcePath,
        uint targetWidth,
        uint targetHeight,
        CancellationToken cancellationToken = default)
    {
        var tempOverlayPath = Path.Combine(Path.GetTempPath(), $"alpha_overlay_{targetWidth}x{targetHeight}_{Guid.NewGuid():N}.png");
        using var source = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(overlaySourcePath, cancellationToken);
        using var canvas = new SixLabors.ImageSharp.Image<Rgba32>((int)targetWidth, (int)targetHeight);

        if (targetHeight > targetWidth)
        {
            // Portrait (e.g. 1080x1920 or 9:16)
            var scale = (float)targetWidth / source.Width;

            // 1. Top Ribbon & Emblem
            using var topSection = source.Clone(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(0, 0, source.Width, 250)));
            topSection.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size((int)targetWidth, (int)(250 * scale)),
                Mode = ResizeMode.Stretch
            }));
            canvas.Mutate(ctx => ctx.DrawImage(topSection, new SixLabors.ImageSharp.Point(0, 0), 1f));

            // 2. Bottom Contact Banner
            using var bottomSection = source.Clone(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(0, 900, source.Width, 100)));
            bottomSection.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size((int)targetWidth, (int)(100 * scale)),
                Mode = ResizeMode.Stretch
            }));
            var bottomY = (int)targetHeight - bottomSection.Height;
            canvas.Mutate(ctx => ctx.DrawImage(bottomSection, new SixLabors.ImageSharp.Point(0, bottomY), 1f));

            // 3. Center Watermark Logo
            using var centerSection = source.Clone(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(350, 300, 500, 450)));
            var centerW = (int)(targetWidth * 0.55f);
            var centerH = (int)(centerW * 450f / 500f);
            centerSection.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(centerW, centerH),
                Mode = ResizeMode.Stretch
            }));
            var centerX = ((int)targetWidth - centerW) / 2;
            var centerY = ((int)targetHeight - centerH) / 2;
            canvas.Mutate(ctx => ctx.DrawImage(centerSection, new SixLabors.ImageSharp.Point(centerX, centerY), 1f));
        }
        else
        {
            // Landscape (e.g. 1920x1080, 1280x720, 16:9)
            using var scaled = source.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size((int)targetWidth, (int)targetHeight),
                Mode = ResizeMode.Stretch
            }));
            canvas.Mutate(ctx => ctx.DrawImage(scaled, new SixLabors.ImageSharp.Point(0, 0), 1f));
        }

        await canvas.SaveAsPngAsync(tempOverlayPath, cancellationToken);
        return tempOverlayPath;
    }

    public virtual async Task<BrandedImage> ProcessVideoAsync(
        string inputVideoPath,
        string overlayImagePath,
        string? prefix,
        int index,
        int total,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputVideoPath))
            throw new FileNotFoundException("Input video file not found.", inputVideoPath);
        if (!File.Exists(overlayImagePath))
            throw new FileNotFoundException("Overlay image file not found.", overlayImagePath);

        var videoStorageFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(inputVideoPath)).AsTask(cancellationToken);
        var clip = await MediaClip.CreateFromFileAsync(videoStorageFile).AsTask(cancellationToken);
        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        var videoProps = await videoStorageFile.Properties.GetVideoPropertiesAsync().AsTask(cancellationToken);
        uint width = videoProps.Width > 0 ? videoProps.Width : 1920;
        uint height = videoProps.Height > 0 ? videoProps.Height : 1080;

        var encodingProps = clip.GetVideoEncodingProperties();
        if (encodingProps != null && encodingProps.Width > 0 && encodingProps.Height > 0)
        {
            var rawW = encodingProps.Width;
            var rawH = encodingProps.Height;
            if (videoProps.Orientation == VideoOrientation.Rotate90 || videoProps.Orientation == VideoOrientation.Rotate270)
            {
                (rawW, rawH) = (rawH, rawW);
            }
            width = rawW;
            height = rawH;
        }

        // Generate tailored overlay matching the exact video aspect ratio
        var adaptedOverlayPath = await CreateAdaptedOverlayAsync(overlayImagePath, width, height, cancellationToken);
        var overlayStorageFile = await StorageFile.GetFileFromPathAsync(adaptedOverlayPath).AsTask(cancellationToken);

        var overlayClip = await MediaClip.CreateFromImageFileAsync(overlayStorageFile, clip.OriginalDuration).AsTask(cancellationToken);
        var overlay = new MediaOverlay(overlayClip)
        {
            Position = new Windows.Foundation.Rect(0, 0, width, height),
            Opacity = 1.0
        };

        var overlayLayer = new MediaOverlayLayer();
        overlayLayer.Overlays.Add(overlay);
        composition.OverlayLayers.Add(overlayLayer);

        var tempDir = Path.GetTempPath();
        var tempOutputPath = Path.Combine(tempDir, $"alpha_video_{Guid.NewGuid():N}.mp4");
        var tempFolder = await StorageFolder.GetFolderFromPathAsync(tempDir).AsTask(cancellationToken);
        var outputStorageFile = await tempFolder.CreateFileAsync(Path.GetFileName(tempOutputPath), CreationCollisionOption.ReplaceExisting).AsTask(cancellationToken);

        var encodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        encodingProfile.Video.Width = width;
        encodingProfile.Video.Height = height;
        encodingProfile.Video.PixelAspectRatio.Numerator = 1;
        encodingProfile.Video.PixelAspectRatio.Denominator = 1;
        if (videoProps.Bitrate > 0)
        {
            encodingProfile.Video.Bitrate = Math.Max(videoProps.Bitrate, 5_000_000);
        }

        var renderOp = composition.RenderToFileAsync(outputStorageFile, MediaTrimmingPreference.Precise, encodingProfile);
        renderOp.Progress = (_, percent) =>
        {
            progress?.Report(percent);
        };

        try
        {
            using (cancellationToken.Register(() =>
            {
                try { renderOp.Cancel(); } catch { }
            }))
            {
                await renderOp.AsTask(cancellationToken);
            }
        }
        finally
        {
            if (File.Exists(adaptedOverlayPath))
            {
                try { File.Delete(adaptedOverlayPath); } catch { }
            }
        }

        var (thumbnail, durationText, _) = await GetVideoMetadataAsync(tempOutputPath, cancellationToken);
        if (thumbnail == null)
        {
            var (origThumb, origDuration, _) = await GetVideoMetadataAsync(inputVideoPath, cancellationToken);
            thumbnail = origThumb ?? ImageProcessingService.CreateFallbackThumbnail();
            if (string.IsNullOrEmpty(durationText)) durationText = origDuration;
        }

        var fileName = FileNameGenerator.Generate(prefix, index, total, MediaType.Video);

        return new BrandedImage
        {
            FileName = fileName,
            MediaType = MediaType.Video,
            VideoFilePath = tempOutputPath,
            DurationText = durationText,
            Preview = thumbnail ?? ImageProcessingService.CreateFallbackThumbnail(),
            SequenceIndex = index,
            BatchSize = total
        };
    }
}
