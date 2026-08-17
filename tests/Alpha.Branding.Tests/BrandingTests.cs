using Alpha.Branding.Models;
using Alpha.Branding.Services;
using Alpha.Branding.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO.Compression;

namespace Alpha.Branding.Tests;

public class FileNameGeneratorTests
{
    [Fact]
    public void SanitizesControlsSeparatorsAndTrailingPunctuation()
    {
        Assert.Equal("Listing_01.webp", FileNameGenerator.Generate(" Listing:/\0. ", 0, 10));
        Assert.Equal("Home_100.webp", FileNameGenerator.Generate("Home", 99, 100));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("Lpt9")]
    public void FallsBackForReservedNames(string prefix) =>
        Assert.Equal("AlphaPremier_Photo", FileNameGenerator.FolderName(prefix));

    [Fact]
    public void CapsLongPrefixAndSanitizesExtension()
    {
        var name = FileNameGenerator.Generate(new string('x', 500), 0, 1, ".WEBP!");

        Assert.Equal(120, name.Length);
        Assert.EndsWith("_01.webp", name);
    }
}

public class ImageProcessingTests
{
    [Fact]
    public async Task ProcessingProducesExactWebpDimensionsAndCompositesOverlay()
    {
        var input = Path.GetTempFileName();
        var overlay = Path.GetTempFileName();
        try
        {
            using (var image = new Image<Rgba32>(32, 24, new Rgba32(255, 0, 0, 255)))
                await image.SaveAsPngAsync(input);
            using (var frame = new Image<Rgba32>(8, 8, new Rgba32(0, 0, 255, 255)))
                await frame.SaveAsPngAsync(overlay);

            var result = await new ImageProcessingService().ProcessAsync(input, overlay, "Test", 0, 1);
            using var decoded = Image.Load<Rgba32>(result.WebpBytes);
            var centerPixel = decoded[600, 500];

            Assert.Equal(1200, decoded.Width);
            Assert.Equal(1000, decoded.Height);
            Assert.Equal(WebpFormat.Instance, Image.DetectFormat(result.WebpBytes));
            Assert.True(centerPixel.B > centerPixel.R, "The opaque blue overlay should be visible in the composed output.");
        }
        finally
        {
            File.Delete(input);
            File.Delete(overlay);
        }
    }
}

public class ZipSafetyTests
{
    [Fact]
    public async Task ZipExportContainsExpectedScopedEntries()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var vm = new MainWindowViewModel(new ImageProcessingService());
            var bytes = new byte[] { 1, 2, 3 };
            vm.Results.Add(new BrandedImage
            {
                FileName = FileNameGenerator.Generate("../unsafe", 0, 1),
                WebpBytes = bytes,
                Preview = new System.Windows.Media.Imaging.BitmapImage()
            });
            vm.Prefix = "../unsafe";
            var path = Path.Combine(directory.FullName, "result.zip");

            await vm.ExportZipAsync(path);

            using var archive = ZipFile.OpenRead(path);
            var entry = Assert.Single(archive.Entries);
            Assert.Equal($"{FileNameGenerator.FolderName(vm.Prefix)}/{vm.Results[0].FileName}", entry.FullName);
            Assert.DoesNotContain("..", entry.FullName);
            Assert.Equal(bytes, await ReadEntryAsync(entry));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}
