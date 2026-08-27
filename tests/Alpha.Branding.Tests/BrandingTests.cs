using Alpha.Branding.Models;
using Alpha.Branding.Services;
using Alpha.Branding.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using System.IO.Compression;

namespace Alpha.Branding.Tests;

public class FileNameGeneratorTests
{
    [Fact]
    public void SanitizesControlsSeparatorsAndTrailingPunctuation()
    {
        Assert.Equal("Listing_01.jpg", FileNameGenerator.Generate(" Listing:/\0. ", 0, 10));
        Assert.Equal("Home_100.jpg", FileNameGenerator.Generate("Home", 99, 100));
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
        var name = FileNameGenerator.Generate(new string('x', 500), 0, 1, ".JPG!");

        Assert.Equal(120, name.Length);
        Assert.EndsWith("_01.jpg", name);
    }

    [Theory]
    [InlineData("Bahay_Kubo_#1", "Bahay_Kubo_#1_01.jpg")]
    [InlineData("Mandaluyong / Condo * 101?", "Mandaluyong  Condo  101_01.jpg")]
    [InlineData("  Maynila_Proyekto_  ", "Maynila_Proyekto__01.jpg")]
    public void HandlesUnicodeSpecialCharactersAndWhitespace(string input, string expected)
    {
        Assert.Equal(expected, FileNameGenerator.Generate(input, 0, 5));
    }
}

