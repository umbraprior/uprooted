#[cfg(target_os = "windows")]
pub const PROFILER: &[u8] = include_bytes!("../artifacts/uprooted_profiler.dll");
#[cfg(target_os = "linux")]
pub const PROFILER: &[u8] = include_bytes!("../artifacts/libuprooted_profiler.so");

pub const HOOK_DLL: &[u8] = include_bytes!("../artifacts/UprootedHook.dll");
pub const HOOK_DEPS_JSON: &[u8] = include_bytes!("../artifacts/UprootedHook.deps.json");
pub const PRELOAD_JS: &[u8] = include_bytes!("../artifacts/uprooted-preload.js");
pub const THEME_CSS: &[u8] = include_bytes!("../artifacts/uprooted.css");
