use crate::detection::find_target_html_files;
use crate::hook;
use crate::settings::load_settings;
use serde::Serialize;
use std::fs;
use std::path::Path;

const MARKER_START: &str = "<!-- uprooted:start -->";
const MARKER_END: &str = "<!-- uprooted:end -->";

const LEGACY_MARKER: &str = "<!-- uprooted -->";
const BACKUP_SUFFIX: &str = ".uprooted.bak";

#[derive(Serialize)]
pub struct PatchResult {
    pub success: bool,
    pub message: String,
    pub files_patched: Vec<String>,
}

pub fn is_patched(content: &str) -> bool {
    content.contains(MARKER_START) || content.contains(LEGACY_MARKER)
}

pub fn install() -> PatchResult {
    let uprooted_dir = hook::get_uprooted_dir();

    let preload_path = uprooted_dir
        .join("uprooted-preload.js")
        .to_string_lossy()
        .replace('\\', "/");
    let css_path = uprooted_dir
        .join("uprooted.css")
        .to_string_lossy()
        .replace('\\', "/");

    let settings = load_settings();
    let settings_json = serde_json::to_string(&settings).unwrap_or_else(|_| "{}".to_string());

    let injection = format!(
        "{start}\n    <script>window.__UPROOTED_SETTINGS__={settings};</script>\n    <script src=\"file:///{preload}\"></script>\n    <link rel=\"stylesheet\" href=\"file:///{css}\">\n    {end}",
        start = MARKER_START,
        end = MARKER_END,
        settings = settings_json,
        preload = preload_path,
        css = css_path,
    );

    let targets = find_target_html_files();
    if targets.is_empty() {
        return PatchResult {
            success: false,
            message: "No target HTML files found in profile directory.".to_string(),
            files_patched: vec![],
        };
    }

    let mut patched = Vec::new();
    for file in &targets {
        let content = match fs::read_to_string(file) {
            Ok(c) => c,
            Err(e) => {
                return PatchResult {
                    success: false,
                    message: format!("Failed to read {}: {}", file.display(), e),
                    files_patched: patched,
                };
            }
        };

        if is_patched(&content) {
            continue;
        }


        let backup_path_str = format!("{}{}", file.to_string_lossy(), BACKUP_SUFFIX);
        let backup_path = Path::new(&backup_path_str);
        if !backup_path.exists() {
            if let Err(e) = fs::copy(file, backup_path) {
                return PatchResult {
                    success: false,
                    message: format!("Failed to backup {}: {}", file.display(), e),
                    files_patched: patched,
                };
            }
        }


        let new_content = content.replace("</head>", &format!("    {}\n  </head>", injection));
        if let Err(e) = fs::write(file, &new_content) {
            return PatchResult {
                success: false,
                message: format!("Failed to write {}: {}", file.display(), e),
                files_patched: patched,
            };
        }

        patched.push(file.to_string_lossy().to_string());
    }

    PatchResult {
        success: true,
        message: format!("Uprooted installed. {} files patched.", patched.len()),
        files_patched: patched,
    }
}

pub fn uninstall() -> PatchResult {
    let targets = find_target_html_files();
    let mut restored = Vec::new();

    for file in &targets {
        let backup_path_str = format!("{}{}", file.to_string_lossy(), BACKUP_SUFFIX);
        let backup_path = Path::new(&backup_path_str);

        if backup_path.exists() {
            if let Err(e) = fs::copy(backup_path, file) {
                return PatchResult {
                    success: false,
                    message: format!("Failed to restore {}: {}", file.display(), e),
                    files_patched: restored,
                };
            }
            let _ = fs::remove_file(backup_path);
            restored.push(file.to_string_lossy().to_string());
        } else {

            if let Ok(content) = fs::read_to_string(file) {
                if is_patched(&content) {
                    let cleaned = strip_injection(&content);
                    let _ = fs::write(file, &cleaned);
                    restored.push(file.to_string_lossy().to_string());
                }
            }
        }
    }

    PatchResult {
        success: true,
        message: format!(
            "Uprooted uninstalled. {} files restored.",
            restored.len()
        ),
        files_patched: restored,
    }
}

fn strip_injection(content: &str) -> String {
    let mut result = Vec::new();
    let mut inside_block = false;

    for line in content.lines() {
        if line.contains(MARKER_START) {
            inside_block = true;
            continue;
        }
        if line.contains(MARKER_END) {
            inside_block = false;
            continue;
        }
        if inside_block {
            continue;
        }

        if line.contains(LEGACY_MARKER) {
            continue;
        }
        result.push(line);
    }

    result.join("\n")
}

pub fn repair() -> PatchResult {
    let un = uninstall();
    if !un.success {
        return un;
    }
    install()
}
