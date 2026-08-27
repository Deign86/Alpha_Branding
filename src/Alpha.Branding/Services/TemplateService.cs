using Alpha.Branding.Models;
using SixLabors.ImageSharp;
using System.IO;
using System.Text.Json;

namespace Alpha.Branding.Services;

public class TemplateService : ITemplateService
{
    public const string DefaultTemplateId = "alpha_premier_classic";
    public const string AugustTemplateId = "august_branding";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _storageDirectory;
    private readonly string _metaFilePath;
    private readonly List<BrandingTemplate> _templates = [];
    private string _activeTemplateId = DefaultTemplateId;

    public TemplateService(string? customStorageDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customStorageDirectory))
        {
            _storageDirectory = customStorageDirectory;
        }
        else
        {
            _storageDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Alpha Premier Realty", "Branding Studio", "Templates");
        }

        _metaFilePath = Path.Combine(_storageDirectory, "templates_meta.json");
        InitializeTemplates();
    }

    private class TemplateStoreData
    {
        public string ActiveTemplateId { get; set; } = DefaultTemplateId;
        public List<BrandingTemplate> CustomTemplates { get; set; } = [];
    }

    private void InitializeTemplates()
    {
        _templates.Clear();

        // 1. Built-in: Alpha Premier Classic
        var classicPath = Path.Combine(AppContext.BaseDirectory, "Assets", "alpha_branding.png");
        var classicTemplate = new BrandingTemplate
        {
            Id = DefaultTemplateId,
            Name = "Alpha Premier Classic",
            FilePath = classicPath,
            IsBuiltIn = true,
            Width = 2938,
            Height = 2463,
            AspectRatio = 1.1929,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        _templates.Add(classicTemplate);

        // 2. Built-in: August Branding
        var augustPath = Path.Combine(AppContext.BaseDirectory, "Assets", "august_branding.png");
        if (File.Exists(augustPath))
        {
            var augustTemplate = new BrandingTemplate
            {
                Id = AugustTemplateId,
                Name = "August Branding",
                FilePath = augustPath,
                IsBuiltIn = true,
                Width = 1024,
                Height = 858,
                AspectRatio = 1.1935,
                CreatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)
            };
            _templates.Add(augustTemplate);
        }

        // 3. Load saved user templates from persistent storage
        try
        {
            if (File.Exists(_metaFilePath))
            {
                var json = File.ReadAllText(_metaFilePath);
                var data = JsonSerializer.Deserialize<TemplateStoreData>(json);
                if (data != null)
                {
                    _activeTemplateId = data.ActiveTemplateId;
                    foreach (var custom in data.CustomTemplates)
                    {
                        if (File.Exists(custom.FilePath))
                        {
                            custom.IsBuiltIn = false;
                            _templates.Add(custom);
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback to default if loading custom metadata fails
        }

        if (!_templates.Any(t => string.Equals(t.Id, _activeTemplateId, StringComparison.OrdinalIgnoreCase)))
        {
            _activeTemplateId = DefaultTemplateId;
        }
    }

    public IReadOnlyList<BrandingTemplate> GetTemplates() => _templates.AsReadOnly();

    public BrandingTemplate GetActiveTemplate()
    {
        var match = _templates.FirstOrDefault(t => string.Equals(t.Id, _activeTemplateId, StringComparison.OrdinalIgnoreCase));
        return match ?? _templates.First();
    }

    public void SetActiveTemplate(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId)) return;
        var exists = _templates.Any(t => string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            _activeTemplateId = templateId;
            SaveMetadata();
        }
    }

    public async Task<(bool IsValid, string Message, int Width, int Height, double AspectRatio)> ValidateTemplateAsync(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            return (false, "Template file not found.", 0, 0, 0);
        }

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp"))
        {
            return (false, "Unsupported image format. Please select a PNG, JPG, or WEBP image.", 0, 0, 0);
        }

        try
        {
            await using var stream = File.OpenRead(sourceFilePath);
            var info = await Image.IdentifyAsync(stream);
            if (info == null || info.Width <= 0 || info.Height <= 0)
            {
                return (false, "Unable to read image dimensions.", 0, 0, 0);
            }

            var w = info.Width;
            var h = info.Height;
            var ratio = Math.Round((double)w / h, 4);

            var message = $"Valid template ({w}×{h} px, aspect ratio {ratio:0.00}:1). Aspect ratio will be strictly preserved.";
            return (true, message, w, h, ratio);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid image file: {ex.Message}", 0, 0, 0);
        }
    }

    public async Task<BrandingTemplate> SaveTemplateAsync(string sourceFilePath, string templateName)
    {
        var validation = await ValidateTemplateAsync(sourceFilePath);
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Message, nameof(sourceFilePath));
        }

        var sanitizedName = string.IsNullOrWhiteSpace(templateName)
            ? Path.GetFileNameWithoutExtension(sourceFilePath)
            : templateName.Trim();

        Directory.CreateDirectory(_storageDirectory);

        var id = "tpl_" + Guid.NewGuid().ToString("N")[..8];
        var fileExt = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrWhiteSpace(fileExt)) fileExt = ".png";
        var destFileName = $"{sanitizedName}_{id}{fileExt}";
        var destFilePath = Path.Combine(_storageDirectory, destFileName);

        File.Copy(sourceFilePath, destFilePath, true);

        var template = new BrandingTemplate
        {
            Id = id,
            Name = sanitizedName,
            FilePath = destFilePath,
            IsBuiltIn = false,
            Width = validation.Width,
            Height = validation.Height,
            AspectRatio = validation.AspectRatio,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _templates.Add(template);
        _activeTemplateId = id;
        SaveMetadata();

        return template;
    }

    public bool DeleteTemplate(string templateId)
    {
        var item = _templates.FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase));
        if (item == null || item.IsBuiltIn)
        {
            return false;
        }

        _templates.Remove(item);

        try
        {
            if (File.Exists(item.FilePath))
            {
                File.Delete(item.FilePath);
            }
        }
        catch { }

        if (string.Equals(_activeTemplateId, templateId, StringComparison.OrdinalIgnoreCase))
        {
            _activeTemplateId = DefaultTemplateId;
        }

        SaveMetadata();
        return true;
    }

    private void SaveMetadata()
    {
        try
        {
            Directory.CreateDirectory(_storageDirectory);
            var customOnly = _templates.Where(t => !t.IsBuiltIn).ToList();
            var data = new TemplateStoreData
            {
                ActiveTemplateId = _activeTemplateId,
                CustomTemplates = customOnly
            };
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(_metaFilePath, json);
        }
        catch
        {
            // Graceful error handling for write failures
        }
    }
}
