using Alpha.Branding.Models;
using Alpha.Branding.Services;
using Alpha.Branding.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace Alpha.Branding;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new(new ImageProcessingService());
    private readonly string _overlayPath = Path.Combine(AppContext.BaseDirectory, "Assets", "alpha_branding.png");

    public MainWindow()
    {
        InitializeComponent();
        WindowThemeHelper.EnableDarkTitleBar(this);
        DataContext = _viewModel;
    }

    private void SelectPhotos_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*" };
        if (dialog.ShowDialog() == true) _viewModel.SelectedFiles = dialog.FileNames;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.ApplyAsync(_overlayPath); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Branding failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        var dialog = new SaveFileDialog { FileName = $"{FileNameGenerator.FolderName(_viewModel.Prefix)}_Export.zip", Filter = "ZIP archive|*.zip" };
        if (dialog.ShowDialog() == true)
        {
            try { await _viewModel.ExportZipAsync(dialog.FileName); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy || sender is not FrameworkElement { DataContext: BrandedImage image }) return;
        var dialog = new SaveFileDialog { FileName = image.FileName, Filter = "JPEG image|*.jpg;*.jpeg|All files|*.*" };
        if (dialog.ShowDialog() == true)
        {
            try { await _viewModel.SaveImageAsync(image, dialog.FileName); }
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
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                var supported = Array.FindAll(files, f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp";
                });

                if (supported.Length > 0)
                {
                    _viewModel.SelectedFiles = supported;
                }
            }
        }
    }
}

