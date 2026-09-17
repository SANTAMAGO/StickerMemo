//! StickerMemo localization: single source of truth for translated strings.
//!
//! The translation data lives in exactly one place on disk -
//! `ui/lang/{ko,en,ja}.json` - which the WebView also reads at runtime via
//! `fetch()` (see `ui/i18n.js`). This module embeds the same three JSON
//! files into the binary at compile time (`include_str!`) so Rust-side
//! strings (tray menu, window titles, a handful of command error/status
//! messages) never fall out of sync with the WebView copies and never need
//! a second, hand-duplicated translation table.
//!
//! To add a new language: add `ui/lang/<code>.json` with the same key set
//! as `en.json`, add `<code>` to [`SUPPORTED_LOCALES`], and add one
//! `include_str!` line in [`load_dicts`]. No other Rust file needs to
//! change - callers only ever look keys up by name via [`t`]/[`tf`].

use std::collections::HashMap;
use std::sync::OnceLock;

/// Locale codes StickerMemo ships translations for. Order does not matter.
pub const SUPPORTED_LOCALES: &[&str] = &["ko", "en", "ja"];

/// Fallback locale used whenever a requested locale is unsupported, or a
/// key is missing from the requested locale's dictionary.
pub const DEFAULT_LOCALE: &str = "en";

static DICTS: OnceLock<HashMap<&'static str, HashMap<String, String>>> = OnceLock::new();

fn parse(raw: &str) -> HashMap<String, String> {
    serde_json::from_str(raw).unwrap_or_else(|e| {
        crate::log_startup(&format!("[CRITICAL] i18n: failed to parse embedded locale JSON: {}", e));
        HashMap::new()
    })
}

fn load_dicts() -> HashMap<&'static str, HashMap<String, String>> {
    let mut map = HashMap::new();
    map.insert("ko", parse(include_str!("../../ui/lang/ko.json")));
    map.insert("en", parse(include_str!("../../ui/lang/en.json")));
    map.insert("ja", parse(include_str!("../../ui/lang/ja.json")));
    map
}

fn dicts() -> &'static HashMap<&'static str, HashMap<String, String>> {
    DICTS.get_or_init(load_dicts)
}

/// Maps an arbitrary locale string to one StickerMemo actually ships a
/// dictionary for, falling back to [`DEFAULT_LOCALE`]. Used both for
/// explicit user preferences ("ko"/"en"/"ja") and for normalizing whatever
/// [`crate::platform::locale::detect_windows_ui_locale`] returns.
pub fn normalize_locale(locale: &str) -> &'static str {
    match locale {
        "ko" => "ko",
        "ja" => "ja",
        "en" => "en",
        _ => DEFAULT_LOCALE,
    }
}

/// Looks up `key` in `locale`'s dictionary. Falls back to the English
/// dictionary, then to the raw key itself, so a missing translation shows
/// up as a visibly-wrong-but-harmless key string instead of an empty label
/// or a panic.
pub fn t(locale: &str, key: &str) -> String {
    let locale = normalize_locale(locale);
    if let Some(v) = dicts().get(locale).and_then(|d| d.get(key)) {
        return v.clone();
    }
    if locale != DEFAULT_LOCALE {
        if let Some(v) = dicts().get(DEFAULT_LOCALE).and_then(|d| d.get(key)) {
            return v.clone();
        }
    }
    key.to_string()
}

/// Like [`t`], but also substitutes `{name}` placeholders from `params`.
/// Placeholder names are matched literally (no positional/ordering
/// dependence), so per-locale word order never breaks substitution.
pub fn tf(locale: &str, key: &str, params: &[(&str, &str)]) -> String {
    let mut s = t(locale, key);
    for (name, value) in params {
        s = s.replace(&format!("{{{}}}", name), value);
    }
    s
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::BTreeSet;

    /// Guards the "add a language = add a JSON file" promise: every key
    /// present in en.json must exist in every other locale's JSON (and
    /// vice versa). Run with `cargo test`.
    #[test]
    fn all_locales_have_matching_keys() {
        let d = dicts();
        let en_keys: BTreeSet<&String> = d[DEFAULT_LOCALE].keys().collect();
        for &loc in SUPPORTED_LOCALES {
            let keys: BTreeSet<&String> = d
                .get(loc)
                .unwrap_or_else(|| panic!("no dictionary loaded for locale '{}'", loc))
                .keys()
                .collect();
            let missing: Vec<_> = en_keys.difference(&keys).collect();
            let extra: Vec<_> = keys.difference(&en_keys).collect();
            assert!(missing.is_empty(), "locale '{}' is missing keys present in en.json: {:?}", loc, missing);
            assert!(extra.is_empty(), "locale '{}' has keys not present in en.json: {:?}", loc, extra);
        }
    }

    #[test]
    fn missing_key_falls_back_to_key_itself() {
        assert_eq!(t("en", "this.key.does.not.exist"), "this.key.does.not.exist");
    }

    #[test]
    fn tf_substitutes_named_placeholders() {
        let s = tf("en", "note.window_title_with_title", &[("title", "Groceries")]);
        assert_eq!(s, "Note - Groceries");
    }

    #[test]
    fn unsupported_locale_normalizes_to_default() {
        assert_eq!(normalize_locale("fr"), DEFAULT_LOCALE);
        assert_eq!(normalize_locale("zh-CN"), DEFAULT_LOCALE);
    }
}
