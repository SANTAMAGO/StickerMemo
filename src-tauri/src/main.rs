// Prevents additional console window on Windows in release
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod commands;
mod domain;
mod importer;
mod platform;
mod storage;

use commands::AppState;
use platform::check_single_instance_or_replace;
use storage::Database;
use std::sync::Arc;
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

fn main() {
    log_startup("=== StickerMemo Rust Launching ===");

    // 1. Single-instance check and graceful replacement of prior instance
    let _instance_guard = match check_single_instance_or_replace() {
        Some(guard) => {
            log_startup("Single-instance mutex acquired successfully");
            guard
        }
        None => {
            log_startup("Failed to acquire single-instance mutex; terminating.");
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

    let app_state = AppState { db: Arc::clone(&db) };

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
            commands::start_dragging,
            commands::set_note_window_size,
            commands::get_note_window_size,
            commands::get_note_window_position,
        ])
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                let label = window.label();
                if label.starts_with("note-") {
                    api.prevent_close();
                    let _ = window.hide();
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
                    crate::log_startup(&format!("CloseRequested on {}: intercepted and hidden", label));
                }
            }
        })
        .setup(move |app| {
            log_startup("Tauri setup hook entered");
            let handle = app.handle().clone();

            // Setup listener for exit signal from a newly launched instance
            let exit_handle = handle.clone();
            platform::listen_for_exit_signal(move || {
                perform_clean_exit(&exit_handle);
            });

            // 3. Position Deck Window on Screen Right Edge
            if let Some(deck) = app.get_webview_window("deck") {
                if let Ok(Some(monitor)) = deck.primary_monitor() {
                    let work_area = monitor.size();
                    let deck_width = 460;
                    let deck_height = 640.min((work_area.height as f64 * 0.75) as u32);
                    let _ = deck.set_size(PhysicalSize::new(deck_width, deck_height));

                    let target_x = (work_area.width as i32) - (deck_width as i32);
                    let target_y = ((work_area.height as i32) - (deck_height as i32)) / 2;
                    let _ = deck.set_position(PhysicalPosition::new(target_x, target_y.max(0)));
                    let _ = deck.show();
                    log_startup(&format!("Deck window positioned at ({}, {}) size ({}, {})", target_x, target_y, deck_width, deck_height));
                }
            }

            // 4. Setup System Tray
            setup_tray(&handle)?;
            log_startup("System tray initialized successfully");

            // 5. Restore Floating Notes
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

fn setup_tray(app: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    let new_note_item = MenuItem::with_id(app, "new_note", "➕ 새 메모", true, None::<&str>)?;
    let toggle_deck_item = MenuItem::with_id(app, "toggle_deck", "🗂️ 덱 보이기 / 숨기기", true, None::<&str>)?;
    let import_item = MenuItem::with_id(app, "import", "📥 Sticky Notes 가져오기", true, None::<&str>)?;
    let autostart_item = MenuItem::with_id(
        app,
        "autostart",
        if platform::is_run_at_startup() {
            "🚀 Windows 시작 시 실행 [✓ ON]"
        } else {
            "🚀 Windows 시작 시 실행 [OFF]"
        },
        true,
        None::<&str>,
    )?;
    let quit_item = MenuItem::with_id(app, "quit", "🚪 StickerMemo 종료", true, None::<&str>)?;

    let menu = Menu::with_items(
        app,
        &[
            &new_note_item,
            &toggle_deck_item,
            &import_item,
            &autostart_item,
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
                let _ = platform::set_run_at_startup(!current);
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

    Ok(())
}
