# State Machines

## LectureSession Lifecycle

Every recorded lecture goes through a well-defined state machine. States are mutually exclusive. Transitions must be logged in audit trail.

### State Diagram

```
┌─────────────────┐
│    DETECTED     │ (file created, initial state)
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  PROCESSING     │ (validating file, extracting metadata)
└────────┬────────┘
         │
         ├─────────────────────────────────────────┐
         │                                         │
         ▼                                         ▼
┌──────────────────┐                    ┌──────────────────┐
│  AUTO_ASSIGNED   │ (confidence >= 85) │ REVIEW_REQUIRED  │ (60-84)
└────────┬─────────┘                    └────────┬─────────┘
         │                                       │
         └───────────────────┬───────────────────┘
                             │
                             ▼
                    ┌─────────────────┐
                    │    CONFIRMED    │ (human approved or auto)
                    └────────┬────────┘
                             │
                             ▼
                    ┌─────────────────┐
                    │   UPLOADING     │ (queued to Google Drive)
                    └────────┬────────┘
                             │
                    ┌────────┴─────────┐
                    │                  │
                    ▼                  ▼
            ┌──────────────┐    ┌────────────────┐
            │  UPLOADED    │    │ UPLOAD_FAILED  │ (retry queue)
            └────────┬─────┘    └────────┬───────┘
                     │                   │
                     │   ┌───────────────┘
                     │   │
                     ▼   ▼
            ┌──────────────┐
            │  VERIFIED    │ (hash matched)
            └────────┬─────┘
                     │
            ┌────────┴──────────┐
            │                   │
            ▼                   ▼
    ┌────────────────┐  ┌──────────────┐
    │  PDF_PENDING   │  │  ARCHIVED    │ (retention policy)
    └────────┬───────┘  └──────────────┘
             │
             │ (PDF arrives)
             ▼
    ┌──────────────────┐
    │   RE_UPLOADING   │
    └────────┬─────────┘
             │
             ▼
    ┌──────────────────┐
    │    VERIFIED      │ (complete with PDF)
    └────────┬─────────┘
             │
             ▼
    ┌──────────────────┐
    │    ARCHIVED      │
    └──────────────────┘

EXCEPTION PATHS:

VIDEO_MISSING ──→ ARCHIVED (after grace period + notification)
PDF_MISSING ───→ ARCHIVED (after grace period, if VIDEO_MISSING also set)
EXTRA_LECTURE ─→ ARCHIVED (no matching timetable)
CANCELLED ─────→ ARCHIVED (human marked cancelled)
REJECTION ─────→ ARCHIVED (human rejected recording)
CORRECTION_REQUIRED ─→ CORRECTED → RE_UPLOADING → VERIFIED → ARCHIVED
```

### State Descriptions

| State | Duration | Actions | Next States |
|-------|----------|---------|-------------|
| DETECTED | Seconds | Create session, extract file metadata | PROCESSING |
| PROCESSING | Seconds-mins | Validate file, get dimensions, duration | AUTO_ASSIGNED, REVIEW_REQUIRED |
| AUTO_ASSIGNED | Immediate | High confidence match, queue upload | UPLOADING |
| REVIEW_REQUIRED | Hours/days | Wait for human review | CONFIRMED |
| CONFIRMED | Immediate | Human or system approved, ready to upload | UPLOADING |
| UPLOADING | Minutes-hours | Upload file to Google Drive with retry | UPLOADED, UPLOAD_FAILED |
| UPLOADED | Seconds | File on Drive, verify hash | VERIFIED, UPLOAD_FAILED |
| VERIFIED | Immediate | Hash validated, entry complete | PDF_PENDING (if PDF expected), ARCHIVED |
| PDF_PENDING | Hours-days | Video done, awaiting PDF | RE_UPLOADING (if PDF arrives), ARCHIVED (if grace period expires) |
| RE_UPLOADING | Minutes-hours | Upload PDF to same Drive location | VERIFIED |
| UPLOAD_FAILED | Minutes-days | On queue, exponential backoff | UPLOADING (retry) |
| VIDEO_MISSING | Wait grace period | No video detected by deadline | ARCHIVED |
| PDF_MISSING | Wait grace period | No PDF detected by deadline | ARCHIVED |
| EXTRA_LECTURE | Immediate | No matching timetable slot | Can be manually assigned or ARCHIVED |
| CANCELLED | Immediate | Human marked cancelled | ARCHIVED |
| REJECTED | Immediate | Human rejected | ARCHIVED |
| CORRECTION_REQUIRED | Wait minutes | Timetable changed after assignment | CORRECTED |
| CORRECTED | Immediate | Reassigned after timetable change | RE_UPLOADING |
| ARCHIVED | Final | Moved to archive, no further changes | — |

