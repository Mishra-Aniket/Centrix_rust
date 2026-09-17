namespace LectureAgent.Infrastructure.Matching;

using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Implements the matching engine with confidence scoring.
/// </summary>
public class MatchingEngine : IMatchingEngine
{
    private readonly ITimetableProvider _timetableProvider;
    private readonly LectureContext _dbContext;
    private readonly int _highConfidenceThreshold;
    private readonly int _mediumConfidenceThreshold;
    private readonly int _timeOverlapMinimumPercentage;
    private readonly int _durationToleranceMinutes;
    private readonly ILogger<MatchingEngine> _logger;

    public MatchingEngine(
        ITimetableProvider timetableProvider,
        LectureContext dbContext,
        IConfiguration configuration,
        ILogger<MatchingEngine> logger)
    {
        _timetableProvider = timetableProvider;
        _dbContext = dbContext;
        _highConfidenceThreshold = configuration.GetValue("Matching:HighConfidenceThreshold", 85);
        _mediumConfidenceThreshold = configuration.GetValue("Matching:MediumConfidenceThreshold", 60);
        _timeOverlapMinimumPercentage = configuration.GetValue("Matching:TimeOverlapMinimumPercentage", 50);
        _durationToleranceMinutes = configuration.GetValue("Matching:DurationToleranceMinutes", 10);
        _logger = logger;
    }

    public async Task<MatchingResult> AnalyzeAsync(LectureSession lecture)
    {
        _logger.LogInformation($"Analyzing lecture {lecture.LectureSessionId}");

        var result = new MatchingResult
        {
            Lecture = lecture,
            ScoringDetails = new MatchingScoringDetails()
        };

        // Get timetable for the room and date. Slot times are local (IST), so convert
        // the detected start from UTC to IST before extracting .Date. Without this,
        // lectures at 1 AM IST (= 7:30 PM UTC previous day) query the wrong day.
        var istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(lecture.DetectedStartTime, istZone);
        var date = localStart.Date;
        var timetableSlots = await _timetableProvider.GetTimetableAsync(
            lecture.CenterId, lecture.RoomId, date);

        var activeOverrides = await _timetableProvider.GetActiveOverridesAsync(lecture.CenterId, date);

        // Apply overrides to timetable slots
        var effectiveSlots = new List<(TimetableEntry Slot, TimetableOverride? AppliedOverride)>();
        foreach (var slot in timetableSlots)
        {
            var matchingOverride = activeOverrides.FirstOrDefault(o =>
                o.OriginalSlotId == slot.SlotId || o.OriginalTimetableEntryId == slot.TimetableEntryId);

            if (matchingOverride != null)
            {
                if (matchingOverride.OverrideType == OverrideType.Cancelled)
                {
                    _logger.LogInformation($"Slot {slot.SlotId} was cancelled by override {matchingOverride.OverrideId}");
                    continue; // Slot cancelled, do not match
                }

                // Clone and apply modified values
                var modifiedSlot = new TimetableEntry
                {
                    TimetableEntryId = slot.TimetableEntryId,
                    OrganizationId = slot.OrganizationId,
                    CenterId = slot.CenterId,
                    RoomId = matchingOverride.NewRoomId ?? slot.RoomId,
                    ScheduledDate = slot.ScheduledDate,
                    SlotId = matchingOverride.NewSlotId ?? slot.SlotId,
                    BatchId = matchingOverride.NewBatchId ?? slot.BatchId,
                    SubjectId = matchingOverride.NewSubjectId ?? slot.SubjectId,
                    TeacherId = matchingOverride.NewTeacherId ?? slot.TeacherId,
                    SlotStartTime = matchingOverride.NewSlotStartTime ?? slot.SlotStartTime,
                    SlotEndTime = matchingOverride.NewSlotEndTime ?? slot.SlotEndTime
                };

                effectiveSlots.Add((modifiedSlot, matchingOverride));
            }
            else
            {
                effectiveSlots.Add((slot, null));
            }
        }

        // Also check for extra lecture overrides scheduled for this room and date
        var extraOverrides = activeOverrides.Where(o =>
            o.OverrideType == OverrideType.Extra
            && (o.NewRoomId == lecture.RoomId || (string.IsNullOrEmpty(o.NewRoomId) && o.RoomId == lecture.RoomId)));

        foreach (var extra in extraOverrides)
        {
            if (extra.NewSlotStartTime.HasValue && extra.NewSlotEndTime.HasValue)
            {
                var extraSlot = new TimetableEntry
                {
                    TimetableEntryId = $"EXTRA-{extra.OverrideId}",
                    OrganizationId = extra.OrganizationId,
                    CenterId = extra.CenterId,
                    RoomId = lecture.RoomId,
                    ScheduledDate = date,
                    SlotId = extra.NewSlotId ?? $"SLOT-{extra.OverrideId}",
                    BatchId = extra.NewBatchId ?? "EXTRA",
                    SubjectId = extra.NewSubjectId ?? "EXTRA",
                    TeacherId = extra.NewTeacherId,
                    SlotStartTime = extra.NewSlotStartTime.Value,
                    SlotEndTime = extra.NewSlotEndTime.Value
                };
                effectiveSlots.Add((extraSlot, extra));
            }
        }

        if (!effectiveSlots.Any())
        {
            result.Decision = MatchingDecision.NoMatch;
            result.ConfidenceScore = 0;
            result.FailureCode = MatchFailureCode.NoTimetableSlots;
            result.ReasoningText = "No active timetable slots found for this room on this date (slots may be empty or cancelled)";
            return result;
        }

        // Score each candidate
        // Score each candidate
        var scoredCandidates = new List<(TimetableEntry Slot, int Score, MatchingScoringDetails Details, TimetableOverride? Override)>();

        // Extract PDF metadata hints if this is a PDF file
        var filePath = lecture.VideoFileLocalPath ?? lecture.PdfFileLocalPath;
        var isPdf = !string.IsNullOrWhiteSpace(filePath) && string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase);
        var pdfHints = isPdf ? PdfTextExtractor.Extract(filePath) : null;

