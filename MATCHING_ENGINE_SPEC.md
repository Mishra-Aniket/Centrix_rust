# Smart Matching Engine Specification

## Overview

The Smart Matching Engine is the core intelligence that automatically assigns recorded lectures to timetable slots with a confidence score.

**Design Principle**: Deterministic rule-based scoring, NOT LLM-driven. Confidence scoring always, human review when uncertain.

---

## Architecture

```
DETECTED LECTURE
    │
    ├─ Extract Metadata
    │  ├─ Detected start/end time
    │  ├─ Duration
    │  ├─ Room (from device)
    │  ├─ Center (from device)
    │  └─ File properties (video codec, audio, etc.)
    │
    ├─ Fetch Timetable
    │  └─ All slots for room on this date
    │     Apply overrides (CANCELLED, INTERCHANGED, etc.)
    │
    ├─ Generate Candidates
    │  └─ All timetable slots that *could* match
    │     (room match + date match + time proximity)
    │
    ├─ Score Each Candidate
    │  ├─ Room match: 0–100%
    │  ├─ Time overlap: 0–100%
    │  ├─ Duration: 0–100%
    │  ├─ Batch/Subject: 0–100%
    │  ├─ Teacher: 0–100%
    │  ├─ Historical pattern: 0–100%
    │  ├─ Previous lecture: 0–100%
    │  ├─ Weighted sum: final score
    │  └─ Generate "reason" narrative
    │
    ├─ Decision
    │  ├─ Score >= 85% → AUTO_ASSIGNED
    │  ├─ 60% ≤ Score < 85% → REVIEW_REQUIRED
    │  └─ Score < 60% → REVIEW_REQUIRED (low confidence, high priority)
    │
    └─ Return
       {
         "matchedSlot": {...},
         "confidence": 94,
         "reason": "Room matched, 95% time overlap, duration compatible...",
         "scoringDetails": {...}
       }
```

---

## Scoring Algorithm

### 1. Room Matching (Weight: 15%)

**Logic**: Device → Room ID known, timetable has room ID.

```
Score = 100 if room_id matches
      = 0 if different room
```

**Code**:
```
score_room = 100 if candidate.room_id == device.room_id else 0
```

### 2. Time Overlap (Weight: 20%)

**Logic**: Detected lecture overlaps with timetable slot.

```
Tolerance: ±15 minutes (configurable)
- Slot scheduled 09:00–10:30
- Recording detected 09:10–10:45

time_overlap_percentage = (overlap_duration / slot_duration) * 100

score_time = time_overlap_percentage if >= 50% else 0
```

**Example Calculation**:
- Scheduled: 09:00–10:30 (90 minutes)
- Detected: 09:10–10:40 (90 minutes)
- Overlap: 09:10–10:30 (80 minutes)
- Score: (80/90) * 100 = 89%

**Edge Case**: Extended lecture
- Scheduled: 09:00–10:30
- Detected: 09:05–11:15 (extends into next slot)
- Overlap: 09:05–10:30 (85 minutes)
- Score: (85/90) * 100 = 94%
- Flag: EXTENDED_LECTURE (needs review)

**Code**:
```csharp
double GetTimeOverlapScore(
    TimeSpan scheduledStart, 
    TimeSpan scheduledEnd, 
    TimeSpan detectedStart, 
    TimeSpan detectedEnd)
{
    var overlapStart = Max(scheduledStart, detectedStart);
    var overlapEnd = Min(scheduledEnd, detectedEnd);
    
    if (overlapEnd <= overlapStart)
        return 0; // No overlap
    
    var overlapDuration = (overlapEnd - overlapStart).TotalMinutes;
    var slotDuration = (scheduledEnd - scheduledStart).TotalMinutes;
    
    return (overlapDuration / slotDuration) * 100;
}
```

### 3. Duration Compatibility (Weight: 15%)

**Logic**: Recording duration reasonably matches slot duration.

```
Tolerance: ±10 minutes (configurable)

slot_duration = scheduled_end - scheduled_start
recorded_duration = detected_end - detected_start

difference = abs(recorded_duration - slot_duration)

score_duration = 100 - (difference / slot_duration) * 100
               = max(0, score_duration)
```

**Example**:
- Slot: 90 minutes
- Recorded: 88 minutes (2 min difference)
- Score: 100 - (2/90)*100 = 97.8%

**Example 2**:
- Slot: 90 minutes
- Recorded: 110 minutes (20 min difference, out of tolerance)
- Score: 100 - (20/90)*100 = 77.8% (penalized, but not zero)

