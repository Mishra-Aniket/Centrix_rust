use chrono::Local;
use serde::Serialize;
use std::fs;
use std::io::{Read, Write};
use std::path::PathBuf;
use zip::write::SimpleFileOptions;
use zip::ZipWriter;

/// Result of a diagnostics export
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DiagnosticsResult {
    pub saved_path: String,
    pub file_count: usize,
    pub size_bytes: u64,
}

/// Collects system health snapshot as a text report
fn collect_system_info() -> String {
    let mut sys = sysinfo::System::new();
    sys.refresh_cpu_usage();
    sys.refresh_memory();
    std::thread::sleep(std::time::Duration::from_millis(200));
    sys.refresh_cpu_usage();

    let disks = sysinfo::Disks::new_with_refreshed_list();

    let mut report = String::new();
    report.push_str(&format!("=== Centrix Diagnostics Report ===\n"));
    report.push_str(&format!("Generated: {}\n", Local::now().format("%Y-%m-%d %H:%M:%S %z")));
    report.push_str(&format!("OS: {} {}\n", sysinfo::System::name().unwrap_or_default(), sysinfo::System::os_version().unwrap_or_default()));
    report.push_str(&format!("Hostname: {}\n", sysinfo::System::host_name().unwrap_or_default()));
    report.push_str(&format!("Kernel: {}\n", sysinfo::System::kernel_version().unwrap_or_default()));
    report.push_str(&format!("Uptime: {} seconds\n", sysinfo::System::uptime()));
    report.push_str(&format!("Centrix Version: {}\n\n", env!("CARGO_PKG_VERSION")));

    report.push_str(&format!("=== CPU ===\n"));
    report.push_str(&format!("Overall Usage: {:.1}%\n", sys.global_cpu_usage()));
    report.push_str(&format!("CPU Count: {}\n\n", sys.cpus().len()));

    report.push_str(&format!("=== Memory ===\n"));
    report.push_str(&format!("Total RAM: {} MB\n", sys.total_memory() / 1_048_576));
    report.push_str(&format!("Used RAM: {} MB\n", sys.used_memory() / 1_048_576));
    report.push_str(&format!("Available RAM: {} MB\n\n", sys.available_memory() / 1_048_576));

    report.push_str(&format!("=== Disks ===\n"));
    for disk in disks.iter() {
        report.push_str(&format!(
            "  {} ({}) - Total: {} GB, Free: {} GB, FS: {}\n",
            disk.mount_point().display(),
            disk.name().to_string_lossy(),
            disk.total_space() / 1_073_741_824,
            disk.available_space() / 1_073_741_824,
            disk.file_system().to_string_lossy(),
        ));
    }

    report
}

/// Collects service status as text
fn collect_service_status() -> String {
    match crate::service::get_service_status() {
        Ok(status) => format!("Service State: {}\n", status.state),
        Err(e) => format!("Service State: Error - {}\n", e),
    }
}

/// Gets the shared root path for agent data
fn shared_root() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        PathBuf::from(r"C:\ProgramData\Centrix")
    }
    #[cfg(not(target_os = "windows"))]
    {
        let home = std::env::var("HOME").unwrap_or_else(|_| "~".into());
        PathBuf::from(home).join(".local/share/Centrix")
    }
}

