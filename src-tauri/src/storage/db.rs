use crate::domain::NoteModel;
use rusqlite::{params, Connection, Result};
use std::collections::HashSet;
use std::fs;
use std::path::PathBuf;
use std::sync::Mutex;

pub struct Database {
    conn: Mutex<Connection>,
}

pub fn get_db_path() -> PathBuf {
    let app_data = dirs::config_dir().unwrap_or_else(|| PathBuf::from("."));
    app_data.join("StickerMemo").join("AppNotes.db")
}

impl Database {
    pub fn new() -> Result<Self> {
        let db_path = get_db_path();
        if let Some(parent) = db_path.parent() {
            let _ = fs::create_dir_all(parent);
        }

        // Automatic 1-time backup if file exists and backup doesn't exist yet
        if db_path.exists() {
            let backup_path = db_path.with_extension("db.bak");
            if !backup_path.exists() {
                let _ = fs::copy(&db_path, &backup_path);
            }
        }

        let conn = Connection::open(&db_path)?;
        let db = Self {
            conn: Mutex::new(conn),
        };
        db.init_schema()?;
        Ok(db)
    }

    #[cfg(test)]
    pub fn new_in_memory() -> Result<Self> {
        let conn = Connection::open_in_memory()?;
        let db = Self {
            conn: Mutex::new(conn),
        };
        db.init_schema()?;
        Ok(db)
    }


    fn init_schema(&self) -> Result<()> {
        let conn = self.conn.lock().unwrap();

        conn.execute(
            "CREATE TABLE IF NOT EXISTS Notes (
                Id TEXT PRIMARY KEY,
                RemoteId TEXT,
                Title TEXT,
                Text TEXT,
                X REAL,
                Y REAL,
                Width REAL,
                Height REAL,
                ExpandedHeight REAL,
                IsCollapsed INTEGER,
                IsArchived INTEGER DEFAULT 0,
                IsFloating INTEGER DEFAULT 0,
                SortOrder INTEGER DEFAULT 0,
                ThemeId TEXT,
                IsTopmost INTEGER,
                TapeAngle REAL,
                FontFamily TEXT DEFAULT 'Malgun Gothic',
                FontSize REAL DEFAULT 13.5,
                IsBold INTEGER DEFAULT 0,
                CreatedAt TEXT,
                UpdatedAt TEXT
            );",
            [],
        )?;

        // Auto-migration check
        let mut pragma_stmt = conn.prepare("PRAGMA table_info(Notes);")?;
        let columns: HashSet<String> = pragma_stmt
            .query_map([], |row| row.get::<_, String>(1))?
            .filter_map(|r| r.ok())
            .map(|s| s.to_lowercase())
            .collect();

        let migrations = [
            ("remoteid", "ALTER TABLE Notes ADD COLUMN RemoteId TEXT;"),
            ("title", "ALTER TABLE Notes ADD COLUMN Title TEXT;"),
            ("isarchived", "ALTER TABLE Notes ADD COLUMN IsArchived INTEGER DEFAULT 0;"),
            ("isfloating", "ALTER TABLE Notes ADD COLUMN IsFloating INTEGER DEFAULT 0;"),
            ("sortorder", "ALTER TABLE Notes ADD COLUMN SortOrder INTEGER DEFAULT 0;"),
            ("expandedheight", "ALTER TABLE Notes ADD COLUMN ExpandedHeight REAL DEFAULT 340;"),
            ("iscollapsed", "ALTER TABLE Notes ADD COLUMN IsCollapsed INTEGER DEFAULT 0;"),
            ("fontfamily", "ALTER TABLE Notes ADD COLUMN FontFamily TEXT DEFAULT 'Malgun Gothic';"),
            ("fontsize", "ALTER TABLE Notes ADD COLUMN FontSize REAL DEFAULT 13.5;"),
            ("isbold", "ALTER TABLE Notes ADD COLUMN IsBold INTEGER DEFAULT 0;"),
        ];

        for (col, sql) in migrations {
            if !columns.contains(col) {
                let _ = conn.execute(sql, []);
            }
        }

