namespace LectureAgent.Services.AutoUpdate;

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Visible state of the auto-updater, surfaced through the control API so the
/// dashboard (or an operator over SSH) can see what the agent is about to do.
/// </summary>
public sealed class AutoUpdateState
{
    public string Status { get; set; } = "Idle";
    public DateTime? LastCheckedAt { get; set; }
    public string? CurrentVersion { get; set; }
    public string? AvailableVersion { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Keeps 500 center PCs up to date without anyone driving to them.
/// Periodically fetches a small version manifest, and when a newer build exists:
/// downloads it, verifies its SHA-256, stages the extracted files, then spawns a
/// detached helper script and stops the agent. The helper swaps the install folder
/// while nothing is running, restarts the agent, health-checks it and restores the
/// previous build if the new one never comes up (rollback).
/// Disabled by default; enable with AutoUpdate:Enabled + AutoUpdate:ManifestUrl.
/// </summary>
public sealed class AutoUpdateService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly AutoUpdateState _state;
    private readonly ILogger<AutoUpdateService> _logger;
    private readonly SemaphoreSlim _checkLock = new(1, 1);

    public AutoUpdateService(
        HttpClient httpClient,
        IConfiguration config,
        IHostApplicationLifetime lifetime,
        AutoUpdateState state,
        ILogger<AutoUpdateService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _lifetime = lifetime;
        _state = state;
        _logger = logger;
        _state.CurrentVersion = GetCurrentVersion();
    }

    public string CurrentVersion => _state.CurrentVersion ?? "0.0.0";

    private string WorkspaceRoot
    {
        get
        {
            // On Windows the data root (ProgramData) is separate from the install
            // directory. On macOS/Linux they coincide, so the workspace (staging,
            // backup) must live outside it or the backup would copy itself.
            var dataRoot = Configuration.AgentPaths.DataRoot;
            var baseDir = AppContext.BaseDirectory;
            var root = Path.GetFullPath(dataRoot).Equals(Path.GetFullPath(baseDir), StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(Path.GetTempPath(), "CentrixUpdates")
                : Path.Combine(dataRoot, "updates");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    private bool Enabled => bool.TryParse(_config["AutoUpdate:Enabled"], out var enabled) && enabled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            _logger.LogInformation("Auto-update is disabled");
            return;
        }

        if (string.IsNullOrWhiteSpace(_config["AutoUpdate:ManifestUrl"]))
        {
            _logger.LogWarning("Auto-update is enabled but AutoUpdate:ManifestUrl is not configured");
            return;
        }

        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForUpdateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetState("Failed", $"Auto-update check failed: {ex.Message}");
            }

            var intervalMinutes = _config.GetValue("AutoUpdate:CheckIntervalMinutes", 360);
            await Task.Delay(TimeSpan.FromMinutes(Math.Max(15, intervalMinutes)), stoppingToken);
        }
    }

