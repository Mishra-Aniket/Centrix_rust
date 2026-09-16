use tauri::Manager;
use tauri_plugin_autostart::MacosLauncher;

mod bandwidth;
mod diagnostics;
mod health;
mod service;
mod settings;
mod tray;
mod video_thumbnail;
mod video_validator;

#[tauri::command]
fn pick_folder_native(default_path: Option<String>) -> Option<String> {
    let mut dialog = rfd::FileDialog::new();
    if let Some(path) = default_path {
        dialog = dialog.set_directory(path);
    }
    dialog.pick_folder().map(|p| p.to_string_lossy().to_string())
}

#[tauri::command]
fn pick_file_native(extension_name: Option<String>, extensions: Option<Vec<String>>) -> Option<String> {
    let mut dialog = rfd::FileDialog::new();
    if let (Some(name), Some(exts)) = (extension_name, extensions) {
        let exts_ref: Vec<&str> = exts.iter().map(|s| s.as_str()).collect();
        dialog = dialog.add_filter(&name, &exts_ref);
    }
    dialog.pick_file().map(|p| p.to_string_lossy().to_string())
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            // Focus existing window
            if let Some(w) = app.get_webview_window("main") {
                let _ = w.show();
                let _ = w.set_focus();
            }
        }))
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_notification::init())
        .plugin(tauri_plugin_autostart::init(
            MacosLauncher::LaunchAgent,
            Some(vec!["--minimized"]),
        ))
        .setup(|app| {
            tray::setup_tray(app)?;

            // If launched with --minimized (auto-start), hide window (tray-only mode)
            if std::env::args().any(|a| a == "--minimized") {
                if let Some(w) = app.get_webview_window("main") {
                    let _ = w.hide();
                }
            }

            Ok(())
        })
        .on_window_event(|window, event| {
            // Minimize to tray on close
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .invoke_handler(tauri::generate_handler![
            service::get_service_status,
            service::start_service,
            service::stop_service,
            service::restart_service,
            service::install_service,
            settings::read_settings,
            settings::save_settings,
            settings::generate_api_key,
            settings::get_app_paths,
            health::get_system_health,
            diagnostics::export_diagnostics,
            video_thumbnail::get_video_thumbnail,
            video_validator::validate_recording_file,
            bandwidth::get_bandwidth_settings,
            bandwidth::save_bandwidth_settings,
            pick_folder_native,
            pick_file_native,
        ])
        .run(tauri::generate_context!())
        .expect("error while running Centrix");
}