        foreach (var (slot, appliedOverride) in effectiveSlots)
        {
            var details = new MatchingScoringDetails();
            var timeOverlapScore = CalculateTimeOverlapScore(lecture, slot);

            if (timeOverlapScore == 0)
            {
                // For videos, time overlap is mandatory.
                // For whiteboard PDFs, notes are often exported hours after class ended.
                // If PDF metadata / cover slide OCR directly matches the batch code, allow scoring.
                if (!isPdf || pdfHints == null || !PdfTextExtractor.MatchesBatch(pdfHints, slot.BatchId))
                    continue;

                timeOverlapScore = 50; // Partial score for non-overlapping but exact batch matched notes
            }

            // Calculate all factors
            var roomScore = 100; // Room matched from recording device
            var durationScore = CalculateDurationScore(lecture, slot);
            var batchSubjectScore = CalculateBatchSubjectScore(filePath, slot, pdfHints);
            var teacherScore = CalculateTeacherScore(slot, pdfHints);
            var historicalScore = await CalculateHistoricalPatternScore(lecture.CenterId, lecture.RoomId, slot);
            var contextScore = CalculateContextScore(lecture, slot);

            // Populate details
            details.RoomFactor = new ScoreFactor { Score = roomScore, Weight = 0.15, Contribution = roomScore * 0.15 };
            details.TimeOverlapFactor = new ScoreFactor { Score = timeOverlapScore, Weight = 0.20, Contribution = timeOverlapScore * 0.20 };
            details.DurationFactor = new ScoreFactor { Score = durationScore, Weight = 0.15, Contribution = durationScore * 0.15 };
            details.BatchSubjectFactor = new ScoreFactor { Score = batchSubjectScore, Weight = 0.25, Contribution = batchSubjectScore * 0.25 };
            details.TeacherFactor = new ScoreFactor { Score = teacherScore, Weight = 0.10, Contribution = teacherScore * 0.10 };
            details.HistoricalFactor = new ScoreFactor { Score = historicalScore, Weight = 0.05, Contribution = historicalScore * 0.05 };
            details.ContextFactor = new ScoreFactor { Score = contextScore, Weight = 0.05, Contribution = contextScore * 0.05 };

            var totalScore = (int)Math.Round(details.RoomFactor.Contribution +
                                   details.TimeOverlapFactor.Contribution +
                                   details.DurationFactor.Contribution +
                                   details.BatchSubjectFactor.Contribution +
                                   details.TeacherFactor.Contribution +
                                   details.HistoricalFactor.Contribution +
                                   details.ContextFactor.Contribution);

            scoredCandidates.Add((slot, totalScore, details, appliedOverride));
        }

