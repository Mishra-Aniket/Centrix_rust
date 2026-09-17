namespace LectureAgent.Tests;

using LectureAgent.Infrastructure.FileWatching;
using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// The watcher must pick media up from the recordings folder AND from an optional
/// separate notes/PDF folder, and files are typed by extension, not by folder.
/// </summary>
public sealed class FileWatcherFolderTests : IDisposable
{
    private readonly string _root;
    private readonly string _recordingsFolder;
    private readonly string _notesFolder;

    public FileWatcherFolderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "centrix-filewatcher-tests", Guid.NewGuid().ToString("N"));
        _recordingsFolder = Path.Combine(_root, "recordings");
        _notesFolder = Path.Combine(_root, "notes");
        Directory.CreateDirectory(_recordingsFolder);
        Directory.CreateDirectory(_notesFolder);
    }

    private FileWatcher CreateWatcher()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileWatcher:MaxFileAgeHours"] = "0"
        }).Build();

        return new FileWatcher(NullLogger<FileWatcher>.Instance, configuration);
    }

    [Fact]
    public async Task DetectsFileInAddedNotesFolder()
    {
        using var watcher = CreateWatcher();
        var detected = new TaskCompletionSource<FileDetectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.FileDetected += (_, e) => detected.TrySetResult(e);

        watcher.Start(_recordingsFolder);
        watcher.AddFolder(_notesFolder);

        Assert.Equal(2, watcher.MonitoredFolders.Count);
        Assert.Contains(_notesFolder, watcher.MonitoredFolders);

        var pdfPath = Path.Combine(_notesFolder, "lecture-notes.pdf");
        await File.WriteAllBytesAsync(pdfPath, new byte[2048]);

        var eventArgs = await AwaitAsync(detected.Task);
        Assert.Equal(pdfPath, eventArgs.FilePath, ignoreCase: true);
        Assert.Equal("PDF", eventArgs.FileType);
    }

    [Fact]
    public async Task NotesInRecordingsFolderAreStillDetected()
    {
        using var watcher = CreateWatcher();
        var detected = new TaskCompletionSource<FileDetectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.FileDetected += (_, e) => detected.TrySetResult(e);

        watcher.Start(_recordingsFolder);

        var pdfPath = Path.Combine(_recordingsFolder, "notes.pdf");
        await File.WriteAllBytesAsync(pdfPath, new byte[2048]);

        var eventArgs = await AwaitAsync(detected.Task);
        Assert.Equal("PDF", eventArgs.FileType);
    }

    [Fact]
    public void DuplicateFolderIsIgnored()
    {
        using var watcher = CreateWatcher();
        watcher.Start(_recordingsFolder);
        watcher.AddFolder(_notesFolder);
        watcher.AddFolder(_notesFolder);

        Assert.Equal(2, watcher.MonitoredFolders.Count);
    }

    [Fact]
    public void MissingFolderIsCreatedAndWatched()
    {
        var missing = Path.Combine(_root, "created-by-watcher");
        using var watcher = CreateWatcher();
        watcher.Start(_recordingsFolder);
        watcher.AddFolder(missing);

        Assert.True(Directory.Exists(missing));
        Assert.Contains(missing, watcher.MonitoredFolders);
    }

    /// <summary>
    /// A file needs two consecutive stable-size polls (2s apart) plus an exclusive open
    /// before it is reported, so give the poller a generous but bounded budget.
    /// </summary>
    private static async Task<FileDetectedEventArgs> AwaitAsync(Task<FileDetectedEventArgs> task)
    {
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.True(finished == task, "File watcher did not raise FileDetected in time");
        return await task;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort on file-locked runs.
        }
    }
}
