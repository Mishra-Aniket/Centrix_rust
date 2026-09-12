# LECTURE AGENT — COMPLETE DEPLOYMENT GUIDE
(Lecture Automation System - Room 603 & all rooms)

Last updated: 2026-09-07
Package: dist/LectureAgent.zip (55 MB, self-contained - no .NET install needed)

================================================================
0. QUICK REFERENCE - ALL KEYS & URLS
================================================================

MAC AGENT (already running as a service):
  Dashboard (on Mac):        http://localhost:5200
  Dashboard (same WiFi):     http://192.168.1.77:5200   [IP can change - check Controls tab]
  API Key:                   f6a62b234f4d336326d2c0b2bf162fcc77080adaa6713374
  Monitored folder:          /Users/Extra
  Service restart:           launchctl bootout gui/$(id -u)/com.pcmc.lectureagent
                             launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/com.pcmc.lectureagent.plist
  Logs:                      /Users/aniketmishra/LectureAgent/logs/

WINDOWS ROOM-603 PACKAGE (to install on the center PC):
  API Key (pre-generated):   b73cdbacbe433087e9a2ed90cce28d4df57512eb16c9ce9f
  (also written inside the package: DASHBOARD-ACCESS.txt)
  On-PC dashboard:           http://localhost:5200
  From phone/MaxHub:         http://<PC-IP>:5200  (IP shown in DASHBOARD-ACCESS.txt after setup)

NEW ROOMS (604, 501, ...):
  Same zip, same steps - in the setup wizard just type a different Room ID.
  Each room PC generates its OWN API key (wizard writes it to DASHBOARD-ACCESS.txt).
  Add each room in the Center tab of your admin dashboard.

IMPORTANT: Always use http:// (NOT https://) - port 5200 is HTTP only.

================================================================
1. ROOM 603 WINDOWS PC - INSTALL (10 minutes)
================================================================

STEP 1  Copy "LectureAgent.zip" to the PC (pendrive/download, Desktop is fine).
        Right-click the zip -> "Extract All..." -> in the path box type:  C:\
        -> click Extract.
        Windows creates C:\LectureAgent automatically, with all files
        directly inside (no extra folder, no manual folder creation).

