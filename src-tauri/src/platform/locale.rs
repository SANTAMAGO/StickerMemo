//! Windows UI locale detection, used only to resolve the "System Default"
//! language preference (on first run, and whenever the user selects
//! "System Default" in Settings).
//!
//! This is intentionally the only new native API surface added for i18n:
//! `GetUserDefaultLocaleName` is already covered by the `windows` crate
//! dependency this project uses elsewhere (see `platform/single_instance.rs`),
//! it just needs the `Win32_Globalization` feature enabled in Cargo.toml.

use windows::Win32::Globalization::GetUserDefaultLocaleName;

/// Windows' documented LOCALE_NAME_MAX_LENGTH.
const LOCALE_NAME_MAX_LENGTH: usize = 85;

/// Detects the current Windows UI locale (e.g. "ko-KR", "ja-JP", "en-US")
/// and maps it to one of StickerMemo's supported locale codes ("ko"/"en"/
/// "ja"), falling back to [`crate::i18n::DEFAULT_LOCALE`] for anything else
/// (including detection failure).
pub fn detect_windows_ui_locale() -> String {
    let mut buf = [0u16; LOCALE_NAME_MAX_LENGTH];
    // SAFETY: `buf` is a valid, appropriately-sized mutable u16 buffer for
    // the duration of this call, matching the Win32 API contract.
    let len = unsafe { GetUserDefaultLocaleName(&mut buf) };
    if len > 1 {
        // `len` includes the trailing NUL terminator.
        let name = String::from_utf16_lossy(&buf[..(len as usize - 1)]);
        map_locale_tag(&name)
    } else {
        crate::log_startup("detect_windows_ui_locale: GetUserDefaultLocaleName failed, defaulting to en");
        crate::i18n::DEFAULT_LOCALE.to_string()
    }
}

/// Maps a BCP-47-ish Windows locale tag (e.g. "ko-KR") to one of
/// StickerMemo's supported locale codes.
fn map_locale_tag(tag: &str) -> String {
    let lower = tag.to_lowercase();
    if lower.starts_with("ko") {
        "ko".to_string()
    } else if lower.starts_with("ja") {
        "ja".to_string()
    } else {
        crate::i18n::DEFAULT_LOCALE.to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn maps_known_tags() {
        assert_eq!(map_locale_tag("ko-KR"), "ko");
        assert_eq!(map_locale_tag("ja-JP"), "ja");
        assert_eq!(map_locale_tag("en-US"), "en");
    }

    #[test]
    fn maps_unknown_tags_to_default() {
        assert_eq!(map_locale_tag("fr-FR"), crate::i18n::DEFAULT_LOCALE);
        assert_eq!(map_locale_tag("zh-CN"), crate::i18n::DEFAULT_LOCALE);
        assert_eq!(map_locale_tag(""), crate::i18n::DEFAULT_LOCALE);
    }
}
