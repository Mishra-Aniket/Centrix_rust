using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LectureAgent.Domain.Entities;
using LectureAgent.Infrastructure.Database;
using LectureAgent.Infrastructure.Tracker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LectureAgent.Infrastructure.Timetable;

public sealed class GoogleSheetTimetableSyncService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleSheetTimetableSyncService> _logger;
    private readonly TrackerMappingSyncService? _trackerMapping;

    public GoogleSheetTimetableSyncService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<GoogleSheetTimetableSyncService> logger,
        TrackerMappingSyncService? trackerMapping = null)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _trackerMapping = trackerMapping;
    }

    public async Task<int> SyncScheduleAsync(LectureContext dbContext, string? targetCenterId = null, string? targetRoomId = null, CancellationToken ct = default)
    {
        var spreadsheetId = _config["GoogleSheet:SpreadsheetId"] ?? "1XOfPQ6IqtKXJtG9l7b9JALBNKl8DKbJvlyLnbp5MCCc";
        var centerId = targetCenterId ?? _config["Agent:CenterId"] ?? "Pune - PCMC Vidyapeeth";
        var roomId = targetRoomId ?? _config["Agent:RoomId"] ?? "603";
        var organizationId = _config["Agent:OrganizationId"] ?? "PCMC_VIDYAPEETH";

        _logger.LogInformation("Starting Google Sheet sync for Center: {CenterId}, Room: {RoomId} from Sheet: {SpreadsheetId}",
            centerId, roomId, spreadsheetId);

        try
        {
            // 1. Batch -> room mapping. Prefer the live PW Center Tracker API so mapping
            //    edits made in the tracker flow in automatically; fall back to the
            //    agent sheet's Rooms tab when the tracker is disabled or unreachable.
            var batchToRoom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (_trackerMapping != null && _trackerMapping.IsEnabled)
            {
                try
                {
                    batchToRoom = await _trackerMapping.GetBatchToRoomAsync(centerId, ct);
                    _logger.LogInformation("Using live tracker mapping for room filtering");
                }
                catch (Exception trackerEx)
                {
                    _logger.LogWarning(trackerEx, "Tracker mapping fetch failed; falling back to the Rooms sheet tab");
                }
            }

            if (batchToRoom.Count == 0)
            {
                var roomsCsvUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/gviz/tq?tqx=out:csv&sheet=Rooms";
                var roomsCsvContent = await _httpClient.GetStringAsync(roomsCsvUrl, ct);
                var roomsRows = ParseCsv(roomsCsvContent);

                // Rooms CSV: Col 0 = Center Name, Col 1 = Room, Col 2 = Batch Name, Col 3 = Batch ID
                foreach (var row in roomsRows.Skip(1))
                {
                    if (row.Count >= 3)
                    {
                        var r = row[1].Trim();
                        var b = row[2].Trim();
                        if (!string.IsNullOrEmpty(r) && !string.IsNullOrEmpty(b))
                        {
                            batchToRoom[b] = r;
                        }
                    }
                }
            }

            _logger.LogInformation("Loaded {Count} batch-to-room mappings for Center {CenterId}", batchToRoom.Count, centerId);

            // 2. Fetch TT (Timetable) tab
            var ttCsvUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/gviz/tq?tqx=out:csv&sheet=TT";
            var ttCsvContent = await _httpClient.GetStringAsync(ttCsvUrl, ct);
            var ttRows = ParseCsv(ttCsvContent);

            // TT CSV: Col 0 = Day, Col 1 = Lecture Date, Col 2 = Start Time, Col 3 = End Time,
            //         Col 8 = Batch Code, Col 9 = Faculty Code, Col 10 = Subject
            var dateFormats = new[] { "d-MMM-yyyy", "dd-MMM-yyyy", "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy" };
            var timeFormats = new[] { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm" };

            var newEntries = new List<TimetableEntry>();
            int processedCount = 0;

            foreach (var row in ttRows.Skip(1))
            {
                if (row.Count <= 8)
                    continue;

                var dateStr = row[1].Trim();
                var startStr = row[2].Trim();
                var endStr = row[3].Trim();
                var batchCode = row[8].Trim();
                var facultyCode = row.Count > 9 ? row[9].Trim() : "";
                var subjectName = row.Count > 10 ? row[10].Trim() : "";

                if (string.IsNullOrEmpty(batchCode) || string.IsNullOrEmpty(dateStr))
                    continue;

                // Lookup assigned room for this batch
                if (!batchToRoom.TryGetValue(batchCode, out var assignedRoom))
                {
                    continue; // Skip batches not mapped to any room
                }

                // If RoomId is specified, only sync slots for that room (e.g. Room 603)
                if (!string.IsNullOrEmpty(roomId) && !string.Equals(assignedRoom, roomId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!DateTime.TryParseExact(dateStr, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var scheduledDate))
                {
                    if (!DateTime.TryParse(dateStr, out scheduledDate))
                        continue;
                }

                if (!DateTime.TryParseExact(startStr, timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTimeDt))
                {
                    if (!DateTime.TryParse(startStr, out startTimeDt))
                        continue;
                }

                if (!DateTime.TryParseExact(endStr, timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTimeDt))
                {
                    if (!DateTime.TryParse(endStr, out endTimeDt))
                        continue;
                }

                var slotStart = startTimeDt.TimeOfDay;
                var slotEnd = endTimeDt.TimeOfDay;
                if (slotEnd <= slotStart)
                    continue;

                var slotId = $"SLOT-{assignedRoom}-{scheduledDate:yyyyMMdd}-{slotStart:hhmm}";

                // Subject: sheet ke Subject column se (e.g. "Physics"), warna faculty code,
                // warna batch code - PW jaisa schedule-driven subject label.
                var subjectId = !string.IsNullOrEmpty(subjectName)
                    ? subjectName
                    : !string.IsNullOrEmpty(facultyCode)
                        ? facultyCode
                        : batchCode;

                var entry = new TimetableEntry
                {
                    TimetableEntryId = $"TTE-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
                    OrganizationId = organizationId,
                    CenterId = centerId,
                    RoomId = assignedRoom,
                    ScheduledDate = scheduledDate.Date,
                    SlotStartTime = slotStart,
                    SlotEndTime = slotEnd,
                    SlotId = slotId,
                    BatchId = batchCode,
                    SubjectId = subjectId,
                    TeacherId = facultyCode,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                newEntries.Add(entry);
                processedCount++;
            }

            if (newEntries.Count > 0)
            {
                // Delete existing entries for this room and these dates to prevent duplicates
                var dates = newEntries.Select(e => e.ScheduledDate.Date).Distinct().ToList();
                foreach (var d in dates)
                {
                    var existing = await dbContext.TimetableEntries
                        .Where(e => e.CenterId == centerId && e.RoomId == roomId && e.ScheduledDate == d)
                        .ToListAsync(ct);
                    if (existing.Count > 0)
                    {
                        dbContext.TimetableEntries.RemoveRange(existing);
                    }
                }

                await dbContext.TimetableEntries.AddRangeAsync(newEntries, ct);
                await dbContext.SaveChangesAsync(ct);
            }

            _logger.LogInformation("Google Sheet sync successfully loaded {Count} timetable entries for Room {RoomId}!",
                newEntries.Count, roomId);

            return newEntries.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync timetable from Google Sheet");
            return 0;
        }
    }

    /// <summary>
    /// Robust RFC 4180 CSV parser handling escaped quotes and commas within quotes.
    /// </summary>
    private static List<List<string>> ParseCsv(string csvContent)
    {
        var result = new List<List<string>>();
        using var reader = new StringReader(csvContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var row = new List<string>();
            var inQuotes = false;
            var currentField = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++; // Skip escaped quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    row.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            row.Add(currentField.ToString());
            result.Add(row);
        }

        return result;
    }
}
