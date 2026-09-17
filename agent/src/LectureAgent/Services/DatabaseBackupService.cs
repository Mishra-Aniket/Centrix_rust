using LectureAgent.Configuration;
using Microsoft.Data.Sqlite;

namespace LectureAgent.Services;

/// <summary>
/// Daily safety net for the SQLite database: runs "VACUUM INTO" (safe against a live,
/// WAL-mode database) into backups/ and keeps the most recent ones. Replaces external
/// cron scripts that would copy a live database file and risk corruption.
/// </summary>
public sealed class DatabaseBackupService : BackgroundService
{
    private const int KeepCount = 7;

    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(IConfiguration configuration, ILogger<DatabaseBackupService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give startup (migrations, first sync) a moment, then check a few times a day —
        // the backup itself runs at most once per calendar day.
        await Task.Delay(TimeSpan.FromSeconds(90), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BackupOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Database backup failed; will retry on the next tick");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    internal async Task BackupOnceAsync(CancellationToken ct = default)
    {
        var dbPath = ResolveDatabasePath();
        if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
        {
            return;
        }

        var backupDirectory = Path.Combine(AgentPaths.DataRoot, "backups");
        Directory.CreateDirectory(backupDirectory);

        var target = Path.Combine(backupDirectory, $"centrix-{DateTime.UtcNow:yyyyMMdd}.db");
        if (File.Exists(target))
        {
            PruneOldBackups(backupDirectory);
            return;
        }

        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = "VACUUM INTO @target;";
        command.Parameters.AddWithValue("@target", target);
        await command.ExecuteNonQueryAsync(ct);

        PruneOldBackups(backupDirectory);
        _logger.LogInformation("Database backup created: {File}", target);
    }

    private string ResolveDatabasePath()
    {
        var dbPath = _configuration["Database:SqlitePath"] ?? "data/centrix.db";
        if (!Path.IsPathRooted(dbPath))
        {
            dbPath = Path.Combine(AgentPaths.DataRoot, dbPath);
        }

        return dbPath;
    }

    private void PruneOldBackups(string backupDirectory)
    {
        var backups = Directory.GetFiles(backupDirectory, "centrix-*.db")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        foreach (var old in backups.Skip(KeepCount))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
                // A locked file simply survives until the next prune.
            }
        }
    }
}
