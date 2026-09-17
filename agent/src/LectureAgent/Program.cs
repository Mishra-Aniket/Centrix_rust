using LectureAgent.Application.Services;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.Database;
using LectureAgent.Infrastructure.Cloud;
using LectureAgent.Services;
using Serilog;
using Serilog.Events;
using LectureAgent.Infrastructure.FileProcessing;
using LectureAgent.Infrastructure.FileWatching;
using LectureAgent.Infrastructure.Matching;
using LectureAgent.Infrastructure.Timetable;
using LectureAgent.Infrastructure.Tracker;
using LectureAgent.Infrastructure.StudioApi;
using LectureAgent.Infrastructure.QualityCheck;
using LectureAgent.Infrastructure.YouTube;
using LectureAgent.Infrastructure.Notifications;
using LectureAgent.Security;
using LectureAgent.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using System.Data;

using LectureAgent.Commands;
using LectureAgent.Configuration;

if (args.Contains(AuthorizeGoogleCommand.Flag))
{
    var exitCode = await AuthorizeGoogleCommand.RunAsync();
    Environment.ExitCode = exitCode;
    return;
}

if (args.Contains(AuthorizeGoogleCommand.YouTubeFlag))
{
    var exitCode = await AuthorizeGoogleCommand.RunYouTubeAsync();
    Environment.ExitCode = exitCode;
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Support running as a Windows Service (SCM lifetime hook & content root setup)
builder.Host.UseWindowsService();

// One misbehaving background loop must never take the whole agent down on an
// unattended center PC: with the default StopHost a single unhandled exception
// stops watching, uploading, and the dashboard at once. Each loop already
// catches and logs its own errors, so the host stays up and retries.
builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

// Layer user settings from ProgramData on top of shipped defaults
if (AgentPaths.SharedSettingsFile is { } sharedSettings && File.Exists(sharedSettings))
{
    builder.Configuration.AddJsonFile(sharedSettings, optional: true, reloadOnChange: true);
}

// Logging — use shared ProgramData log folder so desktop app and service share logs
var logDir = AgentPaths.LogDirectory;
Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(logDir, "agent-.txt"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Configuration
var config = builder.Configuration;

// Database
var dbPath = config["Database:SqlitePath"] ?? "centrix.db";
if (!Path.IsPathRooted(dbPath))
{
    dbPath = Path.Combine(AppContext.BaseDirectory, dbPath);
}

var dbDirectory = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

builder.Services.AddDbContext<LectureContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Cache=Shared;Default Timeout=30"));

// Repositories
builder.Services.AddScoped<ILectureRepository, LectureRepository>();
builder.Services.AddScoped<IUploadQueueRepository, UploadQueueRepository>();
builder.Services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));

// Application Services
builder.Services.AddScoped<LectureSessionService>();
builder.Services.AddScoped<MatchingService>();
builder.Services.AddScoped<UploadQueueService>();
builder.Services.AddScoped<TimetableSyncService>();

// Infrastructure Services
builder.Services.AddSingleton<IFileWatcher, FileWatcher>();
builder.Services.AddScoped<IMatchingEngine, MatchingEngine>();
builder.Services.AddScoped<IFileValidator, FileValidator>();
builder.Services.AddScoped<IQcChecker, QcChecker>();
builder.Services.AddScoped<IYouTubePublisher, YouTubePublisher>();
builder.Services.AddSingleton<UploadProgressStore>();
builder.Services.AddSingleton<AgentControlState>();
builder.Services.AddSingleton<YouTubePublishQueue>();

// Background Services
builder.Services.AddHostedService<FileMonitoringService>();
builder.Services.AddHostedService<UploadProcessingService>();
builder.Services.AddHostedService<CloudSyncBackgroundService>();
builder.Services.AddHostedService<TimetableSyncBackgroundService>();
builder.Services.AddHostedService<YouTubeBackgroundWorker>();

builder.Services.AddHttpClient<GoogleSheetTimetableSyncService>();
builder.Services.AddHttpClient<TrackerMappingSyncService>();
builder.Services.AddHttpClient<StudioApiClient>();
builder.Services.AddScoped<StudioApiMappingSyncService>();
builder.Services.AddScoped<GoogleSheetTimetableSyncService>();
builder.Services.AddScoped<ITimetableProvider, LocalTimetableProvider>();
builder.Services.AddScoped<IGoogleDriveUploader>(sp =>
    bool.TryParse(config["GoogleDrive:Enabled"], out var enabled) && enabled
        ? ActivatorUtilities.CreateInstance<GoogleDriveUploader>(sp)
        : ActivatorUtilities.CreateInstance<MockGoogleDriveUploader>(sp));