---

## Review Queue Lifecycle

Separate state machine for human review process.

### Review States

```
PENDING
  │ (reviewer claims)
  ▼
LOCKED (by user X)
  │
  ├─ (timeout 5 min)
  │   → PENDING
  │
  ├─ (reviewer confirms)
  │   → COMPLETED
  │
  ├─ (reviewer changes & confirms)
  │   → COMPLETED
  │
  └─ (reviewer rejects)
      → REJECTED
```

### Review Status Fields

```json
{
  "reviewStatus": "PENDING|LOCKED|COMPLETED|REJECTED",
  "lockedBy": "userId|null",
  "lockedAt": "timestamp|null",
  "completedBy": "userId|null",
  "completedAt": "timestamp|null",
  "lockTimeoutAt": "timestamp|null"
}
```

### Lock Timeout Mechanism

- When reviewer claims item: `lockTimeoutAt = now + 5 minutes`
- Server/app checks periodically
- If timeout reached and still locked, auto-release
- Prevent indefinite locks

---

## Upload Queue Lifecycle

Queue entries move through upload states.

### Upload States

```
PENDING
  │ (upload starts)
  ▼
UPLOADING
  │
  ├─ (completed)
  │   → UPLOADED
  │
  └─ (network error)
      → FAILED (queued for retry)
         │ (wait backoff)
         ▼
         PENDING (retry)
```

### Upload Entry Fields

```json
{
  "status": "PENDING|UPLOADING|UPLOADED|FAILED",
  "bytesUploaded": 1000000,
  "totalBytes": 2000000,
  "progressPercentage": 50,
  "retryCount": 2,
  "nextRetryAt": "timestamp",
  "driveResumableUri": "https://..."
}
```

### Retry Strategy

1. First attempt: immediate
2. Fail: exponential backoff
   - Attempt 1: 1 second delay
   - Attempt 2: 2 seconds delay
   - Attempt 3: 4 seconds delay
   - Attempt 4: 8 seconds delay
   - Attempt 5+: 30 seconds delay
3. Max attempts: 5
4. After 5 failures: manual review needed

---

## Matching Engine Confidence State

Not a state machine, but a confidence scoring system.

### Confidence Ranges

| Range | Action | Status |
|-------|--------|--------|
| 85–100% | Auto-assign | AUTO_ASSIGNED |
| 60–84% | Queue for review | REVIEW_REQUIRED |
| < 60% | Require human intervention | REVIEW_REQUIRED (high priority) |

### Confidence Inputs

```json
{
  "roomMatch": {
    "matched": true,
    "score": 100,
    "weight": 0.15
  },
  "timeOverlap": {
    "overlapPercentage": 95,
    "score": 95,
    "weight": 0.20
  },
  "durationCompatibility": {
    "withinTolerance": true,
    "score": 100,
    "weight": 0.15
  },
  "timetableMatch": {
    "directMatch": true,
    "score": 100,
    "weight": 0.25
  },
  "teacherMatch": {
    "matched": true,
    "score": 100,
    "weight": 0.10
  },
  "historicalPattern": {
    "patternScore": 90,
    "score": 90,
    "weight": 0.05
  },
  "previousLectureContext": {
    "score": 85,
    "weight": 0.05
  }
}
```

Final Score: sum(score * weight)

---

## Device Health State

Devices transition between online/offline states.

```
ACTIVE + ONLINE
  │ (heartbeat sent & received)
  ├─ (no heartbeat in 5 min)
  │   → ACTIVE + OFFLINE
  │
  └─ (manual disable)
      → DISABLED

ACTIVE + OFFLINE
  │ (heartbeat received)
  ├─ → ACTIVE + ONLINE
  │
  └─ (no heartbeat in 2 hours)
      → ACTIVE + OFFLINE (alert sent)

DISABLED
  │
  └─ (manual enable)
      → ACTIVE + ONLINE
```

### Device Status Report

