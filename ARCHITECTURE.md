# System Architecture

## 1. Architectural Overview

The Lecture Automation & Smart Review System (LASRS) follows an **edge-first + serverless** architecture:

- **Heavy lifting**: Local at each center (file processing, matching, queuing)
- **Coordination**: Cloud-based metadata only (Firebase)
- **Storage**: Google Drive (no central storage)
- **Review**: Distributed reviewers via responsive PWA

### High-Level Data Flow

```
Recording Device
    ↓
[FILE WATCHER] → Detects new MKV/MP4/PDF
    ↓
[SMART MATCHER] → Auto-matches against timetable
    ↓
HIGH CONFIDENCE? 
    ├─ YES → [UPLOAD QUEUE] → Google Drive (direct)
    └─ NO → [REVIEW QUEUE] + Firestore notification
    ↓
[GOOGLE DRIVE] → Permanent storage
    ↓
[REVIEWER] (phone/laptop) → Confirms/corrects
    ↓
[AUDIT LOG] → Complete history
```

## 2. Center-Side Architecture (Windows Agent)

### 2.1 Core Components

#### File Watcher
- Monitors configurable recording directories
- Detects: MKV, MP4, MOV, AVI, PDF
- Waits for file stability (size stable, handle released)
- Configurable stability period (default 10s)
- Prevents processing incomplete recordings

**Implementation**: FileSystemWatcher (Windows API) + throttling

#### Lecture Session Manager
- Creates LectureSession for each recording
- Lifecycle management
- State transitions

#### Timetable Manager
- Caches organization's timetable
- Detects timetable changes from Firestore
- Applies overrides (cancellations, interchanges, corrections)
- Efficient querying by: center, room, date, time slot

#### Smart Matching Engine
- Deterministic rule-based matching
- Confidence scoring (0–100%)
- Signals: room, time, batch, subject, teacher, duration, patterns
- Output: confidence level + reasoning
- Decides: AUTO_ASSIGN, REVIEW_REQUIRED, NO_MATCH

#### Upload Queue
- SQLite-backed durable queue
- Resumable uploads to Google Drive
- Retry with exponential backoff
- Duplicate detection
- Survives app/Windows restart

#### Local Review UI
- Desktop popup for ambiguous cases
- Shows suggested batch/subject
- Allows manual correction
- Modal (cannot dismiss via Escape/X)
- Required action: CONFIRM or change

#### Local Dashboard
- Today's summary
- Processing queue
- Upload status
- Missing lectures
- Local errors

### 2.2 Local SQLite Database

Stores:
- LectureSession records
- Timetable cache (synced from Firestore)
- Upload queue
- Device configuration
- Processing logs
- Audit trail (local mirror)

Structure: See [DATABASE_SCHEMA.md](./DATABASE_SCHEMA.md)

### 2.3 Google Drive Integration

- Direct upload from center → Google Drive
- Resumable uploads (survives interruptions)
- Folder structure: configurable hierarchy
  - Example: `Center-001/LJ151MA/Physics/`
- Duplicate prevention via:
  - File IDs
  - Checksums
  - Drive file searches
- Error handling: retry, backoff, notification

## 3. Cloud Architecture (Firebase)

### 3.1 Firebase Services

#### Authentication
- User registration/login
- Multi-center user support
- Role-based permissions
- Token refresh

#### Firestore
Lightweight metadata:
- Organization, Centers, Rooms, Devices
- Batches, Subjects, Teachers
- Timetable (synced to centers)
- Timetable Overrides
- LectureSession metadata
- Review queue items
- Device heartbeats
- Audit logs

#### Firebase Cloud Messaging (FCM)
- Notifications to reviewers
- Device offline alerts
- Upload failures
- Missing lecture alerts
- Timetable correction notifications

#### Firebase Hosting
- PWA static hosting
- Fast CDN delivery
- Automatic SSL

#### Cloud Functions
**Minimal use** — only for:
- Firestore triggers (e.g., timetable override → notify center)
- Scheduled tasks (e.g., garbage collection)
- Complex server-side logic (keep at center where possible)

### 3.2 Firestore Collections

