using Alpha.Branding.Models;
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

        var clip = await MediaClip.CreateFromFileAsync(videoStorageFile).AsTask(cancellationToken);
        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        var videoProps = await videoStorageFile.Properties.GetVideoPropertiesAsync().AsTask(cancellationToken);
        var width = videoProps.Width > 0 ? videoProps.Width : 1920;
        var height = videoProps.Height > 0 ? videoProps.Height : 1080;

        var overlayClip = await MediaClip.CreateFromImageFileAsync(overlayStorageFile, clip.OriginalDuration).AsTask(cancellationToken);
        var overlay = new MediaOverlay(overlayClip)
        {
            Position = new Rect(0, 0, width, height),
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
        if (width > 0 && height > 0)
        {
            encodingProfile.Video.Width = width;
            encodingProfile.Video.Height = height;
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