builder.Services.AddScoped<IAuditLogger, DatabaseAuditLogger>();
builder.Services.AddScoped<INotificationService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return NotificationServiceFactory.Create(httpClientFactory.CreateClient(), configuration, loggerFactory);
});
builder.Services.AddHttpClient<ICloudMetadataSync, HttpCloudMetadataSync>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ICredentialProtector, CredentialProtector>();
builder.Services.AddSingleton<DashboardSessionService>();
builder.Services.AddSingleton<CloudTunnelService>();

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Accept and emit enum names as strings (e.g. "overrideType": "Cancelled")
        // so the dashboard does not need to know numeric enum values.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS (for local development)
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalPolicy", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// Database initialization
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LectureContext>();

    try
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000; PRAGMA foreign_keys=ON;";
        await command.ExecuteNonQueryAsync();

        // Databases produced by the previous EnsureCreated() flow have no EF history.
        // Upgrade their additive columns first, then mark the baseline migration as
        // applied. Fresh installs take the normal Database.MigrateAsync() path.
        if (await TableExistsAsync(connection, "LectureSessions")
            && !await TableExistsAsync(connection, "__EFMigrationsHistory"))
        {
            await MigrateSchemaAsync(db);
            await StampLegacyBaselineAsync(db, connection);
        }

        await db.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Database migration failed; the agent will not start with an unknown schema state");
        throw;
    }
}

static async Task<bool> TableExistsAsync(System.Data.Common.DbConnection connection, string tableName)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
    var parameter = command.CreateParameter();
    parameter.ParameterName = "$name";
    parameter.Value = tableName;
    command.Parameters.Add(parameter);
    return await command.ExecuteScalarAsync() != null;
}

