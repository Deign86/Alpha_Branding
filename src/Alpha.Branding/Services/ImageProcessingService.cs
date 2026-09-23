using Alpha.Branding.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Windows.Media.Imaging;
using EllipsePolygon = SixLabors.ImageSharp.Drawing.EllipsePolygon;

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
        ImageInfo? info;
        try
        {
            await using var stream = File.OpenRead(filePath);
            info = await Image.IdentifyAsync(stream, cancellationToken);
        }
        catch (Exception ex) when (HeicDecodeService.IsHeic(filePath) && ex is not OperationCanceledException)
        {
            info = null;
        }

        if (info == null && HeicDecodeService.IsHeic(filePath))
        {
            await using var jpeg = new MemoryStream(await HeicDecodeService.TranscodeToJpegBytesAsync(filePath, cancellationToken));
            info = await Image.IdentifyAsync(jpeg, cancellationToken);
        }
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
        if (selectedItems is null || selectedItems.Count == 0) return Array.Empty<ImageBatchItem>();

        var plannedItems = new List<(int Order, ImageBatchItem Item)>();
        var candidatesToPair = new List<(SelectedPhotoItem Item, bool IsPortrait, int Index)>();

        for (var i = 0; i < selectedItems.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = selectedItems[i];
            if (item is null || string.IsNullOrWhiteSpace(item.FilePath)) continue;
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

        var explicitlyPairedIndices = new HashSet<int>();
        for (var i = 0; i < candidatesToPair.Count; i++)
        {
            var left = candidatesToPair[i];
            if (explicitlyPairedIndices.Contains(left.Index) || string.IsNullOrWhiteSpace(left.Item.PairedPartnerPath)) continue;

            for (var j = i + 1; j < candidatesToPair.Count; j++)
            {
                var right = candidatesToPair[j];
                if (explicitlyPairedIndices.Contains(right.Index) ||
                    string.Equals(left.Item.FilePath, right.Item.FilePath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(left.Item.PairedPartnerPath, right.Item.FilePath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(right.Item.PairedPartnerPath, left.Item.FilePath, StringComparison.OrdinalIgnoreCase)) continue;

                plannedItems.Add((left.Index, new ImageBatchItem.PortraitPair(
                    left.Item.FilePath,
                    right.Item.FilePath,
                    left.Item.CropSettings.Clone(),
                    right.Item.CropSettings.Clone())));
                explicitlyPairedIndices.Add(left.Index);
                explicitlyPairedIndices.Add(right.Index);
                break;
            }
        }

        var portraitList = candidatesToPair.Where(o => !explicitlyPairedIndices.Contains(o.Index) && o.IsPortrait).ToList();
        var landscapeList = candidatesToPair.Where(o => !explicitlyPairedIndices.Contains(o.Index) && !o.IsPortrait).ToList();

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
        bool isSoloPortrait = false,
        IReadOnlyList<RedactStroke>? strokes = null)
    {
        var originalWidth = photo.Width;
        var originalHeight = photo.Height;
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
        ApplyRedactStrokes(canvas, strokes ?? settings?.RedactStrokes, new Size(originalWidth, originalHeight),
            new Size(srcW, srcH), new Size(scaledW, scaledH), new Point(drawX, drawY), settings?.Rotation ?? 0);
        return canvas;
    }

    public static System.Windows.Point MapNormalizedToSourcePx(
        double nx, double ny, ImageCropSettings crop, int srcW, int srcH, int targetW, int targetH)
    {
        ArgumentNullException.ThrowIfNull(crop);
        if (srcW <= 0 || srcH <= 0 || targetW <= 0 || targetH <= 0)
            throw new ArgumentOutOfRangeException(nameof(srcW), "Image and target dimensions must be positive.");

        var quarterTurn = crop.Rotation == 90 || crop.Rotation == 270;
        var rotatedW = quarterTurn ? srcH : srcW;
        var rotatedH = quarterTurn ? srcW : srcH;
        var scaleFit = Math.Min((double)targetW / rotatedW, (double)targetH / rotatedH);
        var scaleFill = Math.Max((double)targetW / rotatedW, (double)targetH / rotatedH);
        var isSoloPortrait = targetW != HalfWidth && rotatedH > rotatedW && targetW > targetH;
        var scale = (isSoloPortrait ? scaleFit : scaleFill) * crop.Zoom;
        var drawX = (targetW - Math.Max(1.0, rotatedW * scale)) / 2.0 + crop.PanX;
        var drawY = (targetH - Math.Max(1.0, rotatedH * scale)) / 2.0 + crop.PanY;
        var rotatedX = (Math.Clamp(nx, 0, 1) * targetW - drawX) / scale;
        var rotatedY = (Math.Clamp(ny, 0, 1) * targetH - drawY) / scale;
        var (sourceX, sourceY) = crop.Rotation switch
        {
            90 => (rotatedY, srcH - rotatedX),
            180 => (srcW - rotatedX, srcH - rotatedY),
            270 => (srcW - rotatedY, rotatedX),
            _ => (rotatedX, rotatedY)
        };
        return new System.Windows.Point(Math.Clamp(sourceX, 0, srcW), Math.Clamp(sourceY, 0, srcH));
    }

    internal static void ApplyRedactStrokes(
        Image<Rgba32> canvas,
        IReadOnlyList<RedactStroke>? strokes,
        Size sourceSize,
        Size rotatedSize,
        Size scaledSize,
        Point drawOffset,
        int rotation)
    {
        if (strokes is null || strokes.Count == 0) return;

        var scaleX = (float)scaledSize.Width / rotatedSize.Width;
        var scaleY = (float)scaledSize.Height / rotatedSize.Height;
        foreach (var stroke in strokes)
        {
            if (stroke is null || stroke.Points.Count == 0) continue;

            var diameter = (float)(stroke.BrushSizeNormalized * Math.Min(sourceSize.Width, sourceSize.Height)
                * Math.Min(scaleX, scaleY));
            var radius = diameter / 2;
            var color = new Rgba32(stroke.R, stroke.G, stroke.B, 255);
            var points = new PointF[stroke.Points.Count];
            for (var i = 0; i < points.Length; i++)
            {
                var point = stroke.Points[i];
                var sourceX = point.X * sourceSize.Width;
                var sourceY = point.Y * sourceSize.Height;
                var (rotatedX, rotatedY) = rotation switch
                {
                    90 => (sourceSize.Height - sourceY, sourceX),
                    180 => (sourceSize.Width - sourceX, sourceSize.Height - sourceY),
                    270 => (sourceY, sourceSize.Width - sourceX),
                    _ => (sourceX, sourceY)
                };
                points[i] = new PointF(drawOffset.X + (float)(rotatedX * scaleX), drawOffset.Y + (float)(rotatedY * scaleY));
            }

            canvas.Mutate(context =>
            {
                if (points.Length > 1) context.DrawLine(color, diameter, points);
                foreach (var point in points)
                {
                    context.Fill(color, new EllipsePolygon(point, radius));
                }
            });
        }
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
        CancellationToken cancellationToken = default,
        IReadOnlyList<RedactStroke>? strokes = null)
    {
        await using var input = await OpenPhotoAsync(inputPath, cancellationToken);
        await using var overlay = File.OpenRead(overlayPath);
        using var photo = await Image.LoadAsync<Rgba32>(input, cancellationToken);
        using var frame = await Image.LoadAsync<Rgba32>(overlay, cancellationToken);

        frame.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(TargetWidth, TargetHeight), Mode = ResizeMode.Crop }));

        using var canvas = RenderCroppedPhoto(photo, TargetWidth, TargetHeight, cropSettings, isSoloPortrait: false, strokes);
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
        CancellationToken cancellationToken = default,
        IReadOnlyList<RedactStroke>? strokes = null)
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

        await using var input = await OpenPhotoAsync(inputPath, cancellationToken);
        await using var overlay = File.OpenRead(overlayPath);
        using var photo = await Image.LoadAsync<Rgba32>(input, cancellationToken);
        using var frame = await Image.LoadAsync<Rgba32>(overlay, cancellationToken);

        frame.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(TargetWidth, TargetHeight), Mode = ResizeMode.Crop }));

        using var canvas = RenderCroppedPhoto(photo, TargetWidth, TargetHeight, cropSettings, isSoloPortrait: isPortrait, strokes);
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
        CancellationToken cancellationToken = default,
        IReadOnlyList<RedactStroke>? leftStrokes = null,
        IReadOnlyList<RedactStroke>? rightStrokes = null)
    {
        await using var leftStream = await OpenPhotoAsync(leftPath, cancellationToken);
        await using var rightStream = await OpenPhotoAsync(rightPath, cancellationToken);
        await using var overlayStream = File.OpenRead(overlayPath);

        using var leftPhoto = await Image.LoadAsync<Rgba32>(leftStream, cancellationToken);
        using var rightPhoto = await Image.LoadAsync<Rgba32>(rightStream, cancellationToken);
        using var frame = await Image.LoadAsync<Rgba32>(overlayStream, cancellationToken);

        frame.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(TargetWidth, TargetHeight), Mode = ResizeMode.Crop }));

        using var leftCanvas = RenderCroppedPhoto(leftPhoto, HalfWidth, TargetHeight, leftCrop, isSoloPortrait: false, leftStrokes);
        using var rightCanvas = RenderCroppedPhoto(rightPhoto, HalfWidth, TargetHeight, rightCrop, isSoloPortrait: false, rightStrokes);

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
        CancellationToken cancellationToken = default,
        IReadOnlyList<RedactStroke>? strokes = null)
    {
        await using var input = await OpenPhotoAsync(inputPath, cancellationToken);
        await using var overlay = File.OpenRead(overlayPath);

        using var photo = await Image.LoadAsync<Rgba32>(input, cancellationToken);
        using var frame = await Image.LoadAsync<Rgba32>(overlay, cancellationToken);

        frame.Mutate(context => context.Resize(new ResizeOptions { Size = new Size(TargetWidth, TargetHeight), Mode = ResizeMode.Crop }));

        using var portraitCanvas = RenderCroppedPhoto(photo, HalfWidth, TargetHeight, cropSettings, isSoloPortrait: false, strokes);

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

        var leftCrop = newCrop.Clone();
        var rightCrop = (newRightCrop ?? brandedImage.RightCropSettings)?.Clone();
        var leftStrokes = RedactStrokeCollection.Clone(leftCrop.RedactStrokes);
        var rightStrokes = RedactStrokeCollection.Clone(rightCrop?.RedactStrokes);
        BrandedImage updated;
        if (brandedImage.SourceBatchItem is ImageBatchItem.PortraitPair pair)
        {
            var left = pair.LeftFilePath;
            var right = pair.RightFilePath;
            updated = await ProcessPortraitPairAsync(left, right, overlay, null, brandedImage.SequenceIndex, brandedImage.BatchSize, leftCrop, rightCrop, cancellationToken, leftStrokes, rightStrokes);
        }
        else if (brandedImage.SourceBatchItem is ImageBatchItem.SoloImage solo)
        {
            updated = await ProcessSoloImageAsync(solo.FilePath, overlay, null, brandedImage.SequenceIndex, brandedImage.BatchSize, leftCrop, cancellationToken, leftStrokes);
        }
        else if (brandedImage.SourceBatchItem is ImageBatchItem.LonePortrait lone)
        {
            updated = await ProcessLonePortraitAsync(lone.FilePath, overlay, null, brandedImage.SequenceIndex, brandedImage.BatchSize, leftCrop, cancellationToken, leftStrokes);
        }
        else if (brandedImage.SourceFilePaths.Count > 1)
        {
            updated = await ProcessPortraitPairAsync(brandedImage.SourceFilePaths[0], brandedImage.SourceFilePaths[1], overlay, null, brandedImage.SequenceIndex, brandedImage.BatchSize, leftCrop, rightCrop, cancellationToken, leftStrokes, rightStrokes);
        }
        else
        {
            var singlePath = brandedImage.SourceFilePaths.Count > 0 ? brandedImage.SourceFilePaths[0] : throw new InvalidOperationException("Source photo not available for re-branding.");
            updated = await ProcessLandscapeAsync(singlePath, overlay, null, brandedImage.SequenceIndex, brandedImage.BatchSize, leftCrop, cancellationToken, leftStrokes);
        }

        // Apply updated media in place
        brandedImage.ImageBytes = updated.ImageBytes;
        brandedImage.Preview = updated.Preview;
        brandedImage.CropSettings = leftCrop.Clone();
        if (newRightCrop != null)
        {
            brandedImage.RightCropSettings = rightCrop?.Clone();
        }
        brandedImage.OverlayPath = overlay;

        return brandedImage;
    }

    private static async Task<Stream> OpenPhotoAsync(string path, CancellationToken cancellationToken) =>
        HeicDecodeService.IsHeic(path)
            ? new MemoryStream(await HeicDecodeService.TranscodeToJpegBytesAsync(path, cancellationToken))
            : File.OpenRead(path);

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
