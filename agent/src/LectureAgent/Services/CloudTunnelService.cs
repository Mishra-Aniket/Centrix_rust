namespace LectureAgent.Services;

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

public sealed class TunnelStatusDto
{
    public bool Active { get; set; }
    public string? Url { get; set; }
    public string? Message { get; set; }
    public DateTime? StartedAtUtc { get; set; }
}

public sealed class CloudTunnelService : IDisposable
{
    private readonly ILogger<CloudTunnelService> _logger;
    private readonly object _lock = new();
    private Process? _process;
    private string? _tunnelUrl;
    private DateTime? _startedAtUtc;
    private string? _lastError;
    private bool _disposed;

    private static readonly Regex TunnelUrlRegex = new(
        @"https://[a-zA-Z0-9-]+\.trycloudflare\.com",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public CloudTunnelService(ILogger<CloudTunnelService> logger)
    {
        _logger = logger;
    }

    public TunnelStatusDto GetStatus()
    {
        lock (_lock)
        {
            if (_process != null && _process.HasExited)
            {
                _process.Dispose();
                _process = null;
                _tunnelUrl = null;
                _startedAtUtc = null;
            }

            return new TunnelStatusDto
            {
                Active = _process != null && !_process.HasExited && !string.IsNullOrEmpty(_tunnelUrl),
                Url = _tunnelUrl,
                Message = _lastError,
                StartedAtUtc = _startedAtUtc
            };
        }
    }

    public async Task<TunnelStatusDto> StartTunnelAsync(int port = 5200, CancellationToken ct = default)
    {
        var existing = GetStatus();
        if (existing.Active && !string.IsNullOrEmpty(existing.Url))
        {
            return existing;
        }

        var binaryPath = ResolveCloudflaredPath();
        if (string.IsNullOrEmpty(binaryPath) || !File.Exists(binaryPath))
        {
            _lastError = "cloudflared binary not found on system. Please verify installation.";
            _logger.LogError("Cannot start tunnel: cloudflared not found.");
            return new TunnelStatusDto
            {
                Active = false,
                Message = _lastError
            };
        }

        lock (_lock)
        {
            StopInternal();
            _lastError = null;
            _tunnelUrl = null;
        }

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recentLogs = new List<string>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = $"tunnel --url http://127.0.0.1:{port}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            void HandleLogLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;

                lock (recentLogs)
                {
                    if (recentLogs.Count > 30) recentLogs.RemoveAt(0);
                    recentLogs.Add(line);
                }

                _logger.LogDebug("[cloudflared] {Line}", line);

                var match = TunnelUrlRegex.Match(line);
                if (match.Success)
                {
                    tcs.TrySetResult(match.Value);
                }
            }

            process.OutputDataReceived += (_, e) => HandleLogLine(e.Data);
            process.ErrorDataReceived += (_, e) => HandleLogLine(e.Data);

            process.Exited += (_, _) =>
            {
                lock (_lock)
                {
                    if (_process == process)
                    {
                        _tunnelUrl = null;
                        _startedAtUtc = null;
                    }
                }
                tcs.TrySetException(new InvalidOperationException("cloudflared process exited unexpectedly."));
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to spawn cloudflared process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            lock (_lock)
            {
                _process = process;
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            using (linkedCts.Token.Register(() => tcs.TrySetCanceled()))
            {
                var detectedUrl = await tcs.Task;
                lock (_lock)
                {
                    _tunnelUrl = detectedUrl;
                    _startedAtUtc = DateTime.UtcNow;
                    _lastError = null;
                }

                _logger.LogInformation("Cloudflare Quick Tunnel established at {Url}", detectedUrl);

                return new TunnelStatusDto
                {
                    Active = true,
                    Url = detectedUrl,
                    StartedAtUtc = _startedAtUtc
                };
            }
        }
        catch (OperationCanceledException)
        {
            StopInternal();
            _lastError = "Timed out waiting for Cloudflare Tunnel URL. Check internet connection.";
            _logger.LogWarning("Timed out waiting for cloudflared tunnel URL. Recent logs: {Logs}", string.Join(" | ", recentLogs));
            return new TunnelStatusDto { Active = false, Message = _lastError };
        }
        catch (Exception ex)
        {
            StopInternal();
            _lastError = $"Failed to establish Cloudflare Tunnel: {ex.Message}";
            _logger.LogError(ex, "Error starting cloudflared tunnel. Recent logs: {Logs}", string.Join(" | ", recentLogs));
            return new TunnelStatusDto { Active = false, Message = _lastError };
        }
    }

    public Task<TunnelStatusDto> StopTunnelAsync()
    {
        lock (_lock)
        {
            StopInternal();
            _lastError = null;
        }
        return Task.FromResult(GetStatus());
    }

    private void StopInternal()
    {
        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill cloudflared process cleanly.");
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        _tunnelUrl = null;
        _startedAtUtc = null;
    }

    private static string? ResolveCloudflaredPath()
    {
        var candidates = new[]
        {
            "/Users/aniketmishra/LectureAgent/tools/cloudflared",
            "/Users/aniketmishra/Desktop/Centrix/agent/tools/cloudflared",
            Path.Combine(AppContext.BaseDirectory, "tools", "cloudflared"),
            Path.Combine(AppContext.BaseDirectory, "..", "tools", "cloudflared"),
            "/opt/homebrew/bin/cloudflared",
            "/usr/local/bin/cloudflared",
            "/usr/bin/cloudflared"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "cloudflared",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var stdout = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (!string.IsNullOrEmpty(stdout) && File.Exists(stdout))
                {
                    return stdout;
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            StopInternal();
        }
    }
}
