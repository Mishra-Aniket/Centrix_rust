namespace LectureAgent.Infrastructure.FileWatching;

using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;

/// <summary>
/// Watches for video and PDF files in a folder.
/// </summary>
public class FileWatcher : IFileWatcher, IDisposable
{
    private FileSystemWatcher? _watcher;
    private string? _folderPath;
    private readonly ConcurrentDictionary<string, TrackedFileInfo> _trackedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<FileWatcher> _logger;
    private readonly int _stabilityCheckMs = 2000;
    private readonly double _maxFileAgeHours;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public event EventHandler<FileDetectedEventArgs>? FileDetected;

    public bool IsRunning => _watcher != null;
    public string? MonitoredFolderPath => _folderPath;

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

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            _logger.LogInformation($"Created file watcher directory: {folderPath}");
        }

        _logger.LogInformation($"Starting file watcher for: {folderPath}");
        _folderPath = folderPath;

        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollingLoopAsync(_pollCts.Token));

        _watcher = new FileSystemWatcher(folderPath)
        {
            Filter = "*.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Error += OnError;

        ScanExisting();

        _logger.LogInformation("File watcher started with active stability monitoring");
    }

    public int ScanExisting()
    {
        if (_folderPath == null || !Directory.Exists(_folderPath))
            return 0;

        var newlyTracked = 0;

        try
        {
            var enumOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };

            foreach (var file in Directory.EnumerateFiles(_folderPath, "*.*", enumOptions))
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
            _logger.LogInformation($"Directory scan tracked {newlyTracked} new media file(s) for stability monitoring");
        }

        return newlyTracked;
    }

    public void Stop()
    {
        if (_watcher == null)
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

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileEvent;
        _watcher.Changed -= OnFileEvent;
        _watcher.Error -= OnError;
        _watcher.Dispose();
        _watcher = null;
        _folderPath = null;
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

            await Task.Delay(_stabilityCheckMs, cancellationToken);
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
            // Read-only or permissions check; try opening read-only without sharing
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
