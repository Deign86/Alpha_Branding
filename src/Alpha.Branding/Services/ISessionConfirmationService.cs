using System.Windows;
using Microsoft.Win32;

namespace Alpha.Branding.Services;

public enum SessionPromptResult
{
    Cancel,
    DiscardAndContinue,
    SaveAndContinue
}

public interface ISessionConfirmationService
{
    SessionPromptResult PromptUnsavedEdits(string title, string message);
    string? PromptSaveZip(string defaultFileName);
    string? PromptExportFolder();
}

public sealed class DefaultSessionConfirmationService : ISessionConfirmationService
{
    public SessionPromptResult PromptUnsavedEdits(string title, string message)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => PromptUnsavedEdits(title, message));
        }

        var activeWindow = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                           ?? Application.Current?.MainWindow;

        var dialog = new SessionConfirmationDialog(title, message);
        if (activeWindow != null && activeWindow.IsLoaded)
        {
            dialog.Owner = activeWindow;
        }

        dialog.ShowDialog();
        return dialog.Result;
    }

    public string? PromptSaveZip(string defaultFileName)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => PromptSaveZip(defaultFileName));
        }

        var dialog = new SaveFileDialog
        {
            FileName = defaultFileName,
            Filter = "ZIP archive|*.zip"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PromptExportFolder()
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => PromptExportFolder());
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Select Destination Folder for Individual Photos",
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