    /// <summary>
    /// Runs one check-apply cycle. Also invoked manually from the control API.
    /// Never throws for expected failures; the state object carries the outcome.
    /// </summary>
    public async Task<AutoUpdateState> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            var manifestUrl = _config["AutoUpdate:ManifestUrl"];
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                SetState("Failed", "AutoUpdate:ManifestUrl is not configured");
                return Snapshot();
            }

            if (manifestUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !_config.GetValue("AutoUpdate:AllowInsecureHttp", false))
            {
                SetState("Failed", "Manifest URL must use https (set AutoUpdate:AllowInsecureHttp to override)");
                return Snapshot();
            }

            SetState("Checking", $"Checking {manifestUrl}");

            string manifestJson;
            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                manifestJson = await _httpClient.GetStringAsync(manifestUrl, linked.Token);
            }

            var manifest = UpdateManifest.FromJson(manifestJson);
            if (manifest == null)
            {
                SetState("Failed", "Manifest is not valid (needs at least version + downloadUrl)");
                return Snapshot();
            }

            _state.AvailableVersion = manifest.Version;
            _state.LastCheckedAt = DateTime.UtcNow;

            if (!UpdateVersion.IsNewerThan(CurrentVersion, manifest.Version))
            {
                SetState("UpToDate", $"Current version {CurrentVersion} is up to date");
                return Snapshot();
            }

            var isPrerelease = manifest.Version.Contains('-');
            if (isPrerelease && !_config.GetValue("AutoUpdate:AllowPrerelease", false))
            {
                SetState("UpToDate", $"Skipping prerelease {manifest.Version}");
                return Snapshot();
            }

            _logger.LogInformation($"Update available: {manifest.Version} (current {CurrentVersion})");
            await ApplyUpdateAsync(manifest, cancellationToken);
            return Snapshot();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetState("Failed", $"Update check failed: {ex.Message}");
            return Snapshot();
        }
        finally
        {
            _checkLock.Release();
        }
    }

    private async Task ApplyUpdateAsync(UpdateManifest manifest, CancellationToken cancellationToken)
    {
        var workspace = WorkspaceRoot;
        var zipPath = Path.Combine(workspace, $"download-{manifest.Version}.zip");
        var stagingDir = Path.Combine(workspace, "staging");
        var backupDir = Path.Combine(workspace, "backup");
        var logFile = Path.Combine(workspace, "update-helper.log");

        SetState("Downloading", $"Downloading {manifest.DownloadUrl}");

        using (var downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(30)))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, downloadCts.Token))
        {
            using var response = await _httpClient.GetAsync(
                manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            response.EnsureSuccessStatusCode();

            await using var remote = await response.Content.ReadAsStreamAsync(linked.Token);
            await using var local = File.Create(zipPath);
            await remote.CopyToAsync(local, linked.Token);
        }

        var actualHash = await ComputeSha256Async(zipPath, cancellationToken);
        var requireValidHash = _config.GetValue("AutoUpdate:RequireValidHash", true);

        if (string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            if (requireValidHash)
                throw new InvalidOperationException("Manifest has no sha256; refusing to install (set AutoUpdate:RequireValidHash false to override)");

            _logger.LogWarning("Update manifest has no sha256; installing unverified package");
        }
        else if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(zipPath);
            throw new InvalidOperationException(
                $"Downloaded package hash mismatch: expected {manifest.Sha256}, got {actualHash}");
        }

        SetState("Staging", "Verifying and staging package");
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, recursive: true);

        ZipFile.ExtractToDirectory(zipPath, stagingDir, overwriteFiles: true);

        var looksLikeAgent = File.Exists(Path.Combine(stagingDir, "Centrix.dll"))
            || File.Exists(Path.Combine(stagingDir, "Centrix.exe"))
            // Accept one last legacy package format during the rename transition.
            || File.Exists(Path.Combine(stagingDir, "LectureAgent.dll"))
            || File.Exists(Path.Combine(stagingDir, "LectureAgent.exe"));
        if (!looksLikeAgent)
            throw new InvalidOperationException("Package does not contain a valid Centrix agent build (Centrix.dll/.exe missing)");

        SetState("ReadyToApply", $"Version {manifest.Version} staged; restarting to apply");

        SpawnHelperScript(stagingDir, backupDir, logFile);

        _logger.LogWarning(
            $"Update {manifest.Version} staged; helper script spawned and agent exiting now. " +
            $"Helper log: {logFile}");
        _state.Status = "Applying";
        _lifetime.StopApplication();
    }

    private void SpawnHelperScript(string stagingDir, string backupDir, string logFile)
    {
        var installDir = AppContext.BaseDirectory;
        var parentPid = Environment.ProcessId;
        var healthUrl = _config["AutoUpdate:HealthUrl"] ?? "http://localhost:5200/api/health";
        var workspace = WorkspaceRoot;

        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            var scriptPath = Path.Combine(workspace, UpdateScripts.WindowsScriptName);
            File.WriteAllText(scriptPath, UpdateScripts.WindowsScript);
            var serviceName = _config["AutoUpdate:ServiceName"] ?? "Centrix";

            psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                    $"-ParentPid {parentPid} -InstallDir \"{installDir}\" -StagingDir \"{stagingDir}\" " +
                    $"-BackupDir \"{backupDir}\" -LogFile \"{logFile}\" -HealthUrl \"{healthUrl}\" -ServiceName \"{serviceName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workspace
            };
        }
        else
        {
            var scriptPath = Path.Combine(workspace, UpdateScripts.UnixScriptName);
            File.WriteAllText(scriptPath, UpdateScripts.UnixScript);

            var restartCommand = _config["AutoUpdate:RestartCommand"];
            if (string.IsNullOrWhiteSpace(restartCommand))
            {
                var systemdUnit = _config["AutoUpdate:SystemdUnit"];
                restartCommand = string.IsNullOrWhiteSpace(systemdUnit)
                    ? $"cd \"{installDir}\" && nohup ./Centrix >> \"{logFile}\" 2>&1 &"
                    : $"systemctl restart \"{systemdUnit}\"";
            }

            psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"\"{scriptPath}\" {parentPid} \"{installDir}\" \"{stagingDir}\" \"{backupDir}\" \"{healthUrl}\"",
                UseShellExecute = false,
                WorkingDirectory = workspace
            };
            psi.Environment["LA_RESTART_CMD"] = restartCommand;
            psi.Environment["LA_LOG_FILE"] = logFile;
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to spawn the update helper process");
        _logger.LogInformation($"Update helper spawned (pid {process.Id})");
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetCurrentVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

    private void SetState(string status, string message)
    {
        _logger.LogInformation("Auto-update [{Status}]: {Message}", status, message);
        _state.Status = status;
        _state.Message = message;
    }

    private AutoUpdateState Snapshot() => new()
    {
        Status = _state.Status,
        LastCheckedAt = _state.LastCheckedAt,
        CurrentVersion = _state.CurrentVersion,
        AvailableVersion = _state.AvailableVersion,
        Message = _state.Message
    };
}