**Code**:
```csharp
double GetDurationCompatibilityScore(
    int slotDurationMinutes, 
    int recordedDurationMinutes)
{
    var difference = Math.Abs(recordedDurationMinutes - slotDurationMinutes);
    var percentDifference = (difference / (double)slotDurationMinutes) * 100;
    
    return Math.Max(0, 100 - percentDifference);
}
```

### 4. Timetable Batch/Subject Match (Weight: 25%)

**Logic**: Lecture's batch and subject info in timetable.

```
score = 100 if batch and subject both match timetable
      = 80 if only batch matches (subject unknown)
      = 50 if only subject matches (batch uncertain)
      = 0 if neither matches
```

**Note**: For most systems, batch is primary (student enrollment), subject secondary.

**Code**:
```csharp
double GetBatchSubjectScore(
    Lecture lecture,
    TimetableSlot slot)
{
    bool batchMatches = lecture.batch_id == slot.batch_id;
    bool subjectMatches = lecture.subject_id == slot.subject_id;
    
    if (batchMatches && subjectMatches) return 100;
    if (batchMatches) return 80;
    if (subjectMatches) return 50;
    return 0;
}
```

### 5. Teacher Matching (Weight: 10%)

**Logic**: If teacher info is extracted (optional), compare.

```
score = 100 if teacher matches
      = 50 if teacher unknown/not extracted
      = 0 if teacher mismatch (rare, but penalize)
```

**Note**: Teacher info is lower priority than batch/subject.

### 6. Historical Pattern (Weight: 5%)

**Logic**: Past behavior at this room/batch/time.

```
Example:
- Room A, slot 09:00 always has Physics lectures
- System detects recording in Room A at 09:00
- Score: +5% bonus if Physics detected
```

**Implementation**: Query last 10 lectures from same room + slot.
- If 8/10 were Physics: boost score by +5%
- If all 10 were different subjects: no bonus

**Code**:
```csharp
double GetHistoricalPatternScore(
    Room room,
    TimeSlot slot,
    Subject detectedSubject,
    int lookbackDays = 30)
{
    var pastLectures = db.Lectures
        .Where(l => l.room_id == room.id 
            && l.slot_id == slot.id 
            && l.date > DateTime.Now.AddDays(-lookbackDays))
        .ToList();
    
    if (pastLectures.Count < 5)
        return 50; // Not enough data
    
    var matchCount = pastLectures.Count(l => l.subject_id == detectedSubject.id);
    var matchPercentage = (matchCount / (double)pastLectures.Count) * 100;
    
    if (matchPercentage > 70)
        return 100;
    if (matchPercentage > 50)
        return 75;
    return 50;
}
```

### 7. Previous Lecture Context (Weight: 5%)

**Logic**: Lectures usually follow timetable order.

```
If last lecture in room ended at 10:30 and next timetable slot is 10:30–11:15:
→ Strong signal it matches this slot

score = 100 if timing aligns with previous
      = 75 if close but not exact
      = 50 if previous lecture unknown
      = 0 if timing contradicts previous
```

**Code**:
```csharp
double GetPreviousLectureContextScore(
    Room room,
    Date date,
    TimeSpan detectedStartTime)
{
    var previousLecture = db.Lectures
        .Where(l => l.room_id == room.id 
            && l.date == date)
        .OrderByDescending(l => l.detected_end_time)
        .FirstOrDefault();
    
    if (previousLecture == null)
        return 50;
    
    var timeSincePrevious = detectedStartTime - previousLecture.detected_end_time;
    
    if (timeSincePrevious < TimeSpan.FromMinutes(5))
        return 100; // Immediately after previous
    if (timeSincePrevious < TimeSpan.FromMinutes(30))
        return 75;  // Small break
    if (timeSincePrevious < TimeSpan.FromMinutes(60))
        return 50;  // Medium break
    return 25;      // Large gap
}
```

---

## Final Scoring Calculation

```
total_score = (
    score_room * 0.15 +
    score_time * 0.20 +
    score_duration * 0.15 +
    score_batch_subject * 0.25 +
    score_teacher * 0.10 +
    score_historical * 0.05 +
    score_previous_context * 0.05
)

Final: round to nearest integer (0–100)
```

### Example Calculation