STEP 2  Open the folder, double-click:  run-agent.bat
        - First time: a SETUP WIZARD opens and asks (press Enter to accept default):
              Where do recordings appear?   [Documents]
              Center name                   [Pune - PCMC Vidyapeeth]
              Room ID                       [603]
        - It saves everything itself. It also creates/updates DASHBOARD-ACCESS.txt
          (contains the PC's IP + the API key).

STEP 3  The dashboard opens automatically: http://localhost:5200
        - Login screen asks for the API key.
        - Open DASHBOARD-ACCESS.txt, copy the key, paste it. Done (browser remembers).

STEP 4  FIRST TEST:
        - Copy any video or PDF into the recordings folder (default: Documents,
          any subfolder is fine).
        - Watch the "Live" tab: the file should appear and start uploading.
        - The FIRST upload opens a Google page -> sign in with the CENTER's
          Google account -> click Allow. This happens only once in its life.

STEP 5  24x7 AUTO-START (recommended):
        - Double-click:  install-autostart.bat
        - Now the agent starts by itself whenever the PC turns on.
        - To undo later: uninstall-autostart.bat

RULES OF THE SYSTEM:
  - Only files written in the last 24 hours are picked up (old files ignored).
    (Change: appsettings.json -> FileWatcher:MaxFileAgeHours, 0 = no limit)
  - Same content is NEVER uploaded twice (path dedupe + content-hash guard).
  - The agent uploads into EXISTING batch folders on Drive (like
    "Afternoon/Arjuna JEE/27-AJ452NA 2026"). It NEVER creates folders.
    If a batch folder is missing, the upload FAILS with a clear message.
  - Batch/Subject/Teacher come from the timetable (TT sheet) + live tracker
    mapping. Subject column in the TT sheet is the source of truth.

================================================================
2. MAXHUB PANEL SETUP (2 minutes)
================================================================

MaxHub (Android) cannot run the agent itself - it becomes a CONTROLLER:

STEP 1  Connect the MaxHub to the SAME WiFi as the agent PC.
STEP 2  Open Chrome on the MaxHub -> go to:  http://<PC-IP>:5200
        (PC-IP is written in DASHBOARD-ACCESS.txt on the agent PC)
STEP 3  Enter the same API key.
STEP 4  Chrome menu (3 dots) -> "Add to Home screen".
        Now the panel has a LASRS icon - tap to open like an app.

What you can do from MaxHub: watch live uploads, approve lectures,
edit timetable, pause/resume uploads, see missing-lecture alerts.

If recordings are made ON the MaxHub: share its recording folder over
the LAN (SMB) or copy via USB into the PC's monitored folder - the
agent picks them up automatically.

================================================================
3. PHONE & LAPTOP - SAME WIFI CONTROL (1 minute)
================================================================

STEP 1  Phone/laptop on the SAME WiFi as the agent PC.
STEP 2  Browser -> http://<PC-IP>:5200   (use http, NOT https)
STEP 3  Paste the API key once. Bookmark the page.

From the phone you get EVERYTHING: live upload feed, review/approve,
timetable editing, controls, missing-lecture alerts.

================================================================
4. TAILSCALE - CONTROL FROM ANYWHERE (not just WiFi)
================================================================

One account, install on every device (free plan is enough):

STEP 1  Create account:  https://tailscale.com  -> Get Started -> sign in
        with your Google account. This ONE account is used everywhere.

STEP 2  Mac: install "Tailscale" from the App Store (or brew install --cask
        tailscale) -> login -> note the 100.x.x.x IP from the menu-bar icon.

STEP 3  Phone: install the "Tailscale" app (Play Store/App Store) -> same
        account -> toggle ON.

STEP 4  Every room PC: download the Windows installer from
        tailscale.com/download -> install -> same account login.

STEP 5  Now from ANYWHERE (home, market, another city):
        Phone/laptop -> http://100.x.x.x:5200  (the PC's Tailscale IP)
        Same dashboard, same key. The Tailscale IP NEVER changes, so your
        bookmark works forever.

Benefits:
  - Works outside the WiFi (any internet).
  - Private & encrypted - no public exposure of the PCs.
  - Fixed IPs - no more "IP changed, bookmark broke" problem.
  - The agent's Controls tab will also show the 100.x.x.x URL automatically.

================================================================
5. CENTER TAB - ALL ROOMS ON ONE SCREEN
================================================================

After deploying 2+ room agents:

STEP 1  Open YOUR admin dashboard (e.g. the 603 PC dashboard or your Mac).
STEP 2  Go to the "Center" tab -> "Add a room agent":
          Room name : Room 604
          Agent URL : http://<that-room-PC-IP>:5200   (or its Tailscale IP)
          API key   : that room's key (its DASHBOARD-ACCESS.txt)
          Room ID   : 604
STEP 3  Repeat for every room.
        The screen now shows EVERY room: live/down, uploading, failed,
        missing lectures - with center-wide totals on top.
        (Room list is saved on the agent PC whose dashboard you use -
         the admin PC is the best place to keep it.)

================================================================
6. DATABASE (DB)
================================================================

- Location: inside the agent folder ->  data\lecture_agent.db  (+ -wal/-shm)
- One DB per room PC - data stays on the center PC (works offline).
- BACKUP: stop the agent (or before PC restart), copy the data\ folder.
  Monthly backup is enough.
- Google Drive token (after first approval): data\google-drive-token -
  do not delete it, otherwise you must approve Google again.

================================================================
7. DAILY OPERATION (who does what)
================================================================

FACULTY (nothing new to learn):
  - Record the class normally. File lands in the monitored folder.
  - That's all. The agent matches batch/subject/teacher from the
    timetable and uploads to the correct Drive batch folder.

SYSTEM (automatic):
  - Detects file -> waits until fully written -> matches timetable slot
    (room + time + duration, tracker live mapping) -> uploads into the
    existing batch folder on Drive.
  - Skips: old files (>24h), duplicate content, already-filled slots.

YOU (admin, from phone - 2 minutes a day):
  - Live tab: green = all good.
  - Red alert = a lecture's recording is MISSING (slot ended 30+ min ago).
  - Review tab: approve 60-84% confidence matches, upload duplicates if
    you decide to, re-run matching after a timetable fix.
  - Controls tab: pause/resume uploads, rescan folder, retry failed.

================================================================
8. TROUBLESHOOTING
================================================================

Problem: Phone cannot open the dashboard
  -> Same WiFi? URL starts with http:// (not https)? Correct IP?
     (IP is in DASHBOARD-ACCESS.txt; it can change after router restart -
      check the Controls tab for the current IP, or use Tailscale IP)

Problem: 401 Unauthorized on the dashboard
  -> Wrong API key. Open DASHBOARD-ACCESS.txt on the agent PC, copy again.

Problem: Upload fails "Drive folder 'XXX' not found"
  -> The batch folder name on Drive does not match the batch code exactly
     (spaces/case matter). Fix the folder name on Drive, or approve the
     lecture with the correct batch on the Review tab, then Retry.

Problem: Old file got uploaded
  -> It was younger than 24h. Raise FileWatcher:MaxFileAgeHours if needed.

Problem: Upload fails with permission error
  -> The Google account used at first approval needs EDITOR access to the
     batch folders on Drive (they are shared folders).

================================================================
9. SECURITY NOTES (important)
================================================================

- API keys give FULL control of the agent. Do not share in chats.
  If a key leaks: run setup.bat -> choose "n" (new key) -> update phones.
- The Google OAuth client file (config/google_credentials.json) has an
  OLD exposed secret - rotate it in Google Cloud Console when possible.
- LAN traffic is HTTP (key protects the API, not the wire). For sensitive
  networks use the Tailscale route, which is fully encrypted.

================================================================
10. WHAT'S INSIDE THE PACKAGE (reference)
================================================================

  LectureAgent.exe            the agent (self-contained)
  run-agent.bat               start (runs setup wizard on first run)
  setup.bat / setup.ps1       change settings anytime (asks everything)
  install-autostart.bat       add to Windows startup (24x7)
  uninstall-autostart.bat     remove from startup
  DASHBOARD-ACCESS.txt        URL + API key (auto-generated)
  README-INSTRUCTIONS.txt     this guide's short version
  appsettings.json            config (edit only via setup.bat)
  data\lecture_agent.db       the database
  config\google_credentials.json   Google OAuth client
  wwwroot\                    the dashboard app (served on port 5200)

System flow: Recording file -> detected (recent only, no duplicates) ->
matched to batch/subject/teacher via tracker mapping + TT sheet ->
uploaded into the EXISTING batch folder on Google Drive.
Dashboard: live status, review, schedule, all-rooms view, controls.
