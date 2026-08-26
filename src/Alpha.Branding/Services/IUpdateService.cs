using Alpha.Branding.Models;

namespace Alpha.Branding.Services;

public interface IUpdateService
{
    string CurrentVersion { get; }
    string RepositoryOwnerAndName { get; set; }
    IUpdateSettingsStore SettingsStore { get; }

    Task<UpdateCheckResult> CheckForUpdatesAsync(bool isManualCheck = false, CancellationToken cancellationToken = default);
    Task<string> DownloadReleaseAssetAsync(
        GitHubReleaseAsset asset,
        string destinationFilePath,
        IProgress<(long BytesDownloaded, long? TotalBytes, double Percent)>? progress = null,
        CancellationToken cancellationToken = default);
    bool VerifyAuthenticodeSignature(string filePath, string? expectedSubject = null);
    bool InstallAndRestart(string installerFilePath);
    void SkipVersion(string version);
    void RemindLater(TimeSpan duration);
    void OpenReleaseInBrowser(string? url = null);
}