public class UiInitializationTests
{
    [Fact]
    public void CanInstantiateWindowsWithoutException()
    {
        var thread = new System.Threading.Thread(() =>
        {
            if (System.Windows.Application.Current == null)
                _ = new App();
            var window = new MainWindow();
            Assert.NotNull(window);
            var preview = new PreviewWindow(new List<BrandedImage>(), 0);
            Assert.NotNull(preview);
            var confirmation = new SessionConfirmationDialog("Test Title", "Test Message");
            Assert.NotNull(confirmation);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void BrandedImageRaisesPropertyChangedWhenFileNameChanges()
    {
        var item = new BrandedImage
        {
            FileName = "Initial_01.jpg",
            ImageBytes = Array.Empty<byte>(),
            Preview = new System.Windows.Media.Imaging.BitmapImage(),
            SequenceIndex = 0,
            BatchSize = 1
        };

        var fired = false;
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(BrandedImage.FileName))
                fired = true;
        };

        item.FileName = "Updated_01.jpg";
        Assert.True(fired, "BrandedImage must raise PropertyChanged for UI data binding when FileName is mutated.");
        Assert.Equal("Updated_01.jpg", item.FileName);
    }

    [Fact]
    public void MainWindowLoadFilesFiltersSupportedExtensionsOnly()
    {
        var thread = new System.Threading.Thread(() =>
        {
            if (System.Windows.Application.Current == null)
                _ = new App();

            var tempImg = Path.GetTempFileName() + ".png";
            var tempTxt = Path.GetTempFileName() + ".txt";
            try
            {
                File.WriteAllText(tempImg, "fake image");
                File.WriteAllText(tempTxt, "text file");

                var window = new MainWindow();
                window.LoadFiles(new[] { tempImg, tempTxt, "non_existent.jpg" });

                var vm = (MainWindowViewModel)window.DataContext;
                Assert.Single(vm.SelectedFiles);
                Assert.Equal(tempImg, vm.SelectedFiles[0]);
            }
            finally
            {
                if (File.Exists(tempImg)) File.Delete(tempImg);
                if (File.Exists(tempTxt)) File.Delete(tempTxt);
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void MainWindowViewModelCanApplyAndCanExportStateTransitions()
    {
        var vm = new MainWindowViewModel(new ImageProcessingService());
        Assert.False(vm.CanApply);
        Assert.False(vm.CanExport);

        vm.SelectedFiles = new[] { "photo1.jpg" };
        Assert.True(vm.CanApply);
        Assert.False(vm.CanExport);

        vm.Results.Add(new BrandedImage
        {
            FileName = "Test_01.jpg",
            ImageBytes = Array.Empty<byte>(),
            Preview = new System.Windows.Media.Imaging.BitmapImage(),
            SequenceIndex = 0,
            BatchSize = 1
        });
        Assert.True(vm.CanExport);
    }

    [Fact]
    public void PreviewWindowHasMultiplePhotosReflectsResultsCount()
    {
        var thread = new System.Threading.Thread(() =>
        {
            if (System.Windows.Application.Current == null)
                _ = new App();

            var item1 = new BrandedImage
            {
                FileName = "Test_01.jpg",
                ImageBytes = Array.Empty<byte>(),
                Preview = new System.Windows.Media.Imaging.BitmapImage(),
                SequenceIndex = 0,
                BatchSize = 1
            };
            var item2 = new BrandedImage
            {
                FileName = "Test_02.jpg",
                ImageBytes = Array.Empty<byte>(),
                Preview = new System.Windows.Media.Imaging.BitmapImage(),
                SequenceIndex = 1,
                BatchSize = 2
            };

            var singlePreview = new PreviewWindow(new[] { item1 }, 0);
            Assert.False(singlePreview.HasMultiplePhotos);

            var multiPreview = new PreviewWindow(new[] { item1, item2 }, 0);
            Assert.True(multiPreview.HasMultiplePhotos);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}


public class ImageProcessingTests
{
    [Fact]
    public async Task ProcessingProducesExactJpegDimensionsAndCompositesOverlay()
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
            using var decoded = Image.Load<Rgba32>(result.ImageBytes);
            var centerPixel = decoded[600, 500];

            Assert.Equal(1200, decoded.Width);
            Assert.Equal(1000, decoded.Height);
            Assert.Equal(JpegFormat.Instance, Image.DetectFormat(result.ImageBytes));
            Assert.True(centerPixel.B > centerPixel.R, "The opaque blue overlay should be visible in the composed output.");
        }
        finally
        {
            File.Delete(input);
            File.Delete(overlay);
        }
    }

    [Fact]
    public async Task DetectsPortraitAndLandscapeCorrectly()
    {
        var portraitFile = Path.GetTempFileName();
        var landscapeFile = Path.GetTempFileName();
        try
        {
            using (var portrait = new Image<Rgba32>(1000, 1500))
                await portrait.SaveAsPngAsync(portraitFile);
            using (var landscape = new Image<Rgba32>(1500, 1000))
                await landscape.SaveAsPngAsync(landscapeFile);

            Assert.True(await ImageProcessingService.IsPortraitAsync(portraitFile));
            Assert.False(await ImageProcessingService.IsPortraitAsync(landscapeFile));
        }
        finally
        {
            File.Delete(portraitFile);
            File.Delete(landscapeFile);
        }
    }

    [Fact]
    public async Task PlanBatchPairsPortraitImagesAndKeepsLandscapeSingle()
    {
        var p1 = Path.GetTempFileName();
        var p2 = Path.GetTempFileName();
        var p3 = Path.GetTempFileName();
        var p4 = Path.GetTempFileName();
        var l1 = Path.GetTempFileName();
        var l2 = Path.GetTempFileName();

        try
        {
            using (var p = new Image<Rgba32>(600, 1000))
            {
                await p.SaveAsPngAsync(p1);
                await p.SaveAsPngAsync(p2);
                await p.SaveAsPngAsync(p3);
                await p.SaveAsPngAsync(p4);
            }
            using (var l = new Image<Rgba32>(1200, 1000))
            {
                await l.SaveAsPngAsync(l1);
                await l.SaveAsPngAsync(l2);
            }

            // Case 1: 2 portraits -> 1 pair
            var plan2P = await ImageProcessingService.PlanBatchAsync(new[] { p1, p2 });
            Assert.Single(plan2P);
            var pair = Assert.IsType<ImageBatchItem.PortraitPair>(plan2P[0]);
            Assert.Equal(p1, pair.LeftFilePath);
            Assert.Equal(p2, pair.RightFilePath);

            // Case 2: 4 portraits -> 2 pairs
            var plan4P = await ImageProcessingService.PlanBatchAsync(new[] { p1, p2, p3, p4 });
            Assert.Equal(2, plan4P.Count);
            Assert.IsType<ImageBatchItem.PortraitPair>(plan4P[0]);
            Assert.IsType<ImageBatchItem.PortraitPair>(plan4P[1]);

            // Case 3: Mixed: L1, P1, L2, P2, P3 -> P1+P2 pair, P3 matches with L2 side-by-side, L1 remains single
            var planMixed = await ImageProcessingService.PlanBatchAsync(new[] { l1, p1, l2, p2, p3 });
            Assert.Equal(3, planMixed.Count);
            Assert.IsType<ImageBatchItem.Landscape>(planMixed[0]);
            var mixedPair1 = Assert.IsType<ImageBatchItem.PortraitPair>(planMixed[1]);
            Assert.Equal(p1, mixedPair1.LeftFilePath);
            Assert.Equal(p2, mixedPair1.RightFilePath);
            var mixedPair2 = Assert.IsType<ImageBatchItem.PortraitPair>(planMixed[2]);
            Assert.Equal(p3, mixedPair2.LeftFilePath);
            Assert.Equal(l2, mixedPair2.RightFilePath);

            // Case 4: 1 portrait + 1 landscape -> 1 pair side-by-side (never lone)
            var plan1P1L = await ImageProcessingService.PlanBatchAsync(new[] { p1, l1 });
            Assert.Single(plan1P1L);
            var pair1P1L = Assert.IsType<ImageBatchItem.PortraitPair>(plan1P1L[0]);
            Assert.Equal(p1, pair1P1L.LeftFilePath);
            Assert.Equal(l1, pair1P1L.RightFilePath);

            // Case 5: 1 landscape + 1 portrait -> 1 pair side-by-side (never lone)
            var plan1L1P = await ImageProcessingService.PlanBatchAsync(new[] { l1, p1 });
            Assert.Single(plan1L1P);
            var pair1L1P = Assert.IsType<ImageBatchItem.PortraitPair>(plan1L1P[0]);
            Assert.Equal(p1, pair1L1P.LeftFilePath);
            Assert.Equal(l1, pair1L1P.RightFilePath);

            // Case 6: Single lone portrait (no landscape in batch) -> duplicate side-by-side (never lone)
            var plan1P = await ImageProcessingService.PlanBatchAsync(new[] { p1 });
            Assert.Single(plan1P);
            var pair1P = Assert.IsType<ImageBatchItem.PortraitPair>(plan1P[0]);
            Assert.Equal(p1, pair1P.LeftFilePath);
            Assert.Equal(p1, pair1P.RightFilePath);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
            File.Delete(p3);
            File.Delete(p4);
            File.Delete(l1);
            File.Delete(l2);
        }
    }

    [Fact]
    public async Task ProcessPortraitPairCompositesLeftAndRightSideBySide()
    {
        var leftFile = Path.GetTempFileName();
        var rightFile = Path.GetTempFileName();
        var overlayFile = Path.GetTempFileName();

        try
        {
            // Left photo is pure red (255, 0, 0)
            using (var left = new Image<Rgba32>(600, 1000, new Rgba32(255, 0, 0, 255)))
                await left.SaveAsPngAsync(leftFile);

            // Right photo is pure green (0, 255, 0)
            using (var right = new Image<Rgba32>(600, 1000, new Rgba32(0, 255, 0, 255)))
                await right.SaveAsPngAsync(rightFile);

            // Overlay is transparent with a small blue box at top-right
            using (var overlay = new Image<Rgba32>(1200, 1000, new Rgba32(0, 0, 0, 0)))
            {
                overlay[1100, 50] = new Rgba32(0, 0, 255, 255);
                await overlay.SaveAsPngAsync(overlayFile);
            }

            var service = new ImageProcessingService();
            var result = await service.ProcessPortraitPairAsync(leftFile, rightFile, overlayFile, "PairTest", 0, 1);

            Assert.NotNull(result);
            Assert.Equal("PairTest_01.jpg", result.FileName);

            using var decoded = Image.Load<Rgba32>(result.ImageBytes);
            Assert.Equal(1200, decoded.Width);
            Assert.Equal(1000, decoded.Height);

            // Left side pixel (x=200, y=500) should be predominantly Red
            var leftPixel = decoded[200, 500];
            Assert.True(leftPixel.R > 200 && leftPixel.G < 50, "Left side should contain left red image.");

            // Right side pixel (x=900, y=500) should be predominantly Green
            var rightPixel = decoded[900, 500];
            Assert.True(rightPixel.G > 200 && rightPixel.R < 50, "Right side should contain right green image.");
        }
        finally
        {
            File.Delete(leftFile);
            File.Delete(rightFile);
            File.Delete(overlayFile);
        }
    }

    [Fact]
    public async Task ProcessLonePortraitCompositesCentered()
    {
        var loneFile = Path.GetTempFileName();
        var overlayFile = Path.GetTempFileName();

        try
        {
            using (var photo = new Image<Rgba32>(600, 1000, new Rgba32(255, 0, 0, 255)))
                await photo.SaveAsPngAsync(loneFile);
            using (var overlay = new Image<Rgba32>(1200, 1000, new Rgba32(0, 0, 0, 0)))
                await overlay.SaveAsPngAsync(overlayFile);

            var service = new ImageProcessingService();
            var result = await service.ProcessLonePortraitAsync(loneFile, overlayFile, "LoneTest", 0, 1);

            Assert.NotNull(result);
            using var decoded = Image.Load<Rgba32>(result.ImageBytes);
            Assert.Equal(1200, decoded.Width);
            Assert.Equal(1000, decoded.Height);

            // Center pixel (x=600, y=500) should be Red
            var centerPixel = decoded[600, 500];
            Assert.True(centerPixel.R > 200);

            // Left edge pixel (x=50, y=500) should be the dark background
            var darkEdgePixel = decoded[50, 500];
            Assert.True(darkEdgePixel.R < 30 && darkEdgePixel.G < 30 && darkEdgePixel.B < 30);
        }
        finally
        {
            File.Delete(loneFile);
            File.Delete(overlayFile);
        }
    }

    [Fact]
    public async Task ViewModelApplyProcessesPortraitPairsCorrectly()
    {
        var p1 = Path.GetTempFileName();
        var p2 = Path.GetTempFileName();
        var overlay = Path.GetTempFileName();

        try
        {
            using (var portrait = new Image<Rgba32>(600, 1000, new Rgba32(255, 255, 255, 255)))
            {
                await portrait.SaveAsPngAsync(p1);
                await portrait.SaveAsPngAsync(p2);
            }
            using (var frame = new Image<Rgba32>(1200, 1000, new Rgba32(0, 0, 0, 0)))
                await frame.SaveAsPngAsync(overlay);

            var vm = new MainWindowViewModel(new ImageProcessingService())
            {
                SelectedFiles = new[] { p1, p2 },
                Prefix = "Listing"
            };

            await vm.ApplyAsync(overlay);

            // 2 portrait photos should result in 1 paired branded output
            Assert.Single(vm.Results);
            Assert.Equal("Listing_01.jpg", vm.Results[0].FileName);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
            File.Delete(overlay);
        }
    }

    [Fact]
    public async Task ViewModelApplyPairsPortraitWithLandscapeWhenOddPortraitExists()
    {
        var p1 = Path.GetTempFileName();
        var l1 = Path.GetTempFileName();
        var overlay = Path.GetTempFileName();

        try
        {
            using (var portrait = new Image<Rgba32>(600, 1000, new Rgba32(255, 0, 0, 255)))
                await portrait.SaveAsPngAsync(p1);
            using (var landscape = new Image<Rgba32>(1200, 1000, new Rgba32(0, 255, 0, 255)))
                await landscape.SaveAsPngAsync(l1);
            using (var frame = new Image<Rgba32>(1200, 1000, new Rgba32(0, 0, 0, 0)))
                await frame.SaveAsPngAsync(overlay);

            var vm = new MainWindowViewModel(new ImageProcessingService())
            {
                SelectedFiles = new[] { p1, l1 },
                Prefix = "Listing"
            };

            await vm.ApplyAsync(overlay);

            // 1 portrait + 1 landscape should be paired side-by-side into 1 output
            Assert.Single(vm.Results);
            Assert.Equal("Listing_01.jpg", vm.Results[0].FileName);

            using var decoded = Image.Load<Rgba32>(vm.Results[0].ImageBytes);
            Assert.Equal(1200, decoded.Width);
            Assert.Equal(1000, decoded.Height);

            // Left side pixel (x=200, y=500) contains red portrait
            var leftPixel = decoded[200, 500];
            Assert.True(leftPixel.R > 200);

            // Right side pixel (x=900, y=500) contains green landscape
            var rightPixel = decoded[900, 500];
            Assert.True(rightPixel.G > 200);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(l1);
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
                ImageBytes = bytes,
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

public class AntiSlopAndResilienceTests
{
    [Fact]
    public async Task PlanBatchPropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var tempImg = Path.GetTempFileName();
        try
        {
            using (var img = new Image<Rgba32>(100, 100))
                await img.SaveAsPngAsync(tempImg);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await ImageProcessingService.PlanBatchAsync(new[] { tempImg }, cts.Token);
            });
        }
        finally
        {
            if (File.Exists(tempImg)) File.Delete(tempImg);
        }
    }

    [Fact]
    public async Task CreatePreviewProducesValidFrozenBitmapImageFromJpegBytes()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            using (var img = new Image<Rgba32>(1200, 1000, new Rgba32(200, 150, 50, 255)))
                await img.SaveAsJpegAsync(tempFile);

            var bytes = await File.ReadAllBytesAsync(tempFile);
            var bitmap = ImageProcessingService.CreatePreview(bytes);

            Assert.NotNull(bitmap);
            Assert.True(bitmap.IsFrozen, "Preview BitmapImage must be frozen for thread-safe cross-UI usage.");
            Assert.Equal(1200, bitmap.PixelWidth);
            Assert.Equal(1000, bitmap.PixelHeight);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void AppLogCrashDoesNotThrowEvenOnSimulatedExceptions()
    {
        var ex = new InvalidOperationException("Simulated crash for resilience test");
        var record = Record.Exception(() => App.LogCrash(ex));
        Assert.Null(record);
    }
}

public sealed class TestSessionConfirmationService : ISessionConfirmationService
{
    public bool PromptCalled { get; private set; }
    public string? LastPromptTitle { get; private set; }
    public string? LastPromptMessage { get; private set; }
    public SessionPromptResult DesiredPromptResult { get; set; } = SessionPromptResult.Cancel;
    public string? DesiredSaveZipPath { get; set; }
    public bool PromptSaveZipCalled { get; private set; }
    public string? DesiredExportFolderPath { get; set; }
    public bool PromptExportFolderCalled { get; private set; }

    public SessionPromptResult PromptUnsavedEdits(string title, string message)
    {
        PromptCalled = true;
        LastPromptTitle = title;
        LastPromptMessage = message;
        return DesiredPromptResult;
    }

    public string? PromptSaveZip(string defaultFileName)
    {
        PromptSaveZipCalled = true;
        return DesiredSaveZipPath;
    }

    public string? PromptExportFolder()
    {
        PromptExportFolderCalled = true;
        return DesiredExportFolderPath;
    }
}

public class SessionWorkflowSafetyTests
{
    private static async Task<(string Photo1, string Photo2, string Overlay)> CreateSampleImagesAsync()
    {
        var p1 = Path.GetTempFileName() + ".png";
        var p2 = Path.GetTempFileName() + ".png";
        var overlay = Path.GetTempFileName() + ".png";

        using (var img1 = new Image<Rgba32>(1200, 1000, new Rgba32(255, 0, 0, 255)))
            await img1.SaveAsPngAsync(p1);

        using (var img2 = new Image<Rgba32>(1200, 1000, new Rgba32(0, 255, 0, 255)))
            await img2.SaveAsPngAsync(p2);

        using (var frame = new Image<Rgba32>(1200, 1000, new Rgba32(0, 0, 255, 128)))
            await frame.SaveAsPngAsync(overlay);

        return (p1, p2, overlay);
    }

    [Fact]
    public async Task NewSelectionWithExistingResultsPromptsConfirmation()
    {
        var (p1, p2, overlay) = await CreateSampleImagesAsync();
        try
        {
            var promptService = new TestSessionConfirmationService
            {
                DesiredPromptResult = SessionPromptResult.Cancel
            };
            var vm = new MainWindowViewModel(new ImageProcessingService(), promptService)
            {
                SelectedFiles = new[] { p1 },
                Prefix = "Initial"
            };

            // Process first batch
            var appliedInitial = await vm.ApplyWorkflowAsync(overlay);
            Assert.True(appliedInitial);
            Assert.False(promptService.PromptCalled);
            Assert.Single(vm.Results);
            Assert.Equal("Initial_01.jpg", vm.Results[0].FileName);
            Assert.False(vm.HasUnsavedEdits);

            // Select new photos without editing existing results
            vm.SelectedFiles = new[] { p2 };
            Assert.False(vm.HasUnsavedEdits);
            Assert.False(vm.HasApplyWarning);
            Assert.True(vm.HasApplyHint);

            // Apply branding to new selection -> prompts confirmation because results exist
            var appliedSecond = await vm.ApplyWorkflowAsync(overlay);
            Assert.False(appliedSecond);
            Assert.True(promptService.PromptCalled, "Modal prompt must appear when active results exist.");
            Assert.Equal("Start a new branding session?", promptService.LastPromptTitle);
            Assert.Contains("replace the active items", promptService.LastPromptMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Single(vm.Results);
            Assert.Equal("Initial_01.jpg", vm.Results[0].FileName);

            // Now allow discard and continue
            promptService.DesiredPromptResult = SessionPromptResult.DiscardAndContinue;
            var appliedThird = await vm.ApplyWorkflowAsync(overlay);
            Assert.True(appliedThird);
            Assert.Single(vm.Results);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
            File.Delete(overlay);
        }
    }

    [Fact]
    public async Task NewSelectionWithDirtyEditsPromptsConfirmation()
    {
        var (p1, p2, overlay) = await CreateSampleImagesAsync();
        try
        {
            var promptService = new TestSessionConfirmationService
            {
                DesiredPromptResult = SessionPromptResult.Cancel
            };
            var vm = new MainWindowViewModel(new ImageProcessingService(), promptService)
            {
                SelectedFiles = new[] { p1 },
                Prefix = "BatchA"
            };

            await vm.ApplyWorkflowAsync(overlay);
            Assert.False(vm.HasUnsavedEdits);

            // User edits the prefix after processing -> marks session dirty
            vm.Prefix = "CustomRenamedBatch";
            Assert.True(vm.HasUnsavedEdits);
            Assert.Equal("CustomRenamedBatch_01.jpg", vm.Results[0].FileName);

            // User selects new photos
            vm.SelectedFiles = new[] { p2 };
            Assert.Equal("Unsaved edits in current session.", vm.ApplyStatusHint);
            Assert.True(vm.HasApplyWarning);

            // Apply branding with dirty edits
            var applied = await vm.ApplyWorkflowAsync(overlay);
            Assert.False(applied);
            Assert.True(promptService.PromptCalled);
            Assert.Equal("Start a new branding session?", promptService.LastPromptTitle);
            Assert.Contains("unsaved edits", promptService.LastPromptMessage, StringComparison.OrdinalIgnoreCase);

            // Session state must remain untouched on cancel
            Assert.Single(vm.Results);
            Assert.Equal("CustomRenamedBatch_01.jpg", vm.Results[0].FileName);
            Assert.True(vm.HasUnsavedEdits);
            Assert.Contains("canceled", vm.Status, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
            File.Delete(overlay);
        }
    }

    [Fact]
    public async Task DiscardEditsAndContinueClearsOldSessionAndStartsNewSession()
    {
        var (p1, p2, overlay) = await CreateSampleImagesAsync();
        try
        {
            var promptService = new TestSessionConfirmationService
            {
                DesiredPromptResult = SessionPromptResult.DiscardAndContinue
            };
            var vm = new MainWindowViewModel(new ImageProcessingService(), promptService)
            {
                SelectedFiles = new[] { p1 },
                Prefix = "Batch1"
            };

            await vm.ApplyWorkflowAsync(overlay);
            vm.Prefix = "DirtyPrefix";
            Assert.True(vm.HasUnsavedEdits);

            // New selection
            vm.SelectedFiles = new[] { p2 };

            var applied = await vm.ApplyWorkflowAsync(overlay);
            Assert.True(applied);
            Assert.True(promptService.PromptCalled);

            // Old edits cleared, new session active and clean
            Assert.Single(vm.Results);
            Assert.Equal("DirtyPrefix_01.jpg", vm.Results[0].FileName);
            Assert.False(vm.HasUnsavedEdits);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
            File.Delete(overlay);
        }
    }

    [Fact]
    public async Task SaveAndContinueExportsZipThenBeginsNewSession()
    {
        var (p1, p2, overlay) = await CreateSampleImagesAsync();
        var tempZip = Path.GetTempFileName() + ".zip";
        try
        {
            var promptService = new TestSessionConfirmationService
            {
                DesiredPromptResult = SessionPromptResult.SaveAndContinue,
                DesiredSaveZipPath = tempZip
            };
            var vm = new MainWindowViewModel(new ImageProcessingService(), promptService)
            {
                SelectedFiles = new[] { p1 },
                Prefix = "ArchivedProperty"
            };

            await vm.ApplyWorkflowAsync(overlay);
            vm.Prefix = "EditedProperty";
            Assert.True(vm.HasUnsavedEdits);

            // Select new photos
            vm.SelectedFiles = new[] { p2 };

            var applied = await vm.ApplyWorkflowAsync(overlay);
            Assert.True(applied);
            Assert.True(promptService.PromptCalled);
            Assert.True(promptService.PromptSaveZipCalled);

            // Zip file was generated successfully
            Assert.True(File.Exists(tempZip));
            using (var zip = ZipFile.OpenRead(tempZip))
            {
                Assert.Single(zip.Entries);
                Assert.Equal("EditedProperty/EditedProperty_01.jpg", zip.Entries[0].FullName);
            }

            // New session active and clean
            Assert.Single(vm.Results);
            Assert.False(vm.HasUnsavedEdits);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
            File.Delete(overlay);
            if (File.Exists(tempZip)) File.Delete(tempZip);
        }
    }

    [Fact]
    public async Task SaveAndContinueAbortsWhenUserCancelsSaveDialog()
    {
        var (p1, p2, overlay) = await CreateSampleImagesAsync();
        try
        {
            var promptService = new TestSessionConfirmationService
            {
                DesiredPromptResult = SessionPromptResult.SaveAndContinue,
                DesiredSaveZipPath = null // User canceled file picker
            };
            var vm = new MainWindowViewModel(new ImageProcessingService(), promptService)
            {
                SelectedFiles = new[] { p1 },
                Prefix = "SafeBatch"
            };

            await vm.ApplyWorkflowAsync(overlay);
            vm.Prefix = "UnsavedModification";
            Assert.True(vm.HasUnsavedEdits);

            vm.SelectedFiles = new[] { p2 };

            var applied = await vm.ApplyWorkflowAsync(overlay);
            Assert.False(applied);
            Assert.True(promptService.PromptCalled);
            Assert.True(promptService.PromptSaveZipCalled);

            // Original session retained
            Assert.Single(vm.Results);
            Assert.Equal("UnsavedModification_01.jpg", vm.Results[0].FileName);
            Assert.True(vm.HasUnsavedEdits);
            Assert.Contains("Save canceled", vm.Status);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
            File.Delete(overlay);
        }
    }

    [Fact]
    public async Task DroppingNewBatchWithActiveSessionPromptsConfirmationAndCancels()
    {
        var (p1, p2, overlay) = await CreateSampleImagesAsync();
        try
        {
            var promptService = new TestSessionConfirmationService
            {
                DesiredPromptResult = SessionPromptResult.Cancel
            };
            var vm = new MainWindowViewModel(new ImageProcessingService(), promptService)
            {
                SelectedFiles = new[] { p1 },
                Prefix = "ActiveSession"
            };

            await vm.ApplyAsync(overlay);
            Assert.Single(vm.Results);
            Assert.Equal("ActiveSession_01.jpg", vm.Results[0].FileName);

            // User drops a new batch of files onto the upload section
            var loaded = await vm.LoadFilesWorkflowAsync(new[] { p2 });
            Assert.False(loaded);
            Assert.True(promptService.PromptCalled);
            Assert.Equal("Start a new branding session?", promptService.LastPromptTitle);
            Assert.Single(vm.Results);
            Assert.Equal("ActiveSession_01.jpg", vm.Results[0].FileName);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
            File.Delete(overlay);
        }
    }

    [Fact]
    public async Task DroppingNewBatchWithActiveSessionDiscardAndContinues()
    {
        var (p1, p2, overlay) = await CreateSampleImagesAsync();
        try
        {
            var promptService = new TestSessionConfirmationService
            {
                DesiredPromptResult = SessionPromptResult.DiscardAndContinue
            };
            var vm = new MainWindowViewModel(new ImageProcessingService(), promptService)
            {
                SelectedFiles = new[] { p1 },
                Prefix = "FirstSession"
            };

            await vm.ApplyAsync(overlay);
            Assert.Single(vm.Results);

            // User drops a new batch of files onto upload section -> discards and stages new files
            var loaded = await vm.LoadFilesWorkflowAsync(new[] { p2 });
            Assert.True(loaded);
            Assert.True(promptService.PromptCalled);
            Assert.Empty(vm.Results);
            Assert.Single(vm.SelectedFiles);
            Assert.Equal(p2, vm.SelectedFiles[0]);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
            File.Delete(overlay);
        }
    }

    [Fact]
    public async Task DirtyStateLifecycleTransitions()
    {
        var (p1, p2, overlay) = await CreateSampleImagesAsync();
        var tempZip = Path.GetTempFileName() + ".zip";
        var tempSingle = Path.GetTempFileName() + ".jpg";
        try
        {
            var vm = new MainWindowViewModel(new ImageProcessingService());

            // 1. Initial empty state
            Assert.False(vm.HasUnsavedEdits);
            Assert.Empty(vm.ApplyStatusHint);
            Assert.False(vm.HasApplyWarning);
            Assert.False(vm.HasApplyHint);

            // 2. Setting selected files does not mark dirty
            vm.SelectedFiles = new[] { p1 };
            Assert.False(vm.HasUnsavedEdits);
            Assert.False(vm.HasApplyHint);

            // 3. Changing prefix with empty results does not mark dirty
            vm.Prefix = "EmptyPrefix";
            Assert.False(vm.HasUnsavedEdits);

            // 4. Processing initial batch
            await vm.ApplyAsync(overlay);
            Assert.False(vm.HasUnsavedEdits);
            Assert.Single(vm.Results);

            // 5. Mutating prefix with active results marks dirty
            vm.Prefix = "MutatedPrefix";
            Assert.True(vm.HasUnsavedEdits);
            Assert.Equal("MutatedPrefix_01.jpg", vm.Results[0].FileName);

            // 6. Selecting new files while dirty activates hint and warning
            vm.SelectedFiles = new[] { p2 };
            Assert.True(vm.HasApplyHint);
            Assert.True(vm.HasApplyWarning);
            Assert.Equal("Unsaved edits in current session.", vm.ApplyStatusHint);

            // 7. Exporting ZIP resets dirty state
            await vm.ExportZipAsync(tempZip);
            Assert.False(vm.HasUnsavedEdits);
            Assert.True(vm.HasApplyHint);
            Assert.False(vm.HasApplyWarning);
            Assert.Equal("1 new photo(s) selected — applying branding will start a new session.", vm.ApplyStatusHint);

            // 8. Individual BrandedImage mutation marks dirty
            vm.Results[0].FileName = "IndividuallyEdited_01.jpg";
            Assert.True(vm.HasUnsavedEdits);

            // 9. Single image save (single-item session) resets dirty state
            await vm.SaveImageAsync(vm.Results[0], tempSingle);
            Assert.False(vm.HasUnsavedEdits);

            // 10. Explicit MarkDirty and DiscardEdits
            vm.MarkDirty();
            Assert.True(vm.HasUnsavedEdits);
            vm.DiscardEdits();
            Assert.False(vm.HasUnsavedEdits);
            Assert.Empty(vm.Results);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
            File.Delete(overlay);
            if (File.Exists(tempZip)) File.Delete(tempZip);
            if (File.Exists(tempSingle)) File.Delete(tempSingle);
        }
    }

    [Fact]
    public async Task SaveAndContinueFailsWhenPathIsInvalid_RetainsCurrentSession()
    {
        var (p1, p2, overlay) = await CreateSampleImagesAsync();
        try
        {
            var promptService = new TestSessionConfirmationService
            {
                DesiredPromptResult = SessionPromptResult.SaveAndContinue,
                DesiredSaveZipPath = "Z:\\NonExistentDirectory\\invalid.zip"
            };
            var vm = new MainWindowViewModel(new ImageProcessingService(), promptService)
            {
                SelectedFiles = new[] { p1 },
                Prefix = "ImportantListing"
            };

            await vm.ApplyWorkflowAsync(overlay);
            vm.Prefix = "ModifiedPrefix";
            Assert.True(vm.HasUnsavedEdits);

            vm.SelectedFiles = new[] { p2 };

            var applied = await vm.ApplyWorkflowAsync(overlay);
            Assert.False(applied);
            Assert.True(promptService.PromptCalled);
            Assert.True(vm.HasUnsavedEdits);
            Assert.Single(vm.Results);
            Assert.Equal("ModifiedPrefix_01.jpg", vm.Results[0].FileName);
            Assert.Contains("Export failed", vm.Status);
        }
        finally
        {
            File.Delete(p1);
            File.Delete(p2);
            File.Delete(overlay);
        }
    }

    [Fact]
    public async Task ApplyWorkflowThrowsOnEmptySelection()
    {
        var (_, _, overlay) = await CreateSampleImagesAsync();
        try
        {
            var vm = new MainWindowViewModel(new ImageProcessingService());
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ApplyWorkflowAsync(overlay));
        }
        finally
        {
            File.Delete(overlay);
        }
    }
}

public class IndividualFilesExportTests
{
    [Fact]
    public async Task ExportIndividualFilesSavesAllImagesToSpecifiedDirectory()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var vm = new MainWindowViewModel(new ImageProcessingService())
            {
                Prefix = "Condo_Listing"
            };

            var bytes1 = new byte[] { 10, 20, 30 };
            var bytes2 = new byte[] { 40, 50, 60 };

            vm.Results.Add(new BrandedImage
            {
                FileName = FileNameGenerator.Generate(vm.Prefix, 0, 2),
                ImageBytes = bytes1,
                SequenceIndex = 0,
                BatchSize = 2,
                Preview = new System.Windows.Media.Imaging.BitmapImage()
            });

            vm.Results.Add(new BrandedImage
            {
                FileName = FileNameGenerator.Generate(vm.Prefix, 1, 2),
                ImageBytes = bytes2,
                SequenceIndex = 1,
                BatchSize = 2,
                Preview = new System.Windows.Media.Imaging.BitmapImage()
            });

            vm.MarkDirty();
            Assert.True(vm.HasUnsavedEdits);

            var savedCount = await vm.ExportIndividualFilesAsync(tempDir.FullName);

            Assert.Equal(2, savedCount);
            Assert.False(vm.HasUnsavedEdits);
            Assert.Contains("Export complete: 2 file(s) saved", vm.Status);

            var file1 = Path.Combine(tempDir.FullName, "Condo_Listing_01.jpg");
            var file2 = Path.Combine(tempDir.FullName, "Condo_Listing_02.jpg");

            Assert.True(File.Exists(file1));
            Assert.True(File.Exists(file2));
            Assert.Equal(bytes1, await File.ReadAllBytesAsync(file1));
            Assert.Equal(bytes2, await File.ReadAllBytesAsync(file2));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public async Task ExportIndividualFilesThrowsWhenNoResults()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var vm = new MainWindowViewModel(new ImageProcessingService());
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ExportIndividualFilesAsync(tempDir.FullName));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExportIndividualFilesThrowsWhenFolderPathIsInvalid(string? invalidPath)
    {
        var vm = new MainWindowViewModel(new ImageProcessingService());
        vm.Results.Add(new BrandedImage
        {
            FileName = "Test_01.jpg",
            ImageBytes = new byte[] { 1, 2, 3 },
            SequenceIndex = 0,
            BatchSize = 1,
            Preview = new System.Windows.Media.Imaging.BitmapImage()
        });

        await Assert.ThrowsAsync<ArgumentException>(() => vm.ExportIndividualFilesAsync(invalidPath!));
    }

    [Fact]
    public async Task ExportIndividualFilesRespectsCancellationToken()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var vm = new MainWindowViewModel(new ImageProcessingService());
            vm.Results.Add(new BrandedImage
            {
                FileName = "Test_01.jpg",
                ImageBytes = new byte[] { 1, 2, 3 },
                SequenceIndex = 0,
                BatchSize = 1,
                Preview = new System.Windows.Media.Imaging.BitmapImage()
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                vm.ExportIndividualFilesAsync(tempDir.FullName, cts.Token));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }
}

public class SelectedPhotosStagingTests
{
    [Fact]
    public async Task SelectedFilesPopulatesSelectedPhotosAndUpdatesViewStates()
    {
        var p1 = Path.GetTempFileName() + ".jpg";
        var p2 = Path.GetTempFileName() + ".png";
        try
        {
            using (var img = new Image<Rgba32>(800, 600, new Rgba32(255, 0, 0, 255)))
            {
                await img.SaveAsJpegAsync(p1);
                await img.SaveAsPngAsync(p2);
            }

            var vm = new MainWindowViewModel(new ImageProcessingService());

            // Initial State: Empty
            Assert.True(vm.IsEmptyState);
            Assert.False(vm.HasSelectedPhotos);
            Assert.False(vm.HasResults);
            Assert.Empty(vm.SelectedPhotos);

            // Selecting files
            vm.SelectedFiles = new[] { p1, p2 };

            Assert.False(vm.IsEmptyState);
            Assert.True(vm.HasSelectedPhotos);
            Assert.False(vm.HasResults);
            Assert.Equal(2, vm.SelectedPhotos.Count);
            Assert.Equal(Path.GetFileName(p1), vm.SelectedPhotos[0].FileName);
            Assert.Equal(Path.GetFileName(p2), vm.SelectedPhotos[1].FileName);
            Assert.NotEmpty(vm.SelectedPhotos[0].FileSizeText);

            // Remove one file
            vm.RemoveSelectedFile(p1);
            Assert.Single(vm.SelectedPhotos);
            Assert.Equal(Path.GetFileName(p2), vm.SelectedPhotos[0].FileName);
            Assert.True(vm.HasSelectedPhotos);
            Assert.False(vm.IsEmptyState);

            // Remove remaining file -> back to empty state
            vm.RemoveSelectedFile(p2);
            Assert.Empty(vm.SelectedPhotos);
            Assert.False(vm.HasSelectedPhotos);
            Assert.True(vm.IsEmptyState);
        }
        finally
        {
            if (File.Exists(p1)) File.Delete(p1);
            if (File.Exists(p2)) File.Delete(p2);
        }
    }

    [Fact]
    public void ResultsPopulatedSwitchesToHasResultsState()
    {
        var vm = new MainWindowViewModel(new ImageProcessingService())
        {
            SelectedFiles = new[] { "fake.jpg" }
        };

        Assert.True(vm.HasSelectedPhotos);
        Assert.False(vm.IsEmptyState);
        Assert.False(vm.HasResults);

        // When Results has items, HasResults is true and HasSelectedPhotos is false
        vm.Results.Add(new BrandedImage
        {
            FileName = "Output_01.jpg",
            ImageBytes = Array.Empty<byte>(),
            SequenceIndex = 0,
            BatchSize = 1,
            Preview = new System.Windows.Media.Imaging.BitmapImage()
        });

        Assert.True(vm.HasResults);
        Assert.False(vm.HasSelectedPhotos);
        Assert.False(vm.IsEmptyState);

        // DiscardEdits returns to SelectedPhotos state since files are still selected
        vm.DiscardEdits();
        Assert.False(vm.HasResults);
        Assert.True(vm.HasSelectedPhotos);
        Assert.False(vm.IsEmptyState);
    }
}

public class VideoProcessingTests
{
    private sealed class MockVideoProcessingService : VideoProcessingService
    {
        public bool ProcessVideoCalled { get; private set; }
        public string? LastInputPath { get; private set; }

        public override async Task<BrandedImage> ProcessVideoAsync(
            string inputVideoPath,
            string overlayImagePath,
            string? prefix,
            int index,
            int total,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ProcessVideoCalled = true;
            LastInputPath = inputVideoPath;
            progress?.Report(100.0);

            var tempOut = Path.GetTempFileName() + ".mp4";
            await File.WriteAllBytesAsync(tempOut, new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70 }, cancellationToken);

            return new BrandedImage
            {
                FileName = FileNameGenerator.Generate(prefix, index, total, MediaType.Video),
                MediaType = MediaType.Video,
                VideoFilePath = tempOut,
                DurationText = "1:30",
                Preview = ImageProcessingService.CreateFallbackThumbnail(),
                SequenceIndex = index,
                BatchSize = total
            };
        }
    }

    [Theory]
    [InlineData("video.mp4", true)]
    [InlineData("tour.mov", true)]
    [InlineData("clip.wmv", true)]
    [InlineData("clip.avi", true)]
    [InlineData("drone.m4v", true)]
    [InlineData("walkthrough.mkv", true)]
    [InlineData("walkthrough.webm", true)]
    [InlineData("photo.jpg", false)]
    [InlineData("photo.png", false)]
    [InlineData("doc.pdf", false)]
    [InlineData("", false)]
    public void VideoFileDetectionIdentifiesSupportedFormats(string path, bool expected)
    {
        Assert.Equal(expected, VideoProcessingService.IsVideoFile(path));
    }

    [Fact]
    public void FileNameGeneratorGeneratesMp4ForVideo()
    {
        var name = FileNameGenerator.Generate("Listing_Tour", 0, 5, MediaType.Video);
        Assert.Equal("Listing_Tour_01.mp4", name);

        var imgName = FileNameGenerator.Generate("Listing_Tour", 0, 5, MediaType.Image);
        Assert.Equal("Listing_Tour_01.jpg", imgName);
    }

    [Fact]
    public async Task PlanBatchWithMixedMediaPlacesVideosAndPhotosInOrder()
    {
        var p1 = Path.GetTempFileName() + ".jpg";
        var v1 = Path.GetTempFileName() + ".mp4";
        var p2 = Path.GetTempFileName() + ".jpg";
        var v2 = Path.GetTempFileName() + ".mov";

        try
        {
            using (var img = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1200, 1000))
            {
                await img.SaveAsJpegAsync(p1);
                await img.SaveAsJpegAsync(p2);
            }
            await File.WriteAllBytesAsync(v1, new byte[] { 1, 2, 3 });
            await File.WriteAllBytesAsync(v2, new byte[] { 4, 5, 6 });

            var plan = await ImageProcessingService.PlanBatchAsync(new[] { p1, v1, p2, v2 });

            Assert.Equal(4, plan.Count);
            Assert.IsType<ImageBatchItem.Landscape>(plan[0]);
            var video1 = Assert.IsType<ImageBatchItem.Video>(plan[1]);
            Assert.Equal(v1, video1.FilePath);
            Assert.IsType<ImageBatchItem.Landscape>(plan[2]);
            var video2 = Assert.IsType<ImageBatchItem.Video>(plan[3]);
            Assert.Equal(v2, video2.FilePath);
        }
        finally
        {
            if (File.Exists(p1)) File.Delete(p1);
            if (File.Exists(p2)) File.Delete(p2);
            if (File.Exists(v1)) File.Delete(v1);
            if (File.Exists(v2)) File.Delete(v2);
        }
    }

    [Fact]
    public async Task ViewModelAppliesMixedBatchSeamlessly()
    {
        var p1 = Path.GetTempFileName() + ".jpg";
        var v1 = Path.GetTempFileName() + ".mp4";
        var overlay = Path.GetTempFileName() + ".png";

        try
        {
            using (var img = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1200, 1000))
            {
                await img.SaveAsJpegAsync(p1);
                await img.SaveAsPngAsync(overlay);
            }
            await File.WriteAllBytesAsync(v1, new byte[] { 1, 2, 3 });

            var mockVideoProcessor = new MockVideoProcessingService();
            var imgService = new ImageProcessingService(mockVideoProcessor);
            var vm = new MainWindowViewModel(imgService)
            {
                SelectedFiles = new[] { p1, v1 },
                Prefix = "Penthouse"
            };

            Assert.Equal("2 item(s) selected (1 photos, 1 videos)", vm.SelectionSummary);

            await vm.ApplyAsync(overlay);

            Assert.Equal(2, vm.Results.Count);
            Assert.Equal("Penthouse_01.jpg", vm.Results[0].FileName);
            Assert.False(vm.Results[0].IsVideo);

            Assert.Equal("Penthouse_02.mp4", vm.Results[1].FileName);
            Assert.True(vm.Results[1].IsVideo);
            Assert.True(mockVideoProcessor.ProcessVideoCalled);
            Assert.Equal(v1, mockVideoProcessor.LastInputPath);
        }
        finally
        {
            if (File.Exists(p1)) File.Delete(p1);
            if (File.Exists(v1)) File.Delete(v1);
            if (File.Exists(overlay)) File.Delete(overlay);
        }
    }

    [Fact]
    public async Task ExportZipPackagesBothPhotosAndVideos()
    {
        var tempZip = Path.GetTempFileName() + ".zip";
        var tempVideo = Path.GetTempFileName() + ".mp4";

        try
        {
            var videoBytes = new byte[] { 10, 20, 30, 40 };
            await File.WriteAllBytesAsync(tempVideo, videoBytes);

            var vm = new MainWindowViewModel(new ImageProcessingService())
            {
                Prefix = "Estate"
            };

            var photoBytes = new byte[] { 1, 2, 3 };
            vm.Results.Add(new BrandedImage
            {
                FileName = FileNameGenerator.Generate(vm.Prefix, 0, 2, MediaType.Image),
                MediaType = MediaType.Image,
                ImageBytes = photoBytes,
                Preview = ImageProcessingService.CreateFallbackThumbnail(),
                SequenceIndex = 0,
                BatchSize = 2
            });

            vm.Results.Add(new BrandedImage
            {
                FileName = FileNameGenerator.Generate(vm.Prefix, 1, 2, MediaType.Video),
                MediaType = MediaType.Video,
                VideoFilePath = tempVideo,
                DurationText = "0:45",
                Preview = ImageProcessingService.CreateFallbackThumbnail(),
                SequenceIndex = 1,
                BatchSize = 2
            });

            await vm.ExportZipAsync(tempZip);

            Assert.True(File.Exists(tempZip));
            using var archive = ZipFile.OpenRead(tempZip);
            Assert.Equal(2, archive.Entries.Count);

            var photoEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".jpg"));
            var videoEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".mp4"));

            Assert.NotNull(photoEntry);
            Assert.NotNull(videoEntry);
            Assert.Equal("Estate/Estate_01.jpg", photoEntry.FullName);
            Assert.Equal("Estate/Estate_02.mp4", videoEntry.FullName);

            using (var s = videoEntry.Open())
            using (var ms = new MemoryStream())
            {
                await s.CopyToAsync(ms);
                Assert.Equal(videoBytes, ms.ToArray());
            }
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
            if (File.Exists(tempVideo)) File.Delete(tempVideo);
        }
    }

    [Fact]
    public async Task ExportIndividualFilesSavesBothPhotosAndVideos()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var tempVideo = Path.GetTempFileName() + ".mp4";

        try
        {
            var videoBytes = new byte[] { 100, 101, 102 };
            await File.WriteAllBytesAsync(tempVideo, videoBytes);

            var vm = new MainWindowViewModel(new ImageProcessingService())
            {
                Prefix = "Villa"
            };

            var photoBytes = new byte[] { 1, 2, 3, 4 };
            vm.Results.Add(new BrandedImage
            {
                FileName = FileNameGenerator.Generate(vm.Prefix, 0, 2, MediaType.Image),
                MediaType = MediaType.Image,
                ImageBytes = photoBytes,
                Preview = ImageProcessingService.CreateFallbackThumbnail(),
                SequenceIndex = 0,
                BatchSize = 2
            });

            vm.Results.Add(new BrandedImage
            {
                FileName = FileNameGenerator.Generate(vm.Prefix, 1, 2, MediaType.Video),
                MediaType = MediaType.Video,
                VideoFilePath = tempVideo,
                DurationText = "0:30",
                Preview = ImageProcessingService.CreateFallbackThumbnail(),
                SequenceIndex = 1,
                BatchSize = 2
            });

            var count = await vm.ExportIndividualFilesAsync(tempDir.FullName);
            Assert.Equal(2, count);

            var photoPath = Path.Combine(tempDir.FullName, "Villa_01.jpg");
            var videoPath = Path.Combine(tempDir.FullName, "Villa_02.mp4");

            Assert.True(File.Exists(photoPath));
            Assert.True(File.Exists(videoPath));
            Assert.Equal(photoBytes, await File.ReadAllBytesAsync(photoPath));
            Assert.Equal(videoBytes, await File.ReadAllBytesAsync(videoPath));
        }
        finally
        {
            tempDir.Delete(true);
            if (File.Exists(tempVideo)) File.Delete(tempVideo);
        }
    }

    [Fact]
    public async Task SaveMediaSavesVideoAsMp4File()
    {
        var tempSource = Path.GetTempFileName() + ".mp4";
        var tempDest = Path.GetTempFileName() + ".mp4";

        try
        {
            var bytes = new byte[] { 7, 8, 9 };
            await File.WriteAllBytesAsync(tempSource, bytes);

            var item = new BrandedImage
            {
                FileName = "Walkthrough_01.mp4",
                MediaType = MediaType.Video,
                VideoFilePath = tempSource,
                DurationText = "1:00",
                Preview = ImageProcessingService.CreateFallbackThumbnail(),
                SequenceIndex = 0,
                BatchSize = 1
            };

            var vm = new MainWindowViewModel(new ImageProcessingService());
            await vm.SaveMediaAsync(item, tempDest);

            Assert.True(File.Exists(tempDest));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(tempDest));
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
            if (File.Exists(tempDest)) File.Delete(tempDest);
        }
    }

    [Fact]
    public void PreviewWindowRecognizesVideoItems()
    {
        var thread = new System.Threading.Thread(() =>
        {
            if (System.Windows.Application.Current == null)
                _ = new App();

            var photo = new BrandedImage
            {
                FileName = "Photo_01.jpg",
                MediaType = MediaType.Image,
                ImageBytes = Array.Empty<byte>(),
                Preview = ImageProcessingService.CreateFallbackThumbnail(),
                SequenceIndex = 0,
                BatchSize = 2
            };

            var video = new BrandedImage
            {
                FileName = "Video_02.mp4",
                MediaType = MediaType.Video,
                VideoFilePath = "dummy.mp4",
                DurationText = "0:30",
                Preview = ImageProcessingService.CreateFallbackThumbnail(),
                SequenceIndex = 1,
                BatchSize = 2
            };

            var preview = new PreviewWindow(new[] { photo, video }, 0);
            Assert.False(preview.IsVideoCurrent);
            Assert.True(preview.HasMultiplePhotos);

            var videoPreview = new PreviewWindow(new[] { photo, video }, 1);
            Assert.True(videoPreview.IsVideoCurrent);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Theory]
    [InlineData(1920, 1080, 0, 162, 1200, 675)]       // 16:9 landscape -> full width, centered vertically
    [InlineData(1280, 720, 0, 162, 1200, 675)]        // 720p landscape -> full width, centered vertically
    [InlineData(1080, 1920, 319, 0, 562, 1000)]       // 9:16 portrait -> full height, centered horizontally with black bars
    [InlineData(720, 1280, 319, 0, 562, 1000)]        // 9:16 portrait 720p -> centered horizontally
    [InlineData(1000, 1000, 100, 0, 1000, 1000)]      // 1:1 square -> full height, centered horizontally
    [InlineData(1200, 1000, 0, 0, 1200, 1000)]        // 6:5 exact template -> exact fit
    public void CalculateVideoFitFitsWithin1200x1000Template(
        double srcW, double srcH,
        double expectedX, double expectedY, double expectedW, double expectedH)
    {
        var (offX, offY, fitW, fitH) = VideoProcessingService.CalculateVideoFit(srcW, srcH, 1200, 1000);
        Assert.Equal(expectedX, offX);
        Assert.Equal(expectedY, offY);
        Assert.Equal(expectedW, fitW);
        Assert.Equal(expectedH, fitH);
    }
}

public class InstallerPayloadDiscoveryTests
{
    [Fact]
    public void DiscoversPayloadInUnsignedBinaryStream()
    {
        var header = new byte[1000];
        var payload = new byte[500];
        new Random(42).NextBytes(payload);
        var marker = System.Text.Encoding.UTF8.GetBytes(Alpha.Branding.Bootstrapper.InstallerService.Marker);
        var lengthBytes = BitConverter.GetBytes((long)payload.Length);

        using var ms = new MemoryStream();
        ms.Write(header);
        long expectedStart = ms.Position;
        ms.Write(payload);
        ms.Write(marker);
        ms.Write(lengthBytes);

        bool found = Alpha.Branding.Bootstrapper.InstallerService.TryGetPayloadLocation(ms, out long start, out long length);
        Assert.True(found);
        Assert.Equal(expectedStart, start);
        Assert.Equal(payload.Length, length);
    }

    [Fact]
    public void DiscoversPayloadInAuthenticodeSignedBinaryStreamWithTrailingSignature()
    {
        var header = new byte[2000];
        var payload = new byte[800];
        new Random(123).NextBytes(payload);
        var marker = System.Text.Encoding.UTF8.GetBytes(Alpha.Branding.Bootstrapper.InstallerService.Marker);
        var lengthBytes = BitConverter.GetBytes((long)payload.Length);
        var fakeSignatureTable = new byte[7500]; // Simulated Authenticode PKCS#7 table appended at end

        using var ms = new MemoryStream();
        ms.Write(header);
        long expectedStart = ms.Position;
        ms.Write(payload);
        ms.Write(marker);
        ms.Write(lengthBytes);
        ms.Write(fakeSignatureTable);

        bool found = Alpha.Branding.Bootstrapper.InstallerService.TryGetPayloadLocation(ms, out long start, out long length);
        Assert.True(found);
        Assert.Equal(expectedStart, start);
        Assert.Equal(payload.Length, length);
    }

    [Fact]
    public void ReturnsFalseWhenMarkerIsMissingOrStreamTooSmall()
    {
        using var emptyStream = new MemoryStream(new byte[10]);
        Assert.False(Alpha.Branding.Bootstrapper.InstallerService.TryGetPayloadLocation(emptyStream, out _, out _));

        using var randomStream = new MemoryStream(new byte[1000]);
        Assert.False(Alpha.Branding.Bootstrapper.InstallerService.TryGetPayloadLocation(randomStream, out _, out _));
    }
}

public class TemplateManagementTests
{
    [Fact]
    public void InitializesWithBuiltInTemplates()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var service = new TemplateService(tempDir.FullName);
            var templates = service.GetTemplates();
            Assert.NotEmpty(templates);

            var classic = templates.FirstOrDefault(t => t.Id == TemplateService.DefaultTemplateId);
            Assert.NotNull(classic);
            Assert.Equal("Alpha Premier Classic", classic.Name);
            Assert.True(classic.IsBuiltIn);

            var active = service.GetActiveTemplate();
            Assert.NotNull(active);
            Assert.Equal(TemplateService.DefaultTemplateId, active.Id);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public async Task ValidatesTemplateDimensionsAndAspectRatios()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var service = new TemplateService(tempDir.FullName);

            // Valid 1200x1000 PNG
            var validFile = Path.Combine(tempDir.FullName, "valid.png");
            using (var img = new Image<Rgba32>(1200, 1000, new Rgba32(0, 0, 0, 0)))
                await img.SaveAsPngAsync(validFile);

            var (isValid, msg, w, h, ratio) = await service.ValidateTemplateAsync(validFile);
            Assert.True(isValid);
            Assert.Equal(1200, w);
            Assert.Equal(1000, h);
            Assert.Equal(1.2, ratio);

            // Invalid non-image file
            var txtFile = Path.Combine(tempDir.FullName, "invalid.txt");
            await File.WriteAllTextAsync(txtFile, "hello");
            var (isTxtValid, _, _, _, _) = await service.ValidateTemplateAsync(txtFile);
            Assert.False(isTxtValid);

            // Non-existent file
            var (isMissingValid, _, _, _, _) = await service.ValidateTemplateAsync(Path.Combine(tempDir.FullName, "missing.png"));
            Assert.False(isMissingValid);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public async Task SavesCustomTemplateAndPersistsAcrossInstances()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var sourceFile = Path.Combine(tempDir.FullName, "my_custom_template.png");
            using (var img = new Image<Rgba32>(1024, 858, new Rgba32(255, 200, 0, 128)))
                await img.SaveAsPngAsync(sourceFile);

            var service1 = new TemplateService(tempDir.FullName);
            var saved = await service1.SaveTemplateAsync(sourceFile, "August Special Edition");

            Assert.NotNull(saved);
            Assert.Equal("August Special Edition", saved.Name);
            Assert.False(saved.IsBuiltIn);
            Assert.Equal(1024, saved.Width);
            Assert.Equal(858, saved.Height);
            Assert.Equal(saved.Id, service1.GetActiveTemplate().Id);

            // Load new instance pointing to same storage
            var service2 = new TemplateService(tempDir.FullName);
            var templates2 = service2.GetTemplates();
            var loaded = templates2.FirstOrDefault(t => t.Id == saved.Id);
            Assert.NotNull(loaded);
            Assert.Equal("August Special Edition", loaded.Name);
            Assert.Equal(saved.Id, service2.GetActiveTemplate().Id);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public async Task DeletesCustomTemplateAndProtectsBuiltInTemplates()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var sourceFile = Path.Combine(tempDir.FullName, "deletable.png");
            using (var img = new Image<Rgba32>(800, 600))
                await img.SaveAsPngAsync(sourceFile);

            var service = new TemplateService(tempDir.FullName);
            var custom = await service.SaveTemplateAsync(sourceFile, "Temporary Promo");

            // Built-in templates cannot be deleted
            Assert.False(service.DeleteTemplate(TemplateService.DefaultTemplateId));

            // Custom template can be deleted
            Assert.True(service.DeleteTemplate(custom.Id));
            Assert.DoesNotContain(service.GetTemplates(), t => t.Id == custom.Id);
            Assert.Equal(TemplateService.DefaultTemplateId, service.GetActiveTemplate().Id);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }
}

