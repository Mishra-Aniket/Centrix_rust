using LectureAgent.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LectureAgent.Tests;

public sealed class MigrationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly LectureContext _context;

    public MigrationTests()
    {
        _connection.Open();
        _context = new LectureContext(new DbContextOptionsBuilder<LectureContext>()
            .UseSqlite(_connection)
            .Options);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task MigrateAsync_CreatesCurrentSchemaAndTracksBaseline()
    {
        await _context.Database.MigrateAsync();

        await using var historyCommand = _connection.CreateCommand();
        historyCommand.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory;";
        var migrationId = (string?)await historyCommand.ExecuteScalarAsync();

        Assert.Equal("20260917061934_InitialBaseline", migrationId);

        await using var columnsCommand = _connection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info(LectureSessions);";
        await using var reader = await columnsCommand.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));

        Assert.Contains("MatchStatus", columns);
        Assert.Contains("YouTubePublishStatus", columns);
        Assert.Contains("QcStatus", columns);
    }
}
