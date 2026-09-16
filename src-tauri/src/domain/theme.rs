use serde::{Deserialize, Serialize};

#[allow(dead_code)]
#[derive(Debug, Clone, Serialize, Deserialize)]

pub struct MemoTheme {
    pub id: String,
    pub name: String,
    pub background_hex: String,
    pub accent_hex: String,
    pub border_hex: String,
    pub tape_hex: String,
}

#[allow(dead_code)]
pub fn get_theme(theme_id: Option<&str>) -> MemoTheme {
    match theme_id.unwrap_or("Yellow") {
        "Pink" => MemoTheme {
            id: "Pink".into(),
            name: "Pink".into(),
            background_hex: "#FBCFE8".into(),
            accent_hex: "#BE185D".into(),
            border_hex: "#F472B6".into(),
            tape_hex: "#80FFFFFF".into(),
        },
        "Mint" => MemoTheme {
            id: "Mint".into(),
            name: "Mint".into(),
            background_hex: "#A7F3D0".into(),
            accent_hex: "#047857".into(),
            border_hex: "#34D399".into(),
            tape_hex: "#80FFFFFF".into(),
        },
        "Blue" | "Sky" => MemoTheme {
            id: "Blue".into(),
            name: "Blue".into(),
            background_hex: "#BAE6FD".into(),
            accent_hex: "#0369A1".into(),
            border_hex: "#38BDF8".into(),
            tape_hex: "#80FFFFFF".into(),
        },
        "Purple" => MemoTheme {
            id: "Purple".into(),
            name: "Purple".into(),
            background_hex: "#DDD6FE".into(),
            accent_hex: "#6D28D9".into(),
            border_hex: "#A78BFA".into(),
            tape_hex: "#80FFFFFF".into(),
        },
        "Beige" | "Kraft" => MemoTheme {
            id: "Kraft".into(),
            name: "Kraft".into(),
            background_hex: "#E6D5B8".into(),
            accent_hex: "#78350F".into(),
            border_hex: "#C7A77D".into(),
            tape_hex: "#80FFFFFF".into(),
        },
        _ => MemoTheme {
            id: "Yellow".into(),
            name: "Yellow".into(),
            background_hex: "#FEF08A".into(),
            accent_hex: "#A16207".into(),
            border_hex: "#FACC15".into(),
            tape_hex: "#80FFFFFF".into(),
        },
    }
}

pub fn map_windows_sticky_theme(win_theme: Option<&str>) -> String {
    let name = win_theme.unwrap_or("").to_lowercase();
    match name.as_str() {
        "yellow" => "Yellow".into(),
        "green" => "Mint".into(),
        "pink" => "Pink".into(),
        "purple" => "Purple".into(),
        "blue" => "Blue".into(),
        "charcoal" | "grey" | "gray" => "Kraft".into(),
        _ => "Yellow".into(),
    }
}
