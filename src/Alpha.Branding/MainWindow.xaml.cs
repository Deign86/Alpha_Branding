using Alpha.Branding.Models;
using Alpha.Branding.Services;
using Alpha.Branding.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace Alpha.Branding;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    public MainWindowViewModel ViewModel => _viewModel;
    private readonly string _overlayPath = Path.Combine(AppContext.BaseDirectory, "Assets", "alpha_branding.png");

    public MainWindow() : this(new MainWindowViewModel(new ImageProcessingService()))
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        WindowThemeHelper.EnableDarkTitleBar(this);
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_viewModel.HasUnsavedEdits || _viewModel.HasResults)
        {
            e.Cancel = true;
            var message = _viewModel.HasUnsavedEdits
                ? "You have unsaved edits in the current session. Do you want to save them before exiting?"
                : "You have active branded items in the current session. Do you want to save them before exiting?";

            var result = _viewModel.ConfirmationService.PromptUnsavedEdits(
                "Unsaved edits in current session",
                message);

            if (result == SessionPromptResult.Cancel)
            {
                return;
            }

            if (result == SessionPromptResult.DiscardAndContinue)
            {
                _viewModel.DiscardEdits();
                Close();
                return;
            }

            if (result == SessionPromptResult.SaveAndContinue)
            {
                var defaultName = $"{FileNameGenerator.FolderName(_viewModel.Prefix)}_Export.zip";
                var savePath = _viewModel.ConfirmationService.PromptSaveZip(defaultName);
                if (!string.IsNullOrEmpty(savePath))
                {
                    try
                    {
                        await _viewModel.ExportZipAsync(savePath);
                        _viewModel.DiscardEdits();
                        Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to save: {ex.Message}", "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        else
        {
            base.OnClosing(e);
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            LoadFiles(args.Skip(1));
        }
    }

    private const string MediaFilter = "All Supported Media|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.mp4;*.mov;*.wmv;*.avi;*.m4v;*.mkv;*.webm|Photos (*.jpg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Videos (*.mp4;*.mov;*.wmv)|*.mp4;*.mov;*.wmv;*.avi;*.m4v;*.mkv;*.webm|All files|*.*";

    public void LoadFiles(IEnumerable<string> filePaths)
    {
        var supported = filePaths.Where(f =>
        {
            if (!File.Exists(f)) return false;
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp" or ".mp4" or ".mov" or ".wmv" or ".avi" or ".m4v" or ".mkv" or ".webm";
        }).ToArray();

        if (supported.Length > 0)
        {
            _viewModel.SelectedFiles = supported;
        }
    }

    private void SelectPhotos_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        var dialog = new OpenFileDialog { Multiselect = true, Filter = MediaFilter };
        if (dialog.ShowDialog() == true) _viewModel.SelectedFiles = dialog.FileNames;
    }

    private void AddMorePhotos_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        var dialog = new OpenFileDialog { Multiselect = true, Filter = MediaFilter };
        if (dialog.ShowDialog() == true)
        {
            var combined = _viewModel.SelectedFiles.ToList();
            foreach (var file in dialog.FileNames)
            {
                if (!combined.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    combined.Add(file);
                }
            }
            _viewModel.SelectedFiles = combined;
        }
    }

    private void RemoveSelectedPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy || sender is not FrameworkElement { DataContext: SelectedPhotoItem item }) return;
        _viewModel.RemoveSelectedFile(item.FilePath);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.ApplyWorkflowAsync(_overlayPath); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Branding failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportDropdown_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        if (_viewModel.Results.Count == 0)
        {
            MessageBox.Show("Apply branding before exporting.", "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            element.ContextMenu.IsOpen = true;
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e) => ExportDropdown_Click(sender, e);

    private async void ExportZip_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        if (_viewModel.Results.Count == 0)
        {
            MessageBox.Show("Apply branding before exporting.", "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var defaultName = $"{FileNameGenerator.FolderName(_viewModel.Prefix)}_Export.zip";
        var savePath = _viewModel.ConfirmationService.PromptSaveZip(defaultName);
        if (!string.IsNullOrWhiteSpace(savePath))
        {
            try
            {
                await _viewModel.ExportZipAsync(savePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void ExportIndividual_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        if (_viewModel.Results.Count == 0)
        {
            MessageBox.Show("Apply branding before exporting.", "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var targetFolder = _viewModel.ConfirmationService.PromptExportFolder();
        if (!string.IsNullOrWhiteSpace(targetFolder))
        {
            try
            {
                await _viewModel.ExportIndividualFilesAsync(targetFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy || sender is not FrameworkElement { DataContext: BrandedImage image }) return;
        var filter = image.IsVideo
            ? "MP4 Video|*.mp4|All files|*.*"
            : "JPEG image|*.jpg;*.jpeg|All files|*.*";
        var dialog = new SaveFileDialog { FileName = image.FileName, Filter = filter };
        if (dialog.ShowDialog() == true)
        {
            try { await _viewModel.SaveMediaAsync(image, dialog.FileName); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy || sender is not FrameworkElement { DataContext: BrandedImage image }) return;
        var index = _viewModel.Results.IndexOf(image);
        if (index >= 0) new PreviewWindow(_viewModel.Results, index) { Owner = this }.ShowDialog();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            LoadFiles(files);
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_viewModel.IsBusy) return;

        if (e.KeyboardDevice.Modifiers == System.Windows.Input.ModifierKeys.Control && e.Key == System.Windows.Input.Key.O)
        {
            SelectPhotos_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if ((e.KeyboardDevice.Modifiers == System.Windows.Input.ModifierKeys.Control && e.Key == System.Windows.Input.Key.B) || e.Key == System.Windows.Input.Key.F5)
        {
            if (_viewModel.CanApply)
            {
                Apply_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (e.KeyboardDevice.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift) && e.Key == System.Windows.Input.Key.E)
        {
            if (_viewModel.CanExport)
            {
                ExportIndividual_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (e.KeyboardDevice.Modifiers == System.Windows.Input.ModifierKeys.Control && (e.Key == System.Windows.Input.Key.E || e.Key == System.Windows.Input.Key.S))
        {
            if (_viewModel.CanExport)
            {
                ExportZip_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }
}

