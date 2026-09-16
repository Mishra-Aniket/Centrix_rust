use rand::RngCore;
use serde::Serialize;
use serde_json::Value;
use std::fs;
use std::path::PathBuf;

/// Application paths structure
#[derive(Serialize)]
pub struct AppPaths {
    pub shared_root: PathBuf,
    pub settings_file: PathBuf,
    pub data_directory: PathBuf,
    pub agent_executable: Option<PathBuf>,
}

fn shared_root() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        PathBuf::from(r"C:\ProgramData\LectureAgent")
    }
    #[cfg(not(target_os = "windows"))]
    {
        let home = std::env::var("HOME").unwrap_or_else(|_| "~".into());
        PathBuf::from(home).join(".local/share/LectureAgent")
    }
}

fn settings_file() -> PathBuf {
    shared_root().join("appsettings.json")
}

fn data_directory() -> PathBuf {
    shared_root().join("data")
}

fn agent_executable() -> Option<PathBuf> {
    let possible_paths = [
        std::env::current_exe().unwrap_or_default().parent().unwrap_or(std::path::Path::new("")).join("../agent/LectureAgent.exe"),
        PathBuf::from(r"C:\ProgramData\Centrix\agent\LectureAgent.exe"),
        PathBuf::from(r"C:\ProgramData\LectureAgentApp\agent\LectureAgent.exe"),
    ];

    for p in possible_paths.iter() {
        if p.exists() {
            return Some(p.clone());
        }
    }
    None
}

/// Reads the application settings JSON
#[tauri::command]
pub fn read_settings() -> Result<Value, String> {
    let path = settings_file();
    if !path.exists() {
        return Ok(serde_json::json!({
            "Auth": { "ApiKey": "", "Enabled": true },
            "Agent": { "CenterId": "", "RoomId": "", "DeviceId": "" },
            "FileWatcher": { "MonitorFolder": "", "EnableFileWatcher": true },
            "GoogleDrive": { "Enabled": true, "CredentialsPath": "", "RootFolderPath": "", "TokenPath": "" },
            "Kestrel": { "Endpoints": { "Http": { "Url": "http://0.0.0.0:5200" } } },
            "Database": { "SqlitePath": "" }
        }));
    }
    
    let content = fs::read_to_string(&path).map_err(|e| format!("Failed to read settings: {}", e))?;
    let value: Value = serde_json::from_str(&content).map_err(|e| format!("Invalid JSON: {}", e))?;
    Ok(value)
}

/// Saves the application settings JSON
#[tauri::command]
pub fn save_settings(settings: Value) -> Result<(), String> {
    let path = settings_file();
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }
    
    let temp_path = path.with_extension("json.tmp");
    let content = serde_json::to_string_pretty(&settings).map_err(|e| e.to_string())?;
    
    fs::write(&temp_path, content).map_err(|e| format!("Failed to write tmp file: {}", e))?;
    fs::rename(&temp_path, &path).map_err(|e| format!("Failed to save settings: {}", e))?;
    
    Ok(())
}

/// Generates a random API key
#[tauri::command]
pub fn generate_api_key() -> String {
    let mut bytes = [0u8; 24];
    rand::thread_rng().fill_bytes(&mut bytes);
    hex::encode(bytes)
}

/// Gets the application paths
#[tauri::command]
pub fn get_app_paths() -> Result<AppPaths, String> {
    Ok(AppPaths {
        shared_root: shared_root(),
        settings_file: settings_file(),
        data_directory: data_directory(),
        agent_executable: agent_executable(),
    })
}
