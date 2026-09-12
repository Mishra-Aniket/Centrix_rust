# API Contracts

Two sets of APIs: **Center Agent APIs** (internal/local) and **Cloud APIs** (Firebase-based).

---

## Part 1: Center Agent APIs

### Overview

The Windows agent exposes local REST APIs for:
- File processing status
- Timetable management
- Local review UI
- Upload queue status
- Health reporting

**Base URL**: `http://localhost:5001` (configurable)

---

### 1. Lecture Sessions

#### POST /api/lectures
Create a new LectureSession after file detection.

**Request**:
```json
{
  "organizationId": "ORG-001",
  "centerId": "C-001",
  "roomId": "R-001",
  "deviceId": "DEV-001",
  "videoFilePath": "/recordings/2026-09-02_09-10-28.mkv",
  "videoFileSize": 2147483648,
  "videoFileHash": "sha256:abc123...",
  "detectedStartTime": "2026-09-02T09:10:28Z",
  "detectedEndTime": "2026-09-02T10:40:35Z",
  "pdfFilePath": null
}
```

**Response**:
```json
{
  "lectureSessionId": "LSN-2026-09-02-0001",
  "status": "DETECTED",
  "createdAt": "2026-09-02T10:40:45Z"
}
```

**HTTP**: `201 Created`

---

#### GET /api/lectures/{lectureSessionId}
Get lecture session details.

**Response**:
```json
{
  "lectureSessionId": "LSN-2026-09-02-0001",
  "organizationId": "ORG-001",
  "centerId": "C-001",
  "roomId": "R-001",
  "deviceId": "DEV-001",
  "detectedStartTime": "2026-09-02T09:10:28Z",
  "detectedEndTime": "2026-09-02T10:40:35Z",
  "detectedDurationSeconds": 5407,
  "batchId": "LJ151MA",
  "subjectId": "Physics",
  "teacherId": "T-001",
  "assignmentSource": "AUTO",
  "confidenceScore": 94,
  "status": "UPLOADING",
  "reviewStatus": "APPROVED",
  "driveVideoFileId": "gdriveId123",
  "driveFolderPath": "/Center-001/LJ151MA/Physics",
  "uploadStatus": "IN_PROGRESS",
  "uploadProgress": 75,
  "createdAt": "2026-09-02T10:40:45Z",
  "updatedAt": "2026-09-02T10:50:00Z"
}
```

---

#### PUT /api/lectures/{lectureSessionId}/confirm
Confirm assignment (after local review popup).

**Request**:
```json
{
  "batchId": "LJ153EA",
  "subjectId": "Chemistry",
  "teacherId": "T-002",
  "action": "CONFIRM",
  "reviewedBy": "room_operator_001"
}
```

**Response**:
```json
{
  "lectureSessionId": "LSN-2026-09-02-0001",
  "status": "CONFIRMED",
  "updatedAt": "2026-09-02T10:45:00Z"
}
```

---

#### GET /api/lectures?status=UPLOADING&limit=50
List lectures by status.

**Query Params**:
- `status`: DETECTED, PROCESSING, AUTO_ASSIGNED, REVIEW_REQUIRED, CONFIRMED, UPLOADING, UPLOADED, etc.
- `limit`: 50 (default)
- `offset`: 0 (pagination)
- `date`: ISO date (e.g., 2026-09-02)

**Response**:
```json
{
  "total": 152,
  "count": 50,
  "offset": 0,
  "lectures": [
    {
      "lectureSessionId": "LSN-...",
      "status": "UPLOADING",
      "batchId": "LJ151MA",
      "confidenceScore": 94,
      "uploadProgress": 45
    }
  ]
}
```

---

### 2. Matching & Review

#### POST /api/matching/analyze
Analyze a detected file and return matching suggestions.

**Request**:
```json
{
  "lectureSessionId": "LSN-2026-09-02-0001"
}
```

