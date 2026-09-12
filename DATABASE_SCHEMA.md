# Database Schema

## Overview

The system uses two database backends:
- **SQLite**: Local at each center (durable cache, queue, processing)
- **Firestore**: Cloud (lightweight metadata, sync point)

This document specifies both schemas.

---

## SQLite Schema (Center-Local)

### 1. Device Configuration

```sql
CREATE TABLE devices (
  device_id TEXT PRIMARY KEY,
  organization_id TEXT NOT NULL,
  center_id TEXT NOT NULL,
  room_id TEXT NOT NULL,
  device_name TEXT NOT NULL,
  device_type TEXT,
  recording_folder_path TEXT,
  status TEXT DEFAULT 'ACTIVE', -- ACTIVE, DISABLED
  created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX idx_device_org_center_room 
  ON devices(organization_id, center_id, room_id);
```

### 2. Lecture Sessions

```sql
CREATE TABLE lecture_sessions (
  lecture_session_id TEXT PRIMARY KEY,
  organization_id TEXT NOT NULL,
  center_id TEXT NOT NULL,
  room_id TEXT NOT NULL,
  device_id TEXT NOT NULL,
  
  -- Detected timing
  detected_start_time DATETIME NOT NULL,
  detected_end_time DATETIME NOT NULL,
  detected_duration_seconds INTEGER,
  
  -- Scheduled (from timetable)
  scheduled_start_time DATETIME,
  scheduled_end_time DATETIME,
  scheduled_slot_id TEXT,
  
  -- Assignment
  batch_id TEXT,
  subject_id TEXT,
  teacher_id TEXT,
  assignment_source TEXT, -- AUTO, MANUAL, TIMETABLE
  confidence_score INTEGER, -- 0-100
  matching_reason TEXT, -- JSON: why this match
  
  -- Files
  video_file_local_path TEXT,
  video_file_size_bytes INTEGER,
  video_file_hash TEXT,
  pdf_file_local_path TEXT,
  pdf_file_size_bytes INTEGER,
  pdf_file_hash TEXT,
  
  -- Cloud references
  drive_video_file_id TEXT,
  drive_pdf_file_id TEXT,
  drive_folder_path TEXT,
  
  -- Status lifecycle
  status TEXT NOT NULL DEFAULT 'DETECTED', -- see status list below
  review_status TEXT, -- PENDING, LOCKED, APPROVED, REJECTED
  reviewer_id TEXT,
  locked_at DATETIME,
  locked_by TEXT,
  
  -- Audit
  created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
  last_status_change DATETIME
);

CREATE INDEX idx_lecture_center_date 
  ON lecture_sessions(center_id, DATE(detected_start_time));
CREATE INDEX idx_lecture_status 
  ON lecture_sessions(status);
CREATE INDEX idx_lecture_review_status 
  ON lecture_sessions(review_status);
```

### LectureSession Statuses

| Status | Meaning |
|--------|---------|
| DETECTED | File detected, created LectureSession |
| PROCESSING | Files being validated/analyzed |
| AUTO_ASSIGNED | High confidence, assignment made automatically |
| REVIEW_REQUIRED | Low confidence, awaiting human review |
| CONFIRMED | Human confirmed assignment (or auto confirmed) |
| UPLOADING | Files being uploaded to Google Drive |
| UPLOADED | Files successfully uploaded |
| VERIFIED | Upload verified (hash matched) |
| PDF_PENDING | Video uploaded, PDF still expected |
| VIDEO_MISSING | Expected but never detected (after grace period) |
| PDF_MISSING | Expected but never detected (after grace period) |
| EXTRA_LECTURE | Detected but no matching timetable |
| CANCELLED | Marked as cancelled by human/system |
| UPLOAD_FAILED | Upload to Drive failed, queued for retry |
| CORRECTION_REQUIRED | Timetable changed after assignment |
| CORRECTED | Moved/re-assigned after timetable change |
| REJECTED | Human rejected this recording |
| ARCHIVED | Old record (retention policy) |

### 3. Upload Queue

```sql
CREATE TABLE upload_queue (
  queue_entry_id TEXT PRIMARY KEY,
  lecture_session_id TEXT NOT NULL,
  file_type TEXT NOT NULL, -- VIDEO, PDF
  local_file_path TEXT NOT NULL,
  file_size_bytes INTEGER,
  file_hash TEXT,
  
  drive_folder_path TEXT,
  drive_file_name TEXT,
  drive_resumable_uri TEXT,
  
  status TEXT DEFAULT 'PENDING', -- PENDING, UPLOADING, UPLOADED, FAILED
  bytes_uploaded INTEGER DEFAULT 0,
  total_bytes INTEGER,
  progress_percentage INTEGER,
  
  retry_count INTEGER DEFAULT 0,
  max_retries INTEGER DEFAULT 5,
  next_retry_at DATETIME,
  last_error TEXT,
  
  created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_queue_status 
  ON upload_queue(status, next_retry_at);
CREATE INDEX idx_queue_lecture 
  ON upload_queue(lecture_session_id);
```

