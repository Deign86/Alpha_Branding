using Alpha.Branding.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Windows;

namespace Alpha.Branding.Services;

public enum InstallMode
{
    PerUser,
    PerMachine
}

public class UpdateService : IUpdateService
{
    public const string DefaultRepository = "Deign86/Alpha_Branding";
    public const string DefaultPublisherName = "Alpha Premier";

    private readonly HttpClient _httpClient;
    private readonly IUpdateSettingsStore _settingsStore;
    private string _repositoryOwnerAndName = DefaultRepository;

    public string CurrentVersion { get; }
    public string RepositoryOwnerAndName
    {
        get => _repositoryOwnerAndName;
        set => _repositoryOwnerAndName = string.IsNullOrWhiteSpace(value) ? DefaultRepository : value.Trim();
    }

    public IUpdateSettingsStore SettingsStore => _settingsStore;

    public UpdateService(
        HttpClient? httpClient = null,
        IUpdateSettingsStore? settingsStore = null,
        string? currentVersion = null)
    {
        CurrentVersion = !string.IsNullOrWhiteSpace(currentVersion)
            ? currentVersion
            : GetApplicationVersion();

        _settingsStore = settingsStore ?? new UpdateSettingsStore();

        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        EnsureUserAgentHeader(_httpClient, CurrentVersion);
    }

