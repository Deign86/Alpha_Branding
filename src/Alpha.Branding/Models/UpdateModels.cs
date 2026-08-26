using System.Text.Json.Serialization;

namespace Alpha.Branding.Models;

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = [];
}

public sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }
}

public sealed class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public GitHubRelease? Release { get; set; }
    public GitHubReleaseAsset? TargetAsset { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsSkipped { get; set; }
    public bool IsRemindLaterActive { get; set; }
    public bool IsManualCheck { get; set; }
}

public sealed class UpdateSettings
{
    public string? SkippedVersion { get; set; }
    public DateTimeOffset? LastCheckTimeUtc { get; set; }
    public DateTimeOffset? RemindLaterUntilUtc { get; set; }
    public bool AutoCheckEnabled { get; set; } = true;
}
