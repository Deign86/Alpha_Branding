using Alpha.Branding.Models;
using Alpha.Branding.Services;
using ImageMagick;
using System.IO;
using System.Windows;

namespace Alpha.Branding.Tests;

public class RedactTests
{
    [Fact]
    public void Redact_Map_NormalizedPointMatchesPreviewScaleMath()
    {
        // Mirrors CropEditorWindow.UpdatePreview + ImageProcessingService.RenderCroppedPhoto math
        // for a pair slot (targetW = HalfWidth, targetH = TargetHeight).
        double srcW = 600;
        double srcH = 1000;
        double targetW = ImageProcessingService.HalfWidth;
        double targetH = ImageProcessingService.TargetHeight;
        var crop = new ImageCropSettings { PanX = 10, PanY = -20, Zoom = 1.5 };

        var scaleFit = Math.Min(targetW / srcW, targetH / srcH);
        var scaleFill = Math.Max(targetW / srcW, targetH / srcH);
        var baseScale = scaleFill;
        var effectiveScale = baseScale * crop.Zoom;
        var scaledW = Math.Max(1.0, srcW * effectiveScale);
        var scaledH = Math.Max(1.0, srcH * effectiveScale);
        var centerX = (targetW - scaledW) / 2.0;
        var centerY = (targetH - scaledH) / 2.0;
        var drawX = centerX + crop.PanX;
        var drawY = centerY + crop.PanY;

        // Normalized canvas point -> source px: src = (canvas - draw) / effectiveScale.
        double nx = 0.5;
        double ny = 0.5;
        double canvasX = nx * targetW;
        double canvasY = ny * targetH;
        double expectedSrcX = (canvasX - drawX) / effectiveScale;
        double expectedSrcY = (canvasY - drawY) / effectiveScale;

        // Right slot shares the same slot math; only the composited canvas X is offset by HalfWidth.
        double rightCanvasX = ImageProcessingService.HalfWidth + canvasX;
        double rightSlotSrcX = ((rightCanvasX - ImageProcessingService.HalfWidth) - drawX) / effectiveScale;
        Assert.Equal(expectedSrcX, rightSlotSrcX, precision: 9);

        var mapped = ImageProcessingService.MapNormalizedToSourcePx(nx, ny, crop, (int)srcW, (int)srcH,
            (int)targetW, (int)targetH);
        Assert.Equal(Math.Clamp(expectedSrcX, 0, srcW), mapped.X, precision: 9);
        Assert.Equal(Math.Clamp(expectedSrcY, 0, srcH), mapped.Y, precision: 9);
    }

    [Fact]
    public void Redact_Render_StrokesProduceOpaqueBlotch()
    {
        using var photo = new MagickImage(new MagickColor(255, 255, 255, 255), 600, 1000);
        var stroke = new RedactStroke { R = 0, G = 0, B = 0 };
        stroke.AddPoint(new System.Windows.Point(0.45, 0.45));
        stroke.AddPoint(new System.Windows.Point(0.55, 0.55));

        var settings = new ImageCropSettings();
        settings.RedactStrokes.Add(stroke);
        using var canvas = ImageProcessingService.RenderCroppedPhoto(
            photo,
            ImageProcessingService.HalfWidth,
            ImageProcessingService.TargetHeight,
            settings,
            isSoloPortrait: false);

        // RED: once strokes render, the bbox center must be an opaque blotch, not the white photo.
        var blotch = canvas.PixelAt(300, 500);
        Assert.True(blotch.R < 30 && blotch.G < 30 && blotch.B < 30 && blotch.A == 255,
            $"RED: expected opaque blotch in stroke bbox but found R={blotch.R} G={blotch.G} B={blotch.B} A={blotch.A}.");
    }

