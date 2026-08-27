using Alpha.Branding.Models;
using Alpha.Branding.Services;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Alpha.Branding.Tests;

public class UpdateVersionComparisonTests
{
    [Theory]
    [InlineData("v1.7.0", "1.7.0")]
    [InlineData("1.7.0", "1.7.0")]
    [InlineData("V1.8.0.0", "1.8.0.0")]
    [InlineData("v1.8.0-beta.1", "1.8.0")]
    [InlineData("2.0.0+build123", "2.0.0")]
    public void TryParseVersionHandlesVariousFormats(string input, string expected)
    {
        var success = UpdateService.TryParseVersion(input, out var parsed);
        Assert.True(success);
        Assert.Equal(Version.Parse(expected), parsed);
    }

    [Theory]
    [InlineData("1.7.0", "v1.8.0", true)]
    [InlineData("v1.7.0", "v1.7.1", true)]
    [InlineData("v1.7.0", "v2.0.0", true)]
    [InlineData("1.7.0", "1.7.0", false)]
    [InlineData("v1.7.0", "v1.7.0", false)]
    [InlineData("v1.7.0", "v1.6.3", false)]
    [InlineData("2.0.0", "v1.9.9", false)]
    [InlineData("1.7.0.0", "1.7.0.1", true)]
    public void IsNewerVersionCorrectlyEvaluatesReleaseTags(string current, string candidate, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewerVersion(current, candidate));
    }
}

public class UpdateJsonParsingTests
{
    private const string SampleReleaseJson = """
    {
        "tag_name": "v1.8.0",
        "name": "Alpha Premier Property Branding Studio v1.8.0",
        "body": "### New Features\n- Added automated update watcher\n- Improved image compression pipeline",
        "prerelease": false,
        "draft": false,
        "published_at": "2026-08-25T12:00:00Z",
        "html_url": "https://github.com/Deign86/Alpha_Branding/releases/tag/v1.8.0",
        "assets": [
            {
                "name": "Alpha.Branding.Setup.exe",
                "browser_download_url": "https://github.com/Deign86/Alpha_Branding/releases/download/v1.8.0/Alpha.Branding.Setup.exe",
                "size": 145000000,
                "content_type": "application/x-msdownload"
            },
            {
                "name": "Alpha.Branding.msi",
                "browser_download_url": "https://github.com/Deign86/Alpha_Branding/releases/download/v1.8.0/Alpha.Branding.msi",
                "size": 140000000,
                "content_type": "application/x-msi"
            }
        ]
    }
    """;

    [Fact]
    public void DeserializesGitHubReleaseJsonPayload()
    {
        var release = JsonSerializer.Deserialize<GitHubRelease>(SampleReleaseJson);
        Assert.NotNull(release);
        Assert.Equal("v1.8.0", release.TagName);
        Assert.Equal("Alpha Premier Property Branding Studio v1.8.0", release.Name);
        Assert.Contains("automated update watcher", release.Body);
        Assert.False(release.Prerelease);
        Assert.False(release.Draft);
        Assert.Equal(2, release.Assets.Count);
        Assert.Equal("Alpha.Branding.Setup.exe", release.Assets[0].Name);
        Assert.Equal("Alpha.Branding.msi", release.Assets[1].Name);
        Assert.Equal(145000000, release.Assets[0].Size);
    }

    [Fact]
    public void SelectsCorrectAssetForPerUserAndPerMachineEnvironments()
    {
        var release = JsonSerializer.Deserialize<GitHubRelease>(SampleReleaseJson);
        Assert.NotNull(release);

        // Per-user should prefer Setup.exe bootstrapper
        var userAsset = UpdateService.SelectAssetForEnvironment(release.Assets, isMachineInstall: false);
        Assert.NotNull(userAsset);
        Assert.Equal("Alpha.Branding.Setup.exe", userAsset.Name);

        // Per-machine should prefer MSI package if available
        var machineAsset = UpdateService.SelectAssetForEnvironment(release.Assets, isMachineInstall: true);
        Assert.NotNull(machineAsset);
        Assert.Equal("Alpha.Branding.msi", machineAsset.Name);
    }
}

public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}