**Response**:
```json
{
  "lectureSessionId": "LSN-2026-09-02-0001",
  "matchedSlot": {
    "slotId": "C001-R001-09-00-10-30",
    "batchId": "LJ151MA",
    "subjectId": "Physics",
    "teacherId": "T-001",
    "scheduledStartTime": "2026-09-02T09:00:00Z",
    "scheduledEndTime": "2026-09-02T10:30:00Z"
  },
  "confidence": {
    "overallScore": 94,
    "decision": "AUTO_ASSIGNED",
    "scoringDetails": { /* detailed breakdown */ },
    "reasoning": {
      "main": "Perfect room and time match...",
      "positives": ["Room matched", "Time overlap 89%", ...],
      "cautions": ["Started 10 minutes late"]
    }
  },
  "candidates": [
    {
      "slotId": "C001-R001-09-00-10-30",
      "score": 94,
      "batchId": "LJ151MA"
    }
  ]
}
```

---

#### GET /api/review-queue
Get local review queue (low confidence lectures).

**Response**:
```json
{
  "total": 3,
  "reviews": [
    {
      "lectureSessionId": "LSN-2026-09-02-0002",
      "detectedTime": "2026-09-02T14:20:00Z",
      "detectedDuration": "1h 15m",
      "suggestedBatch": "LJ151MA",
      "suggestedSubject": "Physics",
      "confidence": 68,
      "reason": "Duration extended 15 minutes beyond slot"
    }
  ]
}
```

---

### 3. Timetable Management

#### GET /api/timetable?date=2026-09-02&roomId=R-001
Get timetable for a specific date and room.

**Response**:
```json
{
  "date": "2026-09-02",
  "roomId": "R-001",
  "slots": [
    {
      "slotId": "C001-R001-09-00-10-30",
      "startTime": "09:00",
      "endTime": "10:30",
      "batchId": "LJ151MA",
      "subjectId": "Physics",
      "teacherId": "T-001"
    },
    {
      "slotId": "C001-R001-10-30-12-00",
      "startTime": "10:30",
      "endTime": "12:00",
      "batchId": "LJ153EA",
      "subjectId": "Chemistry",
      "teacherId": "T-002"
    }
  ],
  "overrides": [
    {
      "overrideId": "OVR-001",
      "originalSlotId": "C001-R001-10-30-12-00",
      "type": "CANCELLED",
      "effectiveDate": "2026-09-02"
    }
  ]
}
```

---

#### POST /api/timetable/sync
Manually sync timetable from cloud (usually automatic).

**Request**:
```json
{
  "centerId": "C-001"
}
```

**Response**:
```json
{
  "status": "SYNCED",
  "recordsUpdated": 47,
  "lastSyncTime": "2026-09-02T10:50:00Z"
}
```

---

### 4. Upload Queue

#### GET /api/uploads?status=PENDING
Get upload queue status.

**Response**:
```json
{
  "total": 12,
  "uploads": [
    {
      "queueEntryId": "UQ-001",
      "lectureSessionId": "LSN-2026-09-02-0001",
      "fileType": "VIDEO",
      "fileName": "2026-09-02_09-10-28.mkv",
      "fileSize": 2147483648,
      "status": "PENDING",
      "progress": 0,
      "createdAt": "2026-09-02T10:40:45Z"
    },
    {
      "queueEntryId": "UQ-002",
      "lectureSessionId": "LSN-2026-09-02-0002",
      "fileType": "VIDEO",
      "fileName": "2026-09-02_14-20-15.mkv",
      "fileSize": 1610612736,
      "status": "UPLOADING",
      "progress": 65,
      "createdAt": "2026-09-02T14:20:25Z"
    }
  ]
}
```

---

#### POST /api/uploads/retry
Manually retry a failed upload.

**Request**:
```json
{
  "queueEntryId": "UQ-005"
}
```

**Response**:
```json
{
  "queueEntryId": "UQ-005",
  "status": "PENDING",
  "retryCount": 2,
  "message": "Queued for retry"
}
```

---

### 5. Device Health

#### POST /api/heartbeat
Device sends periodic heartbeat to local service (for monitoring).

