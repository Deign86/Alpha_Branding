using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Alpha.Branding.Models;

public sealed class BrandedImage : INotifyPropertyChanged
{
    private string _fileName = string.Empty;
    private byte[] _imageBytes = Array.Empty<byte>();
    private BitmapImage _preview = default!;
    private ImageCropSettings? _cropSettings;
    private ImageCropSettings? _rightCropSettings;

    public required string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName != value)
            {
                _fileName = value;
                OnPropertyChanged();
            }
        }
    }

    public byte[] ImageBytes
    {
        get => _imageBytes;
        set
        {
            _imageBytes = value;
            OnPropertyChanged();
        }
    }

    public string? VideoFilePath { get; init; }
    public MediaType MediaType { get; init; } = MediaType.Image;
    public bool IsVideo => MediaType == MediaType.Video;
    public string DurationText { get; init; } = string.Empty;

    public required BitmapImage Preview
    {
        get => _preview;
        set
        {
            _preview = value;
            OnPropertyChanged();
        }
    }

    public int SequenceIndex { get; init; }
    public int BatchSize { get; init; }

    public IReadOnlyList<string> SourceFilePaths { get; set; } = [];
    public Alpha.Branding.Services.ImageBatchItem? SourceBatchItem { get; set; }

    public ImageCropSettings? CropSettings
    {
        get => _cropSettings;
        set
        {
            _cropSettings = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCustomCrop));
            OnPropertyChanged(nameof(CropStatusText));
        }
    }

    public ImageCropSettings? RightCropSettings
    {
        get => _rightCropSettings;
        set
        {
            _rightCropSettings = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCustomCrop));
            OnPropertyChanged(nameof(CropStatusText));
        }
    }

    public string? OverlayPath { get; set; }

    public bool CanEdit => !IsVideo && SourceFilePaths.Count > 0;
    public bool HasCustomCrop => (_cropSettings != null && !_cropSettings.IsDefault) || (_rightCropSettings != null && !_rightCropSettings.IsDefault);
    public string CropStatusText => HasCustomCrop ? "CUSTOM CROP" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
