# One-Time Setup

Only these two things are required for a real local upload test.

## 1. Google credentials

Put the Desktop OAuth JSON file here:

```text
src/LectureAgent/config/google_credentials.json
```

Do not commit or share this file. The current file should be rotated because its secret was exposed in chat.

## 2. Run with source and Drive folders

From the `agent` folder:

```bash
chmod +x scripts/run-agent.sh scripts/status.sh
./scripts/run-agent.sh "/Users/Extra" "LectureRecordings"
```

The first real upload may open Google authorization in a browser. Approve access with the intended Google account.

The source folder can be changed without editing code:

```bash
./scripts/run-agent.sh "/Users/aniketmishra/Desktop/Recordings" "LectureRecordings/Center-001"
```

Supported path examples:

```text
macOS:  /Users/name/Recordings
Windows: D:/Recordings
Linux: /home/name/Recordings
```

## Monitor in real time

Open this URL while the script is running:

```text
https://localhost:5201/
```

The dashboard refreshes every two seconds. For terminal status:

```bash
./scripts/status.sh
```

Drop a new PDF or video into the source folder. New files are detected automatically. `UploadQueue:QueueUnmatchedFiles` is enabled for testing, so a timetable is not required for the test.

## Control from phone / tablet (same WiFi)

`run-agent.sh` now binds the dashboard to the whole LAN and enables API-key auth:

1. Run `./scripts/run-agent.sh "<source folder>" "<drive folder>"` — it prints the phone URL (`http://<pc-ip>:5200/`) and a fresh API key.
2. Open that URL on the phone, paste the API key once. It is stored in that browser only.
3. From the phone you can watch uploads, approve matches, edit the timetable and pause/resume uploads or monitoring (Controls tab).

HTTP on the LAN is not encrypted; the API key protects the endpoints but not the traffic. For sensitive networks use `https://<pc-ip>:5201/` and accept the dev-certificate warning.

## One-time values needed from the owner

- The local source folder path.
- The Google Drive destination folder name.
- A fresh Google OAuth Desktop JSON file installed locally at the path above.

No password, client secret, or token should be sent in chat.
