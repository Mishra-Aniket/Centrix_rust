use chrono::{Local, NaiveTime};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Serialize, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct BandwidthSettings {
    pub max_upload_speed_mbps: u32,     // 0 = unlimited, 5, 10, 20, etc.
    pub off_peak_enabled: bool,         // If true, upload only in off-peak hours
    pub off_peak_start: String,         // e.g. "20:00" (8 PM)
    pub off_peak_end: String,           // e.g. "08:00" (8 AM)
    pub pause_during_class_hours: bool, // Pause daytime uploads automatically
    pub current_status: Option<String>, // "Unrestricted", "Throttled", "OffPeakActive"
}

impl Default for BandwidthSettings {
    fn default() -> Self {
        Self {
            max_upload_speed_mbps: 0,
            off_peak_enabled: false,
            off_peak_start: "20:00".into(),
            off_peak_end: "08:00".into(),
            pause_during_class_hours: false,
            current_status: Some("Unrestricted".into()),
        }
    }
}

fn config_path() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        PathBuf::from(r"C:\ProgramData\Centrix\bandwidth_settings.json")
    }
    #[cfg(not(target_os = "windows"))]
    {
        let home = std::env::var("HOME").unwrap_or_else(|_| "~".into());
        PathBuf::from(home).join(".local/share/Centrix/bandwidth_settings.json")
    }
}

/// Checks whether the current local time falls into the off-peak window
fn check_is_off_peak(start_str: &str, end_str: &str) -> bool {
    let now = Local::now().time();
    let start = NaiveTime::parse_from_str(start_str, "%H:%M")
        .unwrap_or_else(|_| NaiveTime::from_hms_opt(20, 0, 0).unwrap());
    let end = NaiveTime::parse_from_str(end_str, "%H:%M")
        .unwrap_or_else(|_| NaiveTime::from_hms_opt(8, 0, 0).unwrap());

    if start > end {
        // Window crosses midnight (e.g. 20:00 to 08:00)
        now >= start || now < end
    } else {
        // Window within same day (e.g. 01:00 to 06:00)
        now >= start && now < end
    }
}

#[tauri::command]
pub fn get_bandwidth_settings() -> Result<BandwidthSettings, String> {
    let path = config_path();
    let mut settings = if path.exists() {
        let content = fs::read_to_string(&path)
            .map_err(|e| format!("Failed to read bandwidth settings: {}", e))?;
        serde_json::from_str::<BandwidthSettings>(&content).unwrap_or_default()
    } else {
        BandwidthSettings::default()
    };

    // Calculate current live status
    let is_off_peak = check_is_off_peak(&settings.off_peak_start, &settings.off_peak_end);
    let status = if settings.off_peak_enabled && !is_off_peak && settings.pause_during_class_hours {
        "PeakHoursPaused"
    } else if settings.max_upload_speed_mbps > 0 {
        "Throttled"
    } else {
        "Unrestricted"
    };

    settings.current_status = Some(status.to_string());
    Ok(settings)
}

#[tauri::command]
pub fn save_bandwidth_settings(mut settings: BandwidthSettings) -> Result<(), String> {
    let path = config_path();
    if let Some(parent) = path.parent() {
        let _ = fs::create_dir_all(parent);
    }

    let is_off_peak = check_is_off_peak(&settings.off_peak_start, &settings.off_peak_end);
    let status = if settings.off_peak_enabled && !is_off_peak && settings.pause_during_class_hours {
        "PeakHoursPaused"
    } else if settings.max_upload_speed_mbps > 0 {
        "Throttled"
    } else {
        "Unrestricted"
    };
    settings.current_status = Some(status.to_string());

    let json = serde_json::to_string_pretty(&settings)
        .map_err(|e| format!("Failed to serialize settings: {}", e))?;
    fs::write(&path, json).map_err(|e| format!("Failed to save bandwidth settings: {}", e))?;

    Ok(())
}
