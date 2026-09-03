using Alpha.Branding.Models;
using Alpha.Branding.Services;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Alpha.Branding;

public partial class CropEditorWindow : Window, INotifyPropertyChanged
{
    private readonly string _filePath;
    private readonly string? _secondFilePath;
    private readonly string _overlayPath;
    private readonly bool _isPair;

    private readonly ImageCropSettings _leftCrop;
    private readonly ImageCropSettings _rightCrop;

    private bool _isLeftSlotSelected = true;
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartPanX;
    private double _dragStartPanY;

    private BitmapSource? _leftBitmap;
    private BitmapSource? _rightBitmap;
    private BitmapSource? _overlayBitmap;

    public CropEditorWindow(
        string filePath,
        ImageCropSettings initialCrop,
        string overlayPath,
        string? title = null,
        string? secondFilePath = null,
        ImageCropSettings? secondCrop = null)
    {
        InitializeComponent();
        WindowThemeHelper.EnableDarkTitleBar(this);

        _filePath = filePath;
        _secondFilePath = secondFilePath;
        _isPair = !string.IsNullOrWhiteSpace(secondFilePath);
        _overlayPath = overlayPath;

        _leftCrop = initialCrop.Clone();
        _rightCrop = secondCrop?.Clone() ?? new ImageCropSettings();

        ItemTitle = title ?? Path.GetFileName(filePath);

        DataContext = this;
        Loaded += CropEditorWindow_Loaded;
    }

    public string ItemTitle { get; }
    public new string Title => ItemTitle;

    public bool HasMultipleSlots => _isPair;

