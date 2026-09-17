// Prevents additional console window on Windows in release
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod commands;
mod domain;
mod i18n;
mod importer;
mod platform;
mod storage;

use commands::AppState;
use platform::check_single_instance;
use storage::Database;
use std::sync::{Arc, Mutex};
use tauri::menu::{Menu, MenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Emitter, Manager, PhysicalPosition, PhysicalSize};

pub fn log_startup(msg: &str) {
    let log_path = std::env::temp_dir().join("StickerMemo-rust.log");
    let line = format!("[{}] [PID:{}] {}\n", chrono::Local::now().format("%Y-%m-%d %H:%M:%S%.3f"), std::process::id(), msg);
    let _ = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(log_path)
        .and_then(|mut f| std::io::Write::write_all(&mut f, line.as_bytes()));
}

pub fn perform_clean_exit(app: &AppHandle) {
    log_startup("=== perform_clean_exit called: closing all windows and terminating ===");
    for (label, win) in app.webview_windows() {
        log_startup(&format!("Destroying window: {}", label));
        let _ = win.destroy();
    }
    std::thread::sleep(std::time::Duration::from_millis(50));
    log_startup("Process exiting cleanly with code 0");
    std::process::exit(0);
}

/// Resolves the locale to use at startup.
///
/// - An explicit saved preference ("ko"/"en"/"ja") is normalized and used
///   as-is.
/// - "system" (or no saved preference at all, i.e. first run) re-detects
///   the current Windows UI locale via `platform::detect_windows_ui_locale`.
///   First run persists "system" explicitly, so subsequent launches keep
///   following the OS locale until the user picks an explicit language in
///   Settings.
fn resolve_initial_locale(db: &Database) -> String {
    match db.get_setting("locale") {
        Ok(Some(saved)) if saved == "system" => {
            i18n::normalize_locale(&platform::detect_windows_ui_locale()).to_string()
        }
        Ok(Some(saved)) => i18n::normalize_locale(&saved).to_string(),
        _ => {
            let _ = db.set_setting("locale", "system");
            i18n::normalize_locale(&platform::detect_windows_ui_locale()).to_string()
        }
    }
}

