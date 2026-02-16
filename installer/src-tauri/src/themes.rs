use serde::{Deserialize, Serialize};
use std::collections::HashMap;

#[derive(Serialize, Deserialize, Clone)]
pub struct PreviewColors {
    pub background: String,
    pub text: String,
    pub accent: String,
    pub border: String,
}

#[derive(Serialize, Deserialize, Clone)]
pub struct ThemeDefinition {
    pub name: String,
    pub display_name: String,
    pub description: String,
    pub author: String,
    pub variables: HashMap<String, String>,
    pub preview_colors: PreviewColors,
}

pub fn get_builtin_themes() -> Vec<ThemeDefinition> {
    let json = include_str!("../../../src/plugins/themes/themes.json");
    serde_json::from_str(json).unwrap_or_default()
}
