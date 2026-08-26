using Alpha.Branding.Models;
using System.IO;
using System.Text.Json;

namespace Alpha.Branding.Services;

public interface IUpdateSettingsStore
{
    UpdateSettings LoadSettings();
    void SaveSettings(UpdateSettings settings);
    bool IsAutoUpdateDisabledByPolicy();
    bool IsVersionSkipped(string version);
    bool IsRemindLaterActive();
    void SkipVersion(string version);
    void SetRemindLater(TimeSpan duration);
    void RecordCheckCompleted();
}

public class UpdateSettingsStore : IUpdateSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _primaryFilePath;
    private readonly string _fallbackFilePath;

    public UpdateSettingsStore(string? customSettingsPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customSettingsPath))
        {
            _primaryFilePath = customSettingsPath;
            _fallbackFilePath = Path.Combine(Path.GetTempPath(), "Alpha_Branding_update_settings.json");
        }
        else
        {
            _primaryFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Alpha Premier Realty", "Branding Studio", "update_settings.json");
            _fallbackFilePath = Path.Combine(Path.GetTempPath(), "Alpha_Branding_update_settings.json");
        }
    }

    public virtual bool IsAutoUpdateDisabledByPolicy()
    {
        try
        {
            var envVar = Environment.GetEnvironmentVariable("ALPHA_BRANDING_DISABLE_AUTO_UPDATE");
            if (!string.IsNullOrWhiteSpace(envVar) &&
                (envVar.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                 envVar.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                 envVar.Equals("yes", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch
        {
            // Ignore environment read errors
        }

        return false;
    }

    public UpdateSettings LoadSettings()
    {
        var settings = LoadFromDisk();
        if (IsAutoUpdateDisabledByPolicy())
        {
            settings.AutoCheckEnabled = false;
        }
        return settings;
    }

    private UpdateSettings LoadFromDisk()
    {
        foreach (var path in new[] { _primaryFilePath, _fallbackFilePath })
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var deserialized = JsonSerializer.Deserialize<UpdateSettings>(json);
                    if (deserialized != null)
                    {
                        return deserialized;
                    }
                }
            }
            catch
            {
                // Fallback to next path or default
            }
        }

        return new UpdateSettings();
    }

    public void SaveSettings(UpdateSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var saved = false;

        try
        {
            var dir = Path.GetDirectoryName(_primaryFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(_primaryFilePath, json);
            saved = true;
        }
        catch
        {
            // Primary path failed, try fallback
        }

        if (!saved)
        {
            try
            {
                var dir = Path.GetDirectoryName(_fallbackFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(_fallbackFilePath, json);
            }
            catch
            {
                // Graceful degradation if both writes fail
            }
        }
    }

    public bool IsVersionSkipped(string version)
    {
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.SkippedVersion) || string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var normalizedSkipped = NormalizeVersion(settings.SkippedVersion);
        var normalizedIncoming = NormalizeVersion(version);
        return string.Equals(normalizedSkipped, normalizedIncoming, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsRemindLaterActive()
    {
        var settings = LoadSettings();
        if (settings.RemindLaterUntilUtc == null) return false;
        return DateTimeOffset.UtcNow < settings.RemindLaterUntilUtc.Value;
    }

    public void SkipVersion(string version)
    {
        var settings = LoadSettings();
        settings.SkippedVersion = NormalizeVersion(version);
        settings.RemindLaterUntilUtc = null;
        SaveSettings(settings);
    }

    public void SetRemindLater(TimeSpan duration)
    {
        var settings = LoadSettings();
        settings.RemindLaterUntilUtc = DateTimeOffset.UtcNow.Add(duration);
        SaveSettings(settings);
    }

    public void RecordCheckCompleted()
    {
        var settings = LoadSettings();
        settings.LastCheckTimeUtc = DateTimeOffset.UtcNow;
        SaveSettings(settings);
    }

    private static string NormalizeVersion(string version)
    {
        var v = version.Trim();
        if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            v = v[1..].Trim();
        }
        return v;
    }
}
