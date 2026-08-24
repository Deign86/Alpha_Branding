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

        try
        {
            var existingDialog = Application.Current?.Windows.OfType<SessionConfirmationDialog>().FirstOrDefault(w => w.IsVisible);
            if (existingDialog != null)
            {
                existingDialog.Activate();
                return SessionPromptResult.Cancel;
            }

            var activeWindow = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible && !(w is SessionConfirmationDialog))
                               ?? Application.Current?.MainWindow;

            var dialog = new SessionConfirmationDialog(title, message);
            if (activeWindow != null && activeWindow.IsLoaded && activeWindow.IsVisible && activeWindow != dialog)
            {
                try
                {
                    dialog.Owner = activeWindow;
                }
                catch
                {
                    // Parent window might be closing; proceed without explicit owner
                }
            }

            dialog.ShowDialog();
            return dialog.Result;
        }
        catch (InvalidOperationException)
        {
            return SessionPromptResult.Cancel;
        }
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
