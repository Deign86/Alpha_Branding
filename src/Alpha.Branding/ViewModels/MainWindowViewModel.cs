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
    private readonly ITemplateService _templateService;
    private BrandingTemplate _selectedTemplate;
    private string _prefix = FileNameGenerator.DefaultPrefix;
    private bool _isBusy;
    private bool _isProcessing;
    private bool _hasUnsavedEdits;
    private string _status = "Select property photos and videos to begin.";
    private double _progress;
    private IReadOnlyList<string> _selectedFiles = [];
    private LayoutMode _globalLayoutMode = LayoutMode.Combine;
    private bool _isDragOver;

    public LayoutMode GlobalLayoutMode
    {
        get => _globalLayoutMode;
        set
        {
            if (_globalLayoutMode != value)
            {
                _globalLayoutMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCombineLayout));
                OnPropertyChanged(nameof(IsSeparateLayout));
                OnPropertyChanged(nameof(LayoutModeText));
            }
        }
    }

    public bool IsCombineLayout => GlobalLayoutMode == LayoutMode.Combine;
    public bool IsSeparateLayout => GlobalLayoutMode == LayoutMode.Separate;
    public string LayoutModeText => GlobalLayoutMode == LayoutMode.Combine ? "Combine Pairs" : "Separate All";

    public void SetCombineLayout() => GlobalLayoutMode = LayoutMode.Combine;
    public void SetSeparateLayout() => GlobalLayoutMode = LayoutMode.Separate;

    public MainWindowViewModel(
        ImageProcessingService processor,
        ISessionConfirmationService? confirmationService = null,
        ITemplateService? templateService = null)
    {
        _processor = processor;
        _confirmationService = confirmationService ?? new DefaultSessionConfirmationService();
        _templateService = templateService ?? new TemplateService();
        _selectedTemplate = _templateService.GetActiveTemplate();
        LoadTemplates();
        Results.CollectionChanged += OnResultsCollectionChanged;
    }

    public ISessionConfirmationService ConfirmationService => _confirmationService;
    public ITemplateService TemplateService => _templateService;

    public ObservableCollection<BrandingTemplate> AvailableTemplates { get; } = [];

    public BrandingTemplate SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (value != null && _selectedTemplate?.Id != value.Id)
            {
                _selectedTemplate = value;
                _templateService.SetActiveTemplate(value.Id);
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveTemplateName));
                OnPropertyChanged(nameof(ActiveTemplateDimensions));
                OnPropertyChanged(nameof(ActiveTemplateAspectRatio));
                OnPropertyChanged(nameof(CanDeleteActiveTemplate));
                Status = $"Active template: {value.Name} ({value.DimensionsText})";
            }
        }
    }

    public string ActiveTemplateName => SelectedTemplate?.Name ?? "Alpha Premier Classic";
    public string ActiveTemplateDimensions => SelectedTemplate?.DimensionsText ?? "Standard Resolution";
    public string ActiveTemplateAspectRatio => SelectedTemplate?.AspectRatioText ?? "6:5 (1.20:1)";
    public bool CanDeleteActiveTemplate => SelectedTemplate != null && !SelectedTemplate.IsBuiltIn && !IsBusy;

    public ObservableCollection<BrandedImage> Results { get; } = [];
    public ObservableCollection<SelectedPhotoItem> SelectedPhotos { get; } = [];

    public bool HasResults => Results.Count > 0;
    public bool HasSelectedPhotos => SelectedPhotos.Count > 0 && Results.Count == 0;
    public bool IsEmptyState => SelectedPhotos.Count == 0 && Results.Count == 0;

    public bool IsDragOver
    {
        get => _isDragOver;
        set
        {
            if (_isDragOver != value)
            {
                _isDragOver = value;
                OnPropertyChanged();
            }
        }
    }

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
                if (value) _isDragOver = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(CanDeleteActiveTemplate));
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
        var existingMap = SelectedPhotos.ToDictionary(p => p.FilePath, StringComparer.OrdinalIgnoreCase);
        SelectedPhotos.Clear();
        foreach (var file in _selectedFiles)
        {
            if (existingMap.TryGetValue(file, out var existingItem))
            {
                SelectedPhotos.Add(existingItem);
                continue;
            }

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

    public async Task<bool> LoadFilesWorkflowAsync(IEnumerable<string> filePaths)
    {
        if (IsBusy) return false;
        var filesList = filePaths?.Where(f => !string.IsNullOrWhiteSpace(f)).ToArray() ?? [];
        if (filesList.Length == 0) return false;

        if (HasUnsavedEdits || Results.Count > 0)
        {
            var message = HasUnsavedEdits
                ? "You have unsaved edits in the current session. Loading a new batch will replace the current items and discard those unsaved edits."
                : "Starting a new branding session will replace the active items in the current session. Do you want to export your current items first, discard and continue, or cancel?";

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

        SelectedFiles = filesList;
        return true;
    }

    public void LoadTemplates()
    {
        AvailableTemplates.Clear();
        foreach (var t in _templateService.GetTemplates())
        {
            AvailableTemplates.Add(t);
        }
        var active = _templateService.GetActiveTemplate();
        _selectedTemplate = AvailableTemplates.FirstOrDefault(t => t.Id == active.Id) ?? AvailableTemplates.FirstOrDefault() ?? active;
        OnPropertyChanged(nameof(SelectedTemplate));
        OnPropertyChanged(nameof(ActiveTemplateName));
        OnPropertyChanged(nameof(ActiveTemplateDimensions));
        OnPropertyChanged(nameof(ActiveTemplateAspectRatio));
        OnPropertyChanged(nameof(CanDeleteActiveTemplate));
    }

    public async Task<BrandingTemplate> ImportTemplateAsync(string sourceFilePath, string? templateName = null)
    {
        var saved = await _templateService.SaveTemplateAsync(sourceFilePath, templateName ?? Path.GetFileNameWithoutExtension(sourceFilePath));
        LoadTemplates();
        SelectedTemplate = AvailableTemplates.FirstOrDefault(t => t.Id == saved.Id) ?? saved;
        Status = $"Template '{saved.Name}' saved and activated ({saved.DimensionsText}).";
        return saved;
    }

    public bool DeleteSelectedTemplate()
    {
        if (SelectedTemplate == null || SelectedTemplate.IsBuiltIn) return false;
        var templateToDelete = SelectedTemplate;
        var success = _templateService.DeleteTemplate(templateToDelete.Id);
        if (success)
        {
            LoadTemplates();
            Status = $"Template '{templateToDelete.Name}' deleted.";
        }
        return success;
    }

    public async Task<bool> ApplyWorkflowAsync(string? overlayPath = null, CancellationToken token = default)
    {
        if (IsBusy) return false;
        if (SelectedFiles.Count == 0) throw new InvalidOperationException("Select at least one media file first.");

        var actualOverlay = string.IsNullOrWhiteSpace(overlayPath) ? SelectedTemplate?.FilePath : overlayPath;
        if (string.IsNullOrWhiteSpace(actualOverlay) || !File.Exists(actualOverlay))
        {
            throw new FileNotFoundException("Branding template overlay file not found.", actualOverlay);
        }

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

        await ApplyAsync(actualOverlay, token);
        return true;
    }

    public async Task ApplyAsync(string? overlayPath = null, CancellationToken token = default)
    {
        if (IsBusy) return;
        if (SelectedFiles.Count == 0) throw new InvalidOperationException("Select at least one media file first.");

        var actualOverlay = string.IsNullOrWhiteSpace(overlayPath) ? SelectedTemplate?.FilePath : overlayPath;
        if (string.IsNullOrWhiteSpace(actualOverlay) || !File.Exists(actualOverlay))
        {
            throw new FileNotFoundException("Branding template overlay file not found.", actualOverlay);
        }

        IsBusy = true;
        _isProcessing = true;
        Results.Clear();
        HasUnsavedEdits = false;
        Progress = 0;
        var failures = 0;
        try
        {
            Status = "Analyzing media files…";
            var plan = await ImageProcessingService.PlanBatchAsync(SelectedPhotos.ToList(), GlobalLayoutMode, token);
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
                    ImageBatchItem.SoloImage solo => $"Solo: {Path.GetFileName(solo.FilePath)}",
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
                    Results.Add(await _processor.ProcessBatchItemAsync(item, actualOverlay, Prefix, i, total, videoProgress, token));
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

    public void ResetAllStagedCrops()
    {
        foreach (var photo in SelectedPhotos)
        {
            photo.ResetCrop();
        }
        Status = "All staged image crops have been reset to default.";
    }

    public async Task ResetAllResultCropsAsync(CancellationToken token = default)
    {
        if (IsBusy || Results.Count == 0) return;
        var count = 0;
        foreach (var result in Results.Where(r => r.CanEdit && r.HasCustomCrop).ToList())
        {
            await ResetBrandedImageCropAsync(result, token);
            count++;
        }
        if (count > 0)
        {
            HasUnsavedEdits = true;
            Status = $"Reset crops on {count} branded image(s).";
        }
        else
        {
            Status = "All branded images are already using default framing.";
        }
    }

    public async Task ResetBrandedImageCropAsync(BrandedImage image, CancellationToken token = default)
    {
        if (!image.CanEdit) return;
        var defaultCrop = new ImageCropSettings();
        await UpdateBrandedImageCropAsync(image, defaultCrop, image.RightCropSettings != null ? new ImageCropSettings() : null, token);
    }

    public async Task UpdateBrandedImageCropAsync(BrandedImage image, ImageCropSettings newCrop, ImageCropSettings? newRightCrop = null, CancellationToken token = default)
    {
        if (IsBusy || !image.CanEdit) return;
        IsBusy = true;
        try
        {
            var overlay = SelectedTemplate?.FilePath;
            await _processor.RebrandImageAsync(image, newCrop, newRightCrop, overlay, token);
            HasUnsavedEdits = true;
            Status = $"Updated crop for {image.FileName}.";
        }
        catch (Exception ex)
        {
            Status = $"Failed to update crop: {ex.Message}";
        }
        finally
        {
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
