/// Embedded binary artifacts for deployment.
///
/// These files are compiled into the installer binary via `include_bytes!()`.
/// The build pipeline stages real builds into `installer/src-tauri/artifacts/`
/// before `cargo tauri build`.

#[cfg(target_os = "windows")]
pub const PROFILER: &[u8] = include_bytes!("../artifacts/uprooted_profiler.dll");
#[cfg(target_os = "linux")]
pub const PROFILER: &[u8] = include_bytes!("../artifacts/libuprooted_profiler.so");

pub const HOOK_DLL: &[u8] = include_bytes!("../artifacts/UprootedHook.dll");
pub const HOOK_DEPS_JSON: &[u8] = include_bytes!("../artifacts/UprootedHook.deps.json");
pub const PRELOAD_JS: &[u8] = include_bytes!("../artifacts/uprooted-preload.js");
pub const THEME_CSS: &[u8] = include_bytes!("../artifacts/uprooted.css");
