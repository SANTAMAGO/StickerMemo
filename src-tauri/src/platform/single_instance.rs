use windows::core::w;
use windows::Win32::Foundation::{CloseHandle, GetLastError, ERROR_ALREADY_EXISTS, HANDLE, WAIT_OBJECT_0};
use windows::Win32::System::Threading::{
    CreateEventW, CreateMutexW, OpenEventW, SetEvent, WaitForSingleObject,
    EVENT_MODIFY_STATE, SYNCHRONIZATION_ACCESS_RIGHTS,
};

const SYNCHRONIZE_RIGHT: SYNCHRONIZATION_ACCESS_RIGHTS = SYNCHRONIZATION_ACCESS_RIGHTS(0x00100000);

pub struct SingleInstanceGuard {
    handle: HANDLE,
}

unsafe impl Send for SingleInstanceGuard {}
unsafe impl Sync for SingleInstanceGuard {}

impl Drop for SingleInstanceGuard {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.handle);
        }
    }
}

const MUTEX_NAME: windows::core::PCWSTR = w!(r"Global\StickerMemo_SingleInstance_Mutex_2026");
const WAKEUP_EVENT_NAME: windows::core::PCWSTR = w!(r"Global\StickerMemo_Wakeup_Event_2026");

/// Ensures single instance execution.
/// If an instance is already running, it signals the existing instance to wake up
/// and returns None, indicating that this duplicate process should exit immediately.
pub fn check_single_instance() -> Option<SingleInstanceGuard> {
    unsafe {
        let handle = CreateMutexW(None, true, MUTEX_NAME).ok()?;
        if GetLastError() == ERROR_ALREADY_EXISTS {
            crate::log_startup("Existing instance detected. Signalling wakeup event and exiting duplicate launch.");

            // Signal wakeup event to existing instance
            if let Ok(event) = OpenEventW(EVENT_MODIFY_STATE | SYNCHRONIZE_RIGHT, false, WAKEUP_EVENT_NAME) {
                let _ = SetEvent(event);
                let _ = CloseHandle(event);
            }

            let _ = CloseHandle(handle);
            return None;
        }

        Some(SingleInstanceGuard { handle })
    }
}

/// Listens in background for wakeup event triggered when a user launches another instance.
pub fn listen_for_wakeup_signal<F>(on_wakeup: F)
where
    F: Fn() + Send + 'static,
{
    std::thread::spawn(move || unsafe {
        let event = match CreateEventW(None, false, false, WAKEUP_EVENT_NAME) {
            Ok(h) => h,
            Err(_) => return,
        };

        loop {
            let wait_res = WaitForSingleObject(event, windows::Win32::System::Threading::INFINITE);
            if wait_res == WAIT_OBJECT_0 {
                crate::log_startup("Received wakeup signal from duplicate launch. Bringing app to foreground...");
                on_wakeup();
            } else {
                break;
            }
        }
        let _ = CloseHandle(event);
    });
}
