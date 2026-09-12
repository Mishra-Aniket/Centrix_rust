================================================================
   LECTURE AGENT v2 - ROOM 603 (WINDOWS INSTALL GUIDE)
   New: live tracker mapping + phone/MaxHub dashboard + API key login
================================================================

A. INSTALL ON THIS WINDOWS PC (computer / laptop / OPS module)
------------------------------------------------------------
1. Copy this whole folder to a fixed location, e.g.  C:\LectureAgent\
   (Nothing to install - .NET and everything else is inside this folder)
   NOTE: If Windows shows "Windows protected your PC" when you open a .bat
   file -> click "More info" -> "Run anyway".
   Or before extracting: right-click the zip -> Properties -> tick
   "Unblock" -> OK, then extract.

2. FIRST RUN: double-click "run-agent.bat"
   - A SETUP WIZARD opens automatically and ASKS (Enter = default):
       * Where class data is saved (videos AND notes)   [Documents]
         -> press B to BROWSE and pick the folder from a dialog
       * Center name                                    [Pune - PCMC Vidyapeeth]
       * Room ID                                        [603]
   - It saves everything by itself. No config file editing needed.
   - To change these settings later: double-click "setup.bat"
   - The wizard prints where videos and notes (PDF) are picked from -
     ALL subfolders of that folder are included automatically.

3. The dashboard opens by itself: http://localhost:5200/
   - On the login screen paste the API key from DASHBOARD-ACCESS.txt

4. If Windows asks "Allow LectureAgent to communicate on the network?"
   -> click ALLOW (this is needed for phone/MaxHub access).

5. Google Drive first-time login (only once):
   - On the first video upload, a Google account page opens in the browser
   - Approve access with the center's Google account
   - The token is saved to data\google-drive-token - it will not ask again

6. For 24x7 automatic operation: double-click "install-autostart.bat" (once)
   - The agent starts hidden in the background whenever the PC boots
   - CRASH-PROOF: if the agent ever stops, a watchdog restarts it
     within 5 seconds automatically
   - To remove it later: uninstall-autostart.bat

B. HOW RECORDINGS GET PICKED UP
----------------------------
- Default folder: the logged-in user's "Documents" folder
  (C:\Users\<name>\Documents), including any subfolders inside it
- Wrong folder? Just run "setup.bat" again - it will ask again.
- When a file finishes writing: room/batch match from the tracker API,
  then upload to Google Drive

C. MAXHUB (smart panel)
--------------------------
MaxHub Android panels cannot run the agent directly (it needs Windows or
Linux). Options for MaxHub:

  Option 1 - Use it as a dashboard (easiest):
  - Open MaxHub's Chrome browser: http://<PC-IP>:5200/
  - Enter the API key once, then use "Add to Home screen"
  - The panel now shows live status, review/approve, and controls

  Option 2 - If the MaxHub has a Windows OPS module plugged in:
  - Copy this folder into the OPS module's Windows and follow Section A
  - Set MonitorFolder in appsettings.json to the OPS recording folder

  Option 3 - Getting MaxHub Android recordings to the PC:
  - Share the MaxHub recording folder over LAN (SMB) or USB so it lands
    inside the PC's monitored folder (e.g. Documents\Maxhub);
    the agent picks it up automatically

D. WHERE THE DATABASE (DB) LIVES
--------------------------
- SQLite file:  data\lecture_agent.db  inside this folder
  (+ data\lecture_agent.db-wal / -shm working files - keep all of them)
- Backup = just copy those files (best done while the agent is stopped)
- Each PC keeps its own separate database - data stays on the center PC

E. CONTROL FROM A PHONE
-------------------
- Phone/laptop on the same WiFi: http://<PC-IP>:5200/ + the API key
- The key stays the same across restarts (it is set in appsettings.json)
- Controls tab: pause/resume uploads, rescan folder, retry failed,
  force timetable sync, agent info
================================================================

F. TROUBLESHOOTING
-------------------
- Dashboard not opening from phone?
    Same WiFi? URL starts with http:// (not https)? Correct IP?
    (IP is in DASHBOARD-ACCESS.txt; changes after router restart -
     or install Tailscale for a permanent IP)
- "Windows protected your PC" when opening a .bat
    More info -> Run anyway  (or Unblock the zip before extracting)
- Firewall blocked phone access
    Windows Security -> Firewall -> Allow an app -> tick LectureAgent
    (Private + Public), or re-run setup.bat and click Allow when asked
- Upload fails "Drive folder 'XXX' not found"
    The batch folder must exist on Drive with the exact same name.
    Fix the folder name on Drive, or set the correct batch on the
    Review tab, then press Retry on the failed upload.
- Agent window closed by mistake
    Just open run-agent.bat again - it resumes (no data is lost).
================================================================
