use chrono::Local;
use regex::Regex;
use serde::{Deserialize, Serialize};
use std::sync::OnceLock;

static WS_REGEX: OnceLock<Regex> = OnceLock::new();
static MULTI_SPACE_REGEX: OnceLock<Regex> = OnceLock::new();

fn get_ws_regex() -> &'static Regex {
    WS_REGEX.get_or_init(|| Regex::new(r"[\r\n\t]+").unwrap())
}

fn get_multi_space_regex() -> &'static Regex {
    MULTI_SPACE_REGEX.get_or_init(|| Regex::new(r"\s{2,}").unwrap())
}

pub fn clean_preview_string(input: &str) -> String {
    if input.trim().is_empty() {
        return String::new();
    }
    let step1 = get_ws_regex().replace_all(input, " ");
    let step2 = get_multi_space_regex().replace_all(&step1, " ");
    step2.trim().to_string()
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct NoteModel {
    pub id: String,
    pub remote_id: Option<String>,
    pub title: String,
    pub text: String,
    pub x: Option<f64>,
    pub y: Option<f64>,
    pub width: f64,
    pub height: f64,
    pub expanded_height: f64,
    pub is_collapsed: bool,
    pub is_archived: bool,
    pub is_floating: bool,
    pub sort_order: i32,
    pub theme_id: String,
    pub is_topmost: bool,
    pub tape_angle: f64,
    pub font_family: String,
    pub font_size: f64,
    pub is_bold: bool,
    pub created_at: String,
    pub updated_at: String,

    // Previews computed for UI
    pub display_main_title: String,
    pub display_sub_preview: Option<String>,
    pub display_hover_body_preview: String,
}

impl NoteModel {
    pub fn new() -> Self {
        let now = Local::now().to_rfc3339();
        let mut note = Self {
            id: uuid::Uuid::new_v4().to_string(),
            remote_id: None,
            title: String::new(),
            text: String::new(),
            x: None,
            y: None,
            width: 350.0,
            height: 350.0,
            expanded_height: 350.0,
            is_collapsed: false,
            is_archived: false,
            is_floating: false,
            sort_order: 0,
            theme_id: "Yellow".into(),
            is_topmost: true,
            tape_angle: -0.6,
            font_family: "Malgun Gothic".into(),
            font_size: 13.5,
            is_bold: false,
            created_at: now.clone(),
            updated_at: now,
            display_main_title: "새 메모".into(),
            display_sub_preview: None,
            display_hover_body_preview: "(내용이 비어 있습니다)".into(),
        };
        note.recompute_previews();
        note
    }

    pub fn recompute_previews(&mut self) {
        // 1. Main Title
        if !self.title.trim().is_empty() {
            let clean = clean_preview_string(&self.title);
            let char_count = clean.chars().count();
            self.display_main_title = if char_count > 18 {
                let truncated: String = clean.chars().take(16).collect();
                format!("{}…", truncated)
            } else {
                clean
            };
        } else if !self.text.trim().is_empty() {
            let first_line = self
                .text
                .lines()
                .map(|l| l.trim())
                .find(|l| !l.is_empty())
                .unwrap_or("");
            let clean = clean_preview_string(first_line);
            let char_count = clean.chars().count();
            self.display_main_title = if !clean.is_empty() {
                if char_count > 20 {
                    let truncated: String = clean.chars().take(18).collect();
                    format!("{}…", truncated)
                } else {
                    clean
                }
            } else {
                "새 메모".into()
            };
        } else {
            self.display_main_title = "새 메모".into();
        }

        // 2. Sub Preview
        if !self.title.trim().is_empty() && !self.text.trim().is_empty() {
            let clean = clean_preview_string(&self.text);
            let char_count = clean.chars().count();
            self.display_sub_preview = if !clean.is_empty() {
                if char_count > 22 {
                    let truncated: String = clean.chars().take(20).collect();
                    Some(format!("{}…", truncated))
                } else {
                    Some(clean)
                }
            } else {
                None
            };
        } else {
            self.display_sub_preview = None;
        }

        // 3. Hover Body Preview
        if self.text.trim().is_empty() {
            self.display_hover_body_preview = "(내용이 비어 있습니다)".into();
        } else {
            let clean = clean_preview_string(&self.text);
            let char_count = clean.chars().count();
            self.display_hover_body_preview = if char_count > 85 {
                let truncated: String = clean.chars().take(80).collect();
                format!("{}…", truncated)
            } else {
                clean
            };
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_clean_preview_string() {
        let dirty = "  Hello \r\n\t  World   from   StickerMemo!  ";
        let cleaned = clean_preview_string(dirty);
        assert_eq!(cleaned, "Hello World from StickerMemo!");
    }

    #[test]
    fn test_note_preview_from_title() {
        let mut note = NoteModel::new();
        note.title = "This is a very long title that should definitely be truncated".into();
        note.recompute_previews();
        assert!(note.display_main_title.ends_with('…'));
        assert!(note.display_main_title.chars().count() <= 18);
    }

    #[test]
    fn test_note_preview_from_first_line() {
        let mut note = NoteModel::new();
        note.text = "First line of text\nSecond line of text".into();
        note.recompute_previews();
        assert_eq!(note.display_main_title, "First line of text");
        assert_eq!(note.display_sub_preview, None);
    }

    #[test]
    fn test_note_sub_preview() {
        let mut note = NoteModel::new();
        note.title = "My Title".into();
        note.text = "My body text".into();
        note.recompute_previews();
        assert_eq!(note.display_main_title, "My Title");
        assert_eq!(note.display_sub_preview.as_deref(), Some("My body text"));
    }
}

