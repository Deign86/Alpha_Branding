using Alpha.Branding.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Windows.Media.Imaging;

namespace Alpha.Branding.Services;

public abstract record ImageBatchItem
{
    public sealed record Landscape(string FilePath, ImageCropSettings? CropSettings = null) : ImageBatchItem;
    public sealed record PortraitPair(string LeftFilePath, string RightFilePath, ImageCropSettings? LeftCrop = null, ImageCropSettings? RightCrop = null) : ImageBatchItem;
    public sealed record LonePortrait(string FilePath, ImageCropSettings? CropSettings = null) : ImageBatchItem;
    public sealed record SoloImage(string FilePath, ImageCropSettings? CropSettings = null) : ImageBatchItem;
    public sealed record Video(string FilePath) : ImageBatchItem;
}

public sealed class ImageProcessingService
{
    public const int TargetWidth = 1200;
    public const int TargetHeight = 1000;
    public const int HalfWidth = 600;

    private readonly VideoProcessingService _videoProcessor;

    public ImageProcessingService(VideoProcessingService? videoProcessor = null)
    {
        _videoProcessor = videoProcessor ?? new VideoProcessingService();
    }

    public static async Task<bool> IsPortraitAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var info = await Image.IdentifyAsync(stream, cancellationToken);
        if (info == null) return false;

        var width = info.Width;
        var height = info.Height;

        if (info.Metadata?.ExifProfile != null && info.Metadata.ExifProfile.TryGetValue(ExifTag.Orientation, out var orientationValue))
        {
            if (orientationValue.Value is ushort val && val is 5 or 6 or 7 or 8)
            {
                (width, height) = (height, width);
            }
        }

