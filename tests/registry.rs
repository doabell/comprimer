use comprimer::{
    registry::{self, ExplorerRegistry},
    settings::{Settings, SizeActions},
};
use std::fs;
use winreg::{RegKey, enums::HKEY_CURRENT_USER};

struct Fixture {
    key: String,
    registry: ExplorerRegistry,
    dir: tempfile::TempDir,
}
impl Fixture {
    fn new() -> Self {
        let dir = tempfile::tempdir().unwrap();
        let key = format!(
            r"Software\ComprimerTests\{}",
            dir.path().file_name().unwrap().to_string_lossy()
        );
        Self {
            registry: ExplorerRegistry::at(key.clone()),
            key,
            dir,
        }
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        let _ = RegKey::predef(HKEY_CURRENT_USER).delete_subkey_all(&self.key);
    }
}

#[test]
fn all_extensions_support_flat_nested_and_preserve_other_apps() {
    let fixture = Fixture::new();
    let exe = fixture.dir.path().join("Comprimer with spaces.exe");
    fs::write(&exe, []).unwrap();
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    for nested in [true, false] {
        let settings = Settings {
            nested_menu: nested,
            comprimer_path: Some(exe.to_string_lossy().into_owned()),
            ..Default::default()
        };
        let unrelated = format!(r"{}\.png\shell\AnotherApp", fixture.key);
        hkcu.create_subkey(&unrelated).unwrap();
        fixture.registry.register(&settings, &exe).unwrap();
        assert!(fixture.registry.is_registered().unwrap());
        assert_eq!(
            fixture.registry.registered_path().unwrap(),
            Some(exe.clone())
        );
        for extension in [".jpg", ".jpeg", ".png", ".webp"] {
            let suffix = if nested {
                r"Comprimer\shell\Auto\command"
            } else {
                r"Comprimer.Auto\command"
            };
            let command = hkcu
                .open_subkey(format!(r"{}\{extension}\shell\{suffix}", fixture.key))
                .unwrap()
                .get_value::<String, _>("")
                .unwrap();
            assert_eq!(command, format!("\"{}\" --auto \"%1\"", exe.display()));
        }
        let invalid = Settings {
            comprimer_path: Some(String::new()),
            ..settings.clone()
        };
        assert!(fixture.registry.register(&invalid, &exe).is_err());
        assert!(fixture.registry.is_registered().unwrap());
        fixture.registry.unregister().unwrap();
        assert!(!fixture.registry.is_registered().unwrap());
        assert!(hkcu.open_subkey(unrelated).is_ok());
    }
}

#[test]
fn explicit_selected_executable_survives_launch_from_another_copy() {
    let fixture = Fixture::new();
    let selected = fixture.dir.path().join("selected.exe");
    let running = fixture.dir.path().join("running.exe");
    fs::write(&selected, []).unwrap();
    fs::write(&running, []).unwrap();
    let settings = Settings {
        comprimer_path: Some(selected.to_string_lossy().into_owned()),
        ..Default::default()
    };
    fixture.registry.register(&settings, &running).unwrap();
    assert_eq!(fixture.registry.registered_path().unwrap(), Some(selected));
}

#[test]
fn menu_respects_independent_toggles_and_translates_labels() {
    let mut settings = Settings {
        auto_mode: false,
        language: "fr".into(),
        ..Default::default()
    };
    settings.size_actions.insert(
        512,
        SizeActions {
            auto_resize: true,
            resize: false,
        },
    );
    settings.size_actions.insert(
        1024,
        SizeActions {
            auto_resize: false,
            resize: true,
        },
    );
    settings.jpg.downscale = false;
    let entries = registry::entries(&settings);
    assert!(!entries.iter().any(|e| e.command == "--auto"
        || e.command == "--auto 1024"
        || e.command == "--downscale 512"));
    assert!(
        entries
            .iter()
            .any(|e| e.command == "--auto 512" && e.label.contains("réduire"))
    );
    assert!(
        entries
            .iter()
            .filter(|e| e.command == "--downscale 1024")
            .all(|e| !e.extensions.contains(&".jpg"))
    );
}