**Request**:
```json
{
  "deviceId": "DEV-001",
  "appVersion": "1.0.5",
  "diskFreeBytes": 500000000000,
  "diskTotalBytes": 1000000000000,
  "uploadQueueSize": 12,
  "pendingReviews": 3,
  "lastSuccessfulUploadAt": "2026-09-02T10:45:00Z",
  "errorCount": 0
}
```

**Response**:
```json
{
  "status": "ACK",
  "timestamp": "2026-09-02T10:50:01Z"
}
```

---

#### GET /api/device/health
Get local device health summary.

**Response**:
```json
{
  "deviceId": "DEV-001",
  "status": "HEALTHY",
  "diskUsagePercent": 45,
  "diskFreeGB": 465,
  "uploadQueueSize": 12,
  "pendingReviews": 3,
  "recentErrors": [],
  "lastUpload": "2026-09-02T10:45:00Z"
}
```

---

### 6. Configuration

#### GET /api/config
Get local configuration.

**Response**:
```json
{
  "organizationId": "ORG-001",
  "centerId": "C-001",
  "deviceId": "DEV-001",
  "timezone": "Asia/Kolkata",
  "recordingFolder": "/recordings",
  "googleDriveFolder": "/Center-001",
  "matchingConfig": {
    "confidenceHighThreshold": 85,
    "confidenceMediumThreshold": 60,
    "timeToleranceMinutes": 15,
    "durationToleranceMinutes": 10
  },
  "uploadConfig": {
    "maxConcurrentUploads": 2,
    "maxRetries": 5,
    "backoffBaseMs": 1000
  }
}
```

---

#### PUT /api/config
Update configuration (usually controlled by admin, can be overridden locally for testing).

**Request**:
```json
{
  "matchingConfig": {
    "confidenceHighThreshold": 90
  }
}
```

**Response**:
```json
{
  "status": "UPDATED",
  "appliedAt": "2026-09-02T10:50:00Z"
}
```

---

## Part 2: Cloud APIs (Firestore + Firebase)

### Overview

Cloud APIs are RESTful and Firebase-authenticated.

**Base URL**: `https://api.lasrs.example.com` (or Firebase functions domain)

**Authentication**: Firebase JWT token in `Authorization: Bearer <token>` header

---

### 1. Authentication

#### POST /auth/login
User login.

**Request**:
```json
{
  "email": "reviewer@abc.edu",
  "password": "secure_password"
}
```

**Response**:
```json
{
  "idToken": "eyJhbGciOiJSUzI1NiIs...",
  "refreshToken": "AEwZ8jJ...",
  "userId": "U-001",
  "displayName": "Rahul Kumar",
  "roles": ["REVIEWER"],
  "authorizedCenters": ["C-001", "C-002"],
  "expiresIn": 3600
}
```

---

#### POST /auth/refresh
Refresh token.

**Request**:
```json
{
  "refreshToken": "AEwZ8jJ..."
}
```

**Response**:
```json
{
  "idToken": "eyJhbGciOiJSUzI1NiIs...",
  "expiresIn": 3600
}
```

---

### 2. Review Queue

#### GET /api/review-queue?centerId=C-001&limit=50
Get review queue for a center.

**Response**:
```json
{
  "total": 127,
  "count": 50,
  "cursor": "next_cursor_id",
  "items": [
    {
      "lectureSessionId": "LSN-2026-09-02-0002",
      "centerId": "C-001",
      "roomId": "R-001",
      "detectedStartTime": "2026-09-02T14:20:00Z",
      "detectedDurationSeconds": 4500,
      "suggestedBatchId": "LJ151MA",
      "suggestedSubjectId": "Physics",
      "confidenceScore": 68,
      "status": "PENDING",
      "createdAt": "2026-09-02T14:20:30Z",
      "matchingReason": {
        "roomMatched": true,
        "timeOverlapped": true,
        "durationExtended": 15
      }
    }
  ]
}
```

---

#### GET /api/lectures/{lectureSessionId}
Get full lecture details.

