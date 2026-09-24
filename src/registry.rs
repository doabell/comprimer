use crate::{settings::Settings, tools};
use anyhow::{Context, Result, bail};
use std::{
    io,
    path::{Path, PathBuf},
};
use winreg::{
    RegKey, RegValue,
    enums::{HKEY_CURRENT_USER, KEY_READ, KEY_WRITE},
};

const EXTENSIONS: [&str; 4] = [".jpg", ".jpeg", ".png", ".webp"];
const BASE: &str = r"Software\Classes\SystemFileAssociations";

#[derive(Clone, Debug)]
pub struct MenuEntry {
    pub id: String,
    pub label: String,
    pub command: String,
    pub extensions: Vec<&'static str>,
}

pub fn entries(settings: &Settings) -> Vec<MenuEntry> {
    let fr = settings.language == "fr";
    let tr = |en: &str, french: &str| if fr { french.to_owned() } else { en.to_owned() };
    let mut entries = Vec::new();
    let mut add = |id: String, label: String, command: String, extensions: &[&'static str]| {
        entries.push(MenuEntry {
            id,
            label,
            command,
            extensions: extensions.to_vec(),
        });
    };
    if settings.auto_mode {
        add(
            "Auto".into(),
            tr("Auto · original size", "Auto · taille d'origine"),
            "--auto".into(),
            &EXTENSIONS,
        );
    }
    let mut sizes = settings.available_sizes.clone();
    sizes.retain(|n| *n > 0);
    sizes.sort_unstable();
    sizes.dedup();
    for size in &sizes {
        if settings.actions(*size).auto_resize {
            add(
                format!("Auto{size}"),
                tr(
                    &format!("Auto + resize {size}px"),
                    &format!("Auto + réduire {size}px"),
                ),
                format!("--auto {size}"),
                &EXTENSIONS,
            );
        }
    }
    for (format, extensions) in [
        (&settings.jpg, &[".jpg", ".jpeg"][..]),
        (&settings.png, &[".png"][..]),
        (&settings.webp, &[".webp"][..]),
    ] {
        if format.downscale {
            for size in &sizes {
                if settings.actions(*size).resize {
                    add(
                        format!("Downscale{size}"),
                        tr(
                            &format!("Resize only {size}px"),
                            &format!("Réduire seulement {size}px"),
                        ),
                        format!("--downscale {size}"),
                        extensions,
                    );
                }
            }
        }
        if format.optimize && extensions[0] != ".webp" {
            let (id, encoder, command) = if extensions[0] == ".png" {
                ("Pngquant", "pngquant", "--pngquant")
            } else {
                ("Mozjpeg", "mozjpeg", "--mozjpeg")
            };
            add(
                id.into(),
                tr(
                    &format!("Optimize ({encoder})"),
                    &format!("Optimiser ({encoder})"),
                ),
                command.into(),
                extensions,
            );
        }
        if format.convert_to_webp && extensions[0] != ".webp" {
            add(
                "ToWebP".into(),
                tr("Convert to WebP (libwebp)", "Convertir en WebP (libwebp)"),
                "--to-webp".into(),
                extensions,
            );
        }
        if format.convert_to_jpg && extensions[0] == ".png" {
            add(
                "ToJpg".into(),
                tr("Convert to JPG (mozjpeg)", "Convertir en JPG (mozjpeg)"),
                "--to-jpg".into(),
                extensions,
            );
        }
    }
    entries
}

/// Draft menu labels, using the same definitions and verb names as Explorer.
/// Building a preview never reads or writes the registry.
pub fn preview_entries(settings: &Settings, extension: &str) -> Vec<MenuEntry> {
    let mut result: Vec<_> = entries(settings)
        .into_iter()
        .filter(|entry| entry.extensions.contains(&extension))
        .collect();
    result.sort_by(|a, b| a.id.cmp(&b.id));
    if !settings.nested_menu {
        for entry in &mut result {
            entry.label = format!("Comprimer: {}", entry.label);
        }
    }
    result
}

pub struct ExplorerRegistry {
    base: String,
}

impl Default for ExplorerRegistry {
    fn default() -> Self {
        Self { base: BASE.into() }
    }
}

impl ExplorerRegistry {
    /// Alternate root for isolated registry tests; never changes real Explorer registrations.
    pub fn at(base: String) -> Self {
        Self { base }
    }

    fn shell_path(&self, extension: &str) -> String {
        format!(r"{}\{}\shell", self.base, extension)
    }

    pub fn is_registered(&self) -> Result<bool> {
        for ext in EXTENSIONS {
            if let Some(shell) = open_optional(
                &RegKey::predef(HKEY_CURRENT_USER),
                &self.shell_path(ext),
                KEY_READ,
            )? && !owned_names(&shell)?.is_empty()
            {
                return Ok(true);
            }
        }
        Ok(false)
    }

    pub fn registered_path(&self) -> Result<Option<PathBuf>> {
        for ext in EXTENSIONS {
            if let Some(shell) = open_optional(
                &RegKey::predef(HKEY_CURRENT_USER),
                &self.shell_path(ext),
                KEY_READ,
            )? {
                for name in owned_names(&shell)? {
                    let entry = shell.open_subkey(name)?;
                    if let Some(path) = find_command(&entry)? {
                        return Ok(Some(path));
                    }
                }
            }
        }
        Ok(None)
    }