        if (!scoredCandidates.Any())
        {
            result.Decision = MatchingDecision.NoMatch;
            result.ConfidenceScore = 0;
            result.FailureCode = MatchFailureCode.TimeOverlapFailed;
            result.ReasoningText = "No matching timetable slots (time overlap check failed)";
            return result;
        }

        // Sort by score descending
        var sorted = scoredCandidates.OrderByDescending(c => c.Score).ToList();
        var bestMatch = sorted.First();

        result.MatchedSlot = bestMatch.Slot;
        result.ConfidenceScore = bestMatch.Score;
        result.ScoringDetails = bestMatch.Details;
        result.Candidates = sorted.Select(s => s.Slot).ToList();

        var overrideNote = bestMatch.Override != null ? $" [Schedule override applied: {bestMatch.Override.OverrideType}]" : "";

        // Determine decision
        if (result.ConfidenceScore >= _highConfidenceThreshold)
        {
            result.Decision = MatchingDecision.AutoAssigned;
            result.ReasoningText = $"High confidence match (score: {result.ConfidenceScore}%){overrideNote}";
        }
        else if (result.ConfidenceScore >= _mediumConfidenceThreshold)
        {
            result.Decision = MatchingDecision.ReviewRequired;
            result.ReasoningText = $"Medium confidence - requires review (score: {result.ConfidenceScore}%){overrideNote}";
        }
        else
        {
            result.Decision = MatchingDecision.ReviewRequired;
            result.FailureCode = MatchFailureCode.LowConfidence;
            result.ReasoningText = $"Low confidence - requires review (score: {result.ConfidenceScore}%){overrideNote}";
        }