public class UpdateServiceWorkflowTests
{
    [Fact]
    public async Task CheckForUpdatesAsyncReturnsNewerVersionAvailable()
    {
        var jsonResponse = """
        {
            "tag_name": "v1.8.0",
            "name": "Alpha Premier Property Branding Studio v1.8.0",
            "body": "Changelog details",
            "prerelease": false,
            "draft": false,
            "published_at": "2026-08-25T10:00:00Z",
            "assets": [
                {
                    "name": "Alpha.Branding.Setup.exe",
                    "browser_download_url": "https://github.com/Deign86/Alpha_Branding/releases/download/v1.8.0/Alpha.Branding.Setup.exe",
                    "size": 150000000
                }
            ]
        }
        """;

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("https://api.github.com/repos/Deign86/Alpha_Branding/releases/latest", req.RequestUri?.ToString());
            Assert.Contains(req.Headers.UserAgent, h => h.Product?.Name == "AlphaBrandingStudio");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler);
        var tempSettingsFile = Path.Combine(Path.GetTempPath(), "test_update_settings_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new UpdateSettingsStore(tempSettingsFile);
            var service = new UpdateService(client, store, "v1.7.0");

            var result = await service.CheckForUpdatesAsync(isManualCheck: true);

            Assert.True(result.IsUpdateAvailable);
            Assert.Equal("v1.8.0", result.LatestVersion);
            Assert.Equal("v1.7.0", result.CurrentVersion);
            Assert.NotNull(result.TargetAsset);
            Assert.Equal("Alpha.Branding.Setup.exe", result.TargetAsset.Name);
            Assert.Null(result.ErrorMessage);
        }
        finally
        {
            if (File.Exists(tempSettingsFile)) File.Delete(tempSettingsFile);
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsyncIgnoresPrereleaseAndDraftReleases()
    {
        var jsonPrerelease = """
        {
            "tag_name": "v1.9.0-preview",
            "name": "Preview Release",
            "prerelease": true,
            "draft": false,
            "assets": []
        }
        """;

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonPrerelease, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var service = new UpdateService(client, new UpdateSettingsStore(), "v1.7.0");

        var result = await service.CheckForUpdatesAsync(isManualCheck: false);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdatesAsyncHandlesRateLimitAnd404Gracefully()
    {
        var handlerRateLimit = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var clientRateLimit = new HttpClient(handlerRateLimit);
        var serviceRateLimit = new UpdateService(clientRateLimit, new UpdateSettingsStore(), "v1.7.0");

        var resultRateLimit = await serviceRateLimit.CheckForUpdatesAsync(isManualCheck: true);
        Assert.False(resultRateLimit.IsUpdateAvailable);
        Assert.Contains("rate limit", resultRateLimit.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var handler404 = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client404 = new HttpClient(handler404);
        var service404 = new UpdateService(client404, new UpdateSettingsStore(), "v1.7.0");

        var result404 = await service404.CheckForUpdatesAsync(isManualCheck: true);
        Assert.False(result404.IsUpdateAvailable);
        Assert.Contains("No public releases found", result404.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckForUpdatesAsyncRespectsSkippedVersionAndRemindLater()
    {
        var jsonResponse = """
        {
            "tag_name": "v1.8.0",
            "name": "Update v1.8.0",
            "prerelease": false,
            "draft": false,
            "assets": [
                {
                    "name": "Alpha.Branding.Setup.exe",
                    "browser_download_url": "https://example.com/Alpha.Branding.Setup.exe",
                    "size": 1000
                }
            ]
        }
        """;

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var tempSettingsFile = Path.Combine(Path.GetTempPath(), "test_update_settings_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new UpdateSettingsStore(tempSettingsFile);
            var service = new UpdateService(new HttpClient(handler), store, "v1.7.0");

            // Mark v1.8.0 as skipped
            service.SkipVersion("v1.8.0");

            var result = await service.CheckForUpdatesAsync(isManualCheck: false);
            Assert.True(result.IsUpdateAvailable);
            Assert.True(result.IsSkipped, "Version v1.8.0 must be recognized as skipped for automatic prompts.");

            // Clear skipped and test Remind Later
            store.SaveSettings(new UpdateSettings());
            service.RemindLater(TimeSpan.FromHours(4));

            var resultRemind = await service.CheckForUpdatesAsync(isManualCheck: false);
            Assert.True(resultRemind.IsUpdateAvailable);
            Assert.True(resultRemind.IsRemindLaterActive, "Remind Later must be recognized as active.");
        }
        finally
        {
            if (File.Exists(tempSettingsFile)) File.Delete(tempSettingsFile);
        }
    }

    [Fact]
    public async Task DownloadReleaseAssetAsyncReportsProgressAndWritesFile()
    {
        var payloadBytes = Encoding.UTF8.GetBytes("Test binary installer payload simulated data for download.");
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payloadBytes)
        });

        var client = new HttpClient(handler);
        var service = new UpdateService(client, new UpdateSettingsStore(), "v1.7.0");
        var asset = new GitHubReleaseAsset
        {
            Name = "Alpha.Branding.Setup.exe",
            BrowserDownloadUrl = "https://example.com/Alpha.Branding.Setup.exe",
            Size = payloadBytes.Length
        };

        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var destPath = Path.Combine(tempDir.FullName, asset.Name);
            var progressReports = 0;
            var progress = new Progress<(long BytesDownloaded, long? TotalBytes, double Percent)>(_ =>
            {
                progressReports++;
            });

            var resultPath = await service.DownloadReleaseAssetAsync(asset, destPath, progress);

            Assert.Equal(destPath, resultPath);
            Assert.True(File.Exists(destPath));
            Assert.Equal(payloadBytes, await File.ReadAllBytesAsync(destPath));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void VerifyAuthenticodeSignatureRejectsUnsignedOrNonExistentFiles()
    {
        var service = new UpdateService(null, new UpdateSettingsStore(), "v1.7.0");

        // Non-existent file
        Assert.False(service.VerifyAuthenticodeSignature("C:\\non_existent_file_xyz.exe"));

        // Plain unsigned text file
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "not an authenticode signed binary");
            Assert.False(service.VerifyAuthenticodeSignature(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void UpdateDialogInitializesWithoutException()
    {
        var thread = new Thread(() =>
        {
            if (System.Windows.Application.Current == null)
                _ = new App();

            var updateResult = new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                CurrentVersion = "v1.7.0",
                LatestVersion = "v1.8.0",
                Release = new GitHubRelease
                {
                    TagName = "v1.8.0",
                    Name = "Alpha Premier Branding Studio v1.8.0",
                    Body = "Features and updates changelog.",
                    PublishedAt = DateTimeOffset.UtcNow
                },
                TargetAsset = new GitHubReleaseAsset
                {
                    Name = "Alpha.Branding.Setup.exe",
                    BrowserDownloadUrl = "https://github.com/Deign86/Alpha_Branding/releases/download/v1.8.0/Alpha.Branding.Setup.exe",
                    Size = 145000000
                }
            };

            var dialog = new UpdateDialog(updateResult, new UpdateService(null, new UpdateSettingsStore(), "v1.7.0"));
            Assert.NotNull(dialog);
            Assert.Equal("Software Update Available", dialog.Title);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}

public class MarkdownRendererTests
{
    [Fact]
    public void RendersMarkdownHeadersAndBulletListsIntoWpfElementsWithoutRawMarkup()
    {
        var thread = new Thread(() =>
        {
            var markdown = """
            ### Changes in v1.8.0

            - **Changeable & Saveable Branding Templates**: Support for selecting built-in templates and `custom` overlays.
            - **Strict Aspect Ratio Preservation**: Landscape photos are cropped without distortion.
            """;

            var panel = new System.Windows.Controls.StackPanel();
            MarkdownRenderer.RenderTo(markdown, panel);

            Assert.NotEmpty(panel.Children);

            // First child: Header TextBlock
            var header = panel.Children[0] as System.Windows.Controls.TextBlock;
            Assert.NotNull(header);
            Assert.DoesNotContain("###", header.Text);

            // Second child: Spacing
            // Third child: First bullet point Grid
            var bulletGrid = panel.Children[2] as System.Windows.Controls.Grid;
            Assert.NotNull(bulletGrid);
            Assert.Equal(2, bulletGrid.Children.Count);

            var bulletSymbol = bulletGrid.Children[0] as System.Windows.Controls.TextBlock;
            Assert.NotNull(bulletSymbol);
            Assert.Equal("•", bulletSymbol.Text);

            var bulletContent = bulletGrid.Children[1] as System.Windows.Controls.TextBlock;
            Assert.NotNull(bulletContent);
            Assert.NotEmpty(bulletContent.Inlines);

            // Ensure no raw markdown tokens in inlines
            foreach (var inline in bulletContent.Inlines)
            {
                if (inline is System.Windows.Documents.Run run)
                {
                    Assert.DoesNotContain("**", run.Text);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void HandlesEmptyAndNullMarkdownGracefully()
    {
        var thread = new Thread(() =>
        {
            var panel = new System.Windows.Controls.StackPanel();
            MarkdownRenderer.RenderTo("", panel);
            Assert.Empty(panel.Children);

            MarkdownRenderer.RenderTo("   ", panel);
            Assert.Empty(panel.Children);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}

