pub mod autostart;
pub mod single_instance;

pub use autostart::{is_run_at_startup, set_run_at_startup};
pub use single_instance::{check_single_instance, listen_for_wakeup_signal, SingleInstanceGuard};
