using System.IO;
using System.Windows.Media;

namespace Alpha.Branding.Models;

public sealed class SelectedPhotoItem
{
    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public ImageSource? Thumbnail { get; init; }
    public string FileSizeText { get; init; } = string.Empty;
    public MediaType MediaType { get; init; } = MediaType.Image;
    public bool IsVideo => MediaType == MediaType.Video;
    public string DurationText { get; init; } = string.Empty;
}