public class DistortionPreventionAndPhotoFidelityTests
{
    [Fact]
    public async Task LandscapeCroppingPreservesWidescreenPhotoAspectRatioWithoutStretching()
    {
        var input = Path.GetTempFileName() + ".png";
        var overlay = Path.GetTempFileName() + ".png";
        try
        {
            // 16:9 widescreen photo (1920x1080) with distinct color pattern
            // Upper half is red (255, 0, 0), lower half is green (0, 255, 0)
            using (var img = new Image<Rgba32>(1920, 1080))
            {
                for (int y = 0; y < 1080; y++)
                {
                    for (int x = 0; x < 1920; x++)
                    {
                        img[x, y] = y < 540 ? new Rgba32(255, 0, 0, 255) : new Rgba32(0, 255, 0, 255);
                    }
                }
                await img.SaveAsPngAsync(input);
            }

            // Transparent overlay
            using (var frame = new Image<Rgba32>(1200, 1000, new Rgba32(0, 0, 0, 0)))
            {
                await frame.SaveAsPngAsync(overlay);
            }

            var service = new ImageProcessingService();
            var result = await service.ProcessLandscapeAsync(input, overlay, "AspectTest", 0, 1);

            using var decoded = Image.Load<Rgba32>(result.ImageBytes);
            Assert.Equal(1200, decoded.Width);
            Assert.Equal(1000, decoded.Height);

            // Middle vertical point (y = 500) should cleanly preserve the dividing line
            var topSample = decoded[600, 200];
            var bottomSample = decoded[600, 800];
            Assert.True(topSample.R > 200, "Top portion must contain top of photo without horizontal compression distortion.");
            Assert.True(bottomSample.G > 200, "Bottom portion must contain bottom of photo without vertical stretch distortion.");
        }
        finally
        {
            File.Delete(input);
            File.Delete(overlay);
        }
    }