        Ok(())
    }

    pub fn get_all_active_notes(&self) -> Result<Vec<NoteModel>> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare(
            "SELECT Id, RemoteId, Title, Text, X, Y, Width, Height, ExpandedHeight,
                    IsCollapsed, IsArchived, IsFloating, SortOrder, ThemeId, IsTopmost,
                    TapeAngle, FontFamily, FontSize, IsBold, CreatedAt, UpdatedAt
             FROM Notes
             WHERE IsArchived = 0
             ORDER BY SortOrder ASC, UpdatedAt DESC;",
        )?;

        let note_iter = stmt.query_map([], Self::map_row_to_note)?;
        let mut notes = Vec::new();
        for n in note_iter.flatten() {
            notes.push(n);
        }
        Ok(notes)
    }

    pub fn get_all_archived_notes(&self) -> Result<Vec<NoteModel>> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare(
            "SELECT Id, RemoteId, Title, Text, X, Y, Width, Height, ExpandedHeight,
                    IsCollapsed, IsArchived, IsFloating, SortOrder, ThemeId, IsTopmost,
                    TapeAngle, FontFamily, FontSize, IsBold, CreatedAt, UpdatedAt
             FROM Notes
             WHERE IsArchived = 1
             ORDER BY UpdatedAt DESC;",
        )?;

        let note_iter = stmt.query_map([], Self::map_row_to_note)?;
        let mut notes = Vec::new();
        for n in note_iter.flatten() {
            notes.push(n);
        }
        Ok(notes)
    }

    fn map_row_to_note(row: &rusqlite::Row) -> rusqlite::Result<NoteModel> {
        let is_collapsed_int: i32 = row.get(9).unwrap_or(0);
        let is_archived_int: i32 = row.get(10).unwrap_or(0);
        let is_floating_int: i32 = row.get(11).unwrap_or(0);
        let is_topmost_int: i32 = row.get(14).unwrap_or(1);
        let is_bold_int: i32 = row.get(18).unwrap_or(0);

        let mut note = NoteModel {
            id: row.get(0)?,
            remote_id: row.get(1).ok(),
            title: row.get(2).unwrap_or_default(),
            text: row.get(3).unwrap_or_default(),
            x: row.get(4).ok(),
            y: row.get(5).ok(),
            width: row.get(6).unwrap_or(350.0),
            height: row.get(7).unwrap_or(350.0),
            expanded_height: row.get(8).unwrap_or(350.0),
            is_collapsed: is_collapsed_int != 0,
            is_archived: is_archived_int != 0,
            is_floating: is_floating_int != 0,
            sort_order: row.get(12).unwrap_or(0),
            theme_id: row.get(13).unwrap_or_else(|_| "Yellow".into()),
            is_topmost: is_topmost_int != 0,
            tape_angle: row.get(15).unwrap_or(-0.6),
            font_family: row.get(16).unwrap_or_else(|_| "Malgun Gothic".into()),
            font_size: row.get(17).unwrap_or(13.5),
            is_bold: is_bold_int != 0,
            created_at: row.get(19).unwrap_or_default(),
            updated_at: row.get(20).unwrap_or_default(),
            display_main_title: String::new(),
            display_sub_preview: None,
            display_hover_body_preview: String::new(),
        };
        note.recompute_previews();
        Ok(note)
    }

    pub fn save_or_update_note(&self, note: &NoteModel) -> Result<()> {
        let conn = self.conn.lock().unwrap();
        let sql = "
            INSERT INTO Notes (
                Id, RemoteId, Title, Text, X, Y, Width, Height, ExpandedHeight,
                IsCollapsed, IsArchived, IsFloating, SortOrder, ThemeId, IsTopmost,
                TapeAngle, FontFamily, FontSize, IsBold, CreatedAt, UpdatedAt
            )
            VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20, ?21)
            ON CONFLICT(Id) DO UPDATE SET
                RemoteId = excluded.RemoteId,
                Title = excluded.Title,
                Text = excluded.Text,
                X = excluded.X,
                Y = excluded.Y,
                Width = excluded.Width,
                Height = excluded.Height,
                ExpandedHeight = excluded.ExpandedHeight,
                IsCollapsed = excluded.IsCollapsed,
                IsArchived = excluded.IsArchived,
                IsFloating = excluded.IsFloating,
                SortOrder = excluded.SortOrder,
                ThemeId = excluded.ThemeId,
                IsTopmost = excluded.IsTopmost,
                TapeAngle = excluded.TapeAngle,
                FontFamily = excluded.FontFamily,
                FontSize = excluded.FontSize,
                IsBold = excluded.IsBold,
                UpdatedAt = excluded.UpdatedAt;";

        conn.execute(
            sql,
            params![
                note.id,
                note.remote_id,
                note.title,
                note.text,
                note.x,
                note.y,
                note.width,
                note.height,
                note.expanded_height,
                if note.is_collapsed { 1 } else { 0 },
                if note.is_archived { 1 } else { 0 },
                if note.is_floating { 1 } else { 0 },
                note.sort_order,
                note.theme_id,
                if note.is_topmost { 1 } else { 0 },
                note.tape_angle,
                note.font_family,
                note.font_size,
                if note.is_bold { 1 } else { 0 },
                note.created_at,
                note.updated_at,
            ],
        )?;
        Ok(())
    }

    pub fn save_or_update_notes_batch(&self, notes: &[NoteModel]) -> Result<()> {
        let mut conn = self.conn.lock().unwrap();
        let tx = conn.transaction()?;

        let sql = "
            INSERT INTO Notes (
                Id, RemoteId, Title, Text, X, Y, Width, Height, ExpandedHeight,
                IsCollapsed, IsArchived, IsFloating, SortOrder, ThemeId, IsTopmost,
                TapeAngle, FontFamily, FontSize, IsBold, CreatedAt, UpdatedAt
            )
            VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20, ?21)
            ON CONFLICT(Id) DO UPDATE SET
                RemoteId = excluded.RemoteId,
                Title = excluded.Title,
                Text = excluded.Text,
                X = excluded.X,
                Y = excluded.Y,
                Width = excluded.Width,
                Height = excluded.Height,
                ExpandedHeight = excluded.ExpandedHeight,
                IsCollapsed = excluded.IsCollapsed,
                IsArchived = excluded.IsArchived,
                IsFloating = excluded.IsFloating,
                SortOrder = excluded.SortOrder,
                ThemeId = excluded.ThemeId,
                IsTopmost = excluded.IsTopmost,
                TapeAngle = excluded.TapeAngle,
                FontFamily = excluded.FontFamily,
                FontSize = excluded.FontSize,
                IsBold = excluded.IsBold,
                UpdatedAt = excluded.UpdatedAt;";

        for note in notes {
            tx.execute(
                sql,
                params![
                    note.id,
                    note.remote_id,
                    note.title,
                    note.text,
                    note.x,
                    note.y,
                    note.width,
                    note.height,
                    note.expanded_height,
                    if note.is_collapsed { 1 } else { 0 },
                    if note.is_archived { 1 } else { 0 },
                    if note.is_floating { 1 } else { 0 },
                    note.sort_order,
                    note.theme_id,
                    if note.is_topmost { 1 } else { 0 },
                    note.tape_angle,
                    note.font_family,
                    note.font_size,
                    if note.is_bold { 1 } else { 0 },
                    note.created_at,
                    note.updated_at,
                ],
            )?;
        }

        tx.commit()?;
        Ok(())
    }

    pub fn delete_note(&self, id: &str) -> Result<()> {
        let conn = self.conn.lock().unwrap();
        let deleted = conn.execute("DELETE FROM Notes WHERE Id = ?1", params![id])?;
        if deleted == 0 {
            return Err(rusqlite::Error::QueryReturnedNoRows);
        }
        Ok(())
    }

    pub fn get_existing_remote_ids(&self) -> Result<HashSet<String>> {
        let conn = self.conn.lock().unwrap();
        let mut stmt = conn.prepare("SELECT RemoteId FROM Notes WHERE RemoteId IS NOT NULL;")?;
        let rows = stmt.query_map([], |row| row.get::<_, String>(0))?;
        let mut set = HashSet::new();
        for id in rows.flatten() {
            set.insert(id);
        }
        Ok(set)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_db_crud_and_query() {
        let db = Database::new_in_memory().expect("failed to init in-memory db");

        // 1. Initially empty
        let active = db.get_all_active_notes().unwrap();
        assert_eq!(active.len(), 0);

        // 2. Insert Note
        let mut note1 = NoteModel::new();
        note1.title = "Test 1".into();
        note1.text = "Body 1".into();
        note1.theme_id = "Pink".into();
        db.save_or_update_note(&note1).unwrap();

        let active = db.get_all_active_notes().unwrap();
        assert_eq!(active.len(), 1);
        assert_eq!(active[0].title, "Test 1");
        assert_eq!(active[0].theme_id, "Pink");

        // 3. Update Note
        note1.title = "Updated Title".into();
        db.save_or_update_note(&note1).unwrap();
        let active = db.get_all_active_notes().unwrap();
        assert_eq!(active.len(), 1);
        assert_eq!(active[0].title, "Updated Title");

        // 4. Archive Note
        note1.is_archived = true;
        db.save_or_update_note(&note1).unwrap();
        assert_eq!(db.get_all_active_notes().unwrap().len(), 0);
        assert_eq!(db.get_all_archived_notes().unwrap().len(), 1);

        // 5. Delete Note
        db.delete_note(&note1.id).unwrap();
        assert_eq!(db.get_all_archived_notes().unwrap().len(), 0);
    }
}

