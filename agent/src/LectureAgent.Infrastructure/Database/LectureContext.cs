namespace LectureAgent.Infrastructure.Database;

using LectureAgent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// SQLite DbContext for the lecture agent.
/// </summary>
public class LectureContext : DbContext
{
    public LectureContext(DbContextOptions<LectureContext> options) : base(options) { }

    public DbSet<LectureSession> LectureSessions { get; set; } = null!;
    public DbSet<TimetableEntry> TimetableEntries { get; set; } = null!;
    public DbSet<TimetableOverride> TimetableOverrides { get; set; } = null!;
    public DbSet<Device> Devices { get; set; } = null!;
    public DbSet<UploadQueueEntry> UploadQueues { get; set; } = null!;
    public DbSet<AuditLogEntry> AuditLogs { get; set; } = null!;
    public DbSet<ConfigEntry> Configs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // LectureSession
        modelBuilder.Entity<LectureSession>()
            .HasKey(l => l.LectureSessionId);
        modelBuilder.Entity<LectureSession>()
            .HasIndex(l => new { l.CenterId, l.DetectedStartTime })
            .HasDatabaseName("IX_Lecture_Center_Date");
        modelBuilder.Entity<LectureSession>()
            .HasIndex(l => new { l.OrganizationId, l.CenterId, l.Status })
            .HasDatabaseName("IX_Lecture_Tenant_Status");
        modelBuilder.Entity<LectureSession>()
            .HasIndex(l => l.Status)
            .HasDatabaseName("IX_Lecture_Status");
        modelBuilder.Entity<LectureSession>()
            .HasIndex(l => l.ReviewStatus)
            .HasDatabaseName("IX_Lecture_ReviewStatus");

        // TimetableEntry
        modelBuilder.Entity<TimetableEntry>()
            .HasKey(t => t.TimetableEntryId);
        modelBuilder.Entity<TimetableEntry>()
            .HasIndex(t => new { t.CenterId, t.ScheduledDate })
            .HasDatabaseName("IX_Timetable_Center_Date");
        modelBuilder.Entity<TimetableEntry>()
            .HasIndex(t => new { t.RoomId, t.ScheduledDate })
            .HasDatabaseName("IX_Timetable_Room_Date");
        modelBuilder.Entity<TimetableEntry>()
            .HasIndex(t => new { t.OrganizationId, t.CenterId, t.RoomId, t.ScheduledDate })
            .HasDatabaseName("IX_Timetable_Tenant_Room_Date");

        // TimetableOverride
        modelBuilder.Entity<TimetableOverride>()
            .HasKey(o => o.OverrideId);
        modelBuilder.Entity<TimetableOverride>()
            .HasIndex(o => new { o.CenterId, o.EffectiveDate })
            .HasDatabaseName("IX_Override_Center_Date");
        modelBuilder.Entity<TimetableOverride>()
            .HasIndex(o => o.IsActive)
            .HasDatabaseName("IX_Override_Active");

        // Device
        modelBuilder.Entity<Device>()
            .HasKey(d => d.DeviceId);
        modelBuilder.Entity<Device>()
            .HasIndex(d => new { d.OrganizationId, d.CenterId, d.RoomId })
            .HasDatabaseName("IX_Device_Org_Center_Room")
            .IsUnique();

        // UploadQueueEntry
        modelBuilder.Entity<UploadQueueEntry>()
            .HasKey(u => u.QueueEntryId);
        modelBuilder.Entity<UploadQueueEntry>()
            .HasIndex(u => new { u.Status, u.NextRetryAt })
            .HasDatabaseName("IX_Queue_Status_RetryTime");
        modelBuilder.Entity<UploadQueueEntry>()
            .HasIndex(u => u.LectureSessionId)
            .HasDatabaseName("IX_Queue_LectureId");
        modelBuilder.Entity<UploadQueueEntry>()
            .HasIndex(u => new { u.Status, u.UpdatedAt })
            .HasDatabaseName("IX_Queue_Status_Updated");

        // AuditLogEntry
        modelBuilder.Entity<AuditLogEntry>()
            .HasKey(a => a.AuditEntryId);
        modelBuilder.Entity<AuditLogEntry>()
            .HasIndex(a => new { a.EntityType, a.EntityId })
            .HasDatabaseName("IX_Audit_Entity");
        modelBuilder.Entity<AuditLogEntry>()
            .HasIndex(a => a.SyncedToCloud)
            .HasDatabaseName("IX_Audit_Synced");

        // ConfigEntry
        modelBuilder.Entity<ConfigEntry>()
            .HasKey(c => c.ConfigKey);
    }
}
