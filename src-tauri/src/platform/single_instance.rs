use windows::core::w;
use windows::Win32::Foundation::{CloseHandle, GetLastError, ERROR_ALREADY_EXISTS, HANDLE, WAIT_OBJECT_0};
use windows::Win32::System::Threading::{
    CreateEventW, CreateMutexW, OpenEventW, SetEvent, WaitForSingleObject,
    EVENT_MODIFY_STATE, SYNCHRONIZATION_ACCESS_RIGHTS,
};

const SYNCHRONIZE_RIGHT: SYNCHRONIZATION_ACCESS_RIGHTS = SYNCHRONIZATION_ACCESS_RIGHTS(0x00100000);

pub struct SingleInstanceGuard {
    handle: HANDLE,
    event_handle: Option<HANDLE>,
}

unsafe impl Send for SingleInstanceGuard {}
unsafe impl Sync for SingleInstanceGuard {}

impl Drop for SingleInstanceGuard {
    fn drop(&mut self) {
        unsafe {
            if let Some(ev) = self.event_handle {
                let _ = CloseHandle(ev);
            }
            let _ = CloseHandle(self.handle);
        }
    }
}

const MUTEX_NAME: windows::core::PCWSTR = w!(r"Global\StickerMemo_SingleInstance_Mutex_2026");
const EVENT_NAME: windows::core::PCWSTR = w!(r"Global\StickerMemo_Exit_Event_2026");

/// Ensures a single running instance. If an existing instance is found, signals it
/// to shut down cleanly, waits for it to release the mutex, and acquires the mutex.
pub fn check_single_instance_or_replace() -> Option<SingleInstanceGuard> {
    unsafe {
        let handle = CreateMutexW(None, true, MUTEX_NAME).ok()?;
        if GetLastError() == ERROR_ALREADY_EXISTS {
            crate::log_startup("Existing instance detected. Signalling graceful shutdown event...");

            // 1. Signal exit event to existing instance
            if let Ok(event) = OpenEventW(EVENT_MODIFY_STATE | SYNCHRONIZE_RIGHT, false, EVENT_NAME) {
                let _ = SetEvent(event);
                let _ = CloseHandle(event);
            }

            // 2. Wait up to 2500ms for old instance to exit cleanly
            let wait_res = WaitForSingleObject(handle, 2500);
            if wait_res == WAIT_OBJECT_0 {
                crate::log_startup("Previous instance exited gracefully. Acquired mutex.");
            } else {
                crate::log_startup("Previous instance did not exit within timeout. Terminating lingering processes...");
                kill_other_sticker_instances();
                let _ = WaitForSingleObject(handle, 1000);
            }
        }

        // Create or reset the exit event for this new instance
        let event_handle = CreateEventW(None, false, false, EVENT_NAME).ok();

        Some(SingleInstanceGuard {
            handle,
            event_handle,
        })
    }
}

/// Listens in background for exit event triggered by a new instance launching.
pub fn listen_for_exit_signal<F>(on_exit: F)
where
    F: FnOnce() + Send + 'static,
{
    std::thread::spawn(move || unsafe {
        if let Ok(event) = OpenEventW(SYNCHRONIZE_RIGHT, false, EVENT_NAME) {
            let wait_res = WaitForSingleObject(event, windows::Win32::System::Threading::INFINITE);
            if wait_res == WAIT_OBJECT_0 {
                crate::log_startup("Received exit signal from new instance. Terminating gracefully...");
                on_exit();
            }
            let _ = CloseHandle(event);
        }
    });
}

fn kill_other_sticker_instances() {
    let current_pid = std::process::id();
    let _ = std::process::Command::new("powershell")
        .args([
            "-NoProfile",
            "-Command",
            &format!(
                "Get-Process -Name 'sticker-memo' -ErrorAction SilentlyContinue | Where-Object {{ $_.Id -ne {} }} | Stop-Process -Force -ErrorAction SilentlyContinue",
                current_pid
            ),
        ])
        .output();
}
