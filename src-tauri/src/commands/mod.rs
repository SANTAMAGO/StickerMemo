use crate::domain::NoteModel;
use crate::i18n;
use crate::importer::{import_sticky_notes as do_import, ImportResult};
use crate::platform::{is_run_at_startup, set_run_at_startup};
use crate::storage::Database;
use chrono::Local;
use serde::Serialize;
use std::sync::{Arc, Mutex};
use tauri::{AppHandle, Emitter, Manager, State, WebviewUrl, WebviewWindowBuilder};

pub struct AppState {
    pub db: Arc<Database>,
    /// Effective UI locale ("ko" | "en" | "ja"), resolved once at startup
    /// (see `main::resolve_initial_locale`) and updated in place whenever
    /// `set_locale` runs. This is the in-memory cache the rest of the app
    /// reads; the persisted preference (which may be "system") lives in
    /// the `Settings` table via `Database::get_setting`/`set_setting`.
    pub locale: Mutex<String>,
}

impl AppState {
    pub fn current_locale(&self) -> String {
        self.locale.lock().unwrap().clone()
    }
}

/// Builds the floating note window title for the given locale, matching
/// the same "untitled" vs "with title" distinction used by both
/// `save_note` and `open_floating_note`.
fn note_window_title(locale: &str, title: &str) -> String {
    let trimmed = title.trim();
    if trimmed.is_empty() {
        i18n::t(locale, "note.window_title_untitled")
    } else {
        i18n::tf(locale, "note.window_title_with_title", &[("title", trimmed)])
    }
}

#[tauri::command]
pub fn get_active_notes(state: State<'_, AppState>) -> Result<Vec<NoteModel>, String> {
    let mut notes = state
        .db
        .get_all_active_notes()
        .map_err(|e| e.to_string())?;

    if notes.is_empty() {
        let archived = state
            .db
            .get_all_archived_notes()
            .map_err(|e| e.to_string())?;
        if archived.is_empty() {
            // Create a welcome note matching the original WPF welcome note,
            // in whatever locale is active for this install/session.
            let locale = state.current_locale();
            let mut welcome = NoteModel::new();
            welcome.title = i18n::t(&locale, "seed.welcome_title");
            welcome.text = i18n::t(&locale, "seed.welcome_body");
            welcome.theme_id = "Yellow".into();
            welcome.sort_order = 0;
            welcome.recompute_previews();
            let _ = state.db.save_or_update_note(&welcome);
            notes.push(welcome);
        }
    }

    Ok(notes)
}

