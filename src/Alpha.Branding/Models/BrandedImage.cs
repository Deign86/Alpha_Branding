using System.ComponentModel;

namespace Alpha.Branding.Models;

public sealed class BrandedImage : INotifyPropertyChanged
{
    private string _fileName = string.Empty;

    public required string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName != value)
            {
                _fileName = value;
                PropertyChanged?.Invoke(this, new(nameof(FileName)));
            }
        }
    }

    public byte[] ImageBytes { get; init; } = Array.Empty<byte>();
    public string? VideoFilePath { get; init; }
    public MediaType MediaType { get; init; } = MediaType.Image;
    public bool IsVideo => MediaType == MediaType.Video;
    public string DurationText { get; init; } = string.Empty;
    public required System.Windows.Media.Imaging.BitmapImage Preview { get; init; }
    public int SequenceIndex { get; init; }
    public int BatchSize { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
