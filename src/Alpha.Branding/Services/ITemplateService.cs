using Alpha.Branding.Models;

namespace Alpha.Branding.Services;

public interface ITemplateService
{
    IReadOnlyList<BrandingTemplate> GetTemplates();
    BrandingTemplate GetActiveTemplate();
    void SetActiveTemplate(string templateId);
    Task<BrandingTemplate> SaveTemplateAsync(string sourceFilePath, string templateName);
    bool DeleteTemplate(string templateId);
    Task<(bool IsValid, string Message, int Width, int Height, double AspectRatio)> ValidateTemplateAsync(string sourceFilePath);
}