fn main() {
    log_startup("=== StickerMemo Rust Launching ===");

    // 1. Single-instance check: if another instance is running, wake it and exit
    let _instance_guard = match check_single_instance() {
        Some(guard) => {
            log_startup("Single-instance mutex acquired successfully");
            guard
        }
        None => {
            log_startup("Another instance is already running. Signaled wakeup and exiting cleanly.");
            return;
        }
    };

    // 2. Storage initialization
    let db = match Database::new() {
        Ok(database) => {
            log_startup("Database initialized successfully");
            Arc::new(database)
        }
        Err(e) => {
            log_startup(&format!("[CRITICAL] Failed to initialize database: {}", e));
            return;
        }
    };

    // 3. Locale resolution (must happen before the tray menu is built, since
    // the tray is native and has no WebView to ask navigator.language later).
    let initial_locale = resolve_initial_locale(&db);
    log_startup(&format!("Resolved initial locale: {}", initial_locale));

    let app_state = AppState {
        db: Arc::clone(&db),
        locale: Mutex::new(initial_locale.clone()),
    };

    tauri::Builder::default()
        .manage(app_state)
        .invoke_handler(tauri::generate_handler![
            commands::get_active_notes,
            commands::get_archived_notes,
            commands::create_note,
            commands::save_note,
            commands::delete_note,
            commands::archive_note,
            commands::restore_note,
            commands::open_floating_note,
            commands::close_floating_note,
            commands::get_note_by_id,
            commands::get_current_note,
            commands::get_autostart,
            commands::set_autostart_setting,
            commands::import_sticky_notes_cmd,
            commands::exit_app,
            commands::log_front,
            commands::toggle_deck,
            commands::set_deck_interaction_state,
            commands::start_dragging,
            commands::set_note_window_size,
            commands::get_note_window_size,
            commands::get_note_window_position,
            commands::get_locale_info,
            commands::set_locale,
        ])
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { .. } = event {
                let label = window.label();
                if label.starts_with("note-") {
                    let id = label.strip_prefix("note-").unwrap_or(label).to_string();
                    let app = window.app_handle().clone();
                    let state = app.state::<AppState>();
                    if let Ok(mut notes) = state.db.get_all_active_notes() {
                        if let Some(note) = notes.iter_mut().find(|n| n.id == id) {
                            note.is_floating = false;
                            let _ = state.db.save_or_update_note(note);
                        }
                    }
                    let _ = app.emit("floating-state-changed", (&id, false));
                    crate::log_startup(&format!("CloseRequested on {}: destroying window and updating is_floating=false", label));
                    let _ = window.destroy();
                }
            }
        })
        .setup(move |app| {
            log_startup("Tauri setup hook entered");
            let handle = app.handle().clone();

            // Setup listener for wakeup signal from duplicate launches
            let wakeup_handle = handle.clone();
            platform::listen_for_wakeup_signal(move || {
                if let Some(deck) = wakeup_handle.get_webview_window("deck") {
                    let _ = deck.show();
                    let _ = deck.unminimize();
                    let _ = deck.set_focus();
                }
            });

            // 4. Position Deck Window on Screen Right Edge
            if let Some(deck) = app.get_webview_window("deck") {
                if let Ok(Some(monitor)) = deck.primary_monitor() {
                    let work_area = monitor.size();
                    let deck_width = 16;
                    let deck_height = 320.min((work_area.height as f64 * 0.75) as u32);
                    let _ = deck.set_size(PhysicalSize::new(deck_width, deck_height));

                    let target_x = (work_area.width as i32) - (deck_width as i32);
                    let target_y = ((work_area.height as i32) - (deck_height as i32)) / 2;
                    let _ = deck.set_position(PhysicalPosition::new(target_x, target_y.max(0)));
                    let _ = deck.show();
                    log_startup(&format!("Deck window positioned at ({}, {}) size ({}, {})", target_x, target_y, deck_width, deck_height));
                }
            }

            // 5. Setup System Tray
            let tray_handles = setup_tray(&handle, &initial_locale)?;
            app.manage(tray_handles);
            log_startup("System tray initialized successfully");

            // 6. Restore Floating Notes
            if let Ok(active_notes) = db.get_all_active_notes() {
                let floating_notes: Vec<_> = active_notes.into_iter().filter(|n| n.is_floating).collect();
                log_startup(&format!("Restoring {} floating notes", floating_notes.len()));

                for note in floating_notes {
                    let _ = commands::open_floating_note(handle.clone(), app.state::<AppState>(), note.id);
                }
            }

            log_startup("Tauri setup completed successfully");
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running sticker-memo");
}

/// Handles to the tray's individual menu items, kept around (via
/// `app.manage(...)`) so [`apply_tray_texts`] can retranslate them in
/// place with `MenuItem::set_text` on a locale change, instead of tearing
/// down and rebuilding the whole native tray menu.
pub struct TrayHandles {
    new_note: MenuItem<tauri::Wry>,
    toggle_deck: MenuItem<tauri::Wry>,
    import: MenuItem<tauri::Wry>,
    autostart: MenuItem<tauri::Wry>,
    settings: MenuItem<tauri::Wry>,
    about: MenuItem<tauri::Wry>,
    quit: MenuItem<tauri::Wry>,
}

/// Retranslates every tray menu item's text in place for `locale`. The
/// autostart item's ON/OFF suffix is recomputed from the actual current
/// registry state every time (not cached), so it can never go stale after
/// a toggle.
pub fn apply_tray_texts(app: &AppHandle, locale: &str) {
    let handles = app.state::<TrayHandles>();
    let _ = handles.new_note.set_text(i18n::t(locale, "tray.new_note"));
    let _ = handles.toggle_deck.set_text(i18n::t(locale, "tray.toggle_deck"));
    let _ = handles.import.set_text(i18n::t(locale, "tray.import_sticky_notes"));
    let autostart_key = if platform::is_run_at_startup() { "tray.autostart_on" } else { "tray.autostart_off" };
    let _ = handles.autostart.set_text(i18n::t(locale, autostart_key));
    let _ = handles.settings.set_text(i18n::t(locale, "tray.settings"));
    let _ = handles.about.set_text(i18n::t(locale, "tray.about"));
    let _ = handles.quit.set_text(i18n::t(locale, "tray.quit"));
}

fn setup_tray(app: &AppHandle, locale: &str) -> Result<TrayHandles, Box<dyn std::error::Error>> {
    let new_note_item = MenuItem::with_id(app, "new_note", i18n::t(locale, "tray.new_note"), true, None::<&str>)?;
    let toggle_deck_item = MenuItem::with_id(app, "toggle_deck", i18n::t(locale, "tray.toggle_deck"), true, None::<&str>)?;
    let import_item = MenuItem::with_id(app, "import", i18n::t(locale, "tray.import_sticky_notes"), true, None::<&str>)?;
    let autostart_key = if platform::is_run_at_startup() { "tray.autostart_on" } else { "tray.autostart_off" };
    let autostart_item = MenuItem::with_id(app, "autostart", i18n::t(locale, autostart_key), true, None::<&str>)?;
    let settings_item = MenuItem::with_id(app, "settings", i18n::t(locale, "tray.settings"), true, None::<&str>)?;
    let about_item = MenuItem::with_id(app, "about", i18n::t(locale, "tray.about"), true, None::<&str>)?;
    let quit_item = MenuItem::with_id(app, "quit", i18n::t(locale, "tray.quit"), true, None::<&str>)?;

    let menu = Menu::with_items(
        app,
        &[
            &new_note_item,
            &toggle_deck_item,
            &import_item,
            &autostart_item,
            &settings_item,
            &about_item,
            &quit_item,
        ],
    )?;

    let _tray = TrayIconBuilder::new()
        .icon(app.default_window_icon().cloned().unwrap())
        .menu(&menu)
        .show_menu_on_left_click(false)
        .tooltip("StickerMemo")
        .on_menu_event(|app, event| match event.id.as_ref() {
            "new_note" => {
                let state = app.state::<AppState>();
                if let Ok(note) = commands::create_note(state) {
                    let _ = commands::open_floating_note(app.clone(), app.state::<AppState>(), note.id);
                }
            }
            "toggle_deck" => {
                commands::toggle_deck(app.clone());
            }
            "import" => {
                let _ = commands::import_sticky_notes_cmd(app.clone(), app.state::<AppState>());
            }
            "autostart" => {
                let current = platform::is_run_at_startup();
                let locale = app.state::<AppState>().current_locale();
                let _ = platform::set_run_at_startup(!current, &locale);
                // Refresh the [ON]/[OFF] suffix immediately so the menu
                // never shows a stale state after the user toggles it.
                apply_tray_texts(app, &locale);
            }
            "settings" => {
                if let Some(window) = app.get_webview_window("settings") {
                    let _ = window.show(); let _ = window.unminimize(); let _ = window.set_focus();
                } else {
                    let locale = app.state::<AppState>().current_locale();
                    let _ = tauri::WebviewWindowBuilder::new(app, "settings", tauri::WebviewUrl::App("settings.html".into()))
                        .title(i18n::t(&locale, "settings.window_title"))
                        .inner_size(300.0, 300.0)
                        .min_inner_size(300.0, 300.0)
                        .resizable(false)
                        .decorations(false)
                        .center()
                        .build();
                }
            }
            "about" => {
                if let Some(window) = app.get_webview_window("about") {
                    let _ = window.show(); let _ = window.unminimize(); let _ = window.set_focus();
                } else {
                    let locale = app.state::<AppState>().current_locale();
                    // 340x340 (was 320x310): gives translated credits text
                    // (which no longer relies on a hardcoded <br> line break)
                    // safe room in en/ja without resizing the window.
                    let _ = tauri::WebviewWindowBuilder::new(app, "about", tauri::WebviewUrl::App("about.html".into()))
                        .title(i18n::t(&locale, "about.window_title"))
                        .inner_size(340.0, 340.0)
                        .min_inner_size(340.0, 340.0)
                        .resizable(false)
                        .decorations(false)
                        .center()
                        .build();
                }
            }
            "quit" => {
                perform_clean_exit(app);
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
                let app = tray.app_handle();
                commands::toggle_deck(app.clone());
            }
        })
        .build(app)?;

    Ok(TrayHandles {
        new_note: new_note_item,
        toggle_deck: toggle_deck_item,
        import: import_item,
        autostart: autostart_item,
        settings: settings_item,
        about: about_item,
        quit: quit_item,
    })
}