### 4. Timetable Cache

```sql
CREATE TABLE timetable_entries (
  timetable_entry_id TEXT PRIMARY KEY,
  organization_id TEXT NOT NULL,
  center_id TEXT NOT NULL,
  room_id TEXT NOT NULL,
  
  scheduled_date DATE NOT NULL,
  slot_start_time TIME NOT NULL,
  slot_end_time TIME NOT NULL,
  slot_id TEXT,
  
  batch_id TEXT NOT NULL,
  subject_id TEXT NOT NULL,
  teacher_id TEXT,
  
  created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_timetable_center_date 
  ON timetable_entries(center_id, scheduled_date);
CREATE INDEX idx_timetable_room_date 
  ON timetable_entries(room_id, scheduled_date);
```

### 5. Timetable Overrides

```sql
CREATE TABLE timetable_overrides (
  override_id TEXT PRIMARY KEY,
  organization_id TEXT NOT NULL,
  center_id TEXT NOT NULL,
  room_id TEXT NOT NULL,
  
  -- Which timetable entry is being overridden
  original_timetable_entry_id TEXT,
  original_date DATE,
  original_slot_id TEXT,
  
  -- Override type
  override_type TEXT NOT NULL, -- CANCELLED, INTERCHANGED, EXTRA, CHANGED_BATCH, CHANGED_SUBJECT, CHANGED_ROOM, CHANGED_TIME
  
  -- New values (if applicable)
  new_batch_id TEXT,
  new_subject_id TEXT,
  new_teacher_id TEXT,
  new_room_id TEXT,
  new_date DATE,
  new_slot_start_time TIME,
  new_slot_end_time TIME,
  new_slot_id TEXT,
  
  effective_date DATE NOT NULL,
  is_active BOOLEAN DEFAULT TRUE,
  
  created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_override_center_date 
  ON timetable_overrides(center_id, effective_date);
CREATE INDEX idx_override_active 
  ON timetable_overrides(is_active);
```

### 6. Audit Log (Local Mirror)

```sql
CREATE TABLE audit_log (
  audit_entry_id TEXT PRIMARY KEY,
  organization_id TEXT NOT NULL,
  center_id TEXT NOT NULL,
  
  -- What changed
  entity_type TEXT NOT NULL, -- LECTURE_SESSION, UPLOAD, TIMETABLE
  entity_id TEXT NOT NULL,
  action_type TEXT NOT NULL, -- CREATED, UPDATED, CONFIRMED, REJECTED, MOVED, ASSIGNED, CORRECTED
  
  -- Who changed it
  user_id TEXT,
  user_role TEXT,
  
  -- What was changed
  old_values TEXT, -- JSON
  new_values TEXT, -- JSON
  change_summary TEXT,
  
  -- Sync status to cloud
  synced_to_cloud BOOLEAN DEFAULT FALSE,
  cloud_sync_at DATETIME,
  
  created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_audit_entity 
  ON audit_log(entity_type, entity_id);
CREATE INDEX idx_audit_synced 
  ON audit_log(synced_to_cloud);
```

### 7. Device Health

```sql
CREATE TABLE device_health (
  health_id TEXT PRIMARY KEY,
  device_id TEXT NOT NULL,
  
  disk_free_bytes INTEGER,
  disk_total_bytes INTEGER,
  upload_queue_size INTEGER,
  pending_reviews INTEGER,
  last_successful_upload_at DATETIME,
  
  app_version TEXT,
  last_error TEXT,
  error_count INTEGER DEFAULT 0,
  
  recorded_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_health_device_time 
  ON device_health(device_id, recorded_at DESC);
```

### 8. Processing Configuration

```sql
CREATE TABLE config (
  config_key TEXT PRIMARY KEY,
  config_value TEXT, -- JSON if complex
  data_type TEXT, -- STRING, INTEGER, BOOLEAN, JSON
  is_overridable BOOLEAN DEFAULT FALSE,
  last_updated DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Default configurations:
-- file_stability_period_ms: 10000
-- max_concurrent_uploads: 2
-- upload_retry_max_attempts: 5
-- upload_retry_backoff_base_ms: 1000
-- missing_video_grace_period_minutes: 30
-- missing_pdf_grace_period_minutes: 45
-- confidence_high_threshold: 85
-- confidence_medium_threshold: 60
-- confidence_low_threshold: 40
-- review_lock_timeout_minutes: 5
-- enable_local_review_popup: true
-- google_drive_folder_template: {center}/{batch}/{subject}
```

---

## Firestore Schema (Cloud)

Structure:

```
organizations/{organizationId}/
  ├── metadata
  ├── centers/{centerId}/
  │   ├── metadata
  │   ├── rooms/{roomId}/
  │   │   ├── metadata
  │   │   ├── devices/{deviceId}/ (metadata only)
  │   │   └── timetable/{date}/ (synced from center)
  │   ├── timetable/{date}/
  │   │   └── {slotId}
  │   └── overrides/{overrideId}/
  ├── lectures/{lectureSessionId}/
  ├── reviewQueue/{lectureSessionId}/
  ├── users/{userId}/
  ├── auditLog/{auditEntryId}/
  └── config/{configKey}/
```

### Collections

#### 1. organizations
```json
{
  "organizationId": "ORG-001",
  "name": "University ABC",
  "address": "...",
  "contactEmail": "admin@abc.edu",
  "createdAt": timestamp,
  "updatedAt": timestamp
}
```

#### 2. centers/{centerId}
```json
{
  "centerId": "C-001",
  "name": "North Campus",
  "organizationId": "ORG-001",
  "address": "...",
  "timezone": "Asia/Kolkata",
  "createdAt": timestamp,
  "updatedAt": timestamp
}
```

#### 3. rooms/{roomId}
```json
{
  "roomId": "R-001-A",
  "centerId": "C-001",
  "name": "Lecture Hall A",
  "capacity": 100,
  "createdAt": timestamp
}
```

#### 4. devices/{deviceId}
```json
{
  "deviceId": "DEV-001",
  "centerId": "C-001",
  "roomId": "R-001-A",
  "name": "Camera 1",
  "status": "ACTIVE", // or DISABLED
  "appVersion": "1.0.5",
  "lastHeartbeat": timestamp,
  "lastSuccessfulUpload": timestamp,
  "onlineStatus": "ONLINE", // or OFFLINE
  "createdAt": timestamp
}
```

#### 5. timetable/{date}/{slotId}
```json
{
  "date": "2026-09-02",
  "slotId": "09:00-10:30",
  "roomId": "R-001-A",
  "batchId": "LJ151MA",
  "subjectId": "Physics",
  "teacherId": "T-001",
  "startTime": "09:00",
  "endTime": "10:30",
  "createdAt": timestamp,
  "updatedAt": timestamp
}
```

#### 6. overrides/{overrideId}
```json
{
  "overrideId": "OVR-001",
  "centerId": "C-001",
  "type": "CANCELLED", // or INTERCHANGED, EXTRA, CHANGED_BATCH, etc.
  "effectiveDate": "2026-09-02",
  "originalTimetableEntryId": "...",
  "newBatchId": "LJ153EA", // if type is CHANGED_BATCH
  "newSubjectId": "Chemistry",
  "newRoomId": "...",
  "isActive": true,
  "createdAt": timestamp,
  "appliedAt": timestamp
}
```

#### 7. lectures/{lectureSessionId}
```json
{
  "lectureSessionId": "LSN-2026-09-02-0001",
  "organizationId": "ORG-001",
  "centerId": "C-001",
  "roomId": "R-001-A",
  "deviceId": "DEV-001",
  "detectedStartTime": timestamp,
  "detectedEndTime": timestamp,
  "detectedDurationSeconds": 5400,
  "scheduledStartTime": timestamp,
  "scheduledEndTime": timestamp,
  "batchId": "LJ151MA",
  "subjectId": "Physics",
  "teacherId": "T-001",
  "assignmentSource": "AUTO",
  "confidenceScore": 94,
  "status": "CONFIRMED",
  "reviewStatus": "APPROVED",
  "reviewedBy": "U-001",
  "driveVideoFileId": "gdriveId123",
  "drivePdfFileId": "gdriveId456",
  "driveFolderPath": "/Center-001/LJ151MA/Physics",
  "uploadStatus": "VERIFIED",
  "uploadedAt": timestamp,
  "createdAt": timestamp,
  "updatedAt": timestamp
}
```

#### 8. reviewQueue/{lectureSessionId}
```json
{
  "lectureSessionId": "LSN-2026-09-02-0002",
  "centerId": "C-001",
  "status": "PENDING", // or LOCKED, COMPLETED
  "suggestedBatchId": "LJ151MA",
  "suggestedSubjectId": "Physics",
  "confidenceScore": 62,
  "matchingReason": {
    "roomMatched": true,
    "timeOverlapped": true,
    "timetableMatched": true,
    "durationCompatible": true
  },
  "lockedBy": null, // or "reviewer123"
  "lockedAt": null,
  "createdAt": timestamp,
  "updatedAt": timestamp
}
```