    public bool IsLeftSlotSelected
    {
        get => _isLeftSlotSelected;
        set
        {
            if (_isLeftSlotSelected != value)
            {
                _isLeftSlotSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRightSlotSelected));
                OnPropertyChanged(nameof(ActiveCrop));
                OnPropertyChanged(nameof(ActiveFilePath));
                OnPropertyChanged(nameof(PanCoordinatesText));
                UpdatePreview();
            }
        }
    }

    public bool IsRightSlotSelected
    {
        get => !_isLeftSlotSelected;
        set => IsLeftSlotSelected = !value;
    }

    public ImageCropSettings ActiveCrop => IsLeftSlotSelected ? _leftCrop : _rightCrop;
    public ImageCropSettings LeftCropResult => _leftCrop;
    public ImageCropSettings RightCropResult => _rightCrop;

    public string ActiveFilePath => IsLeftSlotSelected || !_isPair ? _filePath : _secondFilePath!;

    public int TargetWidth => _isPair ? ImageProcessingService.HalfWidth : ImageProcessingService.TargetWidth;
    public int TargetHeight => ImageProcessingService.TargetHeight;

    public string TargetDimensionsText => $"{TargetWidth} × {TargetHeight} px";

    public string PanCoordinatesText => $"X: {Math.Round(ActiveCrop.PanX):0} px  Y: {Math.Round(ActiveCrop.PanY):0} px";

    private void CropEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadImages();
        UpdatePreview();
    }

    private void LoadImages()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                _leftBitmap = LoadBitmapFromFile(_filePath);
            }

            if (_isPair && !string.IsNullOrWhiteSpace(_secondFilePath) && File.Exists(_secondFilePath))
            {
                _rightBitmap = LoadBitmapFromFile(_secondFilePath);
            }

            if (File.Exists(_overlayPath))
            {
                _overlayBitmap = LoadBitmapFromFile(_overlayPath);
                OverlayImage.Source = _overlayBitmap;
            }
        }
        catch
        {
            // Gracefully handle file read exceptions
        }
    }

    private static BitmapImage LoadBitmapFromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private BitmapSource? GetActiveBitmap()
    {
        return (IsLeftSlotSelected || !_isPair) ? _leftBitmap : _rightBitmap;
    }

    private void UpdatePreview()
    {
        var bmp = GetActiveBitmap();
        if (bmp == null) return;

        var rot = ActiveCrop.Rotation;
        BitmapSource source = bmp;
        if (rot != 0)
        {
            var transform = new RotateTransform(rot);
            source = new TransformedBitmap(bmp, transform);
        }

        PhotoImage.Source = source;

        var srcW = source.PixelWidth;
        var srcH = source.PixelHeight;
        if (srcW <= 0 || srcH <= 0) return;

        var targetW = (double)TargetWidth;
        var targetH = (double)TargetHeight;

        var scaleFit = Math.Min(targetW / srcW, targetH / srcH);
        var scaleFill = Math.Max(targetW / srcW, targetH / srcH);

        // Portrait photo solo in landscape frame fits by default to avoid aggressive cropping
        var isSoloPortrait = !_isPair && srcH > srcW && targetW > targetH;
        var baseScale = isSoloPortrait ? scaleFit : scaleFill;

        var effectiveScale = baseScale * ActiveCrop.Zoom;
        var scaledW = Math.Max(1.0, srcW * effectiveScale);
        var scaledH = Math.Max(1.0, srcH * effectiveScale);

        var centerX = (targetW - scaledW) / 2.0;
        var centerY = (targetH - scaledH) / 2.0;

        var drawX = centerX + ActiveCrop.PanX;
        var drawY = centerY + ActiveCrop.PanY;

        PhotoImage.Width = scaledW;
        PhotoImage.Height = scaledH;
        PhotoImage.Margin = new Thickness(drawX, drawY, 0, 0);

        OnPropertyChanged(nameof(PanCoordinatesText));
        OnPropertyChanged(nameof(ActiveCrop));
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(TargetCanvas);
            _dragStartPanX = ActiveCrop.PanX;
            _dragStartPanY = ActiveCrop.PanY;
            TargetCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            var current = e.GetPosition(TargetCanvas);
            var deltaX = current.X - _dragStartPoint.X;
            var deltaY = current.Y - _dragStartPoint.Y;

            ActiveCrop.PanX = _dragStartPanX + deltaX;
            ActiveCrop.PanY = _dragStartPanY + deltaY;

            UpdatePreview();
            e.Handled = true;
        }
    }

    private void Canvas_MouseUp(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            TargetCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var delta = e.Delta > 0 ? 0.08 : -0.08;
        ActiveCrop.Zoom = Math.Clamp(ActiveCrop.Zoom + delta, 0.2, 4.0);
        UpdatePreview();
        e.Handled = true;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ActiveCrop.Zoom = Math.Clamp(ActiveCrop.Zoom + 0.1, 0.2, 4.0);
        UpdatePreview();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ActiveCrop.Zoom = Math.Clamp(ActiveCrop.Zoom - 0.1, 0.2, 4.0);
        UpdatePreview();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdatePreview();
    }

    private void FitToFrame_Click(object sender, RoutedEventArgs e)
    {
        var bmp = GetActiveBitmap();
        if (bmp == null) return;

        var srcW = (double)bmp.PixelWidth;
        var srcH = (double)bmp.PixelHeight;
        if (ActiveCrop.Rotation == 90 || ActiveCrop.Rotation == 270)
        {
            (srcW, srcH) = (srcH, srcW);
        }

        var targetW = (double)TargetWidth;
        var targetH = (double)TargetHeight;

        var scaleFit = Math.Min(targetW / srcW, targetH / srcH);
        var scaleFill = Math.Max(targetW / srcW, targetH / srcH);

        var isSoloPortrait = !_isPair && srcH > srcW && targetW > targetH;
        var baseScale = isSoloPortrait ? scaleFit : scaleFill;

        ActiveCrop.Zoom = scaleFit / baseScale;
        ActiveCrop.PanX = 0;
        ActiveCrop.PanY = 0;
        UpdatePreview();
    }

    private void FillFrame_Click(object sender, RoutedEventArgs e)
    {
        var bmp = GetActiveBitmap();
        if (bmp == null) return;

        var srcW = (double)bmp.PixelWidth;
        var srcH = (double)bmp.PixelHeight;
        if (ActiveCrop.Rotation == 90 || ActiveCrop.Rotation == 270)
        {
            (srcW, srcH) = (srcH, srcW);
        }

        var targetW = (double)TargetWidth;
        var targetH = (double)TargetHeight;

        var scaleFit = Math.Min(targetW / srcW, targetH / srcH);
        var scaleFill = Math.Max(targetW / srcW, targetH / srcH);

        var isSoloPortrait = !_isPair && srcH > srcW && targetW > targetH;
        var baseScale = isSoloPortrait ? scaleFit : scaleFill;

        ActiveCrop.Zoom = scaleFill / baseScale;
        ActiveCrop.PanX = 0;
        ActiveCrop.PanY = 0;
        UpdatePreview();
    }

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        ActiveCrop.Rotation = (ActiveCrop.Rotation + 90) % 360;
        UpdatePreview();
    }

    private void ResetCrop_Click(object sender, RoutedEventArgs e)
    {
        ActiveCrop.Reset();
        UpdatePreview();
    }

    private void SlotRadio_Changed(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private void SaveChanges_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            DialogResult = true;
            Close();
            e.Handled = true;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
