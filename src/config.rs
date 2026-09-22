use crate::settings::{Settings, SizeActions};
use anyhow::{Context, Result};
use std::{
    fs,
    io::Write,
    path::{Path, PathBuf},
};

pub fn settings_path() -> Result<PathBuf> {
    if let Some(path) = std::env::var_os("COMPRIMER_CONFIG") {
        return Ok(path.into());
    }
    let appdata = std::env::var_os("APPDATA").context("APPDATA is not set")?;
    Ok(PathBuf::from(appdata).join("Comprimer/settings.json"))
}

pub fn load(path: &Path) -> Result<Settings> {
    let bytes = match fs::read(path) {
        Ok(bytes) => bytes,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(Settings::default()),
        Err(e) => return Err(e).with_context(|| format!("Read {}", path.display())),
    };
    let mut value: serde_json::Value = serde_json::from_slice(&bytes).with_context(|| {
        format!(
            "Invalid JSON in {}. Your settings have not been changed.",
            path.display()
        )
    })?;
    let legacy = value
        .get("sizeActions")
        .is_none_or(serde_json::Value::is_null);
    if legacy {
        value
            .as_object_mut()
            .context("Settings must be a JSON object")?
            .remove("sizeActions");
    }
    let mut settings: Settings = serde_json::from_value(value).context("Read settings fields")?;
    settings.normalize();
    if legacy {
        for &size in &settings.available_sizes {
            settings.size_actions.insert(
                size,
                SizeActions {
                    auto_resize: settings.auto_mode,
                    resize: true,
                },
            );
        }
    }
    Ok(settings)
}

pub fn save(path: &Path, settings: &Settings) -> Result<()> {
    let parent = path
        .parent()
        .filter(|p| !p.as_os_str().is_empty())
        .unwrap_or(Path::new("."));
    fs::create_dir_all(parent).with_context(|| format!("Create {}", parent.display()))?;
    let mut normalized = settings.clone();
    normalized.normalize();
    let mut file = tempfile::NamedTempFile::new_in(parent)?;
    serde_json::to_writer_pretty(&mut file, &normalized)?;
    file.write_all(b"\n")?;
    file.as_file().sync_all()?;
    file.persist(path)
        .with_context(|| format!("Save {}", path.display()))?;
    Ok(())
}