```
organizations/
  {orgId}/
    ├── centers/
    │   {centerId}/
    │   ├── rooms/
    │   │   {roomId}/
    │   │   ├── devices/
    │   │   │   {deviceId}/ (metadata only)
    │   └── timetable/
    │       {date}/
    │       {slot}/
    │   └── overrides/
    │       {overrideId}/
    ├── lectures/
    │   {lectureSessionId}/
    │   ├── metadata
    │   ├── confidence
    │   ├── assignment
    │   └── status
    ├── reviewQueue/
    │   {lectureSessionId}/
    ├── users/
    │   {userId}/
    │   ├── roles
    │   └── authorizedCenters
    └── auditLog/
        {logId}/
```

## 4. Web Architecture (Next.js PWA)

### 4.1 Pages & Routes

```
/dashboard
  ├── /center-admin
  ├── /center/{centerId}
  ├── /room/{roomId}
  └── /device/{deviceId}

/review
  ├── /queue
  ├── /lecture/{lectureSessionId}
  └── /missing

/admin
  ├── /centers
  ├── /users
  ├── /timetable
  └── /audit-logs

/settings
  ├── /profile
  ├── /preferences
  └── /notifications

/health
  ├── /devices
  ├── /upload-status
  └── /system-overview
```

### 4.2 PWA Features

- Offline queue storage
- Service worker caching
- Install prompt
- Works on: phone (iOS/Android), tablet, laptop
- Responsive design (Tailwind CSS)

### 4.3 Real-Time Updates

- Firestore listeners for live review queue
- FCM for notifications
- WebSocket fallback (optional)

## 5. Multi-Center Data Isolation

### 5.1 Security Model

Every entity has a stable ID:
- `organizationId` — org root
- `centerId` — physical center
- `roomId` — classroom/auditorium
- `deviceId` — recording device
- `lectureSessionId` — unique lecture
- `userId` — reviewer

### 5.2 Access Control

Rules (Firebase security):
- User has roles: SUPER_ADMIN, CENTER_ADMIN, REVIEWER, ROOM_OPERATOR
- CENTER_ADMIN → sees only assigned centers
- REVIEWER → sees only assigned centers
- SUPER_ADMIN → sees all

Example Firestore security rule:
```javascript
match /organizations/{orgId}/centers/{centerId}/lectures/{lectureId} {
  allow read: if request.auth.uid != null && 
    (userHasRole('SUPER_ADMIN') || 
     userHasRole('CENTER_ADMIN', centerId) ||
     userHasRole('REVIEWER', centerId))
  allow update: if userHasRole('REVIEWER', centerId) || 
                  userHasRole('CENTER_ADMIN', centerId)
}
```

### 5.3 Offline-First Review

- Review metadata cached locally
- Submit changes when online
- Conflict resolution: last-write-wins + audit trail

## 6. Upload Architecture

### 6.1 Direct Upload Flow

```
Center PC
    ↓
[UPLOAD QUEUE] ← SQLite-backed, durable
    ↓
[GOOGLE DRIVE API] ← Resumable upload
    ↓
[VERIFICATION] ← Hash check
    ↓
[METADATA UPDATE] → Firestore: uploadStatus = UPLOADED
    ↓
[GOOGLE DRIVE FOLDER] ← File now in correct location
```

### 6.2 Upload Queue Resilience

- Persisted to SQLite (survives restart)
- Exponential backoff (1s, 2s, 4s, 8s, 30s max)
- Max retries: configurable (default 5)
- Resume interrupted uploads (position tracking)
- Duplicate detection before re-upload
- Concurrency: configurable (default 2 simultaneous)

### 6.3 Network Failure Recovery

**Scenario**: Upload interrupted at 60% due to network loss
- Queue entry remains with `bytesUploaded: 60%`
- On reconnect, resume from byte 60%
- Google Drive resumable session: 7-day expiry
- If expired, restart upload (detect via hash)

## 7. Offline-First Operation

### 7.1 Center Operates Offline

While internet unavailable:
- ✅ File watcher continues
- ✅ Lectures detected and created
- ✅ Matching engine runs
- ✅ Review UI works (local data)
- ✅ SQLite queue grows
- ✅ Timetable is locally cached
- ❌ Cannot sync new timetables
- ❌ Cannot upload
- ❌ Cannot notify reviewers

### 7.2 Recovery

When internet returns:
- Upload queue begins processing
- Timetable re-syncs from Firestore
- Notifications queued while offline are sent
- No data loss

### 7.3 State Management

Firestore acts as source-of-truth, local SQLite as cache:
- On startup: fetch Firestore timetable → SQLite
- Periodically: sync Firestore → SQLite
- During offline: use SQLite
- On reconnect: merge & conflict-resolve

## 8. Scalability Design

### 8.1 Database Indexing

