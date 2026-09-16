use serde::Serialize;
use sysinfo::{Disks, System};
use std::path::PathBuf;

/// System health information returned to the frontend
#[derive(Serialize)]
pub struct SystemHealth {
    pub cpu_usage_percent: f32,
    pub ram_total_bytes: u64,
    pub ram_used_bytes: u64,
    pub ram_available_bytes: u64,
    pub disk_total_bytes: u64,
    pub disk_free_bytes: u64,
    pub disk_usage_percent: f32,
    pub disk_mount_point: String,
    pub uptime_seconds: u64,
}

/// Retrieves system health metrics (CPU, RAM, Disk)
#[tauri::command]
pub fn get_system_health(monitor_folder: Option<String>) -> Result<SystemHealth, String> {
    let mut sys = System::new();
    sys.refresh_cpu_usage();
    sys.refresh_memory();

    // Brief pause to get meaningful CPU reading
    std::thread::sleep(std::time::Duration::from_millis(200));
    sys.refresh_cpu_usage();

    let cpu_usage = sys.global_cpu_usage();
    let ram_total = sys.total_memory();
    let ram_used = sys.used_memory();
    let ram_available = sys.available_memory();

    // Find the disk that contains the monitor folder (or the largest disk)
    let disks = Disks::new_with_refreshed_list();
    let target_path = monitor_folder
        .map(PathBuf::from)
        .unwrap_or_else(|| {
            #[cfg(target_os = "windows")]
            { PathBuf::from("C:\\") }
            #[cfg(not(target_os = "windows"))]
            { PathBuf::from("/") }
        });

    // Find the best matching disk for the target path
    // (longest mount point prefix match)
    let matched_disk = disks.iter()
        .filter(|d| target_path.starts_with(d.mount_point()))
        .max_by_key(|d| d.mount_point().as_os_str().len());

    // Fallback to the disk with most total space
    let disk = matched_disk.or_else(|| {
        disks.iter().max_by_key(|d| d.total_space())
    });

    let (disk_total, disk_free, disk_mount) = match disk {
        Some(d) => (
            d.total_space(),
            d.available_space(),
            d.mount_point().to_string_lossy().to_string(),
        ),
        None => (0, 0, "Unknown".to_string()),
    };

    let disk_usage = if disk_total > 0 {
        ((disk_total - disk_free) as f64 / disk_total as f64 * 100.0) as f32
    } else {
        0.0
    };

    Ok(SystemHealth {
        cpu_usage_percent: cpu_usage,
        ram_total_bytes: ram_total,
        ram_used_bytes: ram_used,
        ram_available_bytes: ram_available,
        disk_total_bytes: disk_total,
        disk_free_bytes: disk_free,
        disk_usage_percent: disk_usage,
        disk_mount_point: disk_mount,
        uptime_seconds: System::uptime(),
    })
}