**Response**:
```json
{
  "lectureSessionId": "LSN-2026-09-02-0001",
  "organizationId": "ORG-001",
  "centerId": "C-001",
  "roomId": "R-001",
  "deviceId": "DEV-001",
  "detectedStartTime": "2026-09-02T09:10:28Z",
  "detectedEndTime": "2026-09-02T10:40:35Z",
  "scheduledStartTime": "2026-09-02T09:00:00Z",
  "scheduledEndTime": "2026-09-02T10:30:00Z",
  "batchId": "LJ151MA",
  "subjectId": "Physics",
  "teacherId": "T-001",
  "assignmentSource": "AUTO",
  "confidenceScore": 94,
  "status": "VERIFIED",
  "reviewStatus": "APPROVED",
  "reviewedBy": "U-001",
  "reviewedAt": "2026-09-02T11:00:00Z",
  "driveVideoFileId": "gdriveId123",
  "drivePdfFileId": null,
  "driveFolderPath": "/Center-001/LJ151MA/Physics",
  "uploadStatus": "VERIFIED",
  "uploadedAt": "2026-09-02T10:55:00Z",
  "createdAt": "2026-09-02T10:40:45Z",
  "updatedAt": "2026-09-02T10:55:00Z"
}
```

---

#### PUT /api/lectures/{lectureSessionId}
Update lecture assignment (by reviewer).

**Request**:
```json
{
  "batchId": "LJ153EA",
  "subjectId": "Chemistry",
  "teacherId": "T-002",
  "action": "CONFIRM",
  "notes": "Reviewer corrected from Physics to Chemistry"
}
```

**Response**:
```json
{
  "lectureSessionId": "LSN-2026-09-02-0001",
  "status": "CONFIRMED",
  "batchId": "LJ153EA",
  "subjectId": "Chemistry",
  "updatedAt": "2026-09-02T11:00:00Z",
  "auditLogId": "AUD-001"
}
```

---

#### PUT /api/lectures/{lectureSessionId}/lock
Claim/lock a lecture for review.

**Request**:
```json
{
  "userId": "U-001",
  "timeoutMinutes": 5
}
```

**Response**:
```json
{
  "lectureSessionId": "LSN-2026-09-02-0001",
  "lockedBy": "U-001",
  "lockedAt": "2026-09-02T11:00:00Z",
  "lockTimeoutAt": "2026-09-02T11:05:00Z"
}
```

---

#### DELETE /api/lectures/{lectureSessionId}/lock
Release lock.

**Response**:
```json
{
  "lectureSessionId": "LSN-2026-09-02-0001",
  "unlockedAt": "2026-09-02T11:02:00Z"
}
```

---

### 3. Batch & Subject Data

#### GET /api/batches?centerId=C-001
Get all authorized batches for a center.

**Response**:
```json
{
  "batches": [
    {
      "batchId": "LJ151MA",
      "name": "LJ151MA",
      "organizationId": "ORG-001",
      "studentsCount": 120
    },
    {
      "batchId": "LJ153EA",
      "name": "LJ153EA",
      "organizationId": "ORG-001",
      "studentsCount": 95
    }
  ]
}
```

---

#### GET /api/subjects?batchId=LJ151MA
Get subjects for a batch.

**Response**:
```json
{
  "batchId": "LJ151MA",
  "subjects": [
    {
      "subjectId": "Physics",
      "name": "Physics (Core)",
      "credit": 4
    },
    {
      "subjectId": "Chemistry",
      "name": "Chemistry (Core)",
      "credit": 4
    }
  ]
}
```

---

### 4. Audit Log

#### GET /api/audit-log?centerId=C-001&limit=100
Get audit log entries.

**Query Params**:
- `centerId`: required
- `entityType`: LECTURE_SESSION, UPLOAD, etc. (optional)
- `entityId`: specific entity (optional)
- `limit`: 100 (default)
- `cursor`: pagination