    pub fn registration_path(&self, settings: &Settings, current_exe: &Path) -> Result<PathBuf> {
        let path = match settings.comprimer_path.as_deref() {
            Some(path) => tools::existing_path(path),
            None => self
                .registered_path()?
                .or_else(|| Some(current_exe.to_owned()))
                .filter(|p| p.is_file()),
        }
        .context("Choose a valid Comprimer executable or click Detect before updating Explorer")?;
        if path.to_string_lossy().contains('"') {
            bail!("Executable path cannot contain a quote");
        }
        Ok(path)
    }

    pub fn register(&self, settings: &Settings, current_exe: &Path) -> Result<PathBuf> {
        let exe = self.registration_path(settings, current_exe)?;
        let snapshot = self.snapshot()?;
        if let Err(error) = self.replace(settings, &exe) {
            self.restore(snapshot).context(format!(
                "Register failed: {error:#}; restoring the previous menu also failed"
            ))?;
            return Err(error);
        }
        tracing::info!(executable = %exe.display(), "Explorer menu registered");
        Ok(exe)
    }

    fn replace(&self, settings: &Settings, exe: &Path) -> Result<()> {
        self.unregister()?;
        let hkcu = RegKey::predef(HKEY_CURRENT_USER);
        for entry in entries(settings) {
            for ext in entry.extensions {
                let shell = self.shell_path(ext);
                let path = if settings.nested_menu {
                    let path = format!(r"{shell}\Comprimer");
                    let (menu, _) = hkcu.create_subkey(&path)?;
                    menu.set_value("MUIVerb", &"Comprimer")?;
                    menu.set_value("Icon", &format!("\"{}\",0", exe.display()))?;
                    menu.set_value("SubCommands", &"")?;
                    format!(r"{path}\shell\{}", entry.id)
                } else {
                    format!(r"{shell}\Comprimer.{}", entry.id)
                };
                let (key, _) = hkcu.create_subkey(&path)?;
                if settings.nested_menu {
                    key.set_value("MUIVerb", &entry.label)?;
                } else {
                    key.set_value("", &format!("Comprimer: {}", entry.label))?;
                    key.set_value("Icon", &format!("\"{}\",0", exe.display()))?;
                }
                let (command, _) = key.create_subkey("command")?;
                command.set_value(
                    "",
                    &format!("\"{}\" --headless {} \"%1\"", exe.display(), entry.command),
                )?;
            }
        }
        Ok(())
    }

    pub fn unregister(&self) -> Result<()> {
        let hkcu = RegKey::predef(HKEY_CURRENT_USER);
        for ext in EXTENSIONS {
            if let Some(shell) = open_optional(&hkcu, &self.shell_path(ext), KEY_READ | KEY_WRITE)?
            {
                for name in owned_names(&shell)? {
                    shell.delete_subkey_all(name)?;
                }
            }
        }
        Ok(())
    }

    fn snapshot(&self) -> Result<Vec<(String, RegistryNode)>> {
        let hkcu = RegKey::predef(HKEY_CURRENT_USER);
        let mut nodes = Vec::new();
        for ext in EXTENSIONS {
            let path = self.shell_path(ext);
            if let Some(shell) = open_optional(&hkcu, &path, KEY_READ)? {
                for name in owned_names(&shell)? {
                    nodes.push((
                        format!(r"{path}\{name}"),
                        RegistryNode::read(&shell.open_subkey(name)?)?,
                    ));
                }
            }
        }
        Ok(nodes)
    }

    fn restore(&self, snapshot: Vec<(String, RegistryNode)>) -> Result<()> {
        self.unregister()?;
        let hkcu = RegKey::predef(HKEY_CURRENT_USER);
        for (path, node) in snapshot {
            node.write(&hkcu.create_subkey(path)?.0)?;
        }
        Ok(())
    }
}

fn owned_names(key: &RegKey) -> Result<Vec<String>> {
    Ok(key
        .enum_keys()
        .collect::<io::Result<Vec<_>>>()?
        .into_iter()
        .filter(|name| {
            let name = name.to_ascii_lowercase();
            name == "comprimer" || name.starts_with("comprimer.")
        })
        .collect())
}

fn open_optional(parent: &RegKey, path: &str, access: u32) -> io::Result<Option<RegKey>> {
    match parent.open_subkey_with_flags(path, access) {
        Ok(key) => Ok(Some(key)),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(None),
        Err(error) => Err(error),
    }
}

fn find_command(key: &RegKey) -> Result<Option<PathBuf>> {
    if let Some(command) = open_optional(key, "command", KEY_READ)? {
        let text: String = command.get_value("")?;
        if let Some(text) = text.strip_prefix('"')
            && let Some(end) = text.find('"')
        {
            return Ok(Some(PathBuf::from(&text[..end])));
        }
    }
    if let Some(shell) = open_optional(key, "shell", KEY_READ)? {
        for name in shell.enum_keys() {
            if let Some(path) = find_command(&shell.open_subkey(name?)?)? {
                return Ok(Some(path));
            }
        }
    }
    Ok(None)
}

struct RegistryNode {
    values: Vec<(String, RegValue<'static>)>,
    children: Vec<(String, RegistryNode)>,
}
impl RegistryNode {
    fn read(key: &RegKey) -> Result<Self> {
        let values = key.enum_values().collect::<io::Result<Vec<_>>>()?;
        let mut children = Vec::new();
        for name in key.enum_keys() {
            let name = name?;
            children.push((name.clone(), Self::read(&key.open_subkey(name)?)?));
        }
        Ok(Self { values, children })
    }
    fn write(self, key: &RegKey) -> Result<()> {
        for (name, value) in self.values {
            key.set_raw_value(name, &value)?;
        }
        for (name, node) in self.children {
            node.write(&key.create_subkey(name)?.0)?;
        }
        Ok(())
    }
}
