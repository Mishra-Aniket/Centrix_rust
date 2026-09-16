use tauri::{
    menu::{Menu, MenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    Manager,
};

/// Sets up the system tray
pub fn setup_tray(app: &mut tauri::App) -> Result<(), Box<dyn std::error::Error>> {
    let open_i = MenuItem::with_id(app, "open", "Open Dashboard", true, None::<&str>)?;
    let sep1 = tauri::menu::PredefinedMenuItem::separator(app)?;
    
    // Disable service status, updated via frontend or kept static here
    let status_i = MenuItem::with_id(app, "status", "Service: Checking...", false, None::<&str>)?;
    let restart_i = MenuItem::with_id(app, "restart", "Restart Agent", true, None::<&str>)?;
    
    let sep2 = tauri::menu::PredefinedMenuItem::separator(app)?;
    let quit_i = MenuItem::with_id(app, "quit", "Quit Centrix", true, None::<&str>)?;

    let menu = Menu::with_items(
        app,
        &[&open_i, &sep1, &status_i, &restart_i, &sep2, &quit_i],
    )?;

    TrayIconBuilder::new()
        .menu(&menu)
        .tooltip("Centrix")
        .icon(app.default_window_icon().unwrap().clone())
        .on_menu_event(|app, event| match event.id.as_ref() {
            "open" => {
                if let Some(window) = app.get_webview_window("main") {
                    let _ = window.show();
                    let _ = window.set_focus();
                }
            }
            "restart" => {
                // Ignore error, or could show dialog
                let _ = crate::service::restart_service();
            }
            "quit" => {
                app.exit(0);
            }
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                if let Some(window) = tray.app_handle().get_webview_window("main") {
                    let _ = window.show();
                    let _ = window.set_focus();
                }
            }
        })
        .build(app)?;

    Ok(())
}