| Signal | Score | Weight | Contribution |
|--------|-------|--------|--------------|
| Room | 100 | 0.15 | 15.0 |
| Time Overlap | 89 | 0.20 | 17.8 |
| Duration | 97 | 0.15 | 14.5 |
| Batch/Subject | 100 | 0.25 | 25.0 |
| Teacher | 100 | 0.10 | 10.0 |
| Historical | 75 | 0.05 | 3.75 |
| Previous Context | 80 | 0.05 | 4.0 |
| **TOTAL** | | | **90** |

**Decision**: 90 >= 85 → **AUTO_ASSIGNED**

---

## Confidence Score Interpretation

### 90–100%: Very High Confidence
- Auto-assign immediately
- Minimal review needed
- Example: Exact room, perfect time overlap, batch/subject match

### 80–89%: High Confidence
- Auto-assign with brief monitoring
- Few review overrides needed
- Example: Slight duration mismatch but room/time perfect

### 70–79%: Medium Confidence
- Queue for review if configurable threshold is 70
- Likely correct but human confirmation valuable
- Example: Room right, time overlaps but extends beyond slot

### 60–69%: Low-Medium Confidence
- REVIEW_REQUIRED
- Significant chance of correction needed
- Example: Room right, time okay, but batch uncertain

### 50–59%: Low Confidence
- REVIEW_REQUIRED (high priority)
- Multiple signals unclear
- Example: Right time, but wrong room or batch mismatch

### < 50%: Very Low Confidence
- Require manual intervention
- Possible extra lecture or misdetection
- Example: No matching timetable slot in room

---

## Special Cases

### Case A: Extended Lecture (Overlaps Next Slot)

```
Timetable:
09:00–10:30: Physics (LJ151MA)
10:30–12:00: Chemistry (LJ153EA)

Detected:
09:05–11:15 (70-minute recording)

Overlap with Physics: 09:05–10:30 = 85 minutes
Overlap with Chemistry: 10:30–11:15 = 45 minutes

Matching Engine:
- Physics: score = high (but overlaps next)
- Chemistry: score = medium (starts during Physics)

Action: Mark as EXTENDED_LECTURE, require review
```

### Case B: Different Batch in Same Room

```
Timetable:
09:00–10:30: Physics (LJ151MA)

Recorded Lecture content analyzed (if PDF title available):
"Introduction to Chemistry"

Matching Engine:
- Room matches: +100
- Time matches: +89
- Duration matches: +97
- Batch/Subject MISMATCH: 0

Total: (100*0.15 + 89*0.20 + 97*0.15 + 0*0.25 + ...) ≈ 60%

Result: REVIEW_REQUIRED (low confidence due to subject mismatch)

Reviewer can then:
- Confirm it's actually Chemistry (input error in timetable)
- Change batch to LJ153EA
- Or keep LJ151MA if content is mislabeled
```

### Case C: Lecture Starts Late

```
Timetable:
09:00–10:30: Physics

Detected:
09:22–10:52 (actual recording started 22 minutes late)

Time Overlap Score:
- Overlap: 09:22–10:30 = 68 minutes
- Slot: 90 minutes
- Score: (68/90)*100 = 75.6%

Duration Score:
- Slot: 90 minutes
- Recording: 90 minutes
- Score: 100 (duration matches!)

Final: Moderate-high confidence (≈85%)

Reason for Review: "Lecture started late, but duration consistent with slot"
```

### Case D: No Matching Timetable Slot (Extra Lecture)

```
Timetable:
09:00–10:30: Physics
10:30–12:00: Chemistry

Detected in Room A:
12:15–13:45

Matching Engine:
- No candidate slots overlap with 12:15–13:45
- Confidence: 0%

Result: EXTRA_LECTURE (or no match)

Actions:
1. Notify reviewer: "Extra lecture detected in Room A at 12:15"
2. Reviewer can:
   - Assign manually to a batch/subject
   - Mark as EXTRA_LECTURE (keep but not in timetable)
   - Reject if it's a false positive
```

---

## Configurable Thresholds

All thresholds must be configurable per organization:

```json
{
  "organizationId": "ORG-001",
  "matchingConfig": {
    "confidenceHighThreshold": 85,
    "confidenceMediumThreshold": 60,
    "confidenceLowThreshold": 40,
    
    "timeOverlapMinimumPercentage": 50,
    "timeToleranceMinutes": 15,
    
    "durationToleranceMinutes": 10,
    
    "extendedLectureOverlapThreshold": 30,
    
    "weights": {
      "room": 0.15,
      "timeOverlap": 0.20,
      "duration": 0.15,
      "batchSubject": 0.25,
      "teacher": 0.10,
      "historical": 0.05,
      "previousContext": 0.05
    },
    
    "historicalPatternLookbackDays": 30,
    "minPastLecturesForPattern": 5,
    
    "enableAutoAssign": true,
    "enableHistoricalPatterns": true,
    "enablePreviousLectureContext": true
  }
}
```

