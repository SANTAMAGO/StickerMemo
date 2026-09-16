pub mod autostart;
pub mod single_instance;

pub use autostart::{is_run_at_startup, set_run_at_startup};
pub use single_instance::{check_single_instance_or_replace, listen_for_exit_signal};
#[allow(unused_imports)]
pub use single_instance::SingleInstanceGuard;