        return height > width;
    }

    public static async Task<IReadOnlyList<ImageBatchItem>> PlanBatchAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
    {
        if (filePaths.Count == 0) return Array.Empty<ImageBatchItem>();
        var items = filePaths.Select(f => new SelectedPhotoItem { FilePath = f }).ToList();
        return await PlanBatchAsync(items, LayoutMode.Combine, cancellationToken);
    }

    public static async Task<IReadOnlyList<ImageBatchItem>> PlanBatchAsync(
        IReadOnlyList<SelectedPhotoItem> selectedItems,
        LayoutMode layoutMode = LayoutMode.Combine,
        CancellationToken cancellationToken = default)
    {
        if (selectedItems.Count == 0) return Array.Empty<ImageBatchItem>();

        var plannedItems = new List<(int Order, ImageBatchItem Item)>();
        var candidatesToPair = new List<(SelectedPhotoItem Item, bool IsPortrait, int Index)>();

        for (var i = 0; i < selectedItems.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = selectedItems[i];
            var path = item.FilePath;

            if (VideoProcessingService.IsVideoFile(path) || item.IsVideo)
            {
                plannedItems.Add((i, new ImageBatchItem.Video(path)));
                continue;
            }

            if (layoutMode == LayoutMode.Separate || item.IsSolo)
            {
                // Kept as separate individual branded image
                plannedItems.Add((i, new ImageBatchItem.SoloImage(path, item.CropSettings.Clone())));
                continue;
            }

            var isPortrait = false;
            try
            {
                isPortrait = await IsPortraitAsync(path, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fallback to landscape if orientation check fails for corrupt/missing files
            }

            candidatesToPair.Add((item, isPortrait, i));
        }

        var portraitList = candidatesToPair.Where(o => o.IsPortrait).ToList();
        var landscapeList = candidatesToPair.Where(o => !o.IsPortrait).ToList();

        // 1. Pair up portraits with each other
        var pIdx = 0;
        while (pIdx + 1 < portraitList.Count)
        {
            var pLeft = portraitList[pIdx];
            var pRight = portraitList[pIdx + 1];
            plannedItems.Add((pLeft.Index, new ImageBatchItem.PortraitPair(
                pLeft.Item.FilePath,
                pRight.Item.FilePath,
                pLeft.Item.CropSettings.Clone(),
                pRight.Item.CropSettings.Clone())));
            pIdx += 2;
        }

        // 2. If an odd portrait remains, match it with a landscape photo so it is never lone
        if (pIdx < portraitList.Count)
        {
            var leftoverPortrait = portraitList[pIdx];
            if (landscapeList.Count > 0)
            {
                var matchedLandscape = landscapeList[^1];
                landscapeList.RemoveAt(landscapeList.Count - 1);
                var minOrder = Math.Min(leftoverPortrait.Index, matchedLandscape.Index);
                plannedItems.Add((minOrder, new ImageBatchItem.PortraitPair(
                    leftoverPortrait.Item.FilePath,
                    matchedLandscape.Item.FilePath,
                    leftoverPortrait.Item.CropSettings.Clone(),
                    matchedLandscape.Item.CropSettings.Clone())));
            }
            else
            {
                plannedItems.Add((leftoverPortrait.Index, new ImageBatchItem.PortraitPair(
                    leftoverPortrait.Item.FilePath,
                    leftoverPortrait.Item.FilePath,
                    leftoverPortrait.Item.CropSettings.Clone(),
                    leftoverPortrait.Item.CropSettings.Clone())));
            }
        }

        // 3. Add remaining landscape photos as single landscape items
        foreach (var landscape in landscapeList)
        {
            plannedItems.Add((landscape.Index, new ImageBatchItem.Landscape(
                landscape.Item.FilePath,
                landscape.Item.CropSettings.Clone())));
        }

        // 4. Return items sorted by original input file order
        return plannedItems
            .OrderBy(p => p.Order)
            .Select(p => p.Item)
            .ToArray();
    }

    public static Image<Rgba32> RenderCroppedPhoto(
        Image<Rgba32> photo,
        int targetWidth,
        int targetHeight,
        ImageCropSettings? settings = null,
        bool isSoloPortrait = false)
    {
        if (settings != null && settings.Rotation != 0)
        {
            photo.Mutate(ctx => ctx.Rotate(settings.Rotation));
        }

        var srcW = photo.Width;
        var srcH = photo.Height;

        var scaleFit = Math.Min((double)targetWidth / srcW, (double)targetHeight / srcH);
        var scaleFill = Math.Max((double)targetWidth / srcW, (double)targetHeight / srcH);

        // When a portrait photo is placed in a landscape frame as solo, prioritize showing more of the original image without aggressive cropping
        var baseScale = (isSoloPortrait || (srcH > srcW && targetWidth > targetHeight)) ? scaleFit : scaleFill;

        var zoom = settings?.Zoom ?? 1.0;
        var effectiveScale = baseScale * zoom;

        var scaledW = Math.Max(1, (int)Math.Round(srcW * effectiveScale));
        var scaledH = Math.Max(1, (int)Math.Round(srcH * effectiveScale));

        var centerX = (targetWidth - scaledW) / 2.0;
        var centerY = (targetHeight - scaledH) / 2.0;

        var panX = settings?.PanX ?? 0.0;
        var panY = settings?.PanY ?? 0.0;

        var drawX = (int)Math.Round(centerX + panX);
        var drawY = (int)Math.Round(centerY + panY);

        photo.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(scaledW, scaledH),
            Mode = ResizeMode.Stretch
        }));

        var canvas = new Image<Rgba32>(targetWidth, targetHeight, new Rgba32(18, 18, 18, 255));
        canvas.Mutate(ctx => ctx.DrawImage(photo, new Point(drawX, drawY), 1f));
        return canvas;
    }

    public async Task<BrandedImage> ProcessAsync(string inputPath, string overlayPath, string? prefix, int index, int total, CancellationToken cancellationToken = default)
    {
        var isPortrait = false;
        try
        {
            isPortrait = await IsPortraitAsync(inputPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallback to landscape processing if detection fails
        }

        if (isPortrait)
        {
            return await ProcessPortraitPairAsync(inputPath, inputPath, overlayPath, prefix, index, total, null, null, cancellationToken);
        }

        return await ProcessLandscapeAsync(inputPath, overlayPath, prefix, index, total, null, cancellationToken);
    }

    public async Task<BrandedImage> ProcessLandscapeAsync(
        string inputPath,
        string overlayPath,
        string? prefix,
        int index,
        int total,
        ImageCropSettings? cropSettings = null,
        CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(inputPath);
        await using var overlay = File.OpenRead(overlayPath);
        using var photo = await Image.LoadAsync<Rgba32>(input, cancellationToken);
        using var frame = await Image.LoadAsync<Rgba32>(overlay, cancellationToken);

        frame.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(TargetWidth, TargetHeight), Mode = ResizeMode.Crop }));

        using var canvas = RenderCroppedPhoto(photo, TargetWidth, TargetHeight, cropSettings, isSoloPortrait: false);
        canvas.Mutate(context => context.DrawImage(frame, new Point(0, 0), 1f));

        var branded = await CreateBrandedImageAsync(canvas, prefix, index, total, cancellationToken);
        branded.SourceFilePaths = [inputPath];
        branded.CropSettings = cropSettings?.Clone() ?? new ImageCropSettings();
        branded.OverlayPath = overlayPath;
        branded.SourceBatchItem = new ImageBatchItem.Landscape(inputPath, branded.CropSettings);
        return branded;
    }

    public async Task<BrandedImage> ProcessSoloImageAsync(
        string inputPath,
        string overlayPath,
        string? prefix,
        int index,
        int total,
        ImageCropSettings? cropSettings = null,
        CancellationToken cancellationToken = default)
    {
        var isPortrait = false;
        try
        {
            isPortrait = await IsPortraitAsync(inputPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallback
        }

        await using var input = File.OpenRead(inputPath);
        await using var overlay = File.OpenRead(overlayPath);
        using var photo = await Image.LoadAsync<Rgba32>(input, cancellationToken);
        using var frame = await Image.LoadAsync<Rgba32>(overlay, cancellationToken);

        frame.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(TargetWidth, TargetHeight), Mode = ResizeMode.Crop }));

        using var canvas = RenderCroppedPhoto(photo, TargetWidth, TargetHeight, cropSettings, isSoloPortrait: isPortrait);
        canvas.Mutate(context => context.DrawImage(frame, new Point(0, 0), 1f));

        var branded = await CreateBrandedImageAsync(canvas, prefix, index, total, cancellationToken);
        branded.SourceFilePaths = [inputPath];
        branded.CropSettings = cropSettings?.Clone() ?? new ImageCropSettings();
        branded.OverlayPath = overlayPath;
        branded.SourceBatchItem = new ImageBatchItem.SoloImage(inputPath, branded.CropSettings);
        return branded;
    }

    public async Task<BrandedImage> ProcessPortraitPairAsync(
        string leftPath,
        string rightPath,
        string overlayPath,
        string? prefix,
        int index,
        int total,
        ImageCropSettings? leftCrop = null,
        ImageCropSettings? rightCrop = null,
        CancellationToken cancellationToken = default)
    {
        await using var leftStream = File.OpenRead(leftPath);
        await using var rightStream = File.OpenRead(rightPath);
        await using var overlayStream = File.OpenRead(overlayPath);

        using var leftPhoto = await Image.LoadAsync<Rgba32>(leftStream, cancellationToken);
        using var rightPhoto = await Image.LoadAsync<Rgba32>(rightStream, cancellationToken);
        using var frame = await Image.LoadAsync<Rgba32>(overlayStream, cancellationToken);

        frame.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(TargetWidth, TargetHeight), Mode = ResizeMode.Crop }));

        using var leftCanvas = RenderCroppedPhoto(leftPhoto, HalfWidth, TargetHeight, leftCrop, isSoloPortrait: false);
        using var rightCanvas = RenderCroppedPhoto(rightPhoto, HalfWidth, TargetHeight, rightCrop, isSoloPortrait: false);

        using var canvas = new Image<Rgba32>(TargetWidth, TargetHeight);
        canvas.Mutate(context =>
        {
            context.DrawImage(leftCanvas, new Point(0, 0), 1f);
            context.DrawImage(rightCanvas, new Point(HalfWidth, 0), 1f);
            context.DrawImage(frame, new Point(0, 0), 1f);
        });

        var branded = await CreateBrandedImageAsync(canvas, prefix, index, total, cancellationToken);
        branded.SourceFilePaths = [leftPath, rightPath];
        branded.CropSettings = leftCrop?.Clone() ?? new ImageCropSettings();
        branded.RightCropSettings = rightCrop?.Clone() ?? new ImageCropSettings();
        branded.OverlayPath = overlayPath;
        branded.SourceBatchItem = new ImageBatchItem.PortraitPair(leftPath, rightPath, branded.CropSettings, branded.RightCropSettings);
        return branded;
    }

    public async Task<BrandedImage> ProcessLonePortraitAsync(
        string inputPath,
        string overlayPath,
        string? prefix,
        int index,
        int total,
        ImageCropSettings? cropSettings = null,
        CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(inputPath);
        await using var overlay = File.OpenRead(overlayPath);

        using var photo = await Image.LoadAsync<Rgba32>(input, cancellationToken);
        using var frame = await Image.LoadAsync<Rgba32>(overlay, cancellationToken);

        frame.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(TargetWidth, TargetHeight), Mode = ResizeMode.Crop }));

        using var portraitCanvas = RenderCroppedPhoto(photo, HalfWidth, TargetHeight, cropSettings, isSoloPortrait: false);

        using var canvas = new Image<Rgba32>(TargetWidth, TargetHeight, new Rgba32(18, 18, 18, 255));
        canvas.Mutate(context =>
        {
            var offsetX = (TargetWidth - HalfWidth) / 2;
            context.DrawImage(portraitCanvas, new Point(offsetX, 0), 1f);
            context.DrawImage(frame, new Point(0, 0), 1f);
        });

        var branded = await CreateBrandedImageAsync(canvas, prefix, index, total, cancellationToken);
        branded.SourceFilePaths = [inputPath];
        branded.CropSettings = cropSettings?.Clone() ?? new ImageCropSettings();
        branded.OverlayPath = overlayPath;
        branded.SourceBatchItem = new ImageBatchItem.LonePortrait(inputPath, branded.CropSettings);
        return branded;
    }

    public async Task<BrandedImage> ProcessBatchItemAsync(
        ImageBatchItem item,
        string overlayPath,
        string? prefix,
        int index,
        int total,
        IProgress<double>? videoProgress = null,
        CancellationToken cancellationToken = default)
    {
        return item switch
        {
            ImageBatchItem.Landscape landscape => await ProcessLandscapeAsync(landscape.FilePath, overlayPath, prefix, index, total, landscape.CropSettings, cancellationToken),
            ImageBatchItem.PortraitPair pair => await ProcessPortraitPairAsync(pair.LeftFilePath, pair.RightFilePath, overlayPath, prefix, index, total, pair.LeftCrop, pair.RightCrop, cancellationToken),
            ImageBatchItem.LonePortrait lone => await ProcessLonePortraitAsync(lone.FilePath, overlayPath, prefix, index, total, lone.CropSettings, cancellationToken),
            ImageBatchItem.SoloImage solo => await ProcessSoloImageAsync(solo.FilePath, overlayPath, prefix, index, total, solo.CropSettings, cancellationToken),
            ImageBatchItem.Video video => await _videoProcessor.ProcessVideoAsync(video.FilePath, overlayPath, prefix, index, total, videoProgress, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(item))
        };
    }

    public async Task<BrandedImage> RebrandImageAsync(
        BrandedImage brandedImage,
        ImageCropSettings newCrop,
        ImageCropSettings? newRightCrop = null,
        string? overlayPath = null,
        CancellationToken cancellationToken = default)
    {
        var overlay = !string.IsNullOrWhiteSpace(overlayPath)
            ? overlayPath
            : (!string.IsNullOrWhiteSpace(brandedImage.OverlayPath)
                ? brandedImage.OverlayPath
                : Path.Combine(AppContext.BaseDirectory, "Assets", "alpha_branding.png"));

        if (!File.Exists(overlay))
        {
            throw new FileNotFoundException("Branding template overlay file not found.", overlay);
        }

        BrandedImage updated;
        if (brandedImage.SourceBatchItem is ImageBatchItem.PortraitPair pair)
        {
            var left = pair.LeftFilePath;
            var right = pair.RightFilePath;
            updated = await ProcessPortraitPairAsync(left, right, overlay, null, brandedImage.SequenceIndex, brandedImage.BatchSize, newCrop, newRightCrop ?? brandedImage.RightCropSettings, cancellationToken);
        }
        else if (brandedImage.SourceBatchItem is ImageBatchItem.SoloImage solo)
        {
            updated = await ProcessSoloImageAsync(solo.FilePath, overlay, null, brandedImage.SequenceIndex, brandedImage.BatchSize, newCrop, cancellationToken);
        }
        else if (brandedImage.SourceBatchItem is ImageBatchItem.LonePortrait lone)
        {
            updated = await ProcessLonePortraitAsync(lone.FilePath, overlay, null, brandedImage.SequenceIndex, brandedImage.BatchSize, newCrop, cancellationToken);
        }
        else if (brandedImage.SourceFilePaths.Count > 1)
        {
            updated = await ProcessPortraitPairAsync(brandedImage.SourceFilePaths[0], brandedImage.SourceFilePaths[1], overlay, null, brandedImage.SequenceIndex, brandedImage.BatchSize, newCrop, newRightCrop, cancellationToken);
        }
        else
        {
            var singlePath = brandedImage.SourceFilePaths.Count > 0 ? brandedImage.SourceFilePaths[0] : throw new InvalidOperationException("Source photo not available for re-branding.");
            updated = await ProcessLandscapeAsync(singlePath, overlay, null, brandedImage.SequenceIndex, brandedImage.BatchSize, newCrop, cancellationToken);
        }

        // Apply updated media in place
        brandedImage.ImageBytes = updated.ImageBytes;
        brandedImage.Preview = updated.Preview;
        brandedImage.CropSettings = newCrop.Clone();
        if (newRightCrop != null)
        {
            brandedImage.RightCropSettings = newRightCrop.Clone();
        }
        brandedImage.OverlayPath = overlay;

        return brandedImage;
    }

    private static async Task<BrandedImage> CreateBrandedImageAsync(Image<Rgba32> image, string? prefix, int index, int total, CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 90 }, cancellationToken);
        var imageBytes = output.ToArray();
        return new BrandedImage
        {
            FileName = FileNameGenerator.Generate(prefix, index, total, MediaType.Image),
            MediaType = MediaType.Image,
            ImageBytes = imageBytes,
            Preview = CreatePreview(imageBytes),
            SequenceIndex = index,
            BatchSize = total
        };
    }

    public static BitmapImage CreateFallbackThumbnail()
    {
        using var img = new Image<Rgba32>(320, 240, new Rgba32(24, 24, 24, 255));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return CreatePreview(ms.ToArray());
    }

    public static BitmapImage CreatePreview(byte[] imageBytes)
    {
        using var preview = new MemoryStream(imageBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = preview;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
