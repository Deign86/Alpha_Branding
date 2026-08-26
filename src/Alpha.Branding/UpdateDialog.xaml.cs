using Alpha.Branding.Models;
using Alpha.Branding.Services;
using System.IO;
using System.Windows;

namespace Alpha.Branding;

public partial class UpdateDialog : Window
{
    private readonly UpdateCheckResult _updateResult;
    private readonly IUpdateService _updateService;
    private CancellationTokenSource? _downloadCts;

    public UpdateDialog(UpdateCheckResult updateResult, IUpdateService updateService)
    {
        _updateResult = updateResult;
        _updateService = updateService;

        InitializeComponent();
        WindowThemeHelper.EnableDarkTitleBar(this);

        PopulateReleaseInfo();
    }

    private void PopulateReleaseInfo()
    {
        var release = _updateResult.Release;
        var latestVersion = !string.IsNullOrWhiteSpace(_updateResult.LatestVersion)
            ? _updateResult.LatestVersion
            : release?.TagName ?? "New Version";

        VersionBadgeTextBlock.Text = latestVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? latestVersion
            : $"v{latestVersion}";

        CurrentVersionTextBlock.Text = $"Current: {(_updateResult.CurrentVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? _updateResult.CurrentVersion : $"v{_updateResult.CurrentVersion}")}";

        if (!string.IsNullOrWhiteSpace(release?.Name))
        {
            ReleaseTitleTextBlock.Text = release.Name;
        }

        if (release?.PublishedAt.HasValue == true)
        {
            PublishedDateTextBlock.Text = $"Released: {release.PublishedAt.Value.LocalDateTime:MMM d, yyyy}";
        }
        else
        {
            PublishedDateTextBlock.Text = "Latest Release";
        }

        var notes = release?.Body;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            ChangelogTextBox.Text = notes;
        }
        else
        {
            ChangelogTextBox.Text = "No release notes provided for this version. Visit GitHub for details.";
        }
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        var asset = _updateResult.TargetAsset;
        if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            ShowError("No compatible installer asset found for this release.");
            return;
        }

        SetInProgress(true);
        ErrorBorder.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        DownloadProgressBar.Value = 0;
        DownloadPercentTextBlock.Text = "0%";
        DownloadStatusTextBlock.Text = $"Starting download for {asset.Name}…";

        _downloadCts = new CancellationTokenSource();

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "AlphaBranding_Update_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var destinationFilePath = Path.Combine(tempDir, asset.Name);

            var progress = new Progress<(long BytesDownloaded, long? TotalBytes, double Percent)>(report =>
            {
                DownloadProgressBar.Value = report.Percent;
                DownloadPercentTextBlock.Text = $"{report.Percent:0}%";

                var mbDownloaded = report.BytesDownloaded / (1024.0 * 1024.0);
                if (report.TotalBytes.HasValue && report.TotalBytes.Value > 0)
                {
                    var totalMb = report.TotalBytes.Value / (1024.0 * 1024.0);
                    DownloadStatusTextBlock.Text = $"Downloading: {mbDownloaded:0.1} MB of {totalMb:0.1} MB ({report.Percent:0}%)…";
                }
                else
                {
                    DownloadStatusTextBlock.Text = $"Downloading: {mbDownloaded:0.1} MB…";
                }
            });

            await _updateService.DownloadReleaseAssetAsync(asset, destinationFilePath, progress, _downloadCts.Token);

            DownloadStatusTextBlock.Text = "Verifying digital signature…";
            var isSignatureValid = await Task.Run(() => _updateService.VerifyAuthenticodeSignature(destinationFilePath));

            if (!isSignatureValid)
            {
                ShowError("Downloaded installer digital signature verification failed. The file may be corrupt or unauthorized.");
                SetInProgress(false);
                return;
            }

            DownloadStatusTextBlock.Text = "Launching installer…";
            var launched = _updateService.InstallAndRestart(destinationFilePath);
            if (!launched)
            {
                ShowError("Failed to launch the downloaded installer executable.");
                SetInProgress(false);
            }
        }
        catch (OperationCanceledException)
        {
            DownloadStatusTextBlock.Text = "Download canceled.";
            SetInProgress(false);
        }
        catch (Exception ex)
        {
            ShowError($"Update failed: {ex.Message}");
            SetInProgress(false);
        }
    }

    private void RemindLater_Click(object sender, RoutedEventArgs e)
    {
        _updateService.RemindLater(TimeSpan.FromHours(8));
        DialogResult = false;
        Close();
    }

    private void SkipVersion_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_updateResult.LatestVersion))
        {
            _updateService.SkipVersion(_updateResult.LatestVersion);
        }
        DialogResult = false;
        Close();
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        _updateService.OpenReleaseInBrowser(_updateResult.Release?.HtmlUrl);
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void SetInProgress(bool inProgress)
    {
        UpdateNowButton.IsEnabled = !inProgress;
        RemindLaterButton.IsEnabled = !inProgress;
        SkipVersionButton.IsEnabled = !inProgress;
    }

    protected override void OnClosed(EventArgs e)
    {
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        base.OnClosed(e);
    }
}
