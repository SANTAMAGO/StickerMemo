use windows::core::w;
use windows::Win32::Foundation::{GetLastError, ERROR_ALREADY_EXISTS, HANDLE};
use windows::Win32::System::Threading::CreateMutexW;

pub struct SingleInstanceGuard {
    _handle: HANDLE,
}

pub fn check_single_instance() -> Option<SingleInstanceGuard> {
    unsafe {
        let mutex_name = w!(r"Global\StickerMemo_SingleInstance_Mutex_2026");
        let handle = CreateMutexW(None, true, mutex_name).ok()?;
        if GetLastError() == ERROR_ALREADY_EXISTS {
            return None;
        }
        Some(SingleInstanceGuard { _handle: handle })
    }
}