**Response**:
```json
{
  "total": 2450,
  "count": 100,
  "cursor": "next_cursor_id",
  "entries": [
    {
      "auditEntryId": "AUD-001",
      "timestamp": "2026-09-02T11:00:00Z",
      "entityType": "LECTURE_SESSION",
      "entityId": "LSN-2026-09-02-0001",
      "actionType": "CONFIRMED",
      "userId": "U-001",
      "userRole": "REVIEWER",
      "changeSummary": "Changed batch from LJ151MA to LJ153EA",
      "oldValues": {
        "batchId": "LJ151MA",
        "subjectId": "Physics"
      },
      "newValues": {
        "batchId": "LJ153EA",
        "subjectId": "Chemistry"
      }
    }
  ]
}
```

---

### 5. Dashboard & Analytics

#### GET /api/dashboard?centerId=C-001&date=2026-09-02
Get daily dashboard summary.

**Response**:
```json
{
  "date": "2026-09-02",
  "centerId": "C-001",
  "summary": {
    "totalLectures": 18,
    "autoProcessed": 16,
    "reviewRequired": 2,
    "missingRecordings": 1,
    "pdfPending": 2,
    "uploadFailed": 0,
    "extraLectures": 1
  },
  "byStatus": {
    "VERIFIED": 15,
    "UPLOADING": 1,
    "REVIEW_REQUIRED": 2
  }
}
```

---

#### GET /api/health/devices?centerId=C-001
Get device health for a center.

**Response**:
```json
{
  "centerId": "C-001",
  "devices": [
    {
      "deviceId": "DEV-001",
      "roomId": "R-001",
      "onlineStatus": "ONLINE",
      "lastHeartbeat": "2026-09-02T10:50:01Z",
      "diskUsagePercent": 45,
      "appVersion": "1.0.5",
      "uploadQueueSize": 12
    }
  ]
}
```

---

### 6. Missing Lectures Detection

#### GET /api/missing-lectures?centerId=C-001&date=2026-09-02
Get missing lecture alerts.

**Response**:
```json
{
  "centerId": "C-001",
  "date": "2026-09-02",
  "missing": [
    {
      "slotId": "C001-R001-10-30-12-00",
      "roomId": "R-001",
      "startTime": "10:30",
      "endTime": "12:00",
      "batchId": "LJ153EA",
      "subjectId": "Chemistry",
      "teacherId": "T-002",
      "status": "MISSING_VIDEO",
      "reportedAt": "2026-09-02T12:45:00Z",
      "gracePeriodExpiresAt": "2026-09-02T13:00:00Z"
    }
  ]
}
```

---

### 7. Timetable Overrides

#### POST /api/timetable/overrides
Create a timetable override.

**Request**:
```json
{
  "organizationId": "ORG-001",
  "centerId": "C-001",
  "type": "CANCELLED",
  "originalSlotId": "C001-R001-10-30-12-00",
  "effectiveDate": "2026-09-02"
}
```

**Response**:
```json
{
  "overrideId": "OVR-001",
  "status": "ACTIVE",
  "appliedLectures": 0,
  "createdAt": "2026-09-02T10:00:00Z"
}
```

---

### 8. System Health (Admin Only)

#### GET /api/admin/system-health
System-wide health overview.

**Response**:
```json
{
  "centers": {
    "total": 500,
    "online": 493,
    "offline": 7
  },
  "todayRecordings": 18742,
  "autoProcessed": 17981,
  "reviewRequired": 421,
  "uploadFailed": 26,
  "missing": 114,
  "pdfPending": 200,
  "timestamp": "2026-09-02T10:50:00Z"
}
```

---

## Error Response Format

All APIs return consistent error format:

```json
{
  "error": {
    "code": "INVALID_REQUEST",
    "message": "Confidence score must be 0-100",
    "details": {
      "field": "confidenceScore",
      "received": 150,
      "expected": "0-100"
    },
    "timestamp": "2026-09-02T10:50:00Z"
  }
}
```

**HTTP Status Codes**:
- `200`: Success
- `201`: Created
- `400`: Bad request
- `401`: Unauthorized
- `403`: Forbidden
- `404`: Not found
- `409`: Conflict (e.g., already locked)
- `429`: Rate limited
- `500`: Server error
- `503`: Service unavailable

---

**Status**: API Contracts Phase  
**Last Updated**: 2026-09-02  
**Version**: 1.0.0-alpha
