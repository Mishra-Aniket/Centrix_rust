use serde::Serialize;
#[cfg(target_os = "windows")]
use std::process::Command;
#[cfg(target_os = "windows")]
use std::time::{Duration, Instant};

#[cfg(target_os = "windows")]
const SERVICE_NAME: &str = "Centrix";

#[cfg(target_os = "windows")]
const STATE_TIMEOUT: Duration = Duration::from_secs(30);

/// Service status representation
#[derive(Serialize)]
pub struct ServiceStatus {
    pub state: String,
}

/// Retrieves the status of the service
#[tauri::command]
pub fn get_service_status() -> Result<ServiceStatus, String> {
    #[cfg(target_os = "windows")]
    {
        Ok(ServiceStatus {
            state: query_state()?,
        })
    }

    #[cfg(not(target_os = "windows"))]
    {
        Ok(ServiceStatus {
            state: "NotInstalled".into(),
        })
    }
}

#[cfg(target_os = "windows")]
fn query_state() -> Result<String, String> {
    let output = Command::new("sc.exe")
        .args(&["query", SERVICE_NAME])
        .output()
        .map_err(|e| format!("Failed to query service: {}", e))?;

    let out_str = String::from_utf8_lossy(&output.stdout);
    if out_str.contains("1060") {
        return Ok("NotInstalled".into());
    }

    if out_str.contains("RUNNING") {
        Ok("Running".into())
    } else if out_str.contains("STOPPED") {
        Ok("Stopped".into())
    } else if out_str.contains("START_PENDING") {
        Ok("Starting".into())
    } else if out_str.contains("STOP_PENDING") {
        Ok("Stopping".into())
    } else {
        Ok("Unknown".into())
    }
}

/// Polls the real service state until it settles into `expected`, re-checking a moment
/// after first observing it in case the process crashes immediately after Windows
/// reports it as started. Without this, a service that crash-loops on launch (e.g.
/// because another process already holds the dashboard port) looks identical to a
/// successful start/install: `sc.exe`'s exit code only reflects whether the *request*
/// was accepted, not whether the service is actually alive afterwards.
#[cfg(target_os = "windows")]
fn wait_for_state(expected: &str) -> Result<(), String> {
    let deadline = Instant::now() + STATE_TIMEOUT;
    while Instant::now() < deadline {
        if query_state()? == expected {
            std::thread::sleep(Duration::from_millis(1500));
            if query_state()? == expected {
                return Ok(());
            }
            continue;
        }
        std::thread::sleep(Duration::from_millis(500));
    }

    Err(format!(
        "The Centrix service did not reach the '{}' state after {}s. It is most likely crashing \
         immediately on launch (a common cause is another process already using the dashboard port). \
         Check C:\\ProgramData\\Centrix\\logs for the exact error.",
        expected,
        STATE_TIMEOUT.as_secs()
    ))
}

/// Starts the service
#[tauri::command]
pub fn start_service() -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        let output = Command::new("sc.exe")
            .args(&["start", SERVICE_NAME])
            .output()
            .map_err(|e| format!("Failed to execute sc.exe: {}", e))?;

        if !output.status.success() {
            let stderr = String::from_utf8_lossy(&output.stderr);
            let stdout = String::from_utf8_lossy(&output.stdout);
            if stdout.contains("Access is denied") || stderr.contains("Access is denied") {
                // Fallback to ShellExecuteW runas (implement simple runas wrapper or return error instructing user to run as admin)
                return Err("Access denied. Please run as Administrator.".into());
            }
            // "1056" = service already running; treat that as success and let
            // wait_for_state below confirm it, instead of failing spuriously.
            if !stdout.contains("1056") {
                return Err(format!("Failed to start service: {}", stdout));
            }
        }

        wait_for_state("Running")
    }

    #[cfg(not(target_os = "windows"))]
    {
        Err("Not supported on this platform".into())
    }
}

