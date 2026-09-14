# Centrix — Autonomous PW Lecture Operations & Storage Sync

A robust edge-first platform for automated lecture recording detection, smart PDF/Video pairing, timetable-based routing, and 1-tap review across PhysicsWallah Vidyapeeth centers.

## Project Vision

**Maximum automation. Minimum human effort. Minimum infrastructure cost.**

The system automatically:
- Detects recorded lectures
- Identifies center → room → batch → subject
- Uploads videos/PDFs to Google Drive
- Routes ambiguous cases to human reviewers
- Handles timetable corrections retroactively
- Detects missing recordings
- Operates offline-first at each center

## Key Metrics

- **Scale**: 500+ centers
- **Daily Volume**: 10,000–20,000 video lectures
- **Architecture**: Edge-first + serverless
- **Storage**: Google Drive (no central server)
- **Cost Model**: Minimum recurring cloud cost
- **Deployment**: Windows agent + Next.js PWA

## Technology Stack

### Center Agent (Windows, macOS, Linux)
- **Language**: C# / .NET 8
- **Database**: SQLite (local)
- **Architecture**: Cross-platform background service
- **Storage**: Google Drive API (direct upload)

### Cloud Backend
- **Primary**: Firebase (Auth, Firestore, FCM, Hosting)
- **Storage**: Google Drive (videos & PDFs)
- **Functions**: Cloud Functions (lightweight only)

### Web & Review
- **Frontend**: Next.js + TypeScript
- **Styling**: Tailwind CSS
- **PWA**: Full offline support
- **Targets**: Phone, tablet, laptop

## System Architecture at a Glance

```
ORGANIZATION
├── CENTERS (500+)
│   ├── ROOMS (multiple per center)
│   │   ├── Recording Devices
│   │   └── Timetable Schedules
│   └── Local Processing
│       ├── File Watcher
│       ├── SQLite Cache
│       ├── Smart Matcher
│       └── Google Drive Uploader
│
CLOUD (Lightweight Metadata Only)
├── Firebase Auth
├── Firestore (timetable, sessions, reviews)
├── FCM (notifications)
└── Cloud Functions (coordination only)
│
WEB DASHBOARD
├── Admin Dashboard
├── Review Queue
├── Center Management
└── Audit Logs
```

## Key Design Principles

1. **Edge-First**: Heavy processing at the center, cloud only for coordination
2. **Offline-First**: Center continues recording/processing without internet
3. **No Video Routing**: Files go directly from center → Google Drive, never through our cloud
4. **Deterministic Matching**: Rule-based before AI, confidence scoring always
5. **Human-in-Loop**: Automate first, human only when uncertain
6. **Audit Everything**: Every assignment change is tracked and reversible
7. **No Immutable Timetable**: Corrections can arrive after lectures are recorded

## Documentation Structure

- [ARCHITECTURE.md](./ARCHITECTURE.md) — Detailed system design
- [DATABASE_SCHEMA.md](./DATABASE_SCHEMA.md) — SQLite & Firestore schema
- [API_CONTRACTS.md](./API_CONTRACTS.md) — Cloud & center agent APIs
- [STATE_MACHINES.md](./STATE_MACHINES.md) — LectureSession lifecycle
- [MATCHING_ENGINE_SPEC.md](./MATCHING_ENGINE_SPEC.md) — Confidence scoring algorithm
- [SECURITY_MODEL.md](./SECURITY_MODEL.md) — Auth, roles, data isolation
- [DEPLOYMENT_PLAN.md](./DEPLOYMENT_PLAN.md) — Rollout strategy
- [COST_CONTROL.md](./COST_CONTROL.md) — Cost optimization
- [PROJECT_STRUCTURE.md](./PROJECT_STRUCTURE.md) — Folder hierarchy

## Build Phases

### Phase 1: Core Windows Agent
- File watcher + lecture detection
- SQLite persistence
- Basic timetable matching
- Local review popup
- Local dashboard

### Phase 2: Google Drive Integration
- OAuth setup
- Resumable uploads
- Folder mapping
- Duplicate detection
- Retry logic

### Phase 3: Smart Matching
- Confidence scoring
- Rule engine
- Ambiguity detection
- Extended lecture handling

### Phase 4: Cloud Sync
- Firebase authentication
- Firestore sync
- Review queue
- Notifications

### Phase 5: PWA Review Dashboard
- Phone & laptop review UI
- Real-time assignment updates
- Multi-reviewer concurrency

