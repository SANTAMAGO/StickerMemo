use crate::domain::NoteModel;
use crate::importer::{import_sticky_notes as do_import, ImportResult};
use crate::platform::{is_run_at_startup, set_run_at_startup};
use crate::storage::Database;
use chrono::Local;
use std::sync::Arc;
use tauri::{AppHandle, Emitter, Manager, State, WebviewUrl, WebviewWindowBuilder};

pub struct AppState {
    pub db: Arc<Database>,
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
            // Create Welcome Note matching WPF welcome note
            let mut welcome = NoteModel::new();
            welcome.title = "반가워요!".into();
            welcome.text = "📌 StickerMemo Edge Deck입니다.\n\n• 평소에는 화면 오른쪽 끝에 얇은 띠(Stripe)로 숨어 있습니다.\n• 마우스를 탭 위로 올리면 본문 3줄 미리보기가 펼쳐집니다.\n• 메모 탭을 클릭하면 화면 어디든 자유롭게 드래그 배치할 수 있는 Floating Note로 열립니다.\n• Floating Note 상단 🔤 버튼으로 글꼴/크기/굵기를 변경할 수 있습니다.".into();
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
        let win_title = if note.title.trim().is_empty() {
            "메모 (새 메모)".to_string()
        } else {
            format!("메모 - {}", note.title.trim())
        };
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

    // Close floating window if open
    let label = format!("note-{}", id);
    if let Some(win) = app.get_webview_window(&label) {
        let _ = win.close();
    }

    let _ = app.emit("note-deleted", &id);
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
            let _ = win.close();
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

#[tauri::command]
pub fn open_floating_note(
    app: AppHandle,
    state: State<'_, AppState>,
    id: String,
) -> Result<(), String> {
    crate::log_startup(&format!("open_floating_note called for ID: {}", id));
    let label = format!("note-{}", id);

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
        let msg = format!("메모를 찾을 수 없습니다: {}", id);
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

    let win_title = if note.title.trim().is_empty() {
        "메모 (새 메모)".to_string()
    } else {
        format!("메모 - {}", note.title.trim())
    };

    let mut builder = WebviewWindowBuilder::new(&app, &label, url)
        .title(&win_title)
        .inner_size(width, height)
        .decorations(false)
        .transparent(true)
        .always_on_top(note.is_topmost)
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

    let _ = win.unminimize();
    let _ = win.show();
    let _ = win.set_focus();
    let _ = win.set_always_on_top(note.is_topmost);

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
        crate::log_startup(&format!("Hiding floating note window: {}", label));
        let _ = win.hide();
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
    crate::log_startup(&format!("Floating note window {} hidden successfully", label));
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
pub fn set_autostart_setting(enable: bool) -> Result<(), String> {
    set_run_at_startup(enable)
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
    let result = do_import(&existing_ids);

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

