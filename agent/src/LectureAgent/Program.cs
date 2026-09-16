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
using LectureAgent.Security;
using LectureAgent.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// Logging — use an absolute path so the Windows service doesn't write to System32
var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "LectureAgentApp", "logs");
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
var dbPath = config["Database:SqlitePath"] ?? "lecture_agent.db";
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
builder.Services.AddSingleton<UploadProgressStore>();
builder.Services.AddSingleton<AgentControlState>();

// Background Services
builder.Services.AddHostedService<FileMonitoringService>();
builder.Services.AddHostedService<UploadProcessingService>();
builder.Services.AddHostedService<CloudSyncBackgroundService>();
builder.Services.AddHostedService<TimetableSyncBackgroundService>();

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
builder.Services.AddScoped<INotificationService>(sp => new MockNotificationService());
builder.Services.AddHttpClient<ICloudMetadataSync, HttpCloudMetadataSync>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ICredentialProtector, CredentialProtector>();
builder.Services.AddSingleton<DashboardSessionService>();

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
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000; PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();
        command.CommandText = @"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name != '__EFMigrationsHistory';";
        var tableCount = (long?)command.ExecuteScalar() ?? 0;

        if (tableCount == 0)
        {
            db.Database.EnsureCreated();
        }
    }
    catch
    {
        // Fresh SQLite files and startup races can briefly fail before the schema exists.
        db.Database.EnsureCreated();
    }
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
app.MapGet("/", (IWebHostEnvironment environment) =>
    TypedResults.PhysicalFile(Path.Combine(environment.WebRootPath!, "index.html"), "text/html"));
app.MapControllers();

app.Run();

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