### Phase 6: Advanced Features
- Timetable overrides
- Automated correction/move
- Audit logging
- Missing lecture detection
- Device health monitoring
- Auto-update mechanism

### Phase 7: Scale Testing
- 500 center simulation
- 10,000–20,000 lectures/day
- Concurrent uploads
- Cloud outage recovery

## Current Code Capabilities

- `POST /api/devices/register` registers a center device and records its heartbeat.
- `POST /api/devices/{deviceId}/heartbeat` updates device availability.
- `GET /api/timetable` and `POST /api/timetable` manage local timetable data.
- `POST /api/timetable/overrides` stores timetable corrections; `GET /api/timetable/overrides` lists them and `POST /api/timetable/overrides/{id}/deactivate` undoes one.
- `POST /api/lectures/{id}/rematch` re-runs the matching engine for a lecture.
- `POST /api/control/uploads/pause|resume`, `POST /api/control/uploads/{id}/retry|cancel` and `POST /api/control/uploads/retry-failed` control the upload queue.
- `POST /api/control/monitoring/pause|resume` and `POST /api/control/monitoring/rescan` control file detection.
- `POST /api/control/timetable/sync` forces a Google Sheet sync; `POST /api/control/timetable/sync/pause|resume` toggles the background sync.
- `GET /api/agent/info` returns agent identity, config and LAN URLs for the dashboard.
- `Auth:Enabled` + `Auth:ApiKey` protect every `/api` route (except `/api/health`) via the `X-Agent-Key` header.
- `scripts/build-web.sh` builds the React dashboard and copies it into the agent's wwwroot.
- `scripts/publish-cross-platform.sh` publishes self-contained builds for Windows, macOS, and Linux with LAN access and a generated dashboard key.
- `scripts/smoke-test.sh https://localhost:5201` checks the running health endpoint.

## Control From Your Phone or Desktop

1. Start the agent with the run script (enables LAN access + generates an API key):

   ```bash
   ./scripts/run-agent.sh "/Users/Extra" "LectureRecordings"
   ```

2. The terminal prints the dashboard URLs and the API key, e.g.:

   ```text
   Open on phone/tablet (same WiFi):
     http://192.168.1.20:5200/
   Dashboard API key: 3fa1...c9
   ```

3. Open the URL on any phone, tablet or laptop on the same WiFi, paste the key once (stored in that browser only), and you get the full dashboard: live uploads, review/approve, timetable editing, and the **Controls** tab (pause/resume uploads, rescan folder, retry failed uploads, sync timetable).

Every restart of `run-agent.sh` generates a fresh key unless `AGENT_API_KEY` is set in the environment.

## Quick Start

```bash
# Clone and initialize
git clone <repo>
cd lecture-automation-system

# Phase 1: Cross-platform Agent Development
cd agent
dotnet new solution -n LectureAgent
dotnet sln add <projects>

# Phase 2: Firebase Setup
cd ../cloud
npm install firebase-admin

# Phase 3: PWA Dashboard
cd ../web
npm create next-app@latest
```

## Select A Computer Folder

Set the absolute local folder in `src/LectureAgent/appsettings.json`:

```json
"FileWatcher": {
	"MonitorFolder": "/Users/your-name/Lectures",
	"EnableFileWatcher": true
},
"GoogleDrive": {
	"Enabled": true,
	"RootFolderPath": "LectureRecordings/Center-001"
}
```

On Windows use a path such as `D:/Lectures`; on Linux use `/home/user/Lectures`.
Place a valid video or a PDF of at least 50 KB in that folder. The agent detects it, creates a local lecture record, and queues it for the configured Drive folder. Real uploads require `config/google_credentials.json`; with `GoogleDrive:Enabled` set to `false`, local mock mode remains available.

## Critical Success Factors

✅ **No central video bottleneck** — Direct Google Drive uploads from centers  
✅ **Offline resilience** — Local SQLite queues survive network outages  
✅ **Confidence-based automation** — Only auto-process high-confidence matches  
✅ **Reversible assignments** — Audit trail enables correction without data loss  
✅ **Role-based access** — Centers see only their data  
✅ **Cost efficiency** — Minimal recurring cloud spend

## Contacts & Roles

- **Project Lead**: [To be assigned]
- **Lead Architect**: [To be assigned]
- **Backend Lead**: [To be assigned]
- **Frontend Lead**: [To be assigned]

---

**Status**: Phase 1 working prototype; Phases 2-7 in progress  
**Last Updated**: 2026-09-02  
**Version**: 1.0.0-alpha
