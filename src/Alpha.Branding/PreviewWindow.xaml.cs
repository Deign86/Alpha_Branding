using Alpha.Branding.Models;
using Alpha.Branding.Services;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Alpha.Branding;

public partial class PreviewWindow : Window, INotifyPropertyChanged
{
    private readonly IReadOnlyList<BrandedImage> _results;
    private int _selectedIndex;
    private readonly DispatcherTimer _timer;
    private bool _isDraggingSlider;
    private bool _isPlaying;

    public PreviewWindow(IReadOnlyList<BrandedImage> results, int selectedIndex)
    {
        InitializeComponent();
        WindowThemeHelper.EnableDarkTitleBar(this);
        _results = results;
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, results.Count - 1));
        DataContext = this;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _timer.Tick += Timer_Tick;

        Loaded += (_, _) => UpdateMediaView();
    }

    public BrandedImage? Current => _results.Count == 0 ? null : _results[_selectedIndex];
    public string PositionText => _results.Count == 0 ? "0 of 0" : $"{_selectedIndex + 1} of {_results.Count}";
    public bool HasMultiplePhotos => _results.Count > 1;
    public bool IsVideoCurrent => Current?.IsVideo == true;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Previous_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int delta)
    {
        if (_results.Count == 0) return;
        _selectedIndex = (_selectedIndex + delta + _results.Count) % _results.Count;
        UpdateMediaView();
        PropertyChanged?.Invoke(this, new(nameof(Current)));
        PropertyChanged?.Invoke(this, new(nameof(PositionText)));
        PropertyChanged?.Invoke(this, new(nameof(IsVideoCurrent)));
    }

    private void UpdateMediaView()
    {
        _timer.Stop();
        VideoPlayer.Stop();
        _isPlaying = false;
        PlayPauseButton.Content = "▶ PLAY";

        if (Current?.IsVideo == true)
        {
            PreviewImageViewer.Visibility = Visibility.Collapsed;
            VideoPlayerContainer.Visibility = Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(Current.VideoFilePath) && File.Exists(Current.VideoFilePath))
            {
                try
                {
                    VideoPlayer.Source = new Uri(Current.VideoFilePath);
                    VideoPlayer.Play();
                    _isPlaying = true;
                    PlayPauseButton.Content = "⏸ PAUSE";
                    _timer.Start();
                }
                catch
                {
                    // Gracefully fallback
                }
            }
        }
        else
        {
            PreviewImageViewer.Visibility = Visibility.Visible;
            VideoPlayerContainer.Visibility = Visibility.Collapsed;
            VideoPlayer.Source = null;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_isDraggingSlider && VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            var total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            var pos = VideoPlayer.Position.TotalSeconds;
            TimelineSlider.Maximum = total;
            TimelineSlider.Value = pos;
            UpdateTimeText(VideoPlayer.Position, VideoPlayer.NaturalDuration.TimeSpan);
        }
    }

    private void UpdateTimeText(TimeSpan current, TimeSpan total)
    {
        var curStr = current.TotalHours >= 1 ? current.ToString(@"h\:mm\:ss") : current.ToString(@"m\:ss");
        var totStr = total.TotalHours >= 1 ? total.ToString(@"h\:mm\:ss") : total.ToString(@"m\:ss");
        TimeTextBlock.Text = $"{curStr} / {totStr}";
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            VideoPlayer.Pause();
            _isPlaying = false;
            PlayPauseButton.Content = "▶ PLAY";
            _timer.Stop();
        }
        else
        {
            VideoPlayer.Play();
            _isPlaying = true;
            PlayPauseButton.Content = "⏸ PAUSE";
            _timer.Start();
        }
    }

    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            TimelineSlider.Maximum = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            TimelineSlider.Value = 0;
            UpdateTimeText(TimeSpan.Zero, VideoPlayer.NaturalDuration.TimeSpan);
        }
    }

    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Position = TimeSpan.Zero;
        VideoPlayer.Pause();
        _isPlaying = false;
        PlayPauseButton.Content = "▶ PLAY";
        _timer.Stop();
        TimelineSlider.Value = 0;
    }

    private void TimelineSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
    }

    private void TimelineSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = false;
        VideoPlayer.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider && VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            var pos = TimeSpan.FromSeconds(TimelineSlider.Value);
            UpdateTimeText(pos, VideoPlayer.NaturalDuration.TimeSpan);
        }
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        VideoPlayer.IsMuted = !VideoPlayer.IsMuted;
        MuteButton.Content = VideoPlayer.IsMuted ? "🔇" : "🔊";
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Left)
        {
            Move(-1);
        }
        else if (e.Key == Key.Right)
        {
            Move(1);
        }
        else if (e.Key == Key.Space && IsVideoCurrent)
        {
            PlayPause_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
        try
        {
            VideoPlayer.Stop();
            VideoPlayer.Close();
        }
        catch { }
    }
}
