using System.Windows;
using System.IO;

namespace Alpha.Branding;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        DispatcherUnhandledException += (s, e) =>
        {
            LogCrash(e.Exception);
            MessageBox.Show(e.Exception.Message, "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogCrash(ex);
            }
        };
    }

    public static void LogCrash(Exception ex)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Alpha Premier Realty", "Branding Studio", "Logs");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "crash.log");
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:u}] {ex}\n\n");
        }
        catch
        {
            try
            {
                var fallbackPath = Path.Combine(Path.GetTempPath(), "Alpha_Branding_crash.log");
                File.AppendAllText(fallbackPath, $"[{DateTime.UtcNow:u}] {ex}\n\n");
            }
            catch
            {
                // Gracefully suppress secondary logging failures to avoid cascading crash
            }
        }
    }
}