```json
{
  "deviceId": "DEV-001",
  "status": "ACTIVE|DISABLED",
  "onlineStatus": "ONLINE|OFFLINE",
  "lastHeartbeat": "timestamp",
  "diskFreeBytes": 500000000000,
  "uploadQueueSize": 12,
  "appVersion": "1.0.5",
  "lastError": "Google Drive API throttled",
  "errorCount": 2
}
```

---

## Timetable Override State

Overrides can be applied/unapplied.

```
CREATED
  │ (set effective date)
  ▼
ACTIVE
  │
  ├─ (cancel override)
  │   → CANCELLED
  │
  └─ (end date reached)
      → EXPIRED
```

### Override Application

When a lecture is assigned and later a timetable override affects it:

1. System detects mismatch
2. Marks lecture: `CORRECTION_REQUIRED`
3. Reads override details
4. Re-assigns batch/subject/room/time
5. Marks lecture: `CORRECTED`
6. Queues re-upload to new Drive folder
7. Records in audit log

---

## Special Cases

### Case 1: PDF Arrives After Video

```
Initial state: VERIFIED (video only)
  │
  ├─ PDF file detected
  │   │
  │   ├─ Match: find existing lecture by room+date+time
  │   │
  │   ▼
  │ Update lecture_session.pdf_file_*
  │
  └─ Queue PDF for upload
      │
      ▼
    RE_UPLOADING
      │
      ▼
    VERIFIED (with PDF)
```

### Case 2: Video Arrives After PDF

Similar flow, but:
```
Initial state: (none)
  │
  ├─ PDF file detected first
  │   │
  │   └─ No matching timetable
  │       → REVIEW_REQUIRED (PDF only, suspicious)
  │
  ├─ Video file detected later
  │   │
  │   ├─ Find matching lecture or create new
  │   │
  │   └─ Associate both files
  │       → Update existing session or create new
```

### Case 3: Double Recording (Same File Detected Twice)

```
First detection: DETECTED
  │
  ├─ File watcher re-detects (watchdog re-scan)
  │
  ├─ System checks: hash already in DB?
  │
  ├─ YES: Skip (duplicate)
  │   → No action
  │
  └─ NO: Create new session
      → DETECTED (error: investigate)
```

---

## State Transitions Table

### Valid Transitions

| From | To | Condition |
|------|----|-----------| 
| DETECTED | PROCESSING | File validated |
| PROCESSING | AUTO_ASSIGNED | Confidence >= 85% |
| PROCESSING | REVIEW_REQUIRED | 60% <= Confidence < 85% |
| AUTO_ASSIGNED | UPLOADING | Scheduled or immediate |
| REVIEW_REQUIRED | CONFIRMED | Human approved |
| CONFIRMED | UPLOADING | Scheduled or immediate |
| UPLOADING | UPLOADED | Upload complete |
| UPLOADING | UPLOAD_FAILED | Network/API error |
| UPLOAD_FAILED | UPLOADING | Retry |
| UPLOADED | VERIFIED | Hash matched |
| VERIFIED | PDF_PENDING | PDF expected but not yet arrived |
| VERIFIED | ARCHIVED | No PDF expected, entry complete |
| PDF_PENDING | RE_UPLOADING | PDF file detected |
| PDF_PENDING | ARCHIVED | Grace period expired |
| RE_UPLOADING | VERIFIED | PDF upload complete and verified |
| CORRECTION_REQUIRED | CORRECTED | Timetable override applied |
| CORRECTED | RE_UPLOADING | New folder determined, re-upload queued |
| RE_UPLOADING | VERIFIED | Re-upload complete |
| VIDEO_MISSING | ARCHIVED | After grace period + notification |
| PDF_MISSING | ARCHIVED | After grace period + notification |
| EXTRA_LECTURE | ARCHIVED | Manually or after retention period |
| CANCELLED | ARCHIVED | Immediate |
| REJECTED | ARCHIVED | Immediate |
| ANY → ARCHIVED | (retention) | Retention policy triggered |

### Invalid Transitions

These should be rejected with audit warning:
- VERIFIED → DETECTED (no rollback)
- ARCHIVED → any state (final state)
- Skipping intermediate states without justification

---

**Status**: State Machine Phase  
**Last Updated**: 2026-09-02  
**Version**: 1.0.0-alpha