        return result;
    }

    private static int CalculateBatchSubjectScore(string? filePath, TimetableEntry slot, PdfExtractedMetadata? pdfHints)
    {
        if (pdfHints != null && pdfHints.HasHints)
        {
            var batchMatch = PdfTextExtractor.MatchesBatch(pdfHints, slot.BatchId);
            var subjectMatch = PdfTextExtractor.MatchesSubject(pdfHints, slot.SubjectId);

            if (batchMatch && subjectMatch)
                return 100;
            if (batchMatch)
                return 95;
            if (subjectMatch)
                return 90;
        }

        if (string.IsNullOrWhiteSpace(filePath))
            return 75;

        var fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
        var nameBatchMatch = !string.IsNullOrEmpty(slot.BatchId) && fileName.Contains(slot.BatchId.ToLowerInvariant());
        var nameSubjectMatch = !string.IsNullOrEmpty(slot.SubjectId) && fileName.Contains(slot.SubjectId.ToLowerInvariant());

        if (nameBatchMatch && nameSubjectMatch)
            return 100;
        if (nameBatchMatch || nameSubjectMatch)
            return 90;

        return 75;
    }

    private static int CalculateTeacherScore(TimetableEntry slot, PdfExtractedMetadata? pdfHints)
    {
        if (pdfHints != null && !string.IsNullOrWhiteSpace(pdfHints.TeacherName) && !string.IsNullOrWhiteSpace(slot.TeacherId))
        {
            if (PdfTextExtractor.MatchesTeacher(pdfHints, slot.TeacherId))
                return 100;
        }

        return !string.IsNullOrEmpty(slot.TeacherId) ? 80 : 50;
    }

    private static int CalculateContextScore(LectureSession lecture, TimetableEntry slot)
    {
        // Use IST date for slot timestamp construction (consistent with AnalyzeAsync)
        var istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(lecture.DetectedStartTime, istZone);
        var slotStart = localStart.Date + slot.SlotStartTime;
        var diffMinutes = Math.Abs((localStart - slotStart).TotalMinutes);
        if (diffMinutes <= 15) return 100;
        if (diffMinutes <= 30) return 80;
        return 50;
    }

    private int CalculateTimeOverlapScore(LectureSession lecture, TimetableEntry slot)
    {
        // Use IST date for slot timestamp construction (consistent with AnalyzeAsync)
        var istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(lecture.DetectedStartTime, istZone);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(lecture.DetectedEndTime, istZone);
        var slotStart = localStart.Date + slot.SlotStartTime;
        var slotEnd = localStart.Date + slot.SlotEndTime;

        var overlapStart = new[] { localStart, slotStart }.Max();
        var overlapEnd = new[] { localEnd, slotEnd }.Min();

        if (overlapEnd <= overlapStart)
            return 0; // No overlap

        var overlapDuration = (overlapEnd - overlapStart).TotalSeconds;
        var slotDuration = (slotEnd - slotStart).TotalSeconds;
        var lectureDuration = (lecture.DetectedEndTime - lecture.DetectedStartTime).TotalSeconds;

        var overlapFractionOfLecture = lectureDuration > 0 ? (overlapDuration / lectureDuration) * 100 : 0;
        var overlapFractionOfSlot = slotDuration > 0 ? (overlapDuration / slotDuration) * 100 : 0;
        var overlapPercentage = Math.Max(overlapFractionOfLecture, overlapFractionOfSlot);

        if (overlapPercentage < _timeOverlapMinimumPercentage)
            return 0;

        return (int)Math.Min(100, overlapPercentage);
    }

    private int CalculateDurationScore(LectureSession lecture, TimetableEntry slot)
    {
        var slotDuration = slot.SlotEndTime.TotalSeconds - slot.SlotStartTime.TotalSeconds;
        if (slotDuration <= 0) return 0;

        var recordedDuration = lecture.DetectedDurationSeconds;
        var differenceSeconds = Math.Abs(recordedDuration - slotDuration);
        var toleranceSeconds = Math.Max(60.0, _durationToleranceMinutes * 60.0);

        if (differenceSeconds <= toleranceSeconds)
        {
            // Within tolerance window (± tolerance minutes), score between 90% and 100%
            return (int)Math.Round(100 - (differenceSeconds / toleranceSeconds) * 10);
        }

        // Beyond tolerance window, penalize progressively
        var excess = differenceSeconds - toleranceSeconds;
        var penalty = (excess / slotDuration) * 100;
        return (int)Math.Max(0, Math.Round(90 - penalty));
    }

    private async Task<int> CalculateHistoricalPatternScore(string centerId, string roomId, TimetableEntry slot)
    {
        var matchingLectures = await _dbContext.LectureSessions
            .Where(lecture => lecture.CenterId == centerId
                && lecture.RoomId == roomId
                && lecture.ScheduledSlotId == slot.SlotId
                && lecture.Status != LectureStatus.Rejected
                && lecture.Status != LectureStatus.Cancelled)
            .OrderByDescending(lecture => lecture.DetectedStartTime)
            .Take(10)
            .CountAsync();

        return matchingLectures switch
        {
            0 => 50,
            1 or 2 => 70,
            3 or 4 => 85,
            _ => 100
        };
    }
}
