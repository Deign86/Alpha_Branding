using Alpha.Branding.Models;
using Alpha.Branding.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace Alpha.Branding.Tests;

public class ManualScreenshotGenerator
{
    private static void RunInSta(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null) _ = new App();
                // Continuations must return to the UI thread, as they do in the running app.
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                action();
            }
            catch (Exception ex) { threadEx = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (threadEx != null) throw new InvalidOperationException(threadEx.ToString(), threadEx);
    }

    private static void Await(Task task)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        task.ContinueWith(_ => dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => frame.Continue = false)),
            TaskContinuationOptions.ExecuteSynchronously);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static void Pump(int ms = 400)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(f => { ((DispatcherFrame)f).Continue = false; return null; }), frame);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(20);
        }
    }

    private static void Show(Window window, int width, int height)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.Width = width;
        window.Height = height;
        window.Show();
        window.UpdateLayout();
        Pump();
    }

    private static void Capture(Window window, string path, bool close = true)
    {
        window.UpdateLayout();
        Pump(200);
        const double dpi = 144;
        var root = (FrameworkElement)window.Content;
        var w = (int)Math.Round(root.ActualWidth * dpi / 96);
        var h = (int)Math.Round(root.ActualHeight * dpi / 96);
        var rtb = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background ?? Brushes.Black, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            // Fixed viewbox: content drawn outside the root (e.g. the pen cursor) must not rescale the capture.
            var bounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            var offset = VisualTreeHelper.GetOffset(root);
            var brush = new VisualBrush(root)
            {
                Viewbox = new Rect(offset.X, offset.Y, root.ActualWidth, root.ActualHeight),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            dc.DrawRectangle(brush, null, bounds);
        }
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using (var stream = File.Create(path)) encoder.Save(stream);
        if (!close) return;
        if (window is MainWindow main) main.SuppressExitConfirmation = true;
        window.Close();
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(current, "Alpha_Branding.sln")))
            current = Directory.GetParent(current)?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        return current;
    }

    /// <summary>
    /// Regenerates the user-manual screenshots in docs/manual/images.
    /// Opt-in: set ALPHA_BRANDING_MANUAL_SCREENSHOTS=1 before running this test.
    /// </summary>
    [Fact]
    public void GenerateManualScreenshots()
    {
        if (Environment.GetEnvironmentVariable("ALPHA_BRANDING_MANUAL_SCREENSHOTS") != "1") return;

        var manualDir = Path.Combine(FindRepositoryRoot(), "docs", "manual");
        var samples = Path.Combine(manualDir, "sample-photos");
        var outDir = Path.Combine(manualDir, "images");
        Directory.CreateDirectory(outDir);
        var exterior = Path.Combine(samples, "01_Exterior_Front.jpg");
        var living = Path.Combine(samples, "02_Living_Room.jpg");
        var kitchen = Path.Combine(samples, "03_Kitchen.jpg");
        var bedroom = Path.Combine(samples, "04_Bedroom.jpg");
        var files = new[] { exterior, living, kitchen, bedroom };
        var overlay = Path.Combine(AppContext.BaseDirectory, "Assets", "alpha_branding.png");

        RunInSta(() =>
        {
            {
                var win = new MainWindow();
                Show(win, 1220, 840);
                Capture(win, Path.Combine(outDir, "01_main_empty.png"));
            }

            {
                var win = new MainWindow();
                Show(win, 1220, 840);
                Await(win.ViewModel.LoadFilesWorkflowAsync(files));
                Pump(800);
                Capture(win, Path.Combine(outDir, "02_main_staged.png"));
            }

            {
                var win = new MainWindow();
                Show(win, 1220, 840);
                Await(win.ViewModel.LoadFilesWorkflowAsync(files));
                Pump(600);
                var photos = win.ViewModel.SelectedPhotos;
                photos[0].CropSettings.Zoom = 1.25;
                photos[1].ToggleLayoutPreference();
                win.ViewModel.PairWith(photos[2], photos[3].FilePath);
                win.ViewModel.Prefix = "Maple_Street_128";
                Pump(600);
                Capture(win, Path.Combine(outDir, "03_main_staged_options.png"));
            }

            IReadOnlyList<BrandedImage> results;
            {
                var win = new MainWindow();
                Show(win, 1220, 840);
                win.ViewModel.Prefix = "Maple_Street_128";
                Await(win.ViewModel.LoadFilesWorkflowAsync(files));
                Pump(600);
                Await(win.ViewModel.ApplyWorkflowAsync(overlay));
                Pump(1500);
                results = win.ViewModel.Results.ToList();
                Capture(win, Path.Combine(outDir, "04_main_results.png"), close: false);
                for (var i = 0; i < results.Count; i++)
                    Await(win.ViewModel.SaveImageAsync(results[i], Path.Combine(outDir, $"output_{i + 1:00}.jpg")));
                win.SuppressExitConfirmation = true;
                win.Close();
            }

            {
                var editor = new CropEditorWindow(exterior, new ImageCropSettings { Zoom = 1.15 }, overlay, "01_Exterior_Front.jpg");
                Show(editor, 1020, 780);
                Capture(editor, Path.Combine(outDir, "05_crop_editor.png"));
            }

            {
                var editor = new CropEditorWindow(kitchen, new ImageCropSettings(), overlay, "03_Kitchen.jpg + 04_Bedroom.jpg",
                    bedroom, new ImageCropSettings());
                Show(editor, 1020, 780);
                Capture(editor, Path.Combine(outDir, "06_crop_editor_pair.png"));
            }

            {
                var crop = new ImageCropSettings();
                var stroke = new RedactStroke { BrushSizeNormalized = 0.035 };
                for (var x = 0.548; x <= 0.592; x += 0.004) stroke.AddPoint(new Point(x, 0.506));
                for (var x = 0.592; x >= 0.548; x -= 0.004) stroke.AddPoint(new Point(x, 0.526));
                crop.RedactStrokes = new List<RedactStroke> { stroke };
                var editor = new CropEditorWindow(exterior, crop, overlay, "01_Exterior_Front.jpg");
                if (editor.FindName("RedactModeToggle") is ToggleButton pen) pen.IsChecked = true;
                Show(editor, 1020, 780);
                Capture(editor, Path.Combine(outDir, "07_crop_editor_pen.png"));
            }

            if (results.Count > 0)
            {
                var preview = new PreviewWindow(results, 0);
                Show(preview, 1080, 760);
                Capture(preview, Path.Combine(outDir, "08_preview.png"));
            }

            {
                var dialog = new SessionConfirmationDialog(
                    "Start a new branding session?",
                    "You have unsaved edits in the current session. Loading a new batch will replace the current items and discard those unsaved edits.");
                Show(dialog, 640, 300);
                Capture(dialog, Path.Combine(outDir, "09_session_dialog.png"));
            }

            {
                var check = new UpdateCheckResult
                {
                    IsUpdateAvailable = true,
                    CurrentVersion = "2.4.0",
                    LatestVersion = "2.5.0",
                    Release = new GitHubRelease
                    {
                        Name = "v2.5.0 (example)",
                        TagName = "v2.5.0",
                        Body = "### New Features\n- Example release note shown for illustration.\n\n### Fixes\n- Example fix entry.",
                        PublishedAt = DateTimeOffset.Now
                    },
                    TargetAsset = new GitHubReleaseAsset { Name = "Alpha.Branding.Setup.exe", BrowserDownloadUrl = "https://example.invalid/setup.exe" }
                };
                var dialog = new UpdateDialog(check, new UpdateService());
                Show(dialog, 680, 580);
                Capture(dialog, Path.Combine(outDir, "10_update_dialog.png"));
            }
        });
    }
}