static async Task StampLegacyBaselineAsync(LectureContext db, System.Data.Common.DbConnection connection)
{
    await using (var historyCommand = connection.CreateCommand())
    {
        historyCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """;
        await historyCommand.ExecuteNonQueryAsync();
    }

    var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "8.0.0";
    foreach (var migrationId in db.Database.GetMigrations())
    {
        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = """
            INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ($migrationId, $productVersion);
            """;
        var migrationParameter = insertCommand.CreateParameter();
        migrationParameter.ParameterName = "$migrationId";
        migrationParameter.Value = migrationId;
        insertCommand.Parameters.Add(migrationParameter);
        var versionParameter = insertCommand.CreateParameter();
        versionParameter.ParameterName = "$productVersion";
        versionParameter.Value = productVersion;
        insertCommand.Parameters.Add(versionParameter);
        await insertCommand.ExecuteNonQueryAsync();
    }
}

static async Task MigrateSchemaAsync(LectureContext db)
{
    var connection = db.Database.GetDbConnection();
    if (connection.State != ConnectionState.Open)
        await connection.OpenAsync();

    using var columnsCommand = connection.CreateCommand();
    columnsCommand.CommandText = "PRAGMA table_info(LectureSessions);";
    var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var reader = await columnsCommand.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            existingColumns.Add(reader.GetString(1));
    }

    var migrations = new (string Column, string Sql)[]
    {
        ("MatchStatus", "ALTER TABLE LectureSessions ADD COLUMN MatchStatus INTEGER NOT NULL DEFAULT 1;"),
        ("FailureCode", "ALTER TABLE LectureSessions ADD COLUMN FailureCode INTEGER NOT NULL DEFAULT 0;"),
        ("FailureReason", "ALTER TABLE LectureSessions ADD COLUMN FailureReason TEXT;"),
        ("MatchedAt", "ALTER TABLE LectureSessions ADD COLUMN MatchedAt TEXT;"),
        ("MatchAttempts", "ALTER TABLE LectureSessions ADD COLUMN MatchAttempts INTEGER NOT NULL DEFAULT 0;"),
        ("YouTubeId", "ALTER TABLE LectureSessions ADD COLUMN YouTubeId TEXT;"),
        ("YouTubePublishStatus", "ALTER TABLE LectureSessions ADD COLUMN YouTubePublishStatus INTEGER NOT NULL DEFAULT 1;"),
        ("YouTubeThumbnailUrl", "ALTER TABLE LectureSessions ADD COLUMN YouTubeThumbnailUrl TEXT;"),
        ("YouTubeFailureReason", "ALTER TABLE LectureSessions ADD COLUMN YouTubeFailureReason TEXT;"),
        ("YouTubePublishedAt", "ALTER TABLE LectureSessions ADD COLUMN YouTubePublishedAt TEXT;"),
        ("QcResults", "ALTER TABLE LectureSessions ADD COLUMN QcResults TEXT;"),
        ("QcPassedAt", "ALTER TABLE LectureSessions ADD COLUMN QcPassedAt TEXT;"),
        ("QcStatus", "ALTER TABLE LectureSessions ADD COLUMN QcStatus INTEGER NOT NULL DEFAULT 1;")
    };

    foreach (var (column, sql) in migrations)
    {
        if (existingColumns.Contains(column))
            continue;

        await using var migrationCommand = connection.CreateCommand();
        migrationCommand.CommandText = sql;
        await migrationCommand.ExecuteNonQueryAsync();
    }

    await using var indexCommand = connection.CreateCommand();
    indexCommand.CommandText = "CREATE INDEX IF NOT EXISTS IX_Lecture_MatchStatus ON LectureSessions(MatchStatus);";
    await indexCommand.ExecuteNonQueryAsync();

    await using var youTubeIndexCommand = connection.CreateCommand();
    youTubeIndexCommand.CommandText = "CREATE INDEX IF NOT EXISTS IX_Lecture_YouTubePublishStatus ON LectureSessions(YouTubePublishStatus);";
    await youTubeIndexCommand.ExecuteNonQueryAsync();

    await using var qcIndexCommand = connection.CreateCommand();
    qcIndexCommand.CommandText = "CREATE INDEX IF NOT EXISTS IX_Lecture_QcStatus ON LectureSessions(QcStatus);";
    await qcIndexCommand.ExecuteNonQueryAsync();
}

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No UseHttpsRedirection: on the LAN a phone hitting http://<pc-ip>:5200 must not be
// bounced to the dev-certificate https port. Both endpoints stay available explicitly.
app.UseCors("LocalPolicy");
app.UseMiddleware<ApiKeyMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");

// app.Run() previously had no surrounding try/catch and Log was never flushed on exit.
// That meant the single most likely startup failure on a center PC — Kestrel unable to
// bind the dashboard port because another Centrix process/service instance already holds
// it — crashed the process without ever writing a line to agent-*.txt, leaving only a
// generic ".NET Runtime" entry in the Windows Event Log. Logging and rethrowing here makes
// that failure (and any other unexpected startup/runtime crash) show up in the log file the
// desktop app's "Open logs folder" button points to.
try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Centrix agent terminated unexpectedly (see below for the cause, e.g. the dashboard port already being in use)");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Mock implementations (placeholder)
public class MockTimetableProvider : ITimetableProvider
{
    public Task<List<TimetableEntry>> GetTimetableAsync(string centerId, string roomId, DateTime date) =>
        Task.FromResult(new List<TimetableEntry>());

    public Task<TimetableOverride?> GetOverrideAsync(string centerId, DateTime date, string slotId) =>
        Task.FromResult<TimetableOverride?>(null);

    public Task<List<TimetableOverride>> GetActiveOverridesAsync(string centerId, DateTime date) =>
        Task.FromResult(new List<TimetableOverride>());

    public Task SyncTimetableAsync(string centerId) => Task.CompletedTask;
}

public class MockGoogleDriveUploader : IGoogleDriveUploader
{
    public Task<string> AuthorizeAsync() => Task.FromResult("mock_token");
    public Task<string> UploadFileAsync(UploadQueueEntry entry, CancellationToken ct = default) => Task.FromResult("mock_file_id");
    public Task<bool> VerifyUploadAsync(string fileId, string expectedHash) => Task.FromResult(true);
    public Task CreateFolderStructureAsync(string folderPath) => Task.CompletedTask;
    public Task<List<string>> ListFoldersAsync(string? query = null, int limit = 200, string? underPath = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<string> { "Batch-2026-A", "Batch-2026-B" });
    public Task<Stream> OpenDownloadStreamAsync(string fileId, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream());
}

public class MockAuditLogger : IAuditLogger
{
    public Task LogAsync(string entityType, string entityId, string actionType, 
        string? userId, object? oldValues, object? newValues, string? summary) =>
        Task.CompletedTask;
}

public class MockNotificationService : INotificationService
{
    public Task NotifyReviewRequiredAsync(string centerId, LectureSession lecture) => Task.CompletedTask;
    public Task NotifyUploadFailedAsync(string centerId, UploadQueueEntry entry, string error) => Task.CompletedTask;
    public Task NotifyMissingLectureAsync(string centerId, TimetableEntry expectedLecture) => Task.CompletedTask;
}