/// Exports diagnostics data into a ZIP file on the Desktop
#[tauri::command]
pub fn export_diagnostics() -> Result<DiagnosticsResult, String> {
    let timestamp = Local::now().format("%Y-%m-%d-%H%M%S").to_string();
    let file_name = format!("Centrix-Diagnostics-{}.zip", timestamp);

    // Try Desktop, fallback to temp dir
    let desktop = dirs_next().join(&file_name);
    let zip_path = if desktop.parent().map(|p| p.exists()).unwrap_or(false) {
        desktop
    } else {
        std::env::temp_dir().join(&file_name)
    };

    let file = fs::File::create(&zip_path)
        .map_err(|e| format!("Failed to create zip file: {}", e))?;
    let mut zip = ZipWriter::new(file);
    let options = SimpleFileOptions::default()
        .compression_method(zip::CompressionMethod::Deflated);

    let mut file_count = 0usize;

    // 1. System info report
    let sys_info = collect_system_info();
    zip.start_file("system_info.txt", options)
        .map_err(|e| e.to_string())?;
    zip.write_all(sys_info.as_bytes())
        .map_err(|e| e.to_string())?;
    file_count += 1;

    // 2. Service status
    let service_info = collect_service_status();
    zip.start_file("service_status.txt", options)
        .map_err(|e| e.to_string())?;
    zip.write_all(service_info.as_bytes())
        .map_err(|e| e.to_string())?;
    file_count += 1;

    // 3. Agent settings (appsettings.json)
    let settings_path = shared_root().join("appsettings.json");
    if settings_path.exists() {
        if let Ok(content) = fs::read_to_string(&settings_path) {
            zip.start_file("appsettings.json", options)
                .map_err(|e| e.to_string())?;
            zip.write_all(content.as_bytes())
                .map_err(|e| e.to_string())?;
            file_count += 1;
        }
    }

    // 4. Agent logs (last 3 log files)
    let logs_dir = shared_root().join("logs");
    if logs_dir.exists() {
        let mut log_files: Vec<_> = fs::read_dir(&logs_dir)
            .map_err(|e| e.to_string())?
            .filter_map(|e| e.ok())
            .filter(|e| {
                e.path()
                    .extension()
                    .map(|ext| ext == "log" || ext == "txt" || ext == "json")
                    .unwrap_or(false)
            })
            .collect();

        // Sort by modified time (newest first)
        log_files.sort_by(|a, b| {
            let a_time = a.metadata().and_then(|m| m.modified()).unwrap_or(std::time::SystemTime::UNIX_EPOCH);
            let b_time = b.metadata().and_then(|m| m.modified()).unwrap_or(std::time::SystemTime::UNIX_EPOCH);
            b_time.cmp(&a_time)
        });

        for entry in log_files.iter().take(3) {
            let path = entry.path();
            let name = path.file_name().unwrap_or_default().to_string_lossy();
            let zip_name = format!("logs/{}", name);

            // Read up to 2 MB of each log file (tail)
            const MAX_LOG_SIZE: u64 = 2 * 1024 * 1024;
            if let Ok(metadata) = entry.metadata() {
                if let Ok(mut f) = fs::File::open(&path) {
                    let file_size = metadata.len();
                    let mut content = Vec::new();

                    if file_size > MAX_LOG_SIZE {
                        // Read only the last 2 MB
                        use std::io::Seek;
                        let _ = f.seek(std::io::SeekFrom::End(-(MAX_LOG_SIZE as i64)));
                        let _ = f.read_to_end(&mut content);
                    } else {
                        let _ = f.read_to_end(&mut content);
                    }

                    if !content.is_empty() {
                        zip.start_file(zip_name, options).map_err(|e| e.to_string())?;
                        zip.write_all(&content).map_err(|e| e.to_string())?;
                        file_count += 1;
                    }
                }
            }
        }
    }

    // 5. Database file info (NOT the actual file — just metadata)
    let data_dir = shared_root().join("data");
    if data_dir.exists() {
        let mut db_info = String::from("=== Database Files ===\n");
        if let Ok(entries) = fs::read_dir(&data_dir) {
            for entry in entries.flatten() {
                let path = entry.path();
                if let Some(ext) = path.extension() {
                    if ext == "db" || ext == "sqlite" || ext == "sqlite3" {
                        if let Ok(meta) = entry.metadata() {
                            db_info.push_str(&format!(
                                "  {} - Size: {} MB, Modified: {:?}\n",
                                path.file_name().unwrap_or_default().to_string_lossy(),
                                meta.len() / 1_048_576,
                                meta.modified().ok(),
                            ));
                        }
                    }
                }
            }
        }
        zip.start_file("database_info.txt", options)
            .map_err(|e| e.to_string())?;
        zip.write_all(db_info.as_bytes())
            .map_err(|e| e.to_string())?;
        file_count += 1;
    }

    zip.finish().map_err(|e| e.to_string())?;

    let size_bytes = fs::metadata(&zip_path)
        .map(|m| m.len())
        .unwrap_or(0);

    Ok(DiagnosticsResult {
        saved_path: zip_path.to_string_lossy().to_string(),
        file_count,
        size_bytes,
    })
}

/// Gets the user's Desktop directory cross-platform
fn dirs_next() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        if let Ok(profile) = std::env::var("USERPROFILE") {
            return PathBuf::from(profile).join("Desktop");
        }
        PathBuf::from(r"C:\Users\Public\Desktop")
    }
    #[cfg(not(target_os = "windows"))]
    {
        let home = std::env::var("HOME").unwrap_or_else(|_| "/tmp".into());
        PathBuf::from(home).join("Desktop")
    }
}