    [Fact]
    public async Task AugustBrandingOverlayCompositesOntoImageProcessingPipeline()
    {
        var photoFile = Path.GetTempFileName() + ".png";
        var augustOverlay = Path.Combine(AppContext.BaseDirectory, "Assets", "august_branding.png");

        if (!File.Exists(augustOverlay))
        {
            // Fallback for isolated test runners
            augustOverlay = Path.GetTempFileName() + ".png";
            using var testFrame = new Image<Rgba32>(1024, 858, new Rgba32(200, 160, 90, 180));
            await testFrame.SaveAsPngAsync(augustOverlay);
        }

        try
        {
            using (var photo = new Image<Rgba32>(1600, 1200, new Rgba32(50, 100, 150, 255)))
                await photo.SaveAsPngAsync(photoFile);

            var service = new ImageProcessingService();
            var result = await service.ProcessLandscapeAsync(photoFile, augustOverlay, "AugustTest", 0, 1);

            Assert.NotNull(result);
            Assert.Equal("AugustTest_01.jpg", result.FileName);

            using var decoded = Image.Load<Rgba32>(result.ImageBytes);
            Assert.Equal(1200, decoded.Width);
            Assert.Equal(1000, decoded.Height);
        }
        finally
        {
            File.Delete(photoFile);
        }
    }