    private static void EnsureUserAgentHeader(HttpClient client, string version)
    {
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            var productVersion = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version[1..] : version;
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AlphaBrandingStudio", productVersion));
        }
    }

    public static string GetApplicationVersion()
    {
        try
        {
            var entryAssembly = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
            var infoVer = entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVer))
            {
                var plusIndex = infoVer.IndexOf('+');
                var cleaned = plusIndex >= 0 ? infoVer[..plusIndex] : infoVer;
                return cleaned.Trim();
            }

            var asmVer = entryAssembly.GetName().Version;
            if (asmVer != null)
            {
                return $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}";
            }
        }
        catch
        {
            // fallback
        }

        return "1.7.0";
    }

    public static bool TryParseVersion(string versionString, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(versionString)) return false;

        var v = versionString.Trim();
        if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            v = v[1..].Trim();
        }

        // Handle possible prerelease suffixes like 1.8.0-beta or 1.8.0+build
        var dashIndex = v.IndexOfAny(['-', '+']);
        if (dashIndex >= 0)
        {
            v = v[..dashIndex];
        }

        var parts = v.Split('.');
        if (parts.Length == 1 && int.TryParse(parts[0], out var major))
        {
            version = new Version(major, 0);
            return true;
        }
        if (parts.Length == 2 && Version.TryParse($"{parts[0]}.{parts[1]}", out var v2))
        {
            version = v2;
            return true;
        }
        if (parts.Length == 3 && Version.TryParse($"{parts[0]}.{parts[1]}.{parts[2]}", out var v3))
        {
            version = v3;
            return true;
        }
        if (parts.Length >= 4 && Version.TryParse($"{parts[0]}.{parts[1]}.{parts[2]}.{parts[3]}", out var v4))
        {
            version = v4;
            return true;
        }

        return Version.TryParse(v, out version!);
    }

    public static bool IsNewerVersion(string currentVersion, string candidateVersion)
    {
        if (TryParseVersion(candidateVersion, out var candidate) && TryParseVersion(currentVersion, out var current))
        {
            return candidate > current;
        }
        return false;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        bool isManualCheck = false,
        CancellationToken cancellationToken = default)
    {
        var result = new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            IsManualCheck = isManualCheck
        };

        if (!isManualCheck && _settingsStore.IsAutoUpdateDisabledByPolicy())
        {
            result.ErrorMessage = "Automatic updates are disabled by system policy.";
            return result;
        }

        try
        {
            var endpoint = $"https://api.github.com/repos/{RepositoryOwnerAndName}/releases/latest";
            Log($"Checking for updates via GitHub REST API: {endpoint}");

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                result.ErrorMessage = "No public releases found on GitHub repository.";
                Log(result.ErrorMessage);
                return result;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                result.ErrorMessage = "GitHub API rate limit exceeded. Please try again later.";
                Log(result.ErrorMessage);
                return result;
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                result.ErrorMessage = "Unable to parse GitHub release information.";
                Log(result.ErrorMessage);
                return result;
            }

            if (release.Draft || release.Prerelease)
            {
                Log($"Skipping draft/prerelease tag {release.TagName}");
                return result;
            }

            result.Release = release;
            result.LatestVersion = release.TagName;
            _settingsStore.RecordCheckCompleted();

            var isNewer = IsNewerVersion(CurrentVersion, release.TagName);
            result.IsUpdateAvailable = isNewer;

            if (isNewer)
            {
                var isSkipped = _settingsStore.IsVersionSkipped(release.TagName);
                var isRemindLater = _settingsStore.IsRemindLaterActive();

                result.IsSkipped = isSkipped;
                result.IsRemindLaterActive = isRemindLater;

                var isMachine = DetectInstallMode() == InstallMode.PerMachine;
                result.TargetAsset = SelectAssetForEnvironment(release.Assets, isMachine);

                Log($"New version available: {release.TagName} (Current: {CurrentVersion}). Target asset: {result.TargetAsset?.Name ?? "None"}");
            }
            else
            {
                Log($"Application is up-to-date (Current: {CurrentVersion}, Latest: {release.TagName}).");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Update check was canceled.";
            return result;
        }
        catch (HttpRequestException ex)
        {
            result.ErrorMessage = $"Network error checking for updates: {ex.Message}";
            Log(result.ErrorMessage, ex);
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Failed to check for updates: {ex.Message}";
            Log(result.ErrorMessage, ex);
            return result;
        }
    }

    public static GitHubReleaseAsset? SelectAssetForEnvironment(IEnumerable<GitHubReleaseAsset>? assets, bool isMachineInstall)
    {
        if (assets == null) return null;
        var assetList = assets.ToList();
        if (assetList.Count == 0) return null;

        if (isMachineInstall)
        {
            // For machine install, prefer MSI if available, then Setup.exe
            var msi = assetList.FirstOrDefault(a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
            if (msi != null) return msi;

            var setupExe = assetList.FirstOrDefault(a => a.Name.Equals("Alpha.Branding.Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                                                         a.Name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase));
            if (setupExe != null) return setupExe;
        }
        else
        {
            // For per-user install, prefer Setup.exe bootstrapper, then MSI
            var setupExe = assetList.FirstOrDefault(a => a.Name.Equals("Alpha.Branding.Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                                                         a.Name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase));
            if (setupExe != null) return setupExe;

            var msi = assetList.FirstOrDefault(a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
            if (msi != null) return msi;
        }

        // Fallback to any .exe or .zip
        return assetList.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ??
               assetList.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public static InstallMode DetectInstallMode()
    {
        try
        {
            var processPath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if (!string.IsNullOrEmpty(programFiles) && processPath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
            {
                return InstallMode.PerMachine;
            }

            if (!string.IsNullOrEmpty(programFilesX86) && processPath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase))
            {
                return InstallMode.PerMachine;
            }

            // Check HKLM uninstall registry key
            using var hklmKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Alpha Premier Realty Branding Studio");
            if (hklmKey != null)
            {
                return InstallMode.PerMachine;
            }
        }
        catch
        {
            // Default to per-user
        }

        return InstallMode.PerUser;
    }

    public async Task<string> DownloadReleaseAssetAsync(
        GitHubReleaseAsset asset,
        string destinationFilePath,
        IProgress<(long BytesDownloaded, long? TotalBytes, double Percent)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            throw new ArgumentException("Asset or Download URL is missing.", nameof(asset));
        }

        var dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        Log($"Downloading asset '{asset.Name}' from {asset.BrowserDownloadUrl} to {destinationFilePath}");

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.BrowserDownloadUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : (long?)null);

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                var percent = (double)totalRead / totalBytes.Value * 100.0;
                progress?.Report((totalRead, totalBytes, percent));
            }
            else
            {
                progress?.Report((totalRead, null, 0));
            }
        }

        progress?.Report((totalRead, totalBytes, 100.0));
        Log($"Successfully downloaded {totalRead} bytes to {destinationFilePath}");
        return destinationFilePath;
    }

    public virtual bool VerifyAuthenticodeSignature(string filePath, string? expectedSubject = null)
    {
        if (!File.Exists(filePath))
        {
            Log($"Verification failed: File not found at '{filePath}'.");
            return false;
        }

        expectedSubject ??= DefaultPublisherName;

        try
        {
            // Inspect digital signature certificate
            using var cert = new X509Certificate2(X509Certificate2.CreateFromSignedFile(filePath));
            var subject = cert.Subject;
            var issuer = cert.Issuer;

            Log($"Authenticode Signature Found: Subject='{subject}', Issuer='{issuer}', NotAfter={cert.NotAfter:u}");

            if (string.IsNullOrWhiteSpace(subject))
            {
                Log("Verification failed: Empty certificate subject.");
                return false;
            }

            if (!subject.Contains(expectedSubject, StringComparison.OrdinalIgnoreCase))
            {
                Log($"Verification failed: Subject '{subject}' does not contain expected publisher '{expectedSubject}'.");
                return false;
            }

            return true;
        }
        catch (CryptographicException ex)
        {
            Log($"Authenticode verification failed: File '{filePath}' is unsigned or signature is invalid ({ex.Message}).", ex);
            return false;
        }
        catch (Exception ex)
        {
            Log($"Authenticode verification unexpected error: {ex.Message}", ex);
            return false;
        }
    }

    public virtual bool InstallAndRestart(string installerFilePath)
    {
        if (!File.Exists(installerFilePath))
        {
            Log($"Install failed: Installer file does not exist at '{installerFilePath}'.");
            return false;
        }

        try
        {
            Log($"Launching installer: {installerFilePath}");
            ProcessStartInfo psi;

            var ext = Path.GetExtension(installerFilePath).ToLowerInvariant();
            if (ext == ".msi")
            {
                psi = new ProcessStartInfo("msiexec.exe", $"/i \"{installerFilePath}\"")
                {
                    UseShellExecute = true
                };
            }
            else
            {
                psi = new ProcessStartInfo(installerFilePath)
                {
                    UseShellExecute = true
                };
            }

            Process.Start(psi);

            // Shutdown the current app to release file handles
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to launch installer '{installerFilePath}': {ex.Message}", ex);
            return false;
        }
    }

    public void SkipVersion(string version)
    {
        Log($"Skipping version {version}");
        _settingsStore.SkipVersion(version);
    }

    public void RemindLater(TimeSpan duration)
    {
        Log($"Remind me later set for duration: {duration}");
        _settingsStore.SetRemindLater(duration);
    }

    public void OpenReleaseInBrowser(string? url = null)
    {
        var targetUrl = !string.IsNullOrWhiteSpace(url)
            ? url
            : $"https://github.com/{RepositoryOwnerAndName}/releases/latest";

        try
        {
            Log($"Opening release in browser: {targetUrl}");
            Process.Start(new ProcessStartInfo
            {
                FileName = targetUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"Failed to open URL in browser: {targetUrl}", ex);
        }
    }

    public static void Log(string message, Exception? ex = null)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Alpha Premier Realty", "Branding Studio", "Logs");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "update.log");
            var exText = ex != null ? $"\nException: {ex}" : string.Empty;
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:u}] {message}{exText}\n");
        }
        catch
        {
            try
            {
                var fallbackPath = Path.Combine(Path.GetTempPath(), "Alpha_Branding_update.log");
                var exText = ex != null ? $"\nException: {ex}" : string.Empty;
                File.AppendAllText(fallbackPath, $"[{DateTime.UtcNow:u}] {message}{exText}\n");
            }
            catch
            {
                // Suppress secondary logging failures
            }
        }
    }
}
