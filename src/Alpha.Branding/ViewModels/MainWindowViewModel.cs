using Alpha.Branding.Models;
using Alpha.Branding.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Alpha.Branding.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ImageProcessingService _processor;
    private readonly ISessionConfirmationService _confirmationService;
    private string _prefix = FileNameGenerator.DefaultPrefix;
    private bool _isBusy;
    private bool _isProcessing;
    private bool _hasUnsavedEdits;
    private string _status = "Select property photos and videos to begin.";
    private double _progress;
    private IReadOnlyList<string> _selectedFiles = [];

    public MainWindowViewModel(
        ImageProcessingService processor,
        ISessionConfirmationService? confirmationService = null)
    {
        _processor = processor;
        _confirmationService = confirmationService ?? new DefaultSessionConfirmationService();
        Results.CollectionChanged += OnResultsCollectionChanged;
    }

    public ISessionConfirmationService ConfirmationService => _confirmationService;

    public ObservableCollection<BrandedImage> Results { get; } = [];
    public ObservableCollection<SelectedPhotoItem> SelectedPhotos { get; } = [];

    public bool HasResults => Results.Count > 0;
    public bool HasSelectedPhotos => SelectedPhotos.Count > 0 && Results.Count == 0;
    public bool IsEmptyState => SelectedPhotos.Count == 0 && Results.Count == 0;

    public string Prefix
    {
        get => _prefix;
        set
        {
            var sanitized = value ?? string.Empty;
            if (_prefix != sanitized)
            {
                _prefix = sanitized;
                RenameResults();
                if (Results.Count > 0 && !_isProcessing)
                {
                    HasUnsavedEdits = true;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(PatternPreview));
                UpdateApplyStatus();
            }
        }
    }

    public string PatternPreview => FileNameGenerator.Generate(Prefix, 0, Results.Count > 0 ? Results.Count : 10);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(CanExport));
            }
        }
    }

    public bool HasUnsavedEdits
    {
        get => _hasUnsavedEdits;
        set
        {
            if (_hasUnsavedEdits != value)
            {
                _hasUnsavedEdits = value;
                OnPropertyChanged();
                UpdateApplyStatus();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public double Progress
    {
        get => _progress;
        private set
        {
            _progress = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> SelectedFiles
    {
        get => _selectedFiles;
        set
        {
            _selectedFiles = value ?? [];
            UpdateSelectedPhotos();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(HasSelectedPhotos));
            OnPropertyChanged(nameof(IsEmptyState));
            UpdateApplyStatus();
        }
    }

    public bool CanApply => !IsBusy && SelectedFiles.Count > 0;
    public bool CanExport => !IsBusy && Results.Count > 0;

    public string SelectionSummary
    {
        get
        {
            if (SelectedFiles.Count == 0) return "No photos selected";
            var photoCount = SelectedFiles.Count(f => !VideoProcessingService.IsVideoFile(f));
            var videoCount = SelectedFiles.Count(f => VideoProcessingService.IsVideoFile(f));

            if (videoCount == 0)
                return $"{photoCount} photo(s) selected";
            if (photoCount == 0)
                return $"{videoCount} video(s) selected";

            return $"{SelectedFiles.Count} item(s) selected ({photoCount} photos, {videoCount} videos)";
        }
    }

    public string ApplyStatusHint
    {
        get
        {
            if (SelectedFiles.Count > 0 && Results.Count > 0)
            {
                if (HasUnsavedEdits)
                {
                    return "Unsaved edits in current session.";
                }
                var photoCount = SelectedFiles.Count(f => !VideoProcessingService.IsVideoFile(f));
                var videoCount = SelectedFiles.Count(f => VideoProcessingService.IsVideoFile(f));
                var noun = videoCount == 0 ? "photo(s)" : (photoCount == 0 ? "video(s)" : "item(s)");
                return $"{SelectedFiles.Count} new {noun} selected — applying branding will start a new session.";
            }

            return string.Empty;
        }
    }

    public bool HasApplyWarning => HasUnsavedEdits && SelectedFiles.Count > 0;
    public bool HasApplyHint => SelectedFiles.Count > 0 && Results.Count > 0;

    public void RemoveSelectedFile(string filePath)
    {
        if (IsBusy) return;
        var updated = _selectedFiles.Where(f => !string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        SelectedFiles = updated;
    }

    private void UpdateSelectedPhotos()
    {
        SelectedPhotos.Clear();
        foreach (var file in _selectedFiles)
        {
            var sizeText = string.Empty;
            BitmapImage? thumb = null;
            var isVideo = VideoProcessingService.IsVideoFile(file);
            var durationText = string.Empty;

            if (File.Exists(file))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var mb = fileInfo.Length / (1024.0 * 1024.0);
                    sizeText = mb >= 1.0 ? $"{mb:0.1} MB" : $"{Math.Max(1, fileInfo.Length / 1024)} KB";
                }
                catch { }

                if (isVideo)
                {
                    try
                    {
                        var task = Task.Run(() => VideoProcessingService.GetVideoMetadataAsync(file));
                        if (task.Wait(TimeSpan.FromMilliseconds(500)))
                        {
                            var meta = task.Result;
                            thumb = meta.Thumbnail;
                            durationText = meta.DurationText;
                        }
                    }
                    catch { }

                    thumb ??= ImageProcessingService.CreateFallbackThumbnail();
                }
                else
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(file);
                        using var ms = new MemoryStream(bytes);
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = ms;
                        bmp.DecodePixelWidth = 270;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        thumb = bmp;
                    }
                    catch { }
                }
            }

            SelectedPhotos.Add(new SelectedPhotoItem
            {
                FilePath = file,
                Thumbnail = thumb,
                FileSizeText = sizeText,
                MediaType = isVideo ? MediaType.Video : MediaType.Image,
                DurationText = durationText
            });
        }

        OnPropertyChanged(nameof(HasSelectedPhotos));
        OnPropertyChanged(nameof(IsEmptyState));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void MarkDirty()
    {
        if (Results.Count > 0 && !_isProcessing)
        {
            HasUnsavedEdits = true;
        }
    }

    public void DiscardEdits()
    {
        Results.Clear();
        HasUnsavedEdits = false;
        UpdateApplyStatus();
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasSelectedPhotos));
        OnPropertyChanged(nameof(IsEmptyState));
    }

    public async Task<bool> ApplyWorkflowAsync(string overlayPath, CancellationToken token = default)
    {
        if (IsBusy) return false;
        if (SelectedFiles.Count == 0) throw new InvalidOperationException("Select at least one media file first.");

        if (HasUnsavedEdits || Results.Count > 0)
        {
            var message = HasUnsavedEdits
                ? "You have unsaved edits in the current session. Applying branding to the newly selected files will replace the current items and discard those unsaved edits."
                : "Starting a new branding session will replace the active items in the current session. Applying branding to the newly selected files will discard the current items.";

            var promptResult = _confirmationService.PromptUnsavedEdits(
                "Start a new branding session?",
                message);

            switch (promptResult)
            {
                case SessionPromptResult.Cancel:
                    Status = "New session canceled. Current session retained.";
                    return false;

                case SessionPromptResult.SaveAndContinue:
                    var defaultFileName = $"{FileNameGenerator.FolderName(Prefix)}_Export.zip";
                    var exportPath = _confirmationService.PromptSaveZip(defaultFileName);
                    if (string.IsNullOrWhiteSpace(exportPath))
                    {
                        Status = "Save canceled. Current session retained.";
                        return false;
                    }

                    try
                    {
                        await ExportZipAsync(exportPath);
                        DiscardEdits();
                    }
                    catch (Exception ex)
                    {
                        Status = $"Export failed: {ex.Message}";
                        return false;
                    }
                    break;

                case SessionPromptResult.DiscardAndContinue:
                    DiscardEdits();
                    break;
            }
        }

        await ApplyAsync(overlayPath, token);
        return true;
    }

    public async Task ApplyAsync(string overlayPath, CancellationToken token = default)
    {
        if (IsBusy) return;
        if (SelectedFiles.Count == 0) throw new InvalidOperationException("Select at least one media file first.");

        IsBusy = true;
        _isProcessing = true;
        Results.Clear();
        HasUnsavedEdits = false;
        Progress = 0;
        var failures = 0;
        try
        {
            Status = "Analyzing media files…";
            var plan = await ImageProcessingService.PlanBatchAsync(SelectedFiles, token);
            var total = plan.Count;

            for (var i = 0; i < total; i++)
            {
                token.ThrowIfCancellationRequested();
                var item = plan[i];
                var itemDescription = item switch
                {
                    ImageBatchItem.PortraitPair pair => pair.LeftFilePath == pair.RightFilePath
                        ? $"Side-by-side: {Path.GetFileName(pair.LeftFilePath)}"
                        : $"Pair: {Path.GetFileName(pair.LeftFilePath)} + {Path.GetFileName(pair.RightFilePath)}",
                    ImageBatchItem.Landscape landscape => Path.GetFileName(landscape.FilePath),
                    ImageBatchItem.LonePortrait lone => Path.GetFileName(lone.FilePath),
                    ImageBatchItem.Video video => $"Video: {Path.GetFileName(video.FilePath)}",
                    _ => string.Empty
                };

                Status = $"Processing {i + 1} of {total} ({itemDescription})…";
                var itemBaseProgress = (double)i / total * 100.0;
                var itemPortion = 100.0 / total;

                var videoProgress = new Progress<double>(percent =>
                {
                    Progress = Math.Min(100.0, itemBaseProgress + (percent / 100.0) * itemPortion);
                    Status = $"Watermarking video {i + 1} of {total} ({percent:0}%)…";
                });

                try
                {
                    Results.Add(await _processor.ProcessBatchItemAsync(item, overlayPath, Prefix, i, total, videoProgress, token));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures++;
                    Status = $"Skipped {itemDescription}: {ex.Message}";
                }

                Progress = (i + 1d) / total * 100;
            }

            RenameResults();
            HasUnsavedEdits = false;
            OnPropertyChanged(nameof(CanExport));
            var hasVideos = Results.Any(r => r.IsVideo);
            var itemNoun = hasVideos ? "item(s)" : "image(s)";
            Status = failures == 0
                ? $"Completed {Results.Count} {itemNoun}."
                : $"Completed {Results.Count} {itemNoun}; skipped {failures}.";
        }
        finally
        {
            _isProcessing = false;
            IsBusy = false;
            UpdateApplyStatus();
        }
    }

    public async Task SaveImageAsync(BrandedImage image, string path) => await SaveMediaAsync(image, path);

    public async Task SaveMediaAsync(BrandedImage media, string path)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = $"Saving {media.FileName}…";
            if (media.IsVideo && !string.IsNullOrWhiteSpace(media.VideoFilePath) && File.Exists(media.VideoFilePath))
            {
                await Task.Run(() => File.Copy(media.VideoFilePath, path, true));
            }
            else
            {
                await File.WriteAllBytesAsync(path, media.ImageBytes);
            }
            Status = "Export complete.";
            if (Results.Count == 1)
            {
                HasUnsavedEdits = false;
            }
        }
        finally
        {
            IsBusy = false;
            UpdateApplyStatus();
        }
    }

    public async Task ExportZipAsync(string path)
    {
        if (IsBusy) return;
        if (Results.Count == 0) throw new InvalidOperationException("Apply branding before exporting.");

        IsBusy = true;
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Status = "Creating ZIP export…";
            await using (var file = File.Create(temporaryPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var folder = FileNameGenerator.FolderName(Prefix);
                for (var i = 0; i < Results.Count; i++)
                {
                    var result = Results[i];
                    var fileName = FileNameGenerator.Generate(Prefix, result.SequenceIndex, result.BatchSize, result.MediaType);
                    var entry = archive.CreateEntry($"{folder}/{fileName}");
                    await using var stream = entry.Open();

                    if (result.IsVideo && !string.IsNullOrWhiteSpace(result.VideoFilePath) && File.Exists(result.VideoFilePath))
                    {
                        await using var videoStream = File.OpenRead(result.VideoFilePath);
                        await videoStream.CopyToAsync(stream);
                    }
                    else
                    {
                        await stream.WriteAsync(result.ImageBytes);
                    }
                }
            }

            File.Move(temporaryPath, path, true);
            HasUnsavedEdits = false;
            Status = "ZIP export complete.";
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }

            IsBusy = false;
            UpdateApplyStatus();
        }
    }

    public async Task<int> ExportIndividualFilesAsync(string folderPath, CancellationToken token = default)
    {
        if (IsBusy) return 0;
        if (Results.Count == 0) throw new InvalidOperationException("Apply branding before exporting.");
        if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("Destination folder path must be specified.", nameof(folderPath));

        IsBusy = true;
        var savedCount = 0;
        try
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            Status = $"Exporting {Results.Count} individual item(s)…";
            Progress = 0;
            var total = Results.Count;

            for (var i = 0; i < total; i++)
            {
                token.ThrowIfCancellationRequested();
                var result = Results[i];
                var fileName = FileNameGenerator.Generate(Prefix, result.SequenceIndex, result.BatchSize, result.MediaType);
                var destinationFilePath = Path.Combine(folderPath, fileName);

                Status = $"Saving {i + 1} of {total} ({fileName})…";
                if (result.IsVideo && !string.IsNullOrWhiteSpace(result.VideoFilePath) && File.Exists(result.VideoFilePath))
                {
                    await Task.Run(() => File.Copy(result.VideoFilePath, destinationFilePath, true), token);
                }
                else
                {
                    await File.WriteAllBytesAsync(destinationFilePath, result.ImageBytes, token);
                }

                savedCount++;
                Progress = (i + 1d) / total * 100;
            }

            HasUnsavedEdits = false;
            var folderDisplayName = string.IsNullOrEmpty(Path.GetFileName(folderPath)) ? folderPath : Path.GetFileName(folderPath);
            Status = $"Export complete: {savedCount} file(s) saved to {folderDisplayName}.";
            return savedCount;
        }
        finally
        {
            IsBusy = false;
            UpdateApplyStatus();
        }
    }

    private void RenameResults()
    {
        for (var i = 0; i < Results.Count; i++)
        {
            var result = Results[i];
            result.FileName = FileNameGenerator.Generate(Prefix, result.SequenceIndex, result.BatchSize, result.MediaType);
        }
    }

    private void OnResultsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (BrandedImage item in e.OldItems)
            {
                item.PropertyChanged -= OnBrandedImagePropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (BrandedImage item in e.NewItems)
            {
                item.PropertyChanged += OnBrandedImagePropertyChanged;
            }
        }

        UpdateApplyStatus();
        OnPropertyChanged(nameof(CanExport));
    }

    private void OnBrandedImagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isProcessing)
        {
            HasUnsavedEdits = true;
        }
    }

    private void UpdateApplyStatus()
    {
        OnPropertyChanged(nameof(ApplyStatusHint));
        OnPropertyChanged(nameof(HasApplyHint));
        OnPropertyChanged(nameof(HasApplyWarning));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasSelectedPhotos));
        OnPropertyChanged(nameof(IsEmptyState));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
