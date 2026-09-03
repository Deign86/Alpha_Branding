using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Alpha.Branding.Models;

public sealed class SelectedPhotoItem : INotifyPropertyChanged
{
    private PhotoLayoutPreference _layoutPreference = PhotoLayoutPreference.Auto;
    private ImageSource? _thumbnail;

    public SelectedPhotoItem()
    {
        CropSettings.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCustomCrop));
            OnPropertyChanged(nameof(CropStatusText));
        };
    }

    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (_thumbnail != value)
            {
                _thumbnail = value;
                OnPropertyChanged();
            }
        }
    }

    public string FileSizeText { get; init; } = string.Empty;
    public MediaType MediaType { get; init; } = MediaType.Image;
    public bool IsVideo => MediaType == MediaType.Video;
    public bool CanEdit => !IsVideo;
    public string DurationText { get; init; } = string.Empty;

    public ImageCropSettings CropSettings { get; } = new();

    public PhotoLayoutPreference LayoutPreference
    {
        get => _layoutPreference;
        set
        {
            if (_layoutPreference != value)
            {
                _layoutPreference = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSolo));
                OnPropertyChanged(nameof(LayoutModeText));
            }
        }
    }

    public bool IsSolo => LayoutPreference == PhotoLayoutPreference.Solo;
    public bool HasCustomCrop => !CropSettings.IsDefault;
    public string LayoutModeText => IsSolo ? "SOLO" : "AUTO/COMBINE";
    public string CropStatusText => HasCustomCrop ? $"CROPPED ({CropSettings.ZoomPercentageText})" : string.Empty;

    public void ToggleLayoutPreference()
    {
        LayoutPreference = IsSolo ? PhotoLayoutPreference.Auto : PhotoLayoutPreference.Solo;
    }

    public void ResetCrop()
    {
        CropSettings.Reset();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
