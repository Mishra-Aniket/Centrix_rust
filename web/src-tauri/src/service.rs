use serde::Serialize;
#[cfg(target_os = "windows")]
use std::process::Command;

#[cfg(target_os = "windows")]
const SERVICE_NAME: &str = "LectureAgent";

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
        let output = Command::new("sc.exe")
            .args(&["query", SERVICE_NAME])
            .output()
            .map_err(|e| format!("Failed to query service: {}", e))?;
        
        let out_str = String::from_utf8_lossy(&output.stdout);
        if out_str.contains("1060") {
            return Ok(ServiceStatus { state: "NotInstalled".into() });
        }
        
        if out_str.contains("RUNNING") {
            Ok(ServiceStatus { state: "Running".into() })
        } else if out_str.contains("STOPPED") {
            Ok(ServiceStatus { state: "Stopped".into() })
        } else if out_str.contains("START_PENDING") {
            Ok(ServiceStatus { state: "Starting".into() })
        } else if out_str.contains("STOP_PENDING") {
            Ok(ServiceStatus { state: "Stopping".into() })
        } else {
            Ok(ServiceStatus { state: "Unknown".into() })
        }
    }
    
    #[cfg(not(target_os = "windows"))]
    {
        Ok(ServiceStatus { state: "NotInstalled".into() })
    }
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
            return Err(format!("Failed to start service: {}", stdout));
        }
        Ok(())
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
            std::env::current_exe().unwrap_or_default().parent().unwrap_or(std::path::Path::new("")).join("../agent/LectureAgent.exe"),
            PathBuf::from(r"C:\ProgramData\Centrix\agent\LectureAgent.exe"),
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
            return Err("Could not find LectureAgent.exe".into());
        }
        
        let script = format!(
            "@echo off\r\n\
            sc create {} binpath= \"{}\" start= auto\r\n\
            sc description {} \"Watches the recordings folder and uploads lectures to Google Drive.\"\r\n\
            sc failure {} reset= 86400 actions= restart/5000/restart/10000/restart/60000\r\n\
            sc sdset {} D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;RPWPDTLO;;;IU)\r\n\
            netsh advfirewall firewall delete rule name=\"Centrix Dashboard\" >nul 2>&1\r\n\
            netsh advfirewall firewall add rule name=\"Centrix Dashboard\" dir=in action=allow protocol=TCP localport={}\r\n\
            sc start {}\r\n",
            SERVICE_NAME, bin_path, SERVICE_NAME, SERVICE_NAME, SERVICE_NAME, dashboard_port, SERVICE_NAME
        );
        
        let script_path = std::env::temp_dir().join("install_service.cmd");
        std::fs::write(&script_path, script).map_err(|e| e.to_string())?;
        
        let output = Command::new("cmd.exe")
            .args(&["/c", script_path.to_str().unwrap()])
            .output()
            .map_err(|e| format!("Failed to execute install script: {}", e))?;
            
        let stdout = String::from_utf8_lossy(&output.stdout);
        if stdout.contains("Access is denied") {
            return Err("Access denied. Please run as Administrator.".into());
        }
        
        Ok(())
    }
    
    #[cfg(not(target_os = "windows"))]
    {
        let _ = dashboard_port;
        Ok(())
    }
}
