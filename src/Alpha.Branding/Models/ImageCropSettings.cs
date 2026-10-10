using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Alpha.Branding.Models;

public sealed class ImageCropSettings : INotifyPropertyChanged
{
    private double _panX;
    private double _panY;
    private double _zoom = 1.0;
    private int _rotation;
    private List<RedactStroke> _redactStrokes = new();

    public double PanX
    {
        get => _panX;
        set
        {
            if (Math.Abs(_panX - value) > 0.001)
            {
                _panX = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDefault));
            }
        }
    }

    public double PanY
    {
        get => _panY;
        set
        {
            if (Math.Abs(_panY - value) > 0.001)
            {
                _panY = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDefault));
            }
        }
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            var clamped = Math.Clamp(value, 0.2, 5.0);
            if (Math.Abs(_zoom - clamped) > 0.001)
            {
                _zoom = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDefault));
                OnPropertyChanged(nameof(ZoomPercentageText));
            }
        }
    }

    public int Rotation
    {
        get => _rotation;
        set
        {
            var normalized = ((value % 360) + 360) % 360;
            if (_rotation != normalized)
            {
                _rotation = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDefault));
            }
        }
    }

    public string ZoomPercentageText => $"{Math.Round(Zoom * 100):0}%";

    public List<RedactStroke> RedactStrokes
    {
        get => _redactStrokes;
        set
        {
            _redactStrokes = RedactStrokeCollection.Clone(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRedactions));
        }
    }

    public bool HasRedactions => _redactStrokes.Count > 0;

    public bool IsDefault =>
        Math.Abs(PanX) < 0.001 &&
        Math.Abs(PanY) < 0.001 &&
        Math.Abs(Zoom - 1.0) < 0.001 &&
        Rotation == 0;

    /// <summary>Resets framing (pan, zoom, rotation). Pen marks are kept: they hide private details, and
    /// strokes are stored in source-image coordinates so they stay valid under any framing.</summary>
    public void Reset()
    {
        PanX = 0;
        PanY = 0;
        Zoom = 1.0;
        Rotation = 0;
    }

    public ImageCropSettings Clone() => new()
    {
        PanX = PanX,
        PanY = PanY,
        Zoom = Zoom,
        Rotation = Rotation,
        RedactStrokes = RedactStrokeCollection.Clone(RedactStrokes)
    };

    public void CopyFrom(ImageCropSettings source)
    {
        PanX = source.PanX;
        PanY = source.PanY;
        Zoom = source.Zoom;
        Rotation = source.Rotation;
        RedactStrokes = RedactStrokeCollection.Clone(source.RedactStrokes);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
