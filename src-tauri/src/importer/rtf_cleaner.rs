use regex::Regex;

pub fn clean_sticky_note_text(raw_text: &str) -> String {
    if raw_text.trim().is_empty() {
        return String::new();
    }

    let mut text = raw_text.to_string();

    // 1. If wrapped in RTF
    if text.starts_with("{\\rtf") || text.starts_with("{\\RTF") {
        text = clean_rtf(&text);
    }

    // 2. Strip internal Sticky Notes metadata markup
    let id_regex = Regex::new(r"(?i)\\id=[a-zA-Z0-9_\-]+").unwrap();
    text = id_regex.replace_all(&text, "").to_string();

    let meta_regex = Regex::new(r"(?i)\\metadata\{[^}]*\}").unwrap();
    text = meta_regex.replace_all(&text, "").to_string();

    // 3. Normalize line breaks
    text = text.replace("\r\n", "\n").replace('\r', "\n");

    // 4. Strip excess trailing and leading null/whitespace characters
    text.trim_matches(|c: char| c == '\0' || c == ' ' || c == '\t' || c == '\n').to_string()
}

fn clean_rtf(rtf: &str) -> String {
    let par_regex = Regex::new(r"(?i)\\par[d]? ?").unwrap();
    let mut result = par_regex.replace_all(rtf, "\n").to_string();

    let tab_regex = Regex::new(r"(?i)\\tab ?").unwrap();
    result = tab_regex.replace_all(&result, "\t").to_string();

    let line_regex = Regex::new(r"(?i)\\line ?").unwrap();
    result = line_regex.replace_all(&result, "\n").to_string();

    // Handle unicode \u2612?
    let u_regex = Regex::new(r"\\u(-?\d+)\??").unwrap();
    result = u_regex
        .replace_all(&result, |caps: &regex::Captures| {
            if let Some(code_match) = caps.get(1) {
                if let Ok(code) = code_match.as_str().parse::<i16>() {
                    if let Some(ch) = char::from_u32(code as u16 as u32) {
                        return ch.to_string();
                    }
                }
            }
            "".to_string()
        })
        .to_string();

    // Remove remaining RTF tags \keyword
    let tag_regex = Regex::new(r"\\[a-zA-Z]+(-?\d+)? ?").unwrap();
    result = tag_regex.replace_all(&result, "").to_string();

    // Remove braces
    result = result.replace('{', "").replace('}', "");

    result
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_clean_sticky_note_plain_text() {
        let raw = "\\id=12345 Hello from sticky note\\metadata{xyz}";
        let cleaned = clean_sticky_note_text(raw);
        assert_eq!(cleaned, "Hello from sticky note");
    }

    #[test]
    fn test_clean_rtf_simple() {
        let rtf = "{\\rtf1\\ansi Hello\\par World!}";
        let cleaned = clean_sticky_note_text(rtf);
        assert_eq!(cleaned, "Hello\nWorld!");
    }
}

