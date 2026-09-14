# Centrix — Complete Guide

**What this document is:** the single, plain-English guide to the whole system — what it does, how it works internally, how to set it up, and how to use it every day. Everything else in this folder is deeper design documentation for developers; this file is the one to read first.

**Language note:** the entire product — dashboard UI, desktop app, installer, agent logs, and all documentation — is in English only. No Hindi text is used anywhere in the app.

---

## Table of Contents

1. [What is Centrix?](#1-what-is-centrix)
2. [What it does — the daily job](#2-what-it-does--the-daily-job)
3. [How it works — the full pipeline](#3-how-it-works--the-full-pipeline)
4. [How the matching engine decides](#4-how-the-matching-engine-decides)
5. [The web dashboard — screen by screen](#5-the-web-dashboard--screen-by-screen)
6. [How to set it up](#6-how-to-set-it-up)
7. [How to use it day to day](#7-how-to-use-it-day-to-day)
8. [Configuration reference](#8-configuration-reference)
9. [Troubleshooting and FAQ](#9-troubleshooting-and-faq)
10. [Security and data rules](#10-security-and-data-rules)
11. [Where to learn more](#11-where-to-learn-more)

---

## 1. What is Centrix?

Centrix is the PW Classroom Edge Engine & Drive Pipeline software for educational centers that record their classes. It runs on the center's own PC and does one job end to end:

> **It watches the folder where class recordings appear, figures out which batch, subject, and teacher each recording belongs to, uploads it to the center's Google Drive in the right folder, and asks a human only when it is not sure.**

It is designed for 500+ centers handling 10,000–20,000 lecture recordings per day, with three goals:

- **Maximum automation** — most lectures are handled with zero human work.
- **Minimum human effort** — staff only review the few genuinely ambiguous files (2–5 minutes each).
- **Minimum cloud cost** — videos go **directly** from the center PC to Google Drive; they never pass through any central server, so video bandwidth costs nothing.

---

## 2. What it does — the daily job

Here is the life of one lecture recording, from camera to Drive:

1. **A class is recorded.** The recording software saves a video (`.mkv`, `.mp4`, `.mov`, `.avi`, `.webm`) or a PDF into the center PC's recordings folder.
2. **The agent notices it** within seconds of the file becoming stable (finished writing).
3. **The agent validates it** — correct file type, minimum size (1 MB for video, 50 KB for PDF), not a leftover temp file.
4. **The agent matches it** against the center's timetable: *which room was this PC in, what slot was running at that time, which batch, which subject, which teacher?* It also reads the file name for hints (batch codes, subject keywords).
5. **The agent decides:**
   - Confidence **≥ 85%** → auto-assign and queue the upload. No human involved.
   - Confidence **60–84%** → assign a *suggested* batch/subject and put it in the **Review queue**.
   - Below 60%, or no timetable slot fits → put it in the Review queue with no suggestion.
6. **The file is uploaded** straight from the center PC to Google Drive using a resumable upload, with automatic retries and duplicate detection. The target folder follows the pattern:
   `LectureRecordings/<centerId>/<batchId>/<subjectId>/`
7. **Everything is recorded** — status, confidence score, why it was assigned, every change — in a local SQLite database and an audit trail, so any decision can be traced and reversed.
8. **If the internet is down**, nothing is lost: uploads and metadata wait in the local queue and sync automatically when the connection returns.

The only human step in the normal case is **step 5's review queue**, and only for the files the engine was not sure about.

---

## 3. How it works — the full pipeline

### 3.1 The three moving parts

| Part | What it is | Where it runs |
|---|---|---|
| **Center Agent** | A C# / .NET background service — the brain and the muscle. Watches files, matches, uploads, serves the dashboard. | The center's PC (Windows, macOS, or Linux), 24×7 |
| **Web Dashboard** | A React + TypeScript web app, served *by the agent itself* over the local WiFi. | Any phone, tablet, or laptop on the same WiFi |
| **Google Drive** | Where the recordings end up. | Cloud (each center's own Drive) |

```
CENTER PC (agent — all heavy lifting happens here)
├── File watcher ──▶ SQLite queue ──▶ Google Drive (direct upload)
├── Matching engine (rule-based, confidence-scored)
└── Kestrel API server + built-in web dashboard (LAN)

CLOUD (metadata & coordination only — optional, Phase 4+)
├── Firebase Auth / Firestore / FCM
└── Only lecture metadata, review items, heartbeats — never the videos

WEB (React + Vite dashboard, served by the agent)
└── Live uploads, review queue, timetable editor, controls
```

### 3.2 Pipeline, step by step

1. **File monitoring** — a watcher on the recordings folder fires when a new file appears. The agent waits for the file size to stop changing (so half-written files are ignored), then validates type and size.
2. **Lecture session created** — a record is written to local SQLite with detection time, file path, size, and (if FFmpeg's `ffprobe` is installed) the real video duration. Without `ffprobe`, duration is estimated from file size, which slightly weakens matching.
3. **Matching** — the rule-based engine scores the file against every timetable slot for that room in a time window around the recording (details in section 4).
4. **Decision** — Auto-assign, Review, or No-match (see section 4). Notifications (email/webhook) can fire when review is needed or an upload fails.
5. **Upload queue** — every lecture that needs uploading sits in a SQLite-backed queue with retry counters. Uploads are resumable, so a dropped connection continues instead of restarting. Duplicates are detected and skipped.
6. **Confirmation** — on success the lecture is marked `Uploaded`, the audit log gets an entry, and (if cloud sync is on) Firestore is updated. Every write to the cloud is an idempotent upsert, so offline gaps self-heal.
7. **Special cases** — extended lectures (recording runs past the slot), cancelled slots, missing lectures (slot happened but no recording appeared), and PDF-only sessions are all handled with dedicated states.

### 3.3 Why this design

- **Edge-first:** the center PC does the watching, matching, and uploading. If the internet dies, the center keeps working.
- **No central video server:** there is no server anywhere with enough bandwidth for 20,000 videos a day. Videos go PC → Drive directly.
- **Deterministic matching first:** a transparent, rule-based score — not an AI guess — decides. It is fast, explainable, and when it is unsure it always asks a human.
- **Full audit trail:** every assignment records *why* (the factor scores) and every later change records what changed, so mistakes are reversible.

---

## 4. How the matching engine decides

The engine compares the new recording against each candidate timetable slot using **seven weighted factors**:

| Factor | Weight | What it checks |
|---|---|---|
| Batch / subject signals | 25% | Batch codes and subject keywords parsed from the file name vs. the slot |
| Time overlap | 20% | How much of the slot's time window the recording covers |
| Room | 15% | The recording PC is bound to this room |
| Duration | 15% | Real (or estimated) video length vs. slot length |
| Teacher | 10% | Teacher known for the slot |
| History | 5% | Past lectures already accepted for this slot |
| Context | 5% | How close the recording started to the slot's start time |

Each factor scores 0–100; the weighted total is the **confidence score** (0–100).

**The decision rules:**

| Confidence | Decision | What happens |
|---|---|---|
| ≥ 85 | **Auto-assigned** | Lecture gets batch/subject/teacher and is queued for upload automatically |
| 60–84 | **Review required** | Engine's best guess is attached as a *suggestion*; lecture waits in the Review queue |
| < 60 | **Review required** | No suggestion attached; a human assigns it |
| No slot fits at all | **No match** | Review queue, human assigns |

A contradiction between the file name and the slot (for example the file name says batch `PW-JEE-A` but the slot says `PW-NEET-B`) actively *penalizes* the score below the neutral 75, so contradictory files can never auto-assign.

Thresholds are configurable in `appsettings.json` under `Matching:HighConfidenceThreshold` (default 85) and `Matching:MediumConfidenceThreshold` (default 60).

---

## 5. The web dashboard — screen by screen

Open `http://<center-pc-lan-ip>:5200/` on any device on the same WiFi. The first time you paste the **API key** (printed in the agent's terminal, also saved in `DASHBOARD-ACCESS.txt` on the PC) and sign in with Google.

> **Who can sign in:** the first Google sign-in becomes the **admin**. After that, only the email addresses the admin adds (Controls → Staff access) can get in.

| Tab | What you see and do there |
|---|---|
| **Live** | Real-time upload activity for the agent's own room and any remote rooms you follow: uploading / pending / uploaded / failed counts, current queue items. Data refreshes automatically. |
| **Review** | The human-in-the-loop queue. Every uncertain lecture appears with its file name, suggested batch/subject, target Drive folder, and confidence info. Actions: **Approve match** (accept the suggestion), **Edit** (pick the correct batch/subject yourself), **Re-run matching**, or **Force enqueue**. Duplicate suspects are shown here too. |
| **Schedule** | The center's timetable, live from the Google Sheet. For your own room you can cancel a slot for today (creates a one-day override; the sheet itself is never modified). Other rooms are read-only. |
| **Center** | The multi-room control panel: add one agent per room (its URL + API key), see every room's live status in one place, see batches from the timetable sheet that are not mapped to any room, and assign them. |
| **Controls** | The switches: pause/resume **uploads**, pause/resume **file monitoring**, **rescan the recordings folder** now, pause/resume **timetable sync**, **sync the timetable now**, retry failed uploads, per-item retry/cancel, **staff access** (add/remove the emails allowed to sign in), agent info, and the update check. |

The agent also ships a lightweight standalone **monitor page** (`/monitor.html`) for at-a-glance status, and a PWA manifest so the dashboard can be installed to a phone's home screen like an app.

---

## 6. How to set it up

### Option A — graphical Windows installer (recommended for center PCs; no command line)

1. Build the installer once with `installer/build-installer.sh` (produces `LectureAgent-Setup-<version>.exe`). See `installer/README.md`.
2. Run the setup EXE on the center PC. A wizard asks for:
   - the **center ID**,
   - the **room ID** (each room's PC gets a different one),
   - the **recordings folder** (where the class-recording software saves files).
3. The installer sets up the background service (auto-starts on boot, runs 24×7), opens the firewall port so phones on the WiFi can reach the dashboard, and installs the **desktop app**.
4. Launch the desktop app: its one-time **setup wizard** connects Google Drive. The service itself has no screen, so the wizard runs the agent's `--authorize-google` sign-in as the logged-in user and stores the token where the service reads it.
5. The wizard finishes by writing **`DASHBOARD-ACCESS.txt`** on the PC — it contains the dashboard URL and the API key. Open it on the PC, open that URL on your phone, paste the key, sign in with Google. Done.

### Option B — run from source (developer / evaluation setup)

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) and Node.js.

```bash
# 1. Build the React dashboard into the agent's wwwroot
cd web && npm install && npm run build && cd ..
./agent/scripts/build-web.sh

# 2. Run the agent (it prompts for the recordings folder if not given)
./agent/scripts/run-agent.sh "/path/to/recordings" "LectureRecordings"
```

The terminal prints the dashboard URLs and a freshly generated **API key** on every start. Set the `AGENT_API_KEY` environment variable if you want a stable key instead of a new one each start.

### One-time setup checklist

- [ ] **Google Drive connection** — put the service-account JSON at `agent/src/LectureAgent/config/google_credentials.json` (or use the desktop wizard's OAuth sign-in). Without it, and with `GoogleDrive:Enabled: false`, the agent runs in **local mock mode** (everything works except real uploads).
- [ ] **Timetable source** — point the agent at the center's Google Sheet timetable (timetable sync) or provide a local timetable; make sure every room that records has its batches mapped (the **Center** tab warns about unmapped batches).
- [ ] **FFmpeg (optional but recommended)** — install FFmpeg so `ffprobe` is on the PATH (or set `FileWatcher:FFprobePath`) for accurate video durations.
- [ ] **Sign-in emails** — first Google sign-in becomes admin; add staff emails under Controls → Staff access.
- [ ] **Optional extras** — notifications (webhook/email), cloud sync (Firestore), auto-update — see section 8.

---

## 7. How to use it day to day

### The normal day (staff does nothing)

Recordings appear → the agent matches and uploads them automatically → they land in the right Drive folders. If everything is high-confidence, the Review tab stays empty.

### When the Review tab has items (a few minutes)

1. Open the dashboard on any phone/laptop on the WiFi → **Review** tab.
2. Each item shows the suggested batch/subject. If the suggestion is right, tap **Approve match**. If not, tap **Edit** and pick the correct batch/subject, then approve.
3. Approved lectures upload immediately. Uncertain ones keep waiting until someone approves them.

### Watching progress

- **Live** tab shows what is uploading right now and success/failure counters.
- Failed uploads show up with a retry option; the agent also retries automatically with backoff, and permanently-failed items stay visible in Controls.

### Useful controls

- **Pause uploads** before doing maintenance on the network or Drive; **resume** after.
- **Rescan folder now** after you manually drop a batch of old files into the recordings folder.
- **Sync timetable now** right after the academic team edits the Google Sheet.
- **Cancel a slot** (Schedule tab) when a class is called off, so an unexpected recording is not force-matched to it.

### Managing multiple rooms

In the **Center** tab, add one agent per room (each room's PC runs the same installer with its own Room ID). Every room's status — live/down, uploading/uploaded/failed/missing counts — shows on one screen. If a room goes down after a WiFi reboot, its PC's IP probably changed: open `DASHBOARD-ACCESS.txt` on that PC for the current URL, remove the room entry, and re-add it.

---

## 8. Configuration reference

Main config file: `agent/src/LectureAgent/appsettings.json`.

| Section | Key settings | What it controls |
|---|---|---|
| `FileWatcher` | `MonitorFolder`, `EnableFileWatcher`, `FFprobePath` | Which folder to watch; whether to use real video durations |
| `Matching` | `HighConfidenceThreshold` (85), `MediumConfidenceThreshold` (60) | The auto-assign and review cutoffs |
| `GoogleDrive` | `Enabled`, `RootFolderPath` (e.g. `LectureRecordings/Center-001`), `CredentialsPath`, `TokenPath` | Real uploads vs. mock mode; Drive folder layout |
| `CloudSync` | `Enabled`, `FirebaseProjectId`, `FirebaseCredentialsPath` | Optional Firestore sync of metadata, review items, heartbeats, audit |
| `Notifications` | `Webhook` (Generic/Discord/Slack), `Email` (SMTP), `Events` | Alerts on review-needed / upload-failed / missing-lecture events |
| `AutoUpdate` | `Enabled`, `ManifestUrl`, `CheckIntervalMinutes` | Versioned manifest + SHA-256 verified download + staged swap + rollback |
| `Timetable` | sync source settings | Google Sheet timetable pull frequency and mapping |

**Credential hardening:** on startup the agent replaces a plain-text Google client secret with a protected copy (DPAPI machine scope on Windows, owner-only file permissions on macOS/Linux) stored as `<path>.protected`; both forms are accepted when reading.

**Auto-update:** the manifest needs `version`, `downloadUrl`, and `sha256` of the zip. The agent downloads, hash-verifies, stages the new build, restarts via a helper script that health-checks it, and rolls back automatically if the new version never comes up. Check manually with `POST /api/control/update/check`.

**API surface:** all `/api` routes require the `X-Agent-Key` header (except `/api/health`). Full endpoint list: `API_CONTRACTS.md`. Highlights: device register/heartbeat, timetable + overrides, lecture rematch, upload pause/resume/retry/cancel, monitoring pause/resume/rescan, timetable sync control, `GET /api/agent/info`.

---

## 9. Troubleshooting and FAQ

| Symptom | Likely cause | Fix |
|---|---|---|
| Dashboard will not open | Agent service stopped, or wrong IP | Check the agent is running; the URL and current IP are in `DASHBOARD-ACCESS.txt` on the PC |
| "Could not reach the room agent" in Center tab | The room PC's IP changed (common after a WiFi reboot) | Open `DASHBOARD-ACCESS.txt` on that PC, use the URL with the current IP, remove the room and add it again |
| Login rejected | Your email is not on the staff list | Ask the admin to add it under Controls → Staff access (the very first sign-in becomes admin) |
| Lectures stuck in Review | Confidence below 85% | Approve/edit them in the Review tab; if the same file type is always uncertain, fix batch codes in file names or the timetable mapping |
| Batches listed as unmapped in Center tab | Timetable batches not mapped to any room | Enter the room number next to each unmapped batch |
| Upload keeps failing | Network/Drive issue; item marked `Failed` then `FailedPermanently` | Use Controls → retry (single item or "retry failed"); check Drive quota/credentials |
| Uploads never start | Agent in mock mode | Set `GoogleDrive:Enabled: true` and provide valid credentials (or run the desktop wizard) |
| Duration-based matching is weak | `ffprobe` not installed | Install FFmpeg or set `FileWatcher:FFprobePath` |
| A recording matched the wrong slot | Timetable gap or ambiguous file name | Use Review → Edit to correct it; the audit trail keeps the old assignment, and **Re-run matching** re-scores after you fix the sheet |
| API key lost | — | It is in `DASHBOARD-ACCESS.txt`; or restart the agent to print a new one (or set a fixed `AGENT_API_KEY`) |

**General rule:** nothing is ever silently lost. Files wait in the local SQLite queue while offline and upload when the connection returns; every state change is logged and auditable.

---

## 10. Security and data rules

- **Videos never touch a central server** — they go directly from the center PC to that center's Google Drive.
- **Dashboard access** requires the API key (`X-Agent-Key` header) plus a Google sign-in from an approved email; centers see only their own data.
- **Credentials are protected** — client secrets are converted to DPAPI-protected (Windows) or permission-restricted (macOS/Linux) copies at startup.
- **Audit trail is immutable** — every assignment, approval, edit, and override is recorded with what changed and when, so any action can be traced or reversed.
- **Human-in-the-loop by design** — the system never silently guesses on low-confidence files; it always asks.

---

## 11. Where to learn more

| Document | Contents |
|---|---|
| [README.md](./README.md) | Project overview, quick start, tech stack |
| [ARCHITECTURE.md](./ARCHITECTURE.md) | Detailed system design |
| [MATCHING_ENGINE_SPEC.md](./MATCHING_ENGINE_SPEC.md) | Full scoring algorithm |
| [DATABASE_SCHEMA.md](./DATABASE_SCHEMA.md) | SQLite & Firestore schemas |
| [STATE_MACHINES.md](./STATE_MACHINES.md) | Lecture/queue lifecycles and special cases |
| [API_CONTRACTS.md](./API_CONTRACTS.md) | Complete REST API specifications |
| [SECURITY_MODEL.md](./SECURITY_MODEL.md) | Auth, roles, data isolation |
| [DEPLOYMENT_PLAN.md](./DEPLOYMENT_PLAN.md) | Rollout strategy for 500 centers |
| [COST_CONTROL.md](./COST_CONTROL.md) | Cost optimization |
| [PROJECT_STRUCTURE.md](./PROJECT_STRUCTURE.md) | Folder hierarchy |
| [QUICKREF.md](./QUICKREF.md) | Quick reference card |
| [installer/README.md](./installer/README.md) | Windows installer details |

---

**Status:** Phases 1–3 working prototype · agent-side hardening for Phases 4/6 done · **Version:** 1.0.0-alpha · **Last updated:** 2026-09-13
