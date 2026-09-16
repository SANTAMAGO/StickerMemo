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

pub fn set_run_at_startup(enable: bool) -> Result<(), String> {
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let run_key = hkcu
        .open_subkey_with_flags(RUN_KEY_PATH, KEY_WRITE)
        .map_err(|e| format!("레지스트리 키 열기 실패: {}", e))?;

    if enable {
        let exe_path = std::env::current_exe()
            .map_err(|e| format!("실행 파일 경로 확인 실패: {}", e))?;
        let cmd = format!("\"{}\" --autostart", exe_path.display());
        run_key
            .set_value(APP_NAME, &cmd)
            .map_err(|e| format!("자동 실행 값 설정 실패: {}", e))?;
    } else {
        let _ = run_key.delete_value(APP_NAME);
    }

    Ok(())
}
