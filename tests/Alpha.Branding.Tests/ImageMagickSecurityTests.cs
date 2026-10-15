using Alpha.Branding.Services;
using ImageMagick;
using System.IO;
using System.Runtime.CompilerServices;

namespace Alpha.Branding.Tests;

internal static class ImageMagickSecurityInitializer
{
    // The policy only takes effect if it is applied before the first ImageMagick call in the process.
    [ModuleInitializer]
    internal static void Apply() => ImageMagickSecurity.Apply();
}

public sealed class ImageMagickSecurityTests : IDisposable
{
    private const string SvgPayload =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"32\"><rect width=\"16\" height=\"32\" fill=\"red\"/></svg>";

    private const string MvgPayload =
        "push graphic-context\nviewbox 0 0 16 32\nfill red\nrectangle 0,0 16,32\npop graphic-context\n";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("Alpha_Branding_Policy_");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task SvgPayloadWithImageExtensionIsBlockedByPolicy()
    {
        // ImageMagick detects SVG from the content, so without the policy these files would render.
        var photo = WriteText("listing.jpg", SvgPayload);
        var overlay = WriteText("overlay.png", SvgPayload);
        var heic = WriteText("listing.heic", SvgPayload);
        var validPhoto = await WriteImageAsync("valid.jpg", MagickFormat.Jpeg, 16, 32);
        var validOverlay = await WriteImageAsync("valid-overlay.png", MagickFormat.Png, 1200, 1000);
        var service = new ImageProcessingService();

        await Assert.ThrowsAsync<MagickPolicyErrorException>(() => service.ProcessLandscapeAsync(photo, validOverlay, null, 0, 1));
        await Assert.ThrowsAsync<MagickPolicyErrorException>(() => service.ProcessLandscapeAsync(validPhoto, overlay, null, 0, 1));
        var heicError = await Assert.ThrowsAsync<InvalidDataException>(() => HeicDecodeService.TranscodeToJpegBytesAsync(heic));
        Assert.IsType<MagickPolicyErrorException>(heicError.InnerException);
        await Assert.ThrowsAnyAsync<MagickException>(() => ImageProcessingService.IsPortraitAsync(photo));
        Assert.False((await new TemplateService(_directory.FullName).ValidateTemplateAsync(overlay)).IsValid);
    }

    [Fact]
    public async Task MvgPayloadWithJpgExtensionIsRejected()
    {
        var photo = WriteText("listing.jpg", MvgPayload);
        var overlay = await WriteImageAsync("overlay.png", MagickFormat.Png, 1200, 1000);

        await Assert.ThrowsAnyAsync<MagickException>(() => new ImageProcessingService().ProcessLandscapeAsync(photo, overlay, null, 0, 1));
        await Assert.ThrowsAnyAsync<MagickException>(() => ImageProcessingService.IsPortraitAsync(photo));
    }

    [Theory]
    [InlineData("photo.jpg", MagickFormat.Jpeg)]
    [InlineData("photo.png", MagickFormat.Png)]
    [InlineData("photo.bmp", MagickFormat.Bmp)]
    [InlineData("photo.webp", MagickFormat.WebP)]
    public async Task AcceptedFormatsStillLoad(string fileName, MagickFormat format)
    {
        var photo = await WriteImageAsync(fileName, format, 16, 32);
        var overlay = await WriteImageAsync("overlay.png", MagickFormat.Png, 1200, 1000);

        Assert.True(await ImageProcessingService.IsPortraitAsync(photo));
        Assert.True((await new TemplateService(_directory.FullName).ValidateTemplateAsync(photo)).IsValid);
        var branded = await new ImageProcessingService().ProcessLandscapeAsync(photo, overlay, null, 0, 1);
        Assert.Equal(MagickFormat.Jpeg, new MagickImageInfo(branded.ImageBytes).Format);
    }

    [Fact]
    public async Task HeicStillLoads()
    {
        // 64x48 HEIC encoded with the Windows WIC HEIF encoder; Magick.NET cannot write HEIC.
        var photo = Path.Combine(AppContext.BaseDirectory, "Fixtures", "landscape-64x48.heic");
        var overlay = await WriteImageAsync("overlay.png", MagickFormat.Png, 1200, 1000);

        Assert.False(await ImageProcessingService.IsPortraitAsync(photo));
        var branded = await new ImageProcessingService().ProcessLandscapeAsync(photo, overlay, null, 0, 1);
        var output = new MagickImageInfo(branded.ImageBytes);
        Assert.Equal(MagickFormat.Jpeg, output.Format);
        Assert.Equal((uint)ImageProcessingService.TargetWidth, output.Width);
    }

    [Fact]
    public void ImagesWiderThanTheLimitAreRejected() =>
        Assert.Throws<MagickImageErrorException>(() => new MagickImage(MagickColors.Black, ImageMagickSecurity.MaxImageDimension + 1, 1));

    private string WriteText(string fileName, string content)
    {
        var path = Path.Combine(_directory.FullName, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<string> WriteImageAsync(string fileName, MagickFormat format, uint width, uint height)
    {
        var path = Path.Combine(_directory.FullName, fileName);
        using var image = new MagickImage(MagickColors.SteelBlue, width, height);
        await image.WriteAsync(path, format);
        return path;
    }
}
