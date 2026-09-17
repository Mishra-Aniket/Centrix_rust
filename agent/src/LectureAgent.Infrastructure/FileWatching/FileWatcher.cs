namespace LectureAgent.Infrastructure.FileWatching;

using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

/// <summary>
/// Watches for video and PDF files in one or more folders (recordings folder plus an
/// optional separate notes/PDF folder). Every watched folder is scanned recursively.
/// </summary>
public class FileWatcher : IFileWatcher, IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly List<string> _folderPaths = new();
    private readonly ConcurrentDictionary<string, TrackedFileInfo> _trackedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<FileWatcher> _logger;
    private readonly int _stabilityCheckMs = 2000;
    private readonly double _maxFileAgeHours;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public event EventHandler<FileDetectedEventArgs>? FileDetected;

    public bool IsRunning => _watchers.Count > 0;
    public string? MonitoredFolderPath => _folderPaths.FirstOrDefault();
    public IReadOnlyList<string> MonitoredFolders => _folderPaths;

    private sealed class TrackedFileInfo
    {
        public string FilePath { get; set; } = null!;
        public long LastSizeBytes { get; set; } = -1;
        public int ConsecutiveStableCount { get; set; }
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    }

    public FileWatcher(ILogger<FileWatcher> logger, IConfiguration configuration)
    {
        _logger = logger;
        _maxFileAgeHours = configuration.GetValue("FileWatcher:MaxFileAgeHours", 24);
    }

    /// <summary>
    /// Only recent recordings should be uploaded (FileWatcher:MaxFileAgeHours, 0 = no limit).
    /// A file dropped in later that was last written months ago is ignored.
    /// </summary>
    private bool IsRecentFile(string path)
    {
        if (_maxFileAgeHours <= 0)
            return true;

        try
        {
            var ageHours = (DateTime.Now - File.GetLastWriteTime(path)).TotalHours;
            if (ageHours > _maxFileAgeHours)
            {
                _logger.LogInformation($"Ignoring old file ({ageHours:F0}h old, limit {_maxFileAgeHours:F0}h): {Path.GetFileName(path)}");
                return false;
            }
            return true;
        }
        catch
        {
            return true; // if the timestamp can't be read, don't block the file
        }
    }

    public void Start(string folderPath)
    {
        if (IsRunning)
            throw new InvalidOperationException("File watcher already running");

        _logger.LogInformation($"Starting file watcher for: {folderPath}");
        AddFolderCore(folderPath);

        if (_folderPaths.Count == 0)
        {
            throw new DirectoryNotFoundException($"No watchable folder could be created for: {folderPath}");
        }

        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollingLoopAsync(_pollCts.Token));

        foreach (var folder in _folderPaths)
        {
            CreateWatcherFor(folder);
        }

        ScanExisting();

        _logger.LogInformation("File watcher started with active stability monitoring");
    }

    /// <summary>
    /// Watches one more folder (e.g. a separate notes/PDF folder) while the watcher is
    /// already running, and scans it for existing media straight away.
    /// </summary>
    public void AddFolder(string folderPath)
    {
        if (!IsRunning)
            throw new InvalidOperationException("File watcher is not running; call Start first");

        var countBefore = _folderPaths.Count;
        AddFolderCore(folderPath);
        for (var i = countBefore; i < _folderPaths.Count; i++)
        {
            var folder = _folderPaths[i];
            CreateWatcherFor(folder);
            ScanExisting(folder);
        }
    }

    private void AddFolderCore(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            _logger.LogWarning("Empty folder path passed to the file watcher; ignored");
            return;
        }

        var normalized = Path.GetFullPath(folderPath);
        if (_folderPaths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return; // already watched
        }

        if (!Directory.Exists(normalized))
        {
            try
            {
                Directory.CreateDirectory(normalized);
                _logger.LogInformation($"Created file watcher directory: {normalized}");
            }
            catch (Exception ex)
            {
                // A missing drive or an unreachable share must not stop the agent: the
                // monitoring retry loop will call Start/AddFolder again until it works.
                _logger.LogWarning($"Could not watch folder '{normalized}': {ex.Message}. Will retry in background.");
                return;
            }
        }

        _folderPaths.Add(normalized);
    }

    private void CreateWatcherFor(string folderPath)
    {
        var watcher = new FileSystemWatcher(folderPath)
        {
            Filter = "*.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        watcher.Created += OnFileEvent;
        watcher.Changed += OnFileEvent;
        watcher.Error += OnError;
        _watchers.Add(watcher);
    }

    public int ScanExisting()
    {
        if (!IsRunning)
            return 0;

        var newlyTracked = 0;
        foreach (var folder in _folderPaths.ToArray())
        {
            newlyTracked += ScanExisting(folder);
        }

        return newlyTracked;
    }

    private int ScanExisting(string folder)
    {
        var newlyTracked = 0;

        try
        {
            var enumOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };

            foreach (var file in Directory.EnumerateFiles(folder, "*.*", enumOptions))
            {
                if (IsMediaFile(file) && IsRecentFile(file) && _trackedFiles.TryAdd(file, new TrackedFileInfo { FilePath = file }))
                {
                    newlyTracked++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Directory scan warning: {ex.Message}");
        }

        if (newlyTracked > 0)
        {
            _logger.LogInformation($"Directory scan tracked {newlyTracked} new media file(s) in {folder} for stability monitoring");
        }

        return newlyTracked;
    }

    public void Stop()
    {
        if (_watchers.Count == 0)
            return;

        _logger.LogInformation("Stopping file watcher");

        try
        {
            _pollCts?.Cancel();
            _pollTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
        finally
        {
            _pollCts?.Dispose();
            _pollCts = null;
            _pollTask = null;
        }

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnFileEvent;
            watcher.Changed -= OnFileEvent;
            watcher.Error -= OnError;
            watcher.Dispose();
        }

        _watchers.Clear();
        _folderPaths.Clear();
        _trackedFiles.Clear();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!IsMediaFile(e.FullPath) || !IsRecentFile(e.FullPath))
            return;

        _trackedFiles.TryAdd(e.FullPath, new TrackedFileInfo { FilePath = e.FullPath });
    }

    private async Task PollingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_trackedFiles.IsEmpty)
                {
                    foreach (var kvp in _trackedFiles.ToArray())
                    {
                        var path = kvp.Key;
                        var info = kvp.Value;

                        if (!File.Exists(path))
                        {
                            _trackedFiles.TryRemove(path, out _);
                            continue;
                        }

                        long currentSize;
                        try
                        {
                            currentSize = new FileInfo(path).Length;
                        }
                        catch
                        {
                            continue;
                        }

                        // File must have size > 0 to be considered valid
                        if (currentSize > 0 && currentSize == info.LastSizeBytes && CanAccessExclusively(path))
                        {
                            info.ConsecutiveStableCount++;
                            if (info.ConsecutiveStableCount >= 2)
                            {
                                if (_trackedFiles.TryRemove(path, out _))
                                {
                                    _logger.LogInformation($"File verified stable and complete: {Path.GetFileName(path)} ({currentSize} bytes)");
                                    var fileType = GetFileType(path);
                                    var detectedTime = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.Now;
                                    FileDetected?.Invoke(this, new FileDetectedEventArgs
                                    {
                                        FilePath = path,
                                        FileType = fileType,
                                        FileSizeBytes = currentSize,
                                        DetectedTime = detectedTime
                                    });
                                }
                            }
                        }
                        else
                        {
                            info.LastSizeBytes = currentSize;
                            info.ConsecutiveStableCount = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error in file stability poller: {ex.Message}");
            }

            try
            {
                await Task.Delay(_stabilityCheckMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static bool CanAccessExclusively(string filePath)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return stream.Length > 0;
        }
        catch (IOException)
        {
            // File is locked by recording application or copy process
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only or permissions check; try opening read-only with exclusive access (no sharing)
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return stream.Length > 0;
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        if (e.GetException() is Exception ex)
        {
            _logger.LogError($"File watcher error: {ex.Message}");
        }
    }

    private static bool IsMediaFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var mediaExtensions = new[] { ".mkv", ".mp4", ".mov", ".avi", ".webm", ".pdf", ".pptx", ".ppt" };
        return mediaExtensions.Contains(ext);
    }

    private static string GetFileType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return (ext == ".pdf" || ext == ".pptx" || ext == ".ppt") ? "PDF" : "VIDEO";
    }

    public void Dispose()
    {
        Stop();
    }
}
