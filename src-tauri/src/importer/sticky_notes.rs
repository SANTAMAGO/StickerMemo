use super::rtf_cleaner::clean_sticky_note_text;
use crate::domain::{map_windows_sticky_theme, NoteModel};
use crate::i18n;
use chrono::Local;
use rusqlite::Connection;
use serde::Serialize;
use std::collections::HashSet;
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Serialize)]
pub struct ImportResult {
    pub success: bool,
    pub imported_count: usize,
    pub skipped_count: usize,
    pub message: String,
    pub notes: Vec<NoteModel>,
}

pub fn get_sticky_notes_db_path() -> PathBuf {
    let local_app_data = dirs::data_local_dir().unwrap_or_else(|| PathBuf::from("."));
    local_app_data
        .join("Packages")
        .join("Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe")
        .join("LocalState")
        .join("plum.sqlite")
}

#[allow(dead_code)]
pub fn is_sticky_notes_available() -> bool {
    get_sticky_notes_db_path().exists()
}


pub fn import_sticky_notes(existing_remote_ids: &HashSet<String>, locale: &str) -> ImportResult {
    let src_db = get_sticky_notes_db_path();
    if !src_db.exists() {
        return ImportResult {
            success: false,
            imported_count: 0,
            skipped_count: 0,
            message: i18n::t(locale, "importer.source_not_found"),
            notes: Vec::new(),
        };
    }

    let temp_dir = std::env::temp_dir().join("StickerMemo").join("Import");
    let _ = fs::create_dir_all(&temp_dir);
    let temp_db = temp_dir.join(format!("plum_{}.sqlite", uuid::Uuid::new_v4().simple()));

    // 1. Copy plum.sqlite and WAL/SHM companion files
    if let Err(e) = fs::copy(&src_db, &temp_db) {
        return ImportResult {
            success: false,
            imported_count: 0,
            skipped_count: 0,
            message: i18n::tf(locale, "importer.temp_copy_failed", &[("e", &e.to_string())]),
            notes: Vec::new(),
        };
    }

    let wal_src = src_db.with_file_name("plum.sqlite-wal");
    if wal_src.exists() {
        let _ = fs::copy(&wal_src, temp_db.with_file_name("plum.sqlite-wal"));
    }
    let shm_src = src_db.with_file_name("plum.sqlite-shm");
    if shm_src.exists() {
        let _ = fs::copy(&shm_src, temp_db.with_file_name("plum.sqlite-shm"));
    }

    // 2. Open read-only
    let res = (|| -> Result<(Vec<NoteModel>, usize), rusqlite::Error> {
        let conn = Connection::open_with_flags(
            &temp_db,
            rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY | rusqlite::OpenFlags::SQLITE_OPEN_URI,
        )?;

        // Check columns
        let mut pragma_stmt = conn.prepare("PRAGMA table_info(Note);")?;
        let columns: HashSet<String> = pragma_stmt
            .query_map([], |row| row.get::<_, String>(1))?
            .filter_map(|r| r.ok())
            .map(|s| s.to_lowercase())
            .collect();

        if !columns.contains("text") {
            return Ok((Vec::new(), 0));
        }

        let id_col = if columns.contains("id") {
            "Id"
        } else if columns.contains("remoteid") {
            "RemoteId"
        } else {
            "NULL"
        };

        let theme_col = if columns.contains("theme") {
            "Theme"
        } else {
            "'Yellow'"
        };

        let where_clause = if columns.contains("deletedat") {
            " WHERE DeletedAt IS NULL"
        } else if columns.contains("isdeleted") {
            " WHERE IsDeleted = 0"
        } else {
            ""
        };

        let sql = format!(
            "SELECT {}, Text, {} FROM Note{};",
            id_col, theme_col, where_clause
        );

        let mut stmt = conn.prepare(&sql)?;
        let rows = stmt.query_map([], |row| {
            let remote_id: Option<String> = row.get(0).ok();
            let raw_text: String = row.get(1).unwrap_or_default();
            let theme: String = row.get(2).unwrap_or_else(|_| "Yellow".into());
            Ok((remote_id, raw_text, theme))
        })?;

        let mut imported = Vec::new();
        let mut skipped = 0;
        let mut seen = existing_remote_ids.clone();

        for item in rows.flatten() {
            let (remote_id, raw_text, theme_name) = item;

            if let Some(ref rid) = remote_id {
                if seen.contains(rid) {
                    skipped += 1;
                    continue;
                }
            }

            let clean_text = clean_sticky_note_text(&raw_text);
            if clean_text.trim().is_empty() {
                continue;
            }

            if let Some(ref rid) = remote_id {
                seen.insert(rid.clone());
            }

            let mut note = NoteModel::new();
            note.remote_id = remote_id;
            note.text = clean_text;
            note.theme_id = map_windows_sticky_theme(Some(&theme_name));
            note.created_at = Local::now().to_rfc3339();
            note.updated_at = Local::now().to_rfc3339();
            note.recompute_previews();

            imported.push(note);
        }

        Ok((imported, skipped))
    })();

    // Cleanup temp files
    let _ = fs::remove_file(&temp_db);
    let _ = fs::remove_file(temp_db.with_file_name("plum.sqlite-wal"));
    let _ = fs::remove_file(temp_db.with_file_name("plum.sqlite-shm"));

    match res {
        Ok((notes, skipped_count)) => {
            let imported_count = notes.len();
            let message = if skipped_count > 0 {
                i18n::tf(
                    locale,
                    "importer.imported_with_skipped",
                    &[("n", &imported_count.to_string()), ("skipped", &skipped_count.to_string())],
                )
            } else {
                i18n::tf(locale, "importer.imported", &[("n", &imported_count.to_string())])
            };
            ImportResult {
                success: true,
                imported_count,
                skipped_count,
                message,
                notes,
            }
        }
        Err(e) => ImportResult {
            success: false,
            imported_count: 0,
            skipped_count: 0,
            message: i18n::tf(locale, "importer.import_failed", &[("e", &e.to_string())]),
            notes: Vec::new(),
        },
    }
}
