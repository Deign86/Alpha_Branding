using Alpha.Branding.Models;
using Alpha.Branding.Services;
using Alpha.Branding.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace Alpha.Branding.Tests;

public class ScalingAuditScreenshotGenerator
{
    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Alpha_Branding.sln")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            current = parent?.FullName;
        }
        throw new InvalidOperationException("Could not locate repository root containing Alpha_Branding.sln.");
    }

    private static void RunInSta(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    _ = new App();
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

    /// <summary>
    /// Runs an async task started from the STA thread without deadlocking the dispatcher.
    /// Offloads the execution to a thread-pool thread so the STA dispatcher remains free
    /// to service continuations that post back to it (e.g. ObservableCollection updates).
    /// </summary>
    private static void AwaitOnDispatcher(Task task)
    {
        // Push a nested dispatcher frame so the STA dispatcher keeps pumping while we wait.
        // ContinueWith fires on a pool thread and signals the frame to stop via BeginInvoke.
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        task.ContinueWith(
            _ => dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => frame.Continue = false)),
            TaskContinuationOptions.ExecuteSynchronously);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new DispatcherOperationCallback(f =>
            {
                ((DispatcherFrame)f).Continue = false;
                return null;
            }), frame);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Closes an audit window without hanging on the MainWindow unsaved-edits
    /// exit guard. After ApplyAsync the view model has results, so a plain
    /// Close() would pop the modal SessionConfirmationDialog and block the STA
    /// thread forever. The flag makes OnClosing skip the prompt.
    /// </summary>
    private static void CloseWindowForAudit(Window window)
    {
        if (window is MainWindow mainWindow)
        {
            mainWindow.SuppressExitConfirmation = true;
        }
        window.Close();
    }

    private static void SaveWindowToPng(Window window, string outputPath, int width, int height, double dpi = 96)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.Width = width;
        window.Height = height;
        window.Show();
        window.UpdateLayout();
        PumpDispatcher();

        int pixelWidth = (int)Math.Max(1, Math.Round(width * dpi / 96.0));
        int pixelHeight = (int)Math.Max(1, Math.Round(height * dpi / 96.0));

        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);

        CloseWindowForAudit(window);
    }

    private static void SaveVisualWithDesktopBoundsToPng(
        Window window,
        string outputPath,
        int windowWidth,
        int windowHeight,
        int desktopWidth,
        int desktopWorkingHeight,
        int taskbarHeight,
        string scalingLabel,
        double dpi = 96)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.Width = windowWidth;
        window.Height = windowHeight;
        window.Show();
        window.UpdateLayout();
        PumpDispatcher();

        var canvas = new Canvas
        {
            Width = desktopWidth,
            Height = desktopWorkingHeight + taskbarHeight,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 15, 18))
        };

        var winBrush = new VisualBrush(window)
        {
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };

        var winRect = new System.Windows.Shapes.Rectangle
        {
            Width = windowWidth,
            Height = windowHeight,
            Fill = winBrush,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(66, 55, 34)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(winRect, Math.Max(0, (desktopWidth - windowWidth) / 2));
        Canvas.SetTop(winRect, 0);
        canvas.Children.Add(winRect);

        var taskbar = new Border
        {
            Width = desktopWidth,
            Height = taskbarHeight,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 30)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 70)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new TextBlock
            {
                Text = $"Windows Taskbar ({desktopWidth}x{taskbarHeight}) — {scalingLabel} Display",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 145, 160)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            }
        };
        Canvas.SetLeft(taskbar, 0);
        Canvas.SetTop(taskbar, desktopWorkingHeight);
        canvas.Children.Add(taskbar);

        var redLine = new System.Windows.Shapes.Line
        {
            X1 = 0,
            Y1 = desktopWorkingHeight,
            X2 = desktopWidth,
            Y2 = desktopWorkingHeight,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 }
        };
        canvas.Children.Add(redLine);

        if (windowHeight > desktopWorkingHeight)
        {
            int overflowPx = windowHeight - desktopWorkingHeight;
            var warningBorder = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 185, 28, 28)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 202, 202)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 12, 6),
                Child = new TextBlock
                {
                    Text = $"⚠️ OVERFLOW CRITICAL: Window ({windowHeight}px) exceeds usable screen ({desktopWorkingHeight}px) by {overflowPx}px! Bottom UI pushed behind taskbar / off-screen.",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold
                }
            };
            Canvas.SetLeft(warningBorder, 20);
            Canvas.SetTop(warningBorder, 20);
            canvas.Children.Add(warningBorder);
        }

        canvas.Measure(new System.Windows.Size(desktopWidth, desktopWorkingHeight + taskbarHeight));
        canvas.Arrange(new Rect(0, 0, desktopWidth, desktopWorkingHeight + taskbarHeight));
        canvas.UpdateLayout();

        int pixelWidth = (int)Math.Round(desktopWidth * dpi / 96.0);
        int pixelHeight = (int)Math.Round((desktopWorkingHeight + taskbarHeight) * dpi / 96.0);

        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(canvas);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);

        CloseWindowForAudit(window);
    }

    private static (string Landscape, string Portrait1, string Portrait2) CreateSampleAssets(string dir)
    {
        Directory.CreateDirectory(dir);
        var l = Path.Combine(dir, "Sample_Landscape.jpg");
        var p1 = Path.Combine(dir, "Sample_Portrait_1.jpg");
        var p2 = Path.Combine(dir, "Sample_Portrait_2.jpg");

        if (!File.Exists(l))
        {
            using var imgL = new Image<Rgba32>(1600, 1000, new Rgba32(40, 80, 140, 255));
            imgL.SaveAsJpeg(l);
        }

        if (!File.Exists(p1))
        {
            using var imgP1 = new Image<Rgba32>(800, 1200, new Rgba32(180, 60, 60, 255));
            imgP1.SaveAsJpeg(p1);
        }

        if (!File.Exists(p2))
        {
            using var imgP2 = new Image<Rgba32>(800, 1200, new Rgba32(50, 150, 80, 255));
            imgP2.SaveAsJpeg(p2);
        }

        return (l, p1, p2);
    }

    [Fact]
    public void GenerateAllScalingAuditScreenshots()
    {
        var repoRoot = FindRepositoryRoot();
        var outputDir = Path.Combine(repoRoot, "scaling_audit_issues");
        Directory.CreateDirectory(outputDir);

        var samplesDir = Path.Combine(outputDir, "test_samples");
        var (sampleL, sampleP1, sampleP2) = CreateSampleAssets(samplesDir);

        var overlayPath = Path.Combine(repoRoot, "src", "Alpha.Branding", "Assets", "alpha_branding.png");

        RunInSta(() =>
        {
            // 1. BASELINE 100% DPI (96 DPI)
            {
                var win = new MainWindow();
                SaveWindowToPng(win, Path.Combine(outputDir, "01_MainWindow_100pct_EmptyState_Baseline.png"), 1220, 840, 96);
            }

            {
                var win = new MainWindow();
                win.LoadFiles(new[] { sampleL, sampleP1, sampleP2 });
                SaveWindowToPng(win, Path.Combine(outputDir, "02_MainWindow_100pct_StagedMedia_Baseline.png"), 1220, 840, 96);
            }

            IReadOnlyList<BrandedImage> results;
            {
                var win = new MainWindow();
                win.LoadFiles(new[] { sampleL, sampleP1, sampleP2 });
                AwaitOnDispatcher(win.ViewModel.ApplyAsync(overlayPath));
                results = win.ViewModel.Results.ToList();
                SaveWindowToPng(win, Path.Combine(outputDir, "03_MainWindow_100pct_Results_Baseline.png"), 1220, 840, 96);
            }

            // 2. MINIMUM WIDTH (840 DIPs, new MinWidth floor)
            {
                var win = new MainWindow();
                win.LoadFiles(new[] { sampleL, sampleP1, sampleP2 });
                SaveWindowToPng(win, Path.Combine(outputDir, "04_MainWindow_MinWidth_840.png"), 840, 840, 96);
            }

            // 3. 125% DPI (120 DPI) - 1080p Screen (1536x864 DIPs, 816px Usable Height)
            // Window fitted to the usable area: proves all controls reachable, no overflow.
            {
                var win = new MainWindow();
                win.LoadFiles(new[] { sampleL, sampleP1 });
                SaveVisualWithDesktopBoundsToPng(
                    win,
                    Path.Combine(outputDir, "05_MainWindow_125pct_Desktop_Fitted.png"),
                    1220, 816,
                    1536, 816, 48,
                    "125% DPI (1536x864)",
                    120);
            }

            // 4. 150% DPI (144 DPI) - 1080p Screen (1280x720 DIPs, 672px Usable Height)
            // Window fitted to the usable area: proves all controls reachable, no overflow.
            {
                var win = new MainWindow();
                win.LoadFiles(new[] { sampleL, sampleP1, sampleP2 });
                SaveVisualWithDesktopBoundsToPng(
                    win,
                    Path.Combine(outputDir, "06_MainWindow_150pct_Desktop_Fitted.png"),
                    1220, 672,
                    1280, 672, 48,
                    "150% DPI (1280x720)",
                    144);
            }

            // 5. 200% DPI - window fitted to the usable area (960x492 DIPs).
            {
                var win = new MainWindow();
                win.LoadFiles(new[] { sampleL });
                SaveVisualWithDesktopBoundsToPng(
                    win,
                    Path.Combine(outputDir, "07_MainWindow_200pct_Desktop_Fitted.png"),
                    960, 492,
                    960, 492, 48,
                    "200% DPI (960x540)",
                    192);
            }

            // 6. CROP EDITOR WINDOW
            {
                var editor = new CropEditorWindow(sampleL, new ImageCropSettings(), overlayPath, "Sample Landscape");
                SaveWindowToPng(editor, Path.Combine(outputDir, "08_CropEditorWindow_100pct_Baseline.png"), 1020, 780, 96);
            }

            {
                var editor = new CropEditorWindow(sampleL, new ImageCropSettings(), overlayPath, "Sample Landscape");
                SaveWindowToPng(editor, Path.Combine(outputDir, "09_CropEditorWindow_MinWidth_700.png"), 700, 620, 96);
            }

            {
                var editor = new CropEditorWindow(sampleL, new ImageCropSettings(), overlayPath, "Sample Landscape");
                SaveVisualWithDesktopBoundsToPng(
                    editor,
                    Path.Combine(outputDir, "10_CropEditorWindow_150pct_Fitted.png"),
                    1020, 672,
                    1280, 672, 48,
                    "150% DPI (1280x720)",
                    144);
            }

            // 7. PREVIEW WINDOW
            if (results.Count > 0)
            {
                var preview = new PreviewWindow(results, 0);
                SaveVisualWithDesktopBoundsToPng(
                    preview,
                    Path.Combine(outputDir, "11_PreviewWindow_150pct_Fitted.png"),
                    1080, 672,
                    1280, 672, 48,
                    "150% DPI (1280x720)",
                    144);
            }

            // 8. SESSION CONFIRMATION DIALOG
            {
                var dialog = new SessionConfirmationDialog(
                    "3 photos currently staged",
                    "Selecting new media will replace your current staged batch.");
                SaveWindowToPng(dialog, Path.Combine(outputDir, "12_SessionConfirmationDialog_150pct.png"), 640, 260, 144);
            }

            // 9. UPDATE DIALOG
            {
                var checkResult = new UpdateCheckResult
                {
                    IsUpdateAvailable = true,
                    CurrentVersion = "1.7.0",
                    LatestVersion = "2.2.0",
                    Release = new GitHubRelease
                    {
                        Name = "v2.2.0 - High DPI & Layout Polish",
                        TagName = "v2.2.0",
                        Body = "Release highlights:\n• High DPI per-monitor scaling\n• Responsive layout",
                        PublishedAt = DateTimeOffset.Now
                    },
                    TargetAsset = new GitHubReleaseAsset
                    {
                        Name = "Alpha.Branding.Setup.exe",
                        BrowserDownloadUrl = "http://example.com/setup.exe"
                    }
                };
                var updateDialog = new UpdateDialog(checkResult, new UpdateService());
                SaveWindowToPng(updateDialog, Path.Combine(outputDir, "13_UpdateDialog_150pct.png"), 680, 580, 144);
            }
        });
    }
}
