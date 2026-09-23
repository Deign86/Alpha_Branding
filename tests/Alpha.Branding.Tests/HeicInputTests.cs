using Alpha.Branding.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Alpha.Branding.Tests;

public class HeicInputTests
{
    [Theory]
    [InlineData(".heic")]
    [InlineData(".heif")]
    public void H01_LoadFilesAsyncAcceptsHeicAndHeif(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        try
        {
            // Filename-only fixture, generated here; these bytes are not a valid HEIC image.
            File.WriteAllBytes(path, [0x00]);
            Exception? failure = null;
            bool loaded = false;
            string[] selected = [];
            var thread = new Thread(() =>
            {
                try
                {
                    if (System.Windows.Application.Current == null)
                        _ = new App();
                    var window = new MainWindow();
                    loaded = window.LoadFilesAsync([path]).GetAwaiter().GetResult();
                    selected = window.ViewModel.SelectedFiles.ToArray();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();

            Assert.True(loaded);
            Assert.Equal(path, Assert.Single(selected));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void H02_MediaFilterListsHeicAndHeif()
    {
        var field = typeof(MainWindow).GetField("MediaFilter", BindingFlags.NonPublic | BindingFlags.Static);
        var filter = Assert.IsType<string>(Assert.IsAssignableFrom<FieldInfo>(field).GetRawConstantValue());
        var patterns = filter.Split('|');

        Assert.Contains("*.heic;*.heif", patterns[1]);
        Assert.Contains("*.heic;*.heif", patterns[3]);
    }

    [Fact]
    public async Task E01_CorruptHeicReportsErrorInsteadOfReturningBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.heic");
        try
        {
            // Corrupt fixture generated here, not a valid HEIC binary.
            await File.WriteAllTextAsync(path, "not a HEIC image");

            var error = await Record.ExceptionAsync(() => HeicDecodeService.TranscodeToJpegBytesAsync(path));

            Assert.NotNull(error);
            Assert.IsNotType<NotImplementedException>(error);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void E02_UppercaseHeicIsDetected() =>
        Assert.True(HeicDecodeService.IsHeic("photo.HEIC"));

    [Fact]
    public async Task R01_ExistingJpegPortraitPathStillWorks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");
        try
        {
            using (var image = new Image<Rgba32>(16, 32))
                await image.SaveAsJpegAsync(path);

            Assert.True(await ImageProcessingService.IsPortraitAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.png")]
    public void R02_NonHeicImagesAreNotDetected(string path) =>
        Assert.False(HeicDecodeService.IsHeic(path));
}