#### 9. users/{userId}
```json
{
  "userId": "U-001",
  "email": "reviewer@abc.edu",
  "displayName": "Rahul Kumar",
  "role": "REVIEWER", // SUPER_ADMIN, CENTER_ADMIN, REVIEWER, ROOM_OPERATOR
  "authorizedCenterIds": ["C-001", "C-002"],
  "photoUrl": "...",
  "createdAt": timestamp,
  "updatedAt": timestamp
}
```

#### 10. auditLog/{auditEntryId}
```json
{
  "auditEntryId": "AUD-001",
  "centerId": "C-001",
  "entityType": "LECTURE_SESSION",
  "entityId": "LSN-2026-09-02-0001",
  "actionType": "CONFIRMED",
  "userId": "U-001",
  "userRole": "REVIEWER",
  "oldValues": {
    "status": "REVIEW_REQUIRED",
    "batchId": "LJ151MA",
    "confidenceScore": 62
  },
  "newValues": {
    "status": "CONFIRMED",
    "batchId": "LJ153EA",
    "confidenceScore": 100
  },
  "changeSummary": "Reviewer changed batch from LJ151MA (Physics) to LJ153EA (Chemistry)",
  "syncedToCloud": true,
  "createdAt": timestamp
}
```

#### 11. config/{configKey}
```json
{
  "key": "confidence_high_threshold",
  "value": 85,
  "type": "INTEGER",
  "description": "Auto-assign if confidence >= 85%",
  "organizationId": "ORG-001", // null if global
  "updatedAt": timestamp
}
```

---

## Indexing Strategy

### SQLite Indexes

```sql
-- Lecture queries
CREATE INDEX idx_lecture_center_date ON lecture_sessions(center_id, detected_start_time);
CREATE INDEX idx_lecture_room_date ON lecture_sessions(room_id, detected_start_time);
CREATE INDEX idx_lecture_status ON lecture_sessions(status);
CREATE INDEX idx_lecture_review_status ON lecture_sessions(review_status);

-- Timetable queries
CREATE INDEX idx_timetable_center_date ON timetable_entries(center_id, scheduled_date);
CREATE INDEX idx_timetable_room_date ON timetable_entries(room_id, scheduled_date);

-- Upload queue queries
CREATE INDEX idx_queue_status ON upload_queue(status, next_retry_at);
CREATE INDEX idx_queue_lecture ON upload_queue(lecture_session_id);

-- Audit log queries
CREATE INDEX idx_audit_entity ON audit_log(entity_type, entity_id);
CREATE INDEX idx_audit_synced ON audit_log(synced_to_cloud);
```

### Firestore Indexes

```
Collection: lectures
  Fields: centerId (Asc), detectedStartTime (Desc)
  
Collection: lectures
  Fields: status (Asc), updatedAt (Desc)
  
Collection: reviewQueue
  Fields: centerId (Asc), status (Asc)
  
Collection: reviewQueue
  Fields: status (Asc), createdAt (Asc)
  
Collection: auditLog
  Fields: centerId (Asc), createdAt (Desc)
```

---

## Data Synchronization

### Center → Cloud
- LectureSession metadata (after upload completed)
- Audit logs (batch sync, async)
- Device health (periodic heartbeat)

### Cloud → Center
- Timetable (on change or periodic sync)
- Timetable overrides (real-time or periodic)
- Configuration updates (periodic or on-demand)
- User roles/permissions (periodic)

### Conflict Resolution
- Last-write-wins
- Firestore timestamp takes precedence for metadata
- Local SQLite used for offline processing
- Audit log records all changes

---

## Data Retention Policies

| Table | Retention | Rationale |
|-------|-----------|-----------|
| lecture_sessions | 2 years | Regulatory, searchable history |
| upload_queue | 30 days | Retry window, archive old entries |
| timetable_entries | 1 year | Current + past year for reference |
| timetable_overrides | 2 years | Audit trail for corrections |
| audit_log | 3 years | Compliance, legal |
| device_health | 90 days | Performance monitoring |

---

## Database Capacity Estimates

### SQLite (per center)

Assuming 20 lectures/day:

- **lecture_sessions**: 20 * 365 = 7,300 records/year
- **upload_queue**: transient, ~50 active at peak
- **timetable_entries**: 5 rooms * 10 slots/day * 365 = ~18,250 records/year
- **audit_log**: 3–5 changes per lecture = 100,000 records/year

**Total**: ~130,000 records/year. SQLite can handle millions easily.

### Firestore (cloud-wide)

Assuming 500 centers * 15 lectures/day:

- **lecture_sessions**: 500 * 15 * 365 = 2.7M records/year
- **reviewQueue**: 500 * 15 * 0.05 = 3,750 active records
- **audit_log**: 2.7M * 3 changes = 8.1M entries/year

**Firestore reads/writes**: 10,000–50,000 per day (well within free tier for most days).

---

**Status**: Schema Phase  
**Last Updated**: 2026-09-02  
**Version**: 1.0.0-alpha
