using Alpha.Branding.Models;
using Alpha.Branding.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace Alpha.Branding.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ImageProcessingService _processor;
    private readonly ISessionConfirmationService _confirmationService;
    private string _prefix = FileNameGenerator.DefaultPrefix;
    private bool _isBusy;
    private bool _isProcessing;
    private bool _hasUnsavedEdits;
    private string _status = "Select property photos to begin.";
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

    public string SelectionSummary => SelectedFiles.Count == 0 ? "No photos selected" : $"{SelectedFiles.Count} photo(s) selected";

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
                return $"{SelectedFiles.Count} new photo(s) selected — applying branding will start a new session.";
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
            System.Windows.Media.Imaging.BitmapImage? thumb = null;

            if (File.Exists(file))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var mb = fileInfo.Length / (1024.0 * 1024.0);
                    sizeText = mb >= 1.0 ? $"{mb:0.1} MB" : $"{Math.Max(1, fileInfo.Length / 1024)} KB";
                }
                catch { }

                try
                {
                    var bytes = File.ReadAllBytes(file);
                    using var ms = new MemoryStream(bytes);
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.DecodePixelWidth = 270;
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    thumb = bmp;
                }
                catch { }
            }

            SelectedPhotos.Add(new SelectedPhotoItem
            {
                FilePath = file,
                Thumbnail = thumb,
                FileSizeText = sizeText
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
        if (SelectedFiles.Count == 0) throw new InvalidOperationException("Select at least one image first.");

        if (HasUnsavedEdits)
        {
            var promptResult = _confirmationService.PromptUnsavedEdits(
                "Start a new branding session?",
                "You have unsaved edits in the current session. Applying branding to the newly selected photos will replace the current photos and discard those unsaved edits.");

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
        if (SelectedFiles.Count == 0) throw new InvalidOperationException("Select at least one image first.");

        IsBusy = true;
        _isProcessing = true;
        Results.Clear();
        HasUnsavedEdits = false;
        Progress = 0;
        var failures = 0;
        try
        {
            Status = "Analyzing photo orientations…";
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
                    _ => string.Empty
                };

                Status = $"Processing {i + 1} of {total} ({itemDescription})…";
                try
                {
                    Results.Add(await _processor.ProcessBatchItemAsync(item, overlayPath, Prefix, i, total, token));
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
            Status = failures == 0
                ? $"Completed {Results.Count} image(s)."
                : $"Completed {Results.Count} image(s); skipped {failures}.";
        }
        finally
        {
            _isProcessing = false;
            IsBusy = false;
            UpdateApplyStatus();
        }
    }

    public async Task SaveImageAsync(BrandedImage image, string path)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = $"Saving {image.FileName}…";
            await File.WriteAllBytesAsync(path, image.ImageBytes);
            Status = "Image export complete.";
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
                    var fileName = FileNameGenerator.Generate(Prefix, result.SequenceIndex, result.BatchSize);
                    var entry = archive.CreateEntry($"{folder}/{fileName}");
                    await using var stream = entry.Open();
                    await stream.WriteAsync(result.ImageBytes);
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

            Status = $"Exporting {Results.Count} individual image(s)…";
            Progress = 0;
            var total = Results.Count;

            for (var i = 0; i < total; i++)
            {
                token.ThrowIfCancellationRequested();
                var result = Results[i];
                var fileName = FileNameGenerator.Generate(Prefix, result.SequenceIndex, result.BatchSize);
                var destinationFilePath = Path.Combine(folderPath, fileName);

                Status = $"Saving {i + 1} of {total} ({fileName})…";
                await File.WriteAllBytesAsync(destinationFilePath, result.ImageBytes, token);
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
            result.FileName = FileNameGenerator.Generate(Prefix, result.SequenceIndex, result.BatchSize);
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
