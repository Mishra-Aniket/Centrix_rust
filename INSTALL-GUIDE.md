# 🚀 Centrix (LectureAgent) — Complete Installation & Setup Guide

This guide explains how to install, configure, connect Google Drive, and test **Centrix (LectureAgent)** on classroom and center PCs.

---

## ⚡ Quick Reference (TL;DR)

| Target Platform | Installation File | Instructions |
| :--- | :--- | :--- |
| **Windows PC (Classroom / Room 603)** | `dist/LectureAgent-Setup.exe` *(210 MB)* | Copy to PC ➔ Double-click ➔ Follow Wizard |
| **Windows (Manual Zip Package)** | `dist/LectureAgent.zip` *(55 MB)* | Extract to `C:\` ➔ Double-click `run-agent.bat` |
| **Mac (Apple Silicon M1/M2/M3/M4)** | `dist/LectureAgent-macOS-ARM64.zip` *(52 MB)* | Extract ➔ Run `./LectureAgent` or install via launchd |

- **Local Dashboard URL**: `http://localhost:5200`
- **Phone / MaxHub Panel URL**: `http://<PC-IP>:5200` *(e.g. `http://192.168.1.77:5200`)*
- **Default Port**: `5200` *(HTTP only)*

---

## 🖥️ 1. Windows PC Installation (Single-File Method — Recommended)

You do **NOT** need to install .NET 8, Node.js, or any external runtimes. Everything is bundled inside the installer.

### Step 1: Copy Installer to PC
1. Copy **`LectureAgent-Setup.exe`** from `dist/` to the target Windows PC via a pendrive, local network share, or Google Drive.
2. Double-click **`LectureAgent-Setup.exe`**.

### Step 2: Automatic Extraction & First-Run Wizard
The installer automatically extracts the program files into `C:\ProgramData\LectureAgentApp` and starts the Setup Wizard in a console/window:
1. **Recordings Folder**: Enter the folder where OBS or your camera records videos (default: `Documents`, or press `B` to browse).
2. **Center Name**: Enter your Center Name (e.g. `Pune - PCMC Vidyapeeth`).
3. **Room ID**: Enter the room number (e.g. `603` or `604`).

The wizard automatically generates your unique **API Key** and writes it into `DASHBOARD-ACCESS.txt`.

### Step 3: Open Dashboard
1. The dashboard opens in your browser at: **`http://localhost:5200`**
2. When prompted, paste the **API Key** from `DASHBOARD-ACCESS.txt` (default: `b73cdbacbe433087e9a2ed90cce28d4df57512eb16c9ce9f`).
3. The browser remembers the key automatically.

### Step 4: Enable 24x7 Auto-Start (Runs on PC Boot)
To ensure the agent starts automatically whenever the classroom PC boots:
1. Open `C:\LectureAgent` or `C:\ProgramData\LectureAgentApp\agent`.
2. Right-click **`install-autostart.bat`** ➔ **Run as Administrator**.
3. Done! The agent will now run silently in the background 24x7.

*(To disable auto-start later, run `uninstall-autostart.bat`).*

---

## ☁️ 2. Google Drive Connection & Authorization

The agent uploads directly from classroom PCs into pre-existing batch folders on Google Drive.

### One-Time OAuth Authorization:
1. The OAuth credentials file (`config/google_credentials.json`) is pre-packaged.
2. The **first time** a file is queued for upload (or when you trigger a test upload), a Google Sign-In window will open in your default browser.
3. Sign in with the **Center's Google Account** where lecture recordings are stored.
4. Click **Allow / Continue** to grant access.
5. The browser will display:  
   *"Received verification code. You may now close this window."*
6. The authorization token is saved permanently in `data/google-drive-token`. You will **never** have to log in again on this machine.

---

## 🧪 3. How to Test File Uploads

### Method A: Manual Test via Web Dashboard (Fastest)
1. Open the dashboard at `http://localhost:5200`.
2. Click on the **Controls** tab in the navigation bar.
3. In the **"Manual Upload & Assignment"** card:
   - Click **"Choose File"** and select any sample `.mp4`, `.mkv`, or `.pdf` file.
   - Select the target batch folder from the **Google Drive Folder** dropdown (e.g. `JEE-2026`).
   - Click **"Upload Now"**.
4. Switch to the **Live** tab:
   - You will see the file appear in the upload queue with a live progress bar.
   - Once upload finishes, status will show as **`Uploaded`** with the Google Drive File ID.

### Method B: Automatic Ingestion via Monitored Folder
1. Copy or record any video (`.mkv`/`.mp4`) or PDF into the monitored folder (e.g. `Documents`).
2. Within 2–5 seconds, the agent detects the new file:
   - Check the **Review** tab in the dashboard.
   - If auto-assigned by timetable, it queues directly for upload.
   - If manual confirmation is required, click **"Approve"** or **"Upload Now"**.

---

## 📱 4. MaxHub Panel & Mobile Control (Same WiFi)

Teachers and operators can control Centrix from any smartphone, tablet, or MaxHub flat panel without installing an app:

1. Connect the MaxHub or phone to the **same WiFi network** as the classroom PC.
2. Open Chrome and navigate to:  
   `http://<PC-IP>:5200`  
   *(The PC's local IP address is shown in `DASHBOARD-ACCESS.txt` and on the dashboard Controls tab)*.
3. Enter the API Key once.
4. On Chrome, tap the 3-dot menu ➔ **"Add to Home screen"**.
5. You now have a full-screen controller app on the MaxHub to monitor live uploads, approve sessions, and switch room views!

---

## 🔧 5. Troubleshooting & FAQ

### Q: What if a video is identical or was previously uploaded?
- The agent computes SHA-256 hashes to prevent duplicate bandwidth waste.
- If an identical file exists in the **same Google Drive folder**, it automatically links the Drive file and marks the session as `Uploaded`.
- If you want to upload to a **different folder** or manually force an upload, clicking **"Upload Now"** or **"Force Enqueue"** in the UI overrides the duplicate check.

### Q: Where are log files saved?
- Logs are written to `logs/service.log` and the local console.
- Database entries and audit history are stored in `data/lecture_agent.db`.

### Q: How to restart the service on Mac?
```bash
launchctl unload ~/Library/LaunchAgents/com.pcmc.lectureagent.plist
launchctl load ~/Library/LaunchAgents/com.pcmc.lectureagent.plist
launchctl start com.pcmc.lectureagent
```

### Q: How to restart the service on Windows?
Close the console window and double-click `run-agent.bat`, or if running as a Windows Service / Task, restart via Task Scheduler or Services.
