using Alpha.Branding.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
    public const uint TargetWidth = 1200;
    public const uint TargetHeight = 1000;

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

    public static (double OffsetX, double OffsetY, double FitWidth, double FitHeight) CalculateVideoFit(
        double sourceWidth,
        double sourceHeight,
        double targetWidth = TargetWidth,
        double targetHeight = TargetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return (0, 0, targetWidth, targetHeight);

        var scale = Math.Min(targetWidth / sourceWidth, targetHeight / sourceHeight);
        var fitW = Math.Round(sourceWidth * scale);
        var fitH = Math.Round(sourceHeight * scale);
        var offX = Math.Round((targetWidth - fitW) / 2.0);
        var offY = Math.Round((targetHeight - fitH) / 2.0);

        return (offX, offY, fitW, fitH);
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

    private static async Task<string> EnsureBlackCanvasAsync(CancellationToken cancellationToken)
    {
        var tempCanvasPath = Path.Combine(Path.GetTempPath(), "alpha_black_canvas_1200x1000.png");
        if (!File.Exists(tempCanvasPath))
        {
            using var blackImg = new SixLabors.ImageSharp.Image<Rgba32>((int)TargetWidth, (int)TargetHeight, new Rgba32(0, 0, 0, 255));
            await blackImg.SaveAsPngAsync(tempCanvasPath, cancellationToken);
        }
        return tempCanvasPath;
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
        var overlayStorageFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(overlayImagePath)).AsTask(cancellationToken);

        var videoClip = await MediaClip.CreateFromFileAsync(videoStorageFile).AsTask(cancellationToken);
        var videoProps = await videoStorageFile.Properties.GetVideoPropertiesAsync().AsTask(cancellationToken);

        double srcW = videoProps.Width > 0 ? videoProps.Width : 1920;
        double srcH = videoProps.Height > 0 ? videoProps.Height : 1080;

        var encodingProps = videoClip.GetVideoEncodingProperties();
        if (encodingProps != null && encodingProps.Width > 0 && encodingProps.Height > 0)
        {
            srcW = encodingProps.Width;
            srcH = encodingProps.Height;
        }

        if (videoProps.Orientation == VideoOrientation.Rotate90 || videoProps.Orientation == VideoOrientation.Rotate270)
        {
            (srcW, srcH) = (srcH, srcW);
        }

        // Calculate fitted position preserving video aspect ratio within the 1200x1000 template canvas
        var (offsetX, offsetY, fitWidth, fitHeight) = CalculateVideoFit(srcW, srcH, TargetWidth, TargetHeight);

        var composition = new MediaComposition();

        // 1. Base 1200x1000 black canvas for the clip's duration
        var blackCanvasPath = await EnsureBlackCanvasAsync(cancellationToken);
        var canvasStorageFile = await StorageFile.GetFileFromPathAsync(blackCanvasPath).AsTask(cancellationToken);
        var baseCanvasClip = await MediaClip.CreateFromImageFileAsync(canvasStorageFile, videoClip.OriginalDuration).AsTask(cancellationToken);
        composition.Clips.Add(baseCanvasClip);

        // 2. Video Overlay layer (fitted and centered)
        var videoOverlay = new MediaOverlay(videoClip)
        {
            Position = new Windows.Foundation.Rect(offsetX, offsetY, fitWidth, fitHeight),
            Opacity = 1.0
        };

        // 3. Template Watermark Overlay (1200x1000 1:1 scale covering the whole canvas)
        var overlayClip = await MediaClip.CreateFromImageFileAsync(overlayStorageFile, videoClip.OriginalDuration).AsTask(cancellationToken);
        var brandingOverlay = new MediaOverlay(overlayClip)
        {
            Position = new Windows.Foundation.Rect(0, 0, TargetWidth, TargetHeight),
            Opacity = 1.0
        };

        var overlayLayer = new MediaOverlayLayer();
        overlayLayer.Overlays.Add(videoOverlay);
        overlayLayer.Overlays.Add(brandingOverlay);
        composition.OverlayLayers.Add(overlayLayer);

        // 4. Preserve source audio
        try
        {
            var audioTrack = await BackgroundAudioTrack.CreateFromFileAsync(videoStorageFile).AsTask(cancellationToken);
            if (audioTrack != null)
            {
                composition.BackgroundAudioTracks.Add(audioTrack);
            }
        }
        catch
        {
            // Video might not have audio track
        }

        var tempDir = Path.GetTempPath();
        var tempOutputPath = Path.Combine(tempDir, $"alpha_video_{Guid.NewGuid():N}.mp4");
        var tempFolder = await StorageFolder.GetFolderFromPathAsync(tempDir).AsTask(cancellationToken);
        var outputStorageFile = await tempFolder.CreateFileAsync(Path.GetFileName(tempOutputPath), CreationCollisionOption.ReplaceExisting).AsTask(cancellationToken);

        var encodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        encodingProfile.Video.Width = TargetWidth;
        encodingProfile.Video.Height = TargetHeight;
        encodingProfile.Video.PixelAspectRatio.Numerator = 1;
        encodingProfile.Video.PixelAspectRatio.Denominator = 1;
        if (videoProps.Bitrate > 0)
        {
            encodingProfile.Video.Bitrate = Math.Max(videoProps.Bitrate, 6_000_000);
        }

        var renderOp = composition.RenderToFileAsync(outputStorageFile, MediaTrimmingPreference.Precise, encodingProfile);
        renderOp.Progress = (_, percent) =>
        {
            progress?.Report(percent);
        };

        using (cancellationToken.Register(() =>
        {
            try { renderOp.Cancel(); } catch { }
        }))
        {
            await renderOp.AsTask(cancellationToken);
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
