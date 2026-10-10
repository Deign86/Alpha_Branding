using Alpha.Branding.Models;
using Alpha.Branding.Services;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Polyline = System.Windows.Shapes.Polyline;

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
    private RedactStroke? _activeStroke;
    private (ImageCropSettings Crop, RedactStroke[] Strokes)? _clearedStrokes;

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

        ItemTitle = title ?? System.IO.Path.GetFileName(filePath);
        Title = $"{ItemTitle} | Crop & Alignment Editor | Alpha Premier";

        DataContext = this;
        PanModeToggle.Checked += ModeToggle_Checked;
        RedactModeToggle.Checked += ModeToggle_Checked;
        UndoRedactButton.Click += UndoRedact_Click;
        ClearRedactButton.Click += ClearRedact_Click;
        PanModeToggle.IsChecked = true;
        Loaded += CropEditorWindow_Loaded;
    }

    public string ItemTitle { get; }

    public string LeftFileName => Path.GetFileName(_filePath);
    public string RightFileName => Path.GetFileName(_secondFilePath ?? string.Empty);

    public bool HasMultipleSlots => _isPair;

    public bool IsLeftSlotSelected
    {
        get => _isLeftSlotSelected;
        set
        {
            if (_isLeftSlotSelected != value)
            {
                FinishRedactStroke();
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

    public string PanCoordinatesText => $"X: {(int)Math.Round(ActiveCrop.PanX)} px  Y: {(int)Math.Round(ActiveCrop.PanY)} px";

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
        try
        {
            return LoadBitmapFromBytes(bytes);
        }
        catch when (HeicDecodeService.IsHeic(path))
        {
            return LoadBitmapFromBytes(Task.Run(() => HeicDecodeService.TranscodeToJpegBytesAsync(path)).GetAwaiter().GetResult());
        }
    }

    private static BitmapImage LoadBitmapFromBytes(byte[] bytes)
    {
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
        if (bmp == null)
        {
            RedactOverlay.Children.Clear();
            return;
        }

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

        var (effectiveScale, drawX, drawY) = GetPreviewGeometry(ActiveCrop, bmp.PixelWidth, bmp.PixelHeight, TargetWidth, TargetHeight);
        var scaledW = Math.Max(1.0, srcW * effectiveScale);
        var scaledH = Math.Max(1.0, srcH * effectiveScale);

        PhotoImage.Width = scaledW;
        PhotoImage.Height = scaledH;
        PhotoImage.Margin = new Thickness(drawX, drawY, 0, 0);

        RedrawRedactOverlay();
        OnPropertyChanged(nameof(PanCoordinatesText));
        OnPropertyChanged(nameof(ActiveCrop));
    }

    private static (double Scale, double DrawX, double DrawY) GetPreviewGeometry(
        ImageCropSettings crop, int srcW, int srcH, int targetW, int targetH)
    {
        var quarterTurn = crop.Rotation == 90 || crop.Rotation == 270;
        var rotatedW = quarterTurn ? srcH : srcW;
        var rotatedH = quarterTurn ? srcW : srcH;
        var scaleFit = Math.Min((double)targetW / rotatedW, (double)targetH / rotatedH);
        var scaleFill = Math.Max((double)targetW / rotatedW, (double)targetH / rotatedH);
        var isSoloPortrait = targetW != ImageProcessingService.HalfWidth && rotatedH > rotatedW && targetW > targetH;
        var baseScale = isSoloPortrait ? scaleFit : scaleFill;
        var scale = baseScale * crop.Zoom;
        return (scale,
            (targetW - Math.Max(1.0, rotatedW * scale)) / 2.0 + crop.PanX,
            (targetH - Math.Max(1.0, rotatedH * scale)) / 2.0 + crop.PanY);
    }

    public static Point MapNormalizedToSourcePx(
        double nx, double ny, ImageCropSettings crop, int srcW, int srcH, int targetW, int targetH)
    {
        return ImageProcessingService.MapNormalizedToSourcePx(nx, ny, crop, srcW, srcH, targetW, targetH);
    }

    private void RedrawRedactOverlay()
    {
        RedactOverlay.Children.Clear();
        var bmp = GetActiveBitmap();
        if (bmp == null) return;

        var (scale, drawX, drawY) = GetPreviewGeometry(ActiveCrop, bmp.PixelWidth, bmp.PixelHeight, TargetWidth, TargetHeight);
        foreach (var stroke in ActiveCrop.RedactStrokes.Concat(_activeStroke is null ? [] : [_activeStroke]))
        {
            if (stroke.Points.Count == 0) continue;
            var line = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(stroke.R, stroke.G, stroke.B)),
                StrokeThickness = stroke.BrushSizeNormalized * Math.Min(bmp.PixelWidth, bmp.PixelHeight) * scale,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            foreach (var point in stroke.Points)
            {
                var x = point.X * bmp.PixelWidth;
                var y = point.Y * bmp.PixelHeight;
                var (rotatedX, rotatedY) = ActiveCrop.Rotation switch
                {
                    90 => (bmp.PixelHeight - y, x),
                    180 => (bmp.PixelWidth - x, bmp.PixelHeight - y),
                    270 => (y, bmp.PixelWidth - x),
                    _ => (x, y)
                };
                line.Points.Add(new Point(drawX + rotatedX * scale, drawY + rotatedY * scale));
            }
            if (line.Points.Count == 1) line.Points.Add(line.Points[0]);
            RedactOverlay.Children.Add(line);
        }
    }

    private void ModeToggle_Checked(object sender, RoutedEventArgs e)
    {
        var redact = ReferenceEquals(sender, RedactModeToggle);
        if (!redact) FinishRedactStroke();
        TargetCanvas.Cursor = redact ? Cursors.Cross : Cursors.SizeAll;
    }

    private void AddRedactPoint(Point position)
    {
        var bmp = GetActiveBitmap();
        if (_activeStroke == null || bmp == null) return;
        var source = MapNormalizedToSourcePx(position.X / TargetWidth, position.Y / TargetHeight,
            ActiveCrop, bmp.PixelWidth, bmp.PixelHeight, TargetWidth, TargetHeight);
        var normalized = new Point(source.X / bmp.PixelWidth, source.Y / bmp.PixelHeight);
        if (_activeStroke.Points.Count == 0 || _activeStroke.Points[^1] != normalized)
        {
            _activeStroke.AddPoint(normalized);
            RedrawRedactOverlay();
        }
    }

    private void FinishRedactStroke()
    {
        if (_activeStroke == null) return;
        if (_activeStroke.Points.Count >= 2)
        {
            ActiveCrop.RedactStrokes.Add(_activeStroke);
            _clearedStrokes = null;
        }
        _activeStroke = null;
        TargetCanvas.ReleaseMouseCapture();
        RedrawRedactOverlay();
    }

    private void UndoRedact_Click(object sender, RoutedEventArgs e)
    {
        FinishRedactStroke();
        if (ActiveCrop.RedactStrokes.Count == 0)
        {
            // One-level undo of CLEAR MARKS, restored only onto the slot it was cleared from.
            if (_clearedStrokes is { } cleared && ReferenceEquals(cleared.Crop, ActiveCrop))
            {
                ActiveCrop.RedactStrokes.AddRange(cleared.Strokes);
                _clearedStrokes = null;
                RedrawRedactOverlay();
            }
            return;
        }
        ActiveCrop.RedactStrokes.RemoveAt(ActiveCrop.RedactStrokes.Count - 1);
        RedrawRedactOverlay();
    }

    private void ClearRedact_Click(object sender, RoutedEventArgs e)
    {
        FinishRedactStroke();
        if (ActiveCrop.RedactStrokes.Count == 0) return;
        _clearedStrokes = (ActiveCrop, ActiveCrop.RedactStrokes.ToArray());
        ActiveCrop.RedactStrokes.Clear();
        RedrawRedactOverlay();
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (RedactModeToggle.IsChecked == true)
            {
                var bmp = GetActiveBitmap();
                if (bmp == null) return;
                var (scale, _, _) = GetPreviewGeometry(ActiveCrop, bmp.PixelWidth, bmp.PixelHeight, TargetWidth, TargetHeight);
                _activeStroke = new RedactStroke
                {
                    BrushSizeNormalized = BrushSizeSlider.Value / (Math.Min(bmp.PixelWidth, bmp.PixelHeight) * scale)
                };
                AddRedactPoint(e.GetPosition(TargetCanvas));
                TargetCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }
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
        if (_activeStroke != null && e.LeftButton == MouseButtonState.Pressed)
        {
            AddRedactPoint(e.GetPosition(TargetCanvas));
            e.Handled = true;
            return;
        }
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
        if (_activeStroke != null)
        {
            AddRedactPoint(e.GetPosition(TargetCanvas));
            FinishRedactStroke();
            e.Handled = true;
            return;
        }
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

    private void Viewport_KeyDown(object sender, KeyEventArgs e)
    {
        var step = Keyboard.Modifiers == ModifierKeys.Shift ? 50 : 10;
        switch (e.Key)
        {
            case Key.Left: ActiveCrop.PanX -= step; break;
            case Key.Right: ActiveCrop.PanX += step; break;
            case Key.Up: ActiveCrop.PanY -= step; break;
            case Key.Down: ActiveCrop.PanY += step; break;
            default: return;
        }
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
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            UndoRedact_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && RedactModeToggle.IsChecked == true)
        {
            PanModeToggle.IsChecked = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
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