SQLite indexes:
```sql
CREATE INDEX idx_lecture_session_center_date ON lecture_sessions(center_id, session_date);
CREATE INDEX idx_lecture_session_status ON lecture_sessions(status);
CREATE INDEX idx_upload_queue_status ON upload_queue(status, retry_count);
```

Firestore indexes:
- `centerId` + `date`
- `roomId` + `date` + `status`
- `status` + `createdAt` (for review queue)

### 8.2 Pagination

All list endpoints:
- Default page size: 50
- Max page size: 1000
- Cursor-based (not offset)

Example:
```
GET /api/lectures?centerId=C001&date=2026-09-02&limit=50&cursor=abc123
```

### 8.3 Load Testing Targets

Phase 7 will simulate:
- 500 centers online simultaneously
- 10,000–20,000 lectures/day
- Peak: 50 concurrent uploads
- Firestore: 10,000 writes/min
- FCM: 1,000 notifications/min

## 9. Failure Scenarios & Recovery

### Scenario 1: Internet Lost During Upload
- Upload queue keeps retrying
- On reconnect, resume via resumable session
- No data loss

### Scenario 2: PC Restart During Upload
- Restart → app reads SQLite queue
- Detects incomplete upload
- Resume from last known position
- Verifies with hash

### Scenario 3: Google Drive API Throttled
- Exponential backoff
- Wait 30s, retry
- Continue next file
- Notify admin if persistent

### Scenario 4: PDF Arrives After Video
- LectureSession already created with video
- File watcher detects PDF
- System searches for matching LectureSession by:
  - Room, date, time proximity
  - Associated device
- Updates LectureSession: `pdfFileId = <new PDF>`
- Adds PDF to upload queue

### Scenario 5: Timetable Corrected After Lecture Upload
- Center gets new timetable override
- Firestore notifies center
- System detects mismatch:
  - Old assignment: LJ151MA / Physics
  - New assignment: LJ153EA / Chemistry
- Marks as: CORRECTION_REQUIRED
- Queues for re-upload to correct Drive folder
- Records in audit log
- Notifies reviewer if already reviewed

### Scenario 6: Two Reviewers Editing Same Lecture
- Reviewer A locks: `status = LOCKED, lockedBy = userA`
- Reviewer B attempts edit: rejected with "Already locked by Rahul"
- Timeout: auto-release lock after 5 minutes
- Last-write-wins + audit trail for conflict resolution

## 10. Technology Decisions

### Why C# .NET 8?
- Windows Services (background agent)
- Strong typing
- Async/await for file watcher
- Entity Framework for SQLite
- Google Drive SDK

### Why SQLite?
- No separate database server
- Zero setup (file-based)
- ACID transactions
- Adequate for 10,000–20,000 records/center
- Reliable for durable queue

### Why Firebase?
- Managed auth
- Firestore for lightweight metadata
- FCM for notifications
- Fast setup
- Low fixed cost

### Why Google Drive?
- Meets spec requirement
- Users already have accounts
- Direct uploads from center
- Minimal central storage cost

### Why Next.js PWA?
- Server-side rendering for fast loads
- Static export capability
- Offline support via service workers
- TypeScript for type safety
- Tailwind for rapid UI development

## 11. API Overview

### Center Agent APIs

#### POST /api/lectures
Create a LectureSession after file detection.

#### PUT /api/lectures/{lectureSessionId}/confirm
Confirm assignment after review.

#### POST /api/uploads
Add to upload queue.

#### GET /api/timetable
Fetch latest timetable (cached locally).

#### POST /api/heartbeat
Device health report.

### Cloud APIs

#### POST /auth/login
User authentication.

#### GET /api/review-queue
List lectures pending review.

#### PUT /api/lectures/{lectureSessionId}
Update lecture metadata (batch, subject, status).

#### POST /api/audit-log
Append audit entry.

#### GET /api/health/devices
Device status dashboard.

Full API contracts: See [API_CONTRACTS.md](./API_CONTRACTS.md)

## 12. Security & Compliance

### Principles
- Secure OAuth for Google Drive
- JWT tokens (short-lived)
- Refresh tokens secure storage
- No hardcoded credentials
- All actions audited
- TLS/HTTPS everywhere
- Center data isolation

Details: See [SECURITY_MODEL.md](./SECURITY_MODEL.md)

---

**Status**: Architecture Phase  
**Last Updated**: 2026-09-02  
**Version**: 1.0.0-alpha
