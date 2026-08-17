using Alpha.Branding.Models;
using Alpha.Branding.Services;
using Alpha.Branding.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Alpha.Branding.Tests;

public class ScreenshotGenerator
{
    private static void RunInSta(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Ensure Application instance exists for resources
                if (System.Windows.Application.Current == null)
                {
                    _ = new Alpha.Branding.App();
                }
                action();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            throw new InvalidOperationException($"STA execution failed: {threadEx.Message}", threadEx);
        }
    }

    private static void SaveWindowToPng(Window window, string outputPath, int width = 1280, int height = 800)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000; // Position off-screen so user is never interrupted
        window.Top = -32000;
        window.Width = width;
        window.Height = height;
        window.Show();
        window.UpdateLayout();

        // Process all rendering and layout dispatcher frames
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new System.Windows.Threading.DispatcherOperationCallback(f =>
            {
                ((System.Windows.Threading.DispatcherFrame)f).Continue = false;
                return null;
            }), frame);
        System.Windows.Threading.Dispatcher.PushFrame(frame);

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);

        window.Close();
    }

    private static void EnsureStockPhotos(string stockDir)
    {
        Directory.CreateDirectory(stockDir);

        var images = new Dictionary<string, string>
        {
            ["01_Modern_House_And_Lot_Landscape.jpg"] = "https://images.unsplash.com/photo-1600585154340-be6161a56a0c?w=1600&auto=format&fit=crop&q=80",
            ["02_Industrial_Warehouse_Landscape.jpg"] = "https://images.unsplash.com/photo-1586528116311-ad8dd3c8310d?w=1600&auto=format&fit=crop&q=80",
            ["03_Luxury_Condo_HighRise_Portrait.jpg"] = "https://images.unsplash.com/photo-1545324418-cc1a3fa10c00?w=1000&auto=format&fit=crop&q=80",
            ["04_Tall_Villa_Facade_Portrait.jpg"] = "https://images.unsplash.com/photo-1512917774080-9991f1c4c750?w=1000&auto=format&fit=crop&q=80",
            ["05_Commercial_Office_Landscape.jpg"] = "https://images.unsplash.com/photo-1486406146926-c627a92ad1ab?w=1600&auto=format&fit=crop&q=80",
            ["06_Suburban_Home_Landscape.jpg"] = "https://images.unsplash.com/photo-1568605117036-5fe5e7bab0b7?w=1600&auto=format&fit=crop&q=80"
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        foreach (var (filename, url) in images)
        {
            var filePath = Path.Combine(stockDir, filename);
            if (!File.Exists(filePath) || new FileInfo(filePath).Length < 1000)
            {
                try
                {
                    var bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                    File.WriteAllBytes(filePath, bytes);
                }
                catch
                {
                    // Generate fallback realistic image
                    using var img = new Image<Rgba32>(1600, 1000, new Rgba32(60, 90, 130, 255));
                    img.SaveAsJpeg(filePath);
                }
            }
        }
    }

    [Fact]
    public void GenerateAllVisualScreenshotsDirectlyWithoutDesktopInterference()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var outputDir = Path.Combine(downloads, "Alpha_Branding_Screenshots");
        var stockDir = Path.Combine(outputDir, "Sample_Properties");

        Directory.CreateDirectory(outputDir);
        EnsureStockPhotos(stockDir);

        var house = Path.Combine(stockDir, "01_Modern_House_And_Lot_Landscape.jpg");
        var warehouse = Path.Combine(stockDir, "02_Industrial_Warehouse_Landscape.jpg");
        var condo = Path.Combine(stockDir, "03_Luxury_Condo_HighRise_Portrait.jpg");
        var villa = Path.Combine(stockDir, "04_Tall_Villa_Facade_Portrait.jpg");
        var office = Path.Combine(stockDir, "05_Commercial_Office_Landscape.jpg");
        var suburban = Path.Combine(stockDir, "06_Suburban_Home_Landscape.jpg");

        var overlayPath = Path.Combine(AppContext.BaseDirectory, "Assets", "alpha_branding.png");
        if (!File.Exists(overlayPath))
        {
            overlayPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Alpha.Branding", "Assets", "alpha_branding.png"));
        }

        RunInSta(() =>
        {
            // 1. Initial Empty State
            {
                var win = new MainWindow();
                SaveWindowToPng(win, Path.Combine(outputDir, "01_MainWindow_EmptyState.png"));
            }

            // 2. Photos Loaded & Dynamic Pattern Preview
            {
                var win = new MainWindow();
                win.LoadFiles(new[] { house, warehouse, condo, villa, office, suburban });
                win.ViewModel.Prefix = "AlphaPremier_LuxuryProperties";
                SaveWindowToPng(win, Path.Combine(outputDir, "02_MainWindow_PhotosLoaded_PatternPreview.png"));
            }

            // 3. Branded Output Gallery Grid (with 6 photos -> 5 cards, 2 portraits paired)
            IReadOnlyList<BrandedImage> processedResults;
            {
                var win = new MainWindow();
                win.LoadFiles(new[] { house, warehouse, condo, villa, office, suburban });
                win.ViewModel.Prefix = "AlphaPremier_LuxuryProperties";
                win.ViewModel.ApplyAsync(overlayPath).GetAwaiter().GetResult();
                processedResults = win.ViewModel.Results.ToList();
                SaveWindowToPng(win, Path.Combine(outputDir, "03_MainWindow_BrandedGallery_ProcessingComplete.png"));
            }

            // 4. Preview Modal - Landscape Warehouse Inspection
            if (processedResults.Count >= 2)
            {
                var modal = new PreviewWindow(processedResults, 1); // Index 1 is Warehouse
                SaveWindowToPng(modal, Path.Combine(outputDir, "04_PreviewModal_Landscape_Warehouse_Inspector.png"), 1100, 750);
            }

            // 5. Preview Modal - Paired Portrait (Condo + Villa) Side-by-Side Inspection
            if (processedResults.Count >= 3)
            {
                var modal = new PreviewWindow(processedResults, 2); // Index 2 is Paired Condo + Villa
                SaveWindowToPng(modal, Path.Combine(outputDir, "05_PreviewModal_PortraitPair_Condo_And_Villa.png"), 1100, 750);
            }

            // 6. Edge Case: Single Portrait Duplicate Side-by-Side
            {
                var win = new MainWindow();
                win.LoadFiles(new[] { condo });
                win.ViewModel.Prefix = "Lone_Condo_Listing";
                win.ViewModel.ApplyAsync(overlayPath).GetAwaiter().GetResult();
                SaveWindowToPng(win, Path.Combine(outputDir, "06_EdgeCase_SinglePortrait_DuplicatePair.png"));
            }

            // 7. Edge Case: 1 Portrait + 1 Landscape Paired Side-by-Side
            {
                var win = new MainWindow();
                win.LoadFiles(new[] { condo, warehouse });
                win.ViewModel.Prefix = "Condo_Warehouse_Pair";
                win.ViewModel.ApplyAsync(overlayPath).GetAwaiter().GetResult();
                SaveWindowToPng(win, Path.Combine(outputDir, "07_EdgeCase_OddPortraits_MatchedWithLandscape.png"));
            }

            // 8. Edge Case: Special Character Prefix Sanitization
            {
                var win = new MainWindow();
                win.LoadFiles(new[] { house });
                win.ViewModel.Prefix = "BGC / High-End * Villa #88?";
                win.ViewModel.ApplyAsync(overlayPath).GetAwaiter().GetResult();
                SaveWindowToPng(win, Path.Combine(outputDir, "08_EdgeCase_SpecialCharPrefixSanitization.png"));
            }
        });
    }
}
