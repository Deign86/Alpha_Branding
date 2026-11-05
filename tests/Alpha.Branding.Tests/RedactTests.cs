using Alpha.Branding.Models;
using Alpha.Branding.Services;
using ImageMagick;
using ImageMagick.Drawing;
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
    public void Redact_Pixelate_AveragesFineDetailIntoCells()
    {
        using var photo = Stripes(600, 1000);
        using var plain = RenderSlot(Stripes(600, 1000));
        // Brush 0.1 x 600 px = 60 px, so cells are 30 px on a grid anchored at the frame corner.
        using var canvas = RenderSlot(photo, DiagonalMark(RedactEffect.Pixelate, 0.1));

        var center = canvas.PixelAt(300, 500);
        Assert.True(center.R > 90 && center.R < 165 && center.A == 255,
            $"Expected the 2 px stripes to average to opaque gray, found R={center.R} A={center.A}.");
        Assert.Equal(canvas.PixelAt(305, 490), canvas.PixelAt(325, 505));
        // Just past the round end cap (36 px from it): untouched.
        Assert.Equal(plain.PixelAt(300, 430), canvas.PixelAt(300, 430));
    }

    [Fact]
    public void Redact_Blur_SmoothsDetailInsideTheBrushOnly()
    {
        using var plain = RenderSlot(Stripes(600, 1000));
        using var canvas = RenderSlot(Stripes(600, 1000), DiagonalMark(RedactEffect.Blur, 0.1));

        var center = canvas.PixelAt(300, 500);
        Assert.True(center.R > 90 && center.R < 165 && center.A == 255,
            $"Expected the 2 px stripes to blur to opaque gray, found R={center.R} A={center.A}.");
        // Blur reads past the brush edge but must not write past it.
        Assert.Equal(plain.PixelAt(300, 430), canvas.PixelAt(300, 430));
    }

    [Theory]
    [InlineData(RedactEffect.Pixelate)]
    [InlineData(RedactEffect.Blur)]
    public void Redact_Marks_KeepPhotoColorsInsteadOfPainting(RedactEffect effect)
    {
        using var photo = new MagickImage(new MagickColor(200, 30, 30, 255), 600, 1000);
        using var canvas = RenderSlot(photo, DiagonalMark(effect, 0.1));

        var center = canvas.PixelAt(300, 500);
        Assert.True(Math.Abs(center.R - 200) < 3 && Math.Abs(center.G - 30) < 3 && Math.Abs(center.B - 30) < 3,
            $"A {effect} mark over a flat color must leave that color, found R={center.R} G={center.G} B={center.B}.");
    }

    [Fact]
    public void Redact_Clone_KeepsEffect()
    {
        var stroke = new RedactStroke { Effect = RedactEffect.Blur };
        stroke.AddPoint(new System.Windows.Point(0.2, 0.3));

        Assert.Equal(RedactEffect.Pixelate, new RedactStroke().Effect);
        Assert.Equal(RedactEffect.Blur, stroke.Clone().Effect);
        Assert.Equal(RedactEffect.Blur, new ImageCropSettings { RedactStrokes = [stroke] }.Clone().RedactStrokes[0].Effect);
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
            using (var image = Stripes(1200, 1000))
                await image.WriteAsync(input, MagickFormat.Png);
            using (var frame = new MagickImage(new MagickColor(0, 0, 0, 0), 1200, 1000))
                await frame.WriteAsync(overlay, MagickFormat.Png);

            var service = new ImageProcessingService();
            var branded = await service.ProcessSoloImageAsync(input, overlay, "Redact", 0, 1);
            Assert.IsType<ImageBatchItem.SoloImage>(branded.SourceBatchItem);

            var settings = new ImageCropSettings();
            settings.RedactStrokes.Add(DiagonalMark(RedactEffect.Pixelate, 0.1));
            await service.RebrandImageAsync(branded, settings, new ImageCropSettings(), overlay);

            // Rebrand must keep the mark: the stripes under it become one flat gray cell.
            using var decoded = new MagickImage(branded.ImageBytes);
            var cell = decoded.PixelAt(600, 500);
            Assert.True(cell.R > 90 && cell.R < 165,
                $"Expected the pixelated cell to survive rebrand but found R={cell.R} G={cell.G} B={cell.B}.");
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
    public void Redact_ResetCrop_KeepsMarks()
    {
        var stroke = new RedactStroke();
        stroke.AddPoint(new System.Windows.Point(0.25, 0.5));
        stroke.AddPoint(new System.Windows.Point(0.75, 0.5));
        var crop = new ImageCropSettings { PanX = 40, Zoom = 1.8, Rotation = 90, RedactStrokes = [stroke] };

        crop.Reset();

        // Reset is framing-only; silently dropping blur marks would un-hide private details.
        Assert.True(crop.IsDefault);
        Assert.True(crop.HasRedactions);
        Assert.Single(crop.RedactStrokes);
    }

    /// <summary>White photo with 2 px black stripes every 4 px: detail any pixelate or blur must remove.</summary>
    private static MagickImage Stripes(uint width, uint height)
    {
        var image = new MagickImage(MagickColors.White, width, height);
        var stripes = new Drawables().FillColor(MagickColors.Black);
        for (var x = 0; x < width; x += 4)
        {
            stripes.Rectangle(x, 0, x + 1, height);
        }

        stripes.Draw(image);
        return image;
    }

    /// <summary>A mark from 45% to 55% of the photo on both axes.</summary>
    private static RedactStroke DiagonalMark(RedactEffect effect, double brushSize)
    {
        var stroke = new RedactStroke { Effect = effect, BrushSizeNormalized = brushSize };
        stroke.AddPoint(new System.Windows.Point(0.45, 0.45));
        stroke.AddPoint(new System.Windows.Point(0.55, 0.55));
        return stroke;
    }

    private static MagickImage RenderSlot(MagickImage photo, params RedactStroke[] strokes) =>
        ImageProcessingService.RenderCroppedPhoto(photo, ImageProcessingService.HalfWidth, ImageProcessingService.TargetHeight,
            new ImageCropSettings { RedactStrokes = [.. strokes] });
}
