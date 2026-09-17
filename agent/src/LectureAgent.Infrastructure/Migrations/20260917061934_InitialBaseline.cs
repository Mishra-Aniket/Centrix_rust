using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LectureAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    AuditEntryId = table.Column<string>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<string>(type: "TEXT", nullable: false),
                    CenterId = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    UserRole = table.Column<string>(type: "TEXT", nullable: true),
                    OldValues = table.Column<string>(type: "TEXT", nullable: true),
                    NewValues = table.Column<string>(type: "TEXT", nullable: true),
                    ChangeSummary = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedToCloud = table.Column<bool>(type: "INTEGER", nullable: false),
                    CloudSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.AuditEntryId);
                });

            migrationBuilder.CreateTable(
                name: "Configs",
                columns: table => new
                {
                    ConfigKey = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigValue = table.Column<string>(type: "TEXT", nullable: true),
                    DataType = table.Column<string>(type: "TEXT", nullable: false),
                    IsOverridable = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configs", x => x.ConfigKey);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<string>(type: "TEXT", nullable: false),
                    CenterId = table.Column<string>(type: "TEXT", nullable: false),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceType = table.Column<string>(type: "TEXT", nullable: true),
                    RecordingFolderPath = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    OnlineStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastHeartbeat = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "LectureSessions",
                columns: table => new
                {
                    LectureSessionId = table.Column<string>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<string>(type: "TEXT", nullable: false),
                    CenterId = table.Column<string>(type: "TEXT", nullable: false),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                    DetectedStartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DetectedEndTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DetectedDurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledStartTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScheduledEndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScheduledSlotId = table.Column<string>(type: "TEXT", nullable: true),
                    BatchId = table.Column<string>(type: "TEXT", nullable: true),
                    SubjectId = table.Column<string>(type: "TEXT", nullable: true),
                    TeacherId = table.Column<string>(type: "TEXT", nullable: true),
                    AssignmentSource = table.Column<string>(type: "TEXT", nullable: false),
                    ConfidenceScore = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchingReason = table.Column<string>(type: "TEXT", nullable: true),
                    MatchStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureCode = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    MatchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MatchAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    YouTubeId = table.Column<string>(type: "TEXT", nullable: true),
                    YouTubePublishStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    YouTubeThumbnailUrl = table.Column<string>(type: "TEXT", nullable: true),
                    YouTubeFailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    YouTubePublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QcResults = table.Column<string>(type: "TEXT", nullable: true),
                    QcPassedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QcStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoFileLocalPath = table.Column<string>(type: "TEXT", nullable: true),
                    VideoFileSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    VideoFileHash = table.Column<string>(type: "TEXT", nullable: true),
                    PdfFileLocalPath = table.Column<string>(type: "TEXT", nullable: true),
                    PdfFileSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    PdfFileHash = table.Column<string>(type: "TEXT", nullable: true),
                    DriveVideoFileId = table.Column<string>(type: "TEXT", nullable: true),
                    DrivePdfFileId = table.Column<string>(type: "TEXT", nullable: true),
                    DriveFolderPath = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewerId = table.Column<string>(type: "TEXT", nullable: true),
                    LockedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LockedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastStatusChange = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LectureSessions", x => x.LectureSessionId);
                });

            migrationBuilder.CreateTable(
                name: "TimetableEntries",
                columns: table => new
                {
                    TimetableEntryId = table.Column<string>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<string>(type: "TEXT", nullable: false),
                    CenterId = table.Column<string>(type: "TEXT", nullable: false),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    ScheduledDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SlotStartTime = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    SlotEndTime = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    SlotId = table.Column<string>(type: "TEXT", nullable: false),
                    BatchId = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", nullable: false),
                    TeacherId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimetableEntries", x => x.TimetableEntryId);
                });

            migrationBuilder.CreateTable(
                name: "TimetableOverrides",
                columns: table => new
                {
                    OverrideId = table.Column<string>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<string>(type: "TEXT", nullable: false),
                    CenterId = table.Column<string>(type: "TEXT", nullable: false),
                    RoomId = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalTimetableEntryId = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OriginalSlotId = table.Column<string>(type: "TEXT", nullable: true),
                    OverrideType = table.Column<int>(type: "INTEGER", nullable: false),
                    NewBatchId = table.Column<string>(type: "TEXT", nullable: true),
                    NewSubjectId = table.Column<string>(type: "TEXT", nullable: true),
                    NewTeacherId = table.Column<string>(type: "TEXT", nullable: true),
                    NewRoomId = table.Column<string>(type: "TEXT", nullable: true),
                    NewDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NewSlotStartTime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    NewSlotEndTime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    NewSlotId = table.Column<string>(type: "TEXT", nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimetableOverrides", x => x.OverrideId);
                });

            migrationBuilder.CreateTable(
                name: "UploadQueues",
                columns: table => new
                {
                    QueueEntryId = table.Column<string>(type: "TEXT", nullable: false),
                    LectureSessionId = table.Column<string>(type: "TEXT", nullable: false),
                    FileType = table.Column<string>(type: "TEXT", nullable: false),
                    LocalFilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", nullable: true),
                    DriveFolderPath = table.Column<string>(type: "TEXT", nullable: true),
                    DriveFileName = table.Column<string>(type: "TEXT", nullable: true),
                    DriveFileId = table.Column<string>(type: "TEXT", nullable: true),
                    DriveResumableUri = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    BytesUploaded = table.Column<long>(type: "INTEGER", nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxRetries = table.Column<int>(type: "INTEGER", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadQueues", x => x.QueueEntryId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Audit_Entity",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Audit_Synced",
                table: "AuditLogs",
                column: "SyncedToCloud");

            migrationBuilder.CreateIndex(
                name: "IX_Device_Org_Center_Room",
                table: "Devices",
                columns: new[] { "OrganizationId", "CenterId", "RoomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lecture_Center_Date",
                table: "LectureSessions",
                columns: new[] { "CenterId", "DetectedStartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_Lecture_MatchStatus",
                table: "LectureSessions",
                column: "MatchStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Lecture_QcStatus",
                table: "LectureSessions",
                column: "QcStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Lecture_ReviewStatus",
                table: "LectureSessions",
                column: "ReviewStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Lecture_Status",
                table: "LectureSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Lecture_Tenant_Status",
                table: "LectureSessions",
                columns: new[] { "OrganizationId", "CenterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Lecture_YouTubePublishStatus",
                table: "LectureSessions",
                column: "YouTubePublishStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Timetable_Center_Date",
                table: "TimetableEntries",
                columns: new[] { "CenterId", "ScheduledDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Timetable_Room_Date",
                table: "TimetableEntries",
                columns: new[] { "RoomId", "ScheduledDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Timetable_Tenant_Room_Date",
                table: "TimetableEntries",
                columns: new[] { "OrganizationId", "CenterId", "RoomId", "ScheduledDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Override_Active",
                table: "TimetableOverrides",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Override_Center_Date",
                table: "TimetableOverrides",
                columns: new[] { "CenterId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Queue_LectureId",
                table: "UploadQueues",
                column: "LectureSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Queue_Status_RetryTime",
                table: "UploadQueues",
                columns: new[] { "Status", "NextRetryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Queue_Status_Updated",
                table: "UploadQueues",
                columns: new[] { "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "Configs");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "LectureSessions");

            migrationBuilder.DropTable(
                name: "TimetableEntries");

            migrationBuilder.DropTable(
                name: "TimetableOverrides");

            migrationBuilder.DropTable(
                name: "UploadQueues");
        }
    }
}