/// Stops the service
#[tauri::command]
pub fn stop_service() -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        let output = Command::new("sc.exe")
            .args(&["stop", SERVICE_NAME])
            .output()
            .map_err(|e| format!("Failed to execute sc.exe: {}", e))?;

        if !output.status.success() {
            let stderr = String::from_utf8_lossy(&output.stderr);
            let stdout = String::from_utf8_lossy(&output.stdout);
            if stdout.contains("Access is denied") || stderr.contains("Access is denied") {
                return Err("Access denied. Please run as Administrator.".into());
            }
            return Err(format!("Failed to stop service: {}", stdout));
        }
        Ok(())
    }

    #[cfg(not(target_os = "windows"))]
    {
        Err("Not supported on this platform".into())
    }
}

/// Restarts the service
#[tauri::command]
pub fn restart_service() -> Result<(), String> {
    let _ = stop_service();
    std::thread::sleep(std::time::Duration::from_secs(2));
    start_service()
}

/// Installs the service
#[tauri::command]
pub fn install_service(dashboard_port: u16) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use std::path::PathBuf;

        // Find agent executable
        let possible_paths = [
            std::env::current_exe()
                .unwrap_or_default()
                .parent()
                .unwrap_or(std::path::Path::new(""))
                .join("../agent/CentrixAgent.exe"),
            PathBuf::from(r"C:\ProgramData\Centrix\agent\CentrixAgent.exe"),
            PathBuf::from(r"C:\ProgramData\Centrix\agent\Centrix.exe"),
            PathBuf::from(r"C:\ProgramData\LectureAgentApp\agent\LectureAgent.exe"),
        ];

        let mut bin_path = String::new();
        for p in possible_paths.iter() {
            if p.exists() {
                bin_path = p.to_string_lossy().to_string();
                break;
            }
        }

        if bin_path.is_empty() {
            return Err("Could not find the Centrix agent executable".into());
        }

        let script = format!(
            "@echo off\r\n\
            net stop {} >nul 2>&1\r\n\
            sc delete {} >nul 2>&1\r\n\
            sc create {} binpath= \"{}\" start= auto\r\n\
            sc description {} \"Centrix - Automatic Classroom Lecture Uploader\"\r\n\
            sc failure {} reset= 86400 actions= restart/5000/restart/10000/restart/60000\r\n\
            sc sdset {} D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;RPWPDTLO;;;IU)\r\n\
            netsh advfirewall firewall delete rule name=\"Centrix Dashboard\" >nul 2>&1\r\n\
            netsh advfirewall firewall add rule name=\"Centrix Dashboard\" dir=in action=allow protocol=TCP localport={}\r\n\
            sc start {}\r\n",
            SERVICE_NAME, SERVICE_NAME, SERVICE_NAME, bin_path, SERVICE_NAME, SERVICE_NAME, SERVICE_NAME, dashboard_port, SERVICE_NAME
        );

        let script_path = std::env::temp_dir().join("install_service.cmd");
        std::fs::write(&script_path, script).map_err(|e| e.to_string())?;

        let output = Command::new("cmd.exe")
            .args(&["/c", script_path.to_str().unwrap()])
            .output()
            .map_err(|e| format!("Failed to execute install script: {}", e))?;

        // Previously only "Access is denied" was treated as a failure here, so any other
        // problem (e.g. sc create rejecting the binary path, or the elevated script window
        // being closed) silently returned Ok(()) even though nothing was installed.
        let stdout = String::from_utf8_lossy(&output.stdout);
        if stdout.contains("Access is denied") {
            return Err("Access denied. Please run as Administrator.".into());
        }

        if query_state()? == "NotInstalled" {
            return Err(format!(
                "Windows did not register the Centrix service. Script output: {}",
                stdout.trim()
            ));
        }

        wait_for_state("Running")
    }

    #[cfg(not(target_os = "windows"))]
    {
        let _ = dashboard_port;
        Ok(())
    }
}