    [Fact]
    public void Redact_Render_EmptyStrokesMatchUnredactedPixels()
    {
        using var photo = new MagickImage(new MagickColor(255, 255, 255, 255), 600, 1000);
        using var withoutRedactions = ImageProcessingService.RenderCroppedPhoto(
            photo.Clone(), ImageProcessingService.HalfWidth, ImageProcessingService.TargetHeight);
        using var withEmptyRedactions = ImageProcessingService.RenderCroppedPhoto(
            photo.Clone(), ImageProcessingService.HalfWidth, ImageProcessingService.TargetHeight,
            new ImageCropSettings { RedactStrokes = [] });

        // Compare raw pixels, not encoded PNGs: ImageMagick writes date:create/date:modify
        // text chunks, so two PNG writes straddling a second boundary differ in bytes.
        Assert.Equal(withoutRedactions.Width, withEmptyRedactions.Width);
        Assert.Equal(withoutRedactions.Height, withEmptyRedactions.Height);
        using var unredactedPixels = withoutRedactions.GetPixels();
        using var emptyRedactionsPixels = withEmptyRedactions.GetPixels();
        Assert.Equal(
            unredactedPixels.ToByteArray(PixelMapping.RGBA),
            emptyRedactionsPixels.ToByteArray(PixelMapping.RGBA));
    }

    [Fact]
    public async Task Redact_Rebrand_PreservesStrokes()
    {
        var input = Path.GetTempFileName();
        var overlay = Path.GetTempFileName();
        try
        {
            using (var image = new MagickImage(new MagickColor(255, 255, 255, 255), 1200, 1000))
                await image.WriteAsync(input, MagickFormat.Png);
            using (var frame = new MagickImage(new MagickColor(0, 0, 0, 0), 1200, 1000))
                await frame.WriteAsync(overlay, MagickFormat.Png);

            var service = new ImageProcessingService();
            var branded = await service.ProcessSoloImageAsync(input, overlay, "Redact", 0, 1);
            Assert.IsType<ImageBatchItem.SoloImage>(branded.SourceBatchItem);

            var stroke = new RedactStroke { R = 0, G = 0, B = 0 };
            stroke.AddPoint(new System.Windows.Point(0.45, 0.45));
            stroke.AddPoint(new System.Windows.Point(0.55, 0.55));

            var settings = new ImageCropSettings();
            settings.RedactStrokes.Add(stroke);
            await service.RebrandImageAsync(branded, settings, new ImageCropSettings(), overlay);

            // RED: rebrand must preserve strokes, so the blotch must survive in the new bytes.
            using var decoded = new MagickImage(branded.ImageBytes);
            var blotch = decoded.PixelAt(600, 500);
            Assert.True(blotch.R < 30 && blotch.G < 30 && blotch.B < 30,
                $"RED: expected blotch to survive rebrand but found R={blotch.R} G={blotch.G} B={blotch.B}.");
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(overlay)) File.Delete(overlay);
        }
    }

    [Fact]
    public void Redact_Slots_LeftAndRightListsAreIndependent()
    {
        var stroke = new RedactStroke();
        stroke.AddPoint(new System.Windows.Point(0.25, 0.5));
        var left = new ImageCropSettings { RedactStrokes = [stroke] };
        var right = new ImageCropSettings();
        var branded = new BrandedImage
        {
            FileName = "pair.jpg",
            Preview = new System.Windows.Media.Imaging.BitmapImage(),
            CropSettings = left.Clone(),
            RightCropSettings = right.Clone()
        };

        Assert.Single(branded.CropSettings!.RedactStrokes);
        Assert.Empty(branded.RightCropSettings!.RedactStrokes);
        branded.RightCropSettings.RedactStrokes.Add(stroke.Clone());
        Assert.Single(branded.CropSettings.RedactStrokes);
        Assert.Single(branded.RightCropSettings.RedactStrokes);
        Assert.NotSame(branded.CropSettings.RedactStrokes[0], branded.RightCropSettings.RedactStrokes[0]);
    }

    [Fact]
    public void Redact_ResetCrop_KeepsPenMarks()
    {
        var stroke = new RedactStroke();
        stroke.AddPoint(new System.Windows.Point(0.25, 0.5));
        stroke.AddPoint(new System.Windows.Point(0.75, 0.5));
        var crop = new ImageCropSettings { PanX = 40, Zoom = 1.8, Rotation = 90, RedactStrokes = [stroke] };

        crop.Reset();

        // Reset is framing-only; silently dropping pen marks would un-hide private details.
        Assert.True(crop.IsDefault);
        Assert.True(crop.HasRedactions);
        Assert.Single(crop.RedactStrokes);
    }
}
