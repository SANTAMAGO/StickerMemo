use crate::i18n;
use winreg::enums::{HKEY_CURRENT_USER, KEY_READ, KEY_WRITE};
use winreg::RegKey;

const RUN_KEY_PATH: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
const APP_NAME: &str = "StickerMemo";

pub fn is_run_at_startup() -> bool {
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    if let Ok(run_key) = hkcu.open_subkey_with_flags(RUN_KEY_PATH, KEY_READ) {
        let val: Result<String, _> = run_key.get_value(APP_NAME);
        val.is_ok()
    } else {
        false
    }
}

pub fn set_run_at_startup(enable: bool, locale: &str) -> Result<(), String> {
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let run_key = hkcu
        .open_subkey_with_flags(RUN_KEY_PATH, KEY_WRITE)
        .map_err(|e| i18n::tf(locale, "error.registry_open_failed", &[("e", &e.to_string())]))?;

    if enable {
        let exe_path = std::env::current_exe()
            .map_err(|e| i18n::tf(locale, "error.exe_path_failed", &[("e", &e.to_string())]))?;
        let cmd = format!("\"{}\" --autostart", exe_path.display());
        run_key
            .set_value(APP_NAME, &cmd)
            .map_err(|e| i18n::tf(locale, "error.autostart_set_failed", &[("e", &e.to_string())]))?;
    } else {
        let _ = run_key.delete_value(APP_NAME);
    }

    Ok(())
}