---

## Output Format

### Matching Result Object

```json
{
  "lectureSessionId": "LSN-2026-09-02-0001",
  "detectedStartTime": "2026-09-02T09:10:00Z",
  "detectedEndTime": "2026-09-02T10:40:00Z",
  "detectedDurationSeconds": 5400,
  
  "matchedSlot": {
    "slotId": "C001-R001-09-00-10-30",
    "scheduledStartTime": "2026-09-02T09:00:00Z",
    "scheduledEndTime": "2026-09-02T10:30:00Z",
    "batchId": "LJ151MA",
    "subjectId": "Physics",
    "teacherId": "T001"
  },
  
  "confidence": {
    "overallScore": 94,
    "decision": "AUTO_ASSIGNED",
    
    "scoringDetails": {
      "room": { "score": 100, "weight": 0.15, "contribution": 15.0 },
      "timeOverlap": { "score": 89, "weight": 0.20, "contribution": 17.8 },
      "duration": { "score": 97, "weight": 0.15, "contribution": 14.5 },
      "batchSubject": { "score": 100, "weight": 0.25, "contribution": 25.0 },
      "teacher": { "score": 100, "weight": 0.10, "contribution": 10.0 },
      "historical": { "score": 75, "weight": 0.05, "contribution": 3.75 },
      "previousContext": { "score": 80, "weight": 0.05, "contribution": 4.0 }
    },
    
    "reasoning": {
      "main": "Room perfectly matched, 89% time overlap, duration compatible, batch/subject confirmed",
      "positives": [
        "Room ID matches",
        "Time overlap 89% (within tolerance)",
        "Duration difference only 2%",
        "Batch and subject both match timetable",
        "Teacher matches",
        "Historical pattern supports Physics in this slot"
      ],
      "cautions": [
        "Lecture started 10 minutes late",
        "Lecture ended 10 minutes late"
      ],
      "flags": []
    }
  },
  
  "candidates": [
    {
      "slotId": "C001-R001-09-00-10-30",
      "score": 94,
      "timeOverlap": 89,
      "batch": "LJ151MA",
      "subject": "Physics"
    },
    {
      "slotId": "C001-R001-10-30-12-00",
      "score": 12,
      "timeOverlap": 10,
      "batch": "LJ153EA",
      "subject": "Chemistry"
    }
  ],
  
  "requiresReview": false,
  "recommendedAction": "QUEUE_FOR_UPLOAD",
  
  "timestamp": "2026-09-02T10:50:00Z"
}
```

---

## Testing & Calibration

### Unit Tests

```csharp
[Test]
public void TimeOverlapScore_PerfectMatch_ReturnsHundred()
{
    var scheduled = new TimeWindow(9:00, 10:30);
    var detected = new TimeWindow(9:00, 10:30);
    
    var score = matcher.GetTimeOverlapScore(scheduled, detected);
    
    Assert.AreEqual(100, score);
}

[Test]
public void TimeOverlapScore_StartedLate_ReturnsReasonableScore()
{
    var scheduled = new TimeWindow(9:00, 10:30); // 90 min
    var detected = new TimeWindow(9:10, 10:40); // 90 min
    
    var score = matcher.GetTimeOverlapScore(scheduled, detected);
    
    Assert.AreEqual(89, score); // 80/90 = 88.9 ≈ 89
}

[Test]
public void FinalScore_AllPerfect_ReturnsNearHundred()
{
    var lecture = new Lecture { /* perfect match */ };
    
    var score = matcher.CalculateConfidenceScore(lecture);
    
    Assert.GreaterOrEqual(score, 90);
}
```

### Integration Tests

- Test against 1 year of historical lectures
- Verify accuracy >= 95% on known assignments
- Test edge cases (late start, extended, different batch, etc.)

---

## Performance

- Single lecture matching: < 100ms
- Batch 1000 lectures: < 30 seconds
- Queries optimized with indexes on: centerId, roomId, date, batch_id, subject_id

---

**Status**: Matching Engine Specification Phase  
**Last Updated**: 2026-09-02  
**Version**: 1.0.0-alpha