    [Fact]
    public async Task ViewModelTemplateSwitchingAppliesSelectedTemplate()
    {
        var photoFile = Path.GetTempFileName() + ".png";
        var tempStorage = Directory.CreateTempSubdirectory();
        try
        {
            using (var photo = new Image<Rgba32>(1200, 1000, new Rgba32(100, 100, 100, 255)))
                await photo.SaveAsPngAsync(photoFile);

            // Create custom red template
            var redOverlay = Path.Combine(tempStorage.FullName, "red_overlay.png");
            using (var frame = new Image<Rgba32>(1200, 1000, new Rgba32(255, 0, 0, 200)))
                await frame.SaveAsPngAsync(redOverlay);

            var templateService = new TemplateService(tempStorage.FullName);
            var customTemplate = await templateService.SaveTemplateAsync(redOverlay, "Vibrant Red Brand");

            var vm = new MainWindowViewModel(new ImageProcessingService(), null, templateService)
            {
                SelectedFiles = new[] { photoFile },
                Prefix = "SwitchTest"
            };

            // Switch to custom red template
            vm.SelectedTemplate = customTemplate;
            Assert.Equal(customTemplate.Id, vm.SelectedTemplate.Id);
            Assert.Equal("Vibrant Red Brand", vm.ActiveTemplateName);

            await vm.ApplyAsync();

            Assert.Single(vm.Results);
            using var decoded = Image.Load<Rgba32>(vm.Results[0].ImageBytes);
            var pixel = decoded[600, 500];
            Assert.True(pixel.R > 180, "Branded image must incorporate selected custom template color.");
        }
        finally
        {
            File.Delete(photoFile);
            tempStorage.Delete(true);
        }
    }
}


