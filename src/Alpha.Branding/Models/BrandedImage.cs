namespace Alpha.Branding.Models;

public sealed class BrandedImage
{
    public required string FileName { get; set; }
    public required byte[] ImageBytes { get; init; }
    public required System.Windows.Media.Imaging.BitmapImage Preview { get; init; }
    public int SequenceIndex { get; init; }
    public int BatchSize { get; init; }
}