#[tauri::command]
pub fn get_archived_notes(state: State<'_, AppState>) -> Result<Vec<NoteModel>, String> {
    state
        .db
        .get_all_archived_notes()
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn create_note(state: State<'_, AppState>) -> Result<NoteModel, String> {
    let note = NoteModel::new();
    state
        .db
        .save_or_update_note(&note)
        .map_err(|e| e.to_string())?;
    Ok(note)
}

#[tauri::command]
pub fn save_note(
    app: AppHandle,
    state: State<'_, AppState>,
    mut note: NoteModel,
) -> Result<NoteModel, String> {
    note.updated_at = Local::now().to_rfc3339();
    note.recompute_previews();
    state
        .db
        .save_or_update_note(&note)
        .map_err(|e| e.to_string())?;

    // Notify deck to update tab preview in real-time
    let _ = app.emit("note-updated", &note);

    let label = format!("note-{}", note.id);
    if let Some(win) = app.get_webview_window(&label) {
        let locale = state.current_locale();
        let win_title = note_window_title(&locale, &note.title);
        let _ = win.set_title(&win_title);
    }

    Ok(note)
}

#[tauri::command]
pub fn delete_note(
    app: AppHandle,
    state: State<'_, AppState>,
    id: String,
) -> Result<(), String> {
    state.db.delete_note(&id).map_err(|e| e.to_string())?;
    crate::log_startup(&format!("delete_note: DB delete committed for {}", id));
    match app.emit("note-deleted", &id) {
        Ok(()) => crate::log_startup(&format!("delete_note: event emitted for {}", id)),
        Err(e) => crate::log_startup(&format!("delete_note: event error for {}: {}", id, e)),
    }
    Ok(())
}

#[tauri::command]
pub fn archive_note(
    app: AppHandle,
    state: State<'_, AppState>,
    id: String,
) -> Result<(), String> {
    let mut notes = state
        .db
        .get_all_active_notes()
        .map_err(|e| e.to_string())?;
    if let Some(note) = notes.iter_mut().find(|n| n.id == id) {
        note.is_archived = true;
        note.is_floating = false;
        note.updated_at = Local::now().to_rfc3339();
        state
            .db
            .save_or_update_note(note)
            .map_err(|e| e.to_string())?;

        let label = format!("note-{}", id);
        if let Some(win) = app.get_webview_window(&label) {
            let _ = win.destroy();
        }

        let _ = app.emit("notes-changed", ());
    }
    Ok(())
}

#[tauri::command]
pub fn restore_note(
    app: AppHandle,
    state: State<'_, AppState>,
    id: String,
) -> Result<(), String> {
    let mut notes = state
        .db
        .get_all_archived_notes()
        .map_err(|e| e.to_string())?;
    if let Some(note) = notes.iter_mut().find(|n| n.id == id) {
        note.is_archived = false;
        note.is_floating = false;
        note.updated_at = Local::now().to_rfc3339();
        state
            .db
            .save_or_update_note(note)
            .map_err(|e| e.to_string())?;

        let _ = app.emit("notes-changed", ());
    }
    Ok(())
}

#[tauri::command(async)]
pub fn open_floating_note(
    app: AppHandle,
    state: State<'_, AppState>,
    id: String,
) -> Result<(), String> {
    crate::log_startup(&format!("open_floating_note called for ID: {}", id));
    let label = format!("note-{}", id);
    let locale = state.current_locale();

    if let Some(existing) = app.get_webview_window(&label) {
        crate::log_startup(&format!("Existing window found for label: {}, showing and bringing to front", label));

        // Update DB is_floating = true
        if let Ok(mut notes) = state.db.get_all_active_notes() {
            if let Some(note) = notes.iter_mut().find(|n| n.id == id) {
                note.is_floating = true;
                let _ = state.db.save_or_update_note(note);
            }
        }
        let _ = app.emit("floating-state-changed", (&id, true));

        let _ = existing.unminimize();
        let _ = existing.show();
        let _ = existing.set_focus();
        let _ = existing.set_always_on_top(true);
        return Ok(());
    }

    let mut note = if let Ok(active) = state.db.get_all_active_notes() {
        if let Some(n) = active.into_iter().find(|n| n.id == id) {
            Some(n)
        } else if let Ok(archived) = state.db.get_all_archived_notes() {
            archived.into_iter().find(|n| n.id == id)
        } else {
            None
        }
    } else {
        None
    }.ok_or_else(|| {
        let msg = i18n::tf(&locale, "error.note_not_found", &[("id", &id)]);
        crate::log_startup(&msg);
        msg
    })?;

    note.is_floating = true;
    state
        .db
        .save_or_update_note(&note)
        .map_err(|e| e.to_string())?;

    // Use clean filename without invalid NTFS query parameters
    let url = WebviewUrl::App("note.html".into());
    let width = if note.width >= 280.0 { note.width } else { 350.0 };
    let height = if note.height >= 180.0 { note.height } else { 350.0 };

    let win_title = note_window_title(&locale, &note.title);

    let mut builder = WebviewWindowBuilder::new(&app, &label, url)
        .title(&win_title)
        .inner_size(width, height)
        .decorations(false)
        .transparent(true)
        .always_on_top(true)
        .skip_taskbar(true)
        .shadow(false);

    if let (Some(mut x), Some(mut y)) = (note.x, note.y) {
        if let Ok(Some(mon)) = app.primary_monitor() {
            let size = mon.size();
            let max_x = (size.width as f64 - width).max(0.0);
            let max_y = (size.height as f64 - height).max(0.0);
            x = x.clamp(0.0, max_x);
            y = y.clamp(0.0, max_y);
        }
        builder = builder.position(x, y);
    } else {
        builder = builder.center();
    }

    let win = builder.build().map_err(|e| {
        let err_msg = format!("Failed to build floating note window {}: {}", label, e);
        crate::log_startup(&err_msg);
        err_msg
    })?;

    if note.x.is_none() || note.y.is_none() {
        if let Ok(pos) = win.outer_position() {
            note.x = Some(pos.x as f64);
            note.y = Some(pos.y as f64);
            let _ = state.db.save_or_update_note(&note);
        }
    }

    let _ = win.unminimize();
    let _ = win.show();
    let _ = win.set_focus();
    let _ = win.set_always_on_top(true);

    crate::log_startup(&format!("Floating note window {} created and shown successfully", label));
    let _ = app.emit("floating-state-changed", (&id, true));
    Ok(())
}

#[tauri::command]
pub fn get_current_note(
    window: tauri::WebviewWindow,
    state: State<'_, AppState>,
) -> Result<NoteModel, String> {
    let label = window.label();
    let id = label.strip_prefix("note-").unwrap_or(label);
    crate::log_startup(&format!("get_current_note called from window label: {}, resolved id: {}", label, id));
    get_note_by_id(state, id.to_string())
}

#[tauri::command]
pub fn close_floating_note(
    app: AppHandle,
    state: State<'_, AppState>,
    id: String,
) -> Result<(), String> {
    crate::log_startup(&format!("close_floating_note called for ID: {}", id));
    let label = format!("note-{}", id);
    if let Some(win) = app.get_webview_window(&label) {
        crate::log_startup(&format!("Destroying floating note window: {}", label));
        let _ = win.destroy();
    }

    let mut notes = state
        .db
        .get_all_active_notes()
        .map_err(|e| e.to_string())?;
    if let Some(note) = notes.iter_mut().find(|n| n.id == id) {
        note.is_floating = false;
        let _ = state.db.save_or_update_note(note);
    }
    let _ = app.emit("floating-state-changed", (&id, false));
    crate::log_startup(&format!("Floating note window {} destroyed and DB updated (is_floating=false)", label));
    Ok(())
}

#[tauri::command]
pub fn get_note_by_id(state: State<'_, AppState>, id: String) -> Result<NoteModel, String> {
    let active = state
        .db
        .get_all_active_notes()
        .map_err(|e| e.to_string())?;
    if let Some(n) = active.into_iter().find(|n| n.id == id) {
        return Ok(n);
    }
    let archived = state
        .db
        .get_all_archived_notes()
        .map_err(|e| e.to_string())?;
    if let Some(n) = archived.into_iter().find(|n| n.id == id) {
        return Ok(n);
    }
    Err("Note not found".into())
}

#[tauri::command]
pub fn get_autostart() -> bool {
    is_run_at_startup()
}

#[tauri::command]
pub fn set_autostart_setting(state: State<'_, AppState>, enable: bool) -> Result<(), String> {
    let locale = state.current_locale();
    set_run_at_startup(enable, &locale)
}

#[tauri::command]
pub fn import_sticky_notes_cmd(
    app: AppHandle,
    state: State<'_, AppState>,
) -> Result<ImportResult, String> {
    let existing_ids = state
        .db
        .get_existing_remote_ids()
        .map_err(|e| e.to_string())?;
    let locale = state.current_locale();
    let result = do_import(&existing_ids, &locale);

    if result.success && !result.notes.is_empty() {
        let _ = state.db.save_or_update_notes_batch(&result.notes);
        let _ = app.emit("notes-changed", ());
    }

    Ok(result)
}

#[tauri::command]
pub fn exit_app(app: AppHandle) {
    crate::perform_clean_exit(&app);
}

#[tauri::command]
pub fn log_front(msg: String) {
    crate::log_startup(&format!("[FRONT] {}", msg));
}

#[tauri::command]
pub fn toggle_deck(app: AppHandle) {
    if let Some(deck) = app.get_webview_window("deck") {
        if let Ok(is_visible) = deck.is_visible() {
            if is_visible {
                let _ = deck.hide();
            } else {
                let _ = deck.show();
                let _ = deck.set_focus();
            }
        }
    }
}

#[tauri::command]
pub fn set_deck_interaction_state(
    window: tauri::WebviewWindow,
    state: String,
    width: f64,
    height: f64,
) -> Result<(), String> {
    if window.label() != "deck" {
        return Err("set_deck_interaction_state is only valid for the deck window".into());
    }
    let scale = window.scale_factor().map_err(|e| e.to_string())?;
    let physical_width = (width.max(1.0) * scale).round() as u32;
    let physical_height = (height.max(1.0) * scale).round() as u32;
    let old_pos = window.outer_position().map_err(|e| e.to_string())?;
    let old_size = window.outer_size().map_err(|e| e.to_string())?;
    let right = old_pos.x + old_size.width as i32;
    let center_y = old_pos.y + (old_size.height as i32 / 2);
    let new_x = right - physical_width as i32;
    let new_y = center_y - (physical_height as i32 / 2);

    window.set_size(tauri::PhysicalSize::new(physical_width, physical_height)).map_err(|e| e.to_string())?;
    window.set_position(tauri::PhysicalPosition::new(new_x, new_y.max(0))).map_err(|e| e.to_string())?;
    // The deck must stay always-on-top in every interaction state, including
    // "dormant" (the thin 16px stripe). Only the native window's bounds
    // change size/position between states; always-on-top must never be
    // dropped, or the dormant stripe sinks behind other windows.
    let expanding = state != "dormant";
    window.set_always_on_top(true).map_err(|e| e.to_string())?;
    if expanding {
        let _ = window.show();
        let _ = window.set_focus();
    }
    crate::log_startup(&format!("Deck interaction state={} bounds={}x{} scale={} topmost=true", state, physical_width, physical_height, scale));
    Ok(())
}
#[tauri::command]
pub fn start_dragging(window: tauri::WebviewWindow) -> Result<(), String> {
    window.start_dragging().map_err(|e| e.to_string())
}

#[tauri::command]
pub fn set_note_window_size(window: tauri::WebviewWindow, width: f64, height: f64) -> Result<(), String> {
    window.set_size(tauri::PhysicalSize::new(width.max(280.0) as u32, height.max(180.0) as u32))
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn get_note_window_size(window: tauri::WebviewWindow) -> Result<(f64, f64), String> {
    let size = window.inner_size().map_err(|e| e.to_string())?;
    Ok((size.width as f64, size.height as f64))
}

#[tauri::command]
pub fn get_note_window_position(window: tauri::WebviewWindow) -> Result<(f64, f64), String> {
    let pos = window.outer_position().map_err(|e| e.to_string())?;
    Ok((pos.x as f64, pos.y as f64))
}

/// What the WebView needs to know about the current locale: the
/// *effective* code actually in use ("ko"/"en"/"ja", always one of
/// `i18n::SUPPORTED_LOCALES`), and the raw *preference* as stored in
/// Settings ("system" or an explicit code), so the Settings screen can
/// show the right radio button selected even when it's following the OS.
#[derive(Serialize)]
pub struct LocaleInfo {
    pub effective: String,
    pub preference: String,
}

#[tauri::command]
pub fn get_locale_info(state: State<'_, AppState>) -> Result<LocaleInfo, String> {
    let preference = state
        .db
        .get_setting("locale")
        .map_err(|e| e.to_string())?
        .unwrap_or_else(|| "system".to_string());
    Ok(LocaleInfo {
        effective: state.current_locale(),
        preference,
    })
}

/// Applies `locale` to every currently open window's OS-level title
/// (floating notes, About, Settings) and to the tray menu, then broadcasts
/// `locale-changed` so each open WebView can reload its own dictionary and
/// retranslate its static markup. Called both from `set_locale` (user
/// picked a language) and indirectly covers the "System Default" case
/// since the caller already resolved `locale` before invoking this.
fn retranslate_everything(app: &AppHandle, state: &State<'_, AppState>, locale: &str) {
    crate::apply_tray_texts(app, locale);

    let mut notes_by_id = std::collections::HashMap::new();
    if let Ok(active) = state.db.get_all_active_notes() {
        for n in active {
            notes_by_id.insert(n.id.clone(), n);
        }
    }
    if let Ok(archived) = state.db.get_all_archived_notes() {
        for n in archived {
            notes_by_id.entry(n.id.clone()).or_insert(n);
        }
    }

    for (label, win) in app.webview_windows() {
        if let Some(id) = label.strip_prefix("note-") {
            if let Some(note) = notes_by_id.get(id) {
                let _ = win.set_title(&note_window_title(locale, &note.title));
            }
        } else if label == "about" {
            let _ = win.set_title(&i18n::t(locale, "about.window_title"));
        } else if label == "settings" {
            let _ = win.set_title(&i18n::t(locale, "settings.window_title"));
        }
    }

    let _ = app.emit("locale-changed", locale);
}

#[tauri::command]
pub fn set_locale(
    app: AppHandle,
    state: State<'_, AppState>,
    preference: String,
) -> Result<(), String> {
    let pref = preference.trim();
    let valid = pref == "system" || i18n::SUPPORTED_LOCALES.contains(&pref);
    if !valid {
        return Err(format!("Unsupported locale preference: {}", pref));
    }

    state
        .db
        .set_setting("locale", pref)
        .map_err(|e| e.to_string())?;

    let effective = if pref == "system" {
        i18n::normalize_locale(&crate::platform::detect_windows_ui_locale()).to_string()
    } else {
        i18n::normalize_locale(pref).to_string()
    };
    *state.locale.lock().unwrap() = effective.clone();

    retranslate_everything(&app, &state, &effective);
    Ok(())
}
