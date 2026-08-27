using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace Alpha.Branding.Models;

public class BrandingTemplate : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _filePath = string.Empty;
    private bool _isBuiltIn;
    private int _width;
    private int _height;
    private double _aspectRatio;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private BitmapImage? _thumbnail;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetField(ref _filePath, value))
            {
                _thumbnail = null;
                OnPropertyChanged(nameof(Thumbnail));
            }
        }
    }

    public bool IsBuiltIn
    {
        get => _isBuiltIn;
        set => SetField(ref _isBuiltIn, value);
    }

    public int Width
    {
        get => _width;
        set
        {
            if (SetField(ref _width, value))
            {
                UpdateAspectRatio();
            }
        }
    }

    public int Height
    {
        get => _height;
        set
        {
            if (SetField(ref _height, value))
            {
                UpdateAspectRatio();
            }
        }
    }

    public double AspectRatio
    {
        get => _aspectRatio;
        set => SetField(ref _aspectRatio, value);
    }

    public DateTimeOffset CreatedAt
    {
        get => _createdAt;
        set => SetField(ref _createdAt, value);
    }

    [JsonIgnore]
    public string DimensionsText => Width > 0 && Height > 0 ? $"{Width} × {Height} px" : "Standard Resolution";

    [JsonIgnore]
    public string AspectRatioText
    {
        get
        {
            if (Width <= 0 || Height <= 0) return "6:5 (1.20:1)";
            var ratio = (double)Width / Height;
            if (Math.Abs(ratio - 1.20) < 0.015) return "6:5 (1.20:1) • Official Ratio";
            if (Math.Abs(ratio - (16.0 / 9.0)) < 0.02) return "16:9 • Widescreen";
            if (Math.Abs(ratio - (4.0 / 3.0)) < 0.02) return "4:3 • Standard";
            if (Math.Abs(ratio - 1.0) < 0.02) return "1:1 • Square";
            return $"{ratio:0.00}:1 • Preserved Ratio";
        }
    }

    [JsonIgnore]
    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnail == null && File.Exists(FilePath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(FilePath);
                    using var ms = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.DecodePixelWidth = 200;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    _thumbnail = bmp;
                }
                catch
                {
                    // Fallback to null if loading fails
                }
            }
            return _thumbnail;
        }
    }

    private void UpdateAspectRatio()
    {
        if (Width > 0 && Height > 0)
        {
            AspectRatio = Math.Round((double)Width / Height, 4);
            OnPropertyChanged(nameof(DimensionsText));
            OnPropertyChanged(nameof(AspectRatioText));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
