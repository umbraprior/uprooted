// Prevents additional console window on Windows in release
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod detection;
mod embedded;
mod hook;
mod patcher;
mod settings;
mod themes;

use detection::DetectionResult;
use hook::HookStatus;
use patcher::PatchResult;
use settings::UprootedSettings;
use themes::ThemeDefinition;

#[tauri::command]
fn detect_root() -> DetectionResult {
    detection::detect()
}

#[tauri::command]
fn check_hook_status() -> HookStatus {
    hook::check_hook_status()
}

#[tauri::command]
fn check_root_running() -> bool {
    hook::check_root_running()
}

#[tauri::command]
fn kill_root() -> u32 {
    hook::kill_root_processes()
}

#[tauri::command]
fn install_uprooted() -> PatchResult {
    // Step 1: Deploy embedded files
    if let Err(e) = hook::deploy_files() {
        return PatchResult {
            success: false,
            message: format!("Failed to deploy files: {}", e),
            files_patched: vec![],
        };
    }

    // Step 2: Set environment variables
    if let Err(e) = hook::set_env_vars() {
        return PatchResult {
            success: false,
            message: format!("Failed to set env vars: {}", e),
            files_patched: vec![],
        };
    }

    // Step 3: Patch HTML files
    patcher::install()
}

#[tauri::command]
fn uninstall_uprooted() -> PatchResult {
    // Step 1: Remove environment variables
    if let Err(e) = hook::remove_env_vars() {
        return PatchResult {
            success: false,
            message: format!("Failed to remove env vars: {}", e),
            files_patched: vec![],
        };
    }

    // Step 2: Restore HTML files
    let result = patcher::uninstall();

    // Step 3: Remove deployed files
    if let Err(e) = hook::remove_files() {
        return PatchResult {
            success: false,
            message: format!("HTML restored but failed to remove files: {}", e),
            files_patched: result.files_patched,
        };
    }

    result
}

#[tauri::command]
fn repair_uprooted() -> PatchResult {
    // Re-deploy files (overwrite)
    if let Err(e) = hook::deploy_files() {
        return PatchResult {
            success: false,
            message: format!("Failed to deploy files: {}", e),
            files_patched: vec![],
        };
    }

    // Re-set env vars
    if let Err(e) = hook::set_env_vars() {
        return PatchResult {
            success: false,
            message: format!("Failed to set env vars: {}", e),
            files_patched: vec![],
        };
    }

    // Re-patch HTML
    patcher::repair()
}

#[tauri::command]
fn load_settings() -> UprootedSettings {
    settings::load_settings()
}

#[tauri::command]
fn save_settings(settings: UprootedSettings) -> Result<(), String> {
    settings::save_settings(&settings)
}

#[tauri::command]
fn list_themes() -> Vec<ThemeDefinition> {
    themes::get_builtin_themes()
}

#[tauri::command]
fn apply_theme(name: String) -> Result<(), String> {
    let mut s = settings::load_settings();
    let theme_settings = s.plugins.entry("themes".to_string()).or_insert_with(|| {
        settings::PluginSettings {
            enabled: true,
            config: std::collections::HashMap::new(),
        }
    });
    theme_settings
        .config
        .insert("theme".to_string(), serde_json::Value::String(name));
    settings::save_settings(&s)
}

#[tauri::command]
fn get_uprooted_version() -> String {
    env!("CARGO_PKG_VERSION").to_string()
}

#[tauri::command]
fn open_profile_dir() -> Result<(), String> {
    let profile = detection::get_profile_dir();
    if profile.exists() {
        opener::open(profile).map_err(|e| e.to_string())
    } else {
        Err("Profile directory does not exist.".to_string())
    }
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .invoke_handler(tauri::generate_handler![
            detect_root,
            check_hook_status,
            check_root_running,
            kill_root,
            install_uprooted,
            uninstall_uprooted,
            repair_uprooted,
            load_settings,
            save_settings,
            list_themes,
            apply_theme,
            get_uprooted_version,
            open_profile_dir,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
