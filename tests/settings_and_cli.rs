use comprimer::{
    cli::{self, Command},
    config,
    images::Operation,
    settings::{Settings, SizeActions},
    tools,
};
use std::{ffi::OsString, fs};

#[test]
fn legacy_settings_keep_shortcuts_and_dotnet_field_names() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("settings.json");
    fs::write(&path, r#"{"autoMode":false,"availableSizes":[720,1440],"jpg":{"downscale":false},"png":{"downscale":true,"convertToWebP":true},"webP":{"downscale":false},"executables":{"img2WebPPath":"C:\\tools\\cwebp.exe","dwebpPath":""},"encoders":{"webPQuality":42},"darkMode":false}"#).unwrap();
    let settings = config::load(&path).unwrap();
    assert!(!settings.auto_mode);
    assert_eq!(settings.available_sizes, [720, 1440]);
    assert!(
        settings
            .available_sizes
            .iter()
            .all(|s| !settings.actions(*s).auto_resize && settings.actions(*s).resize)
    );
    assert!(settings.png.convert_to_webp);
    assert!(!settings.webp.downscale);
    assert_eq!(settings.encoders.webp_quality, 42);
    assert_eq!(
        settings.executables.cwebp_path.as_deref(),
        Some(r"C:\tools\cwebp.exe")
    );
    assert_eq!(settings.executables.dwebp_path.as_deref(), Some(""));
    assert!(!settings.dark_mode);
    config::save(&path, &settings).unwrap();
    assert_eq!(config::load(&path).unwrap(), settings);
}

#[test]
fn roundtrip_preserves_independent_actions_paths_and_quality() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("nested/settings.json");
    let mut settings = Settings {
        auto_mode: false,
        comprimer_path: Some(String::new()),
        ..Default::default()
    };
    settings.size_actions.insert(
        512,
        SizeActions {
            auto_resize: true,
            resize: false,
        },
    );
    settings.encoders.png_min_quality = 120;
    settings.encoders.jpg_quality = -1;
    settings.encoders.webp_quality = 101;
    config::save(&path, &settings).unwrap();
    let loaded = config::load(&path).unwrap();
    assert!(loaded.actions(512).auto_resize);
    assert!(!loaded.actions(512).resize);
    assert_eq!(loaded.comprimer_path, Some(String::new()));
    assert_eq!(loaded.executables.pngquant_path, None);
    assert_eq!(loaded.encoders.png_min_quality, 100);
    assert_eq!(loaded.encoders.jpg_quality, 0);
    assert_eq!(loaded.encoders.webp_quality, 100);
}

#[test]
fn invalid_settings_are_reported_and_never_replaced() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("settings.json");
    fs::write(&path, "broken JSON").unwrap();
    assert!(config::load(&path).is_err());
    assert_eq!(fs::read_to_string(&path).unwrap(), "broken JSON");
    assert_eq!(
        config::load(&dir.path().join("missing.json")).unwrap(),
        Settings::default()
    );
}

#[test]
fn explicit_paths_never_fall_back_and_decoder_prefers_sibling() {
    assert!(tools::resolve("cmd", Some("")).is_none());
    assert!(tools::resolve("cmd", Some("missing.exe")).is_none());
    let dir = tempfile::tempdir().unwrap();
    let encoder = dir.path().join("cwebp.exe");
    let decoder = dir.path().join("dwebp.exe");
    fs::write(&encoder, []).unwrap();
    fs::write(&decoder, []).unwrap();
    assert_eq!(tools::detect_decoder(encoder.to_str()), Some(decoder));
    assert_eq!(
        tools::existing_path(&format!("\"{}\"", encoder.display())),
        Some(encoder)
    );
}

fn parse(args: &[&str]) -> anyhow::Result<Command> {
    cli::parse(&args.iter().map(OsString::from).collect::<Vec<_>>())
}

#[test]
fn cli_preserves_legacy_commands_and_unicode_paths() {
    assert_eq!(parse(&[]).unwrap(), Command::Settings);
    assert_eq!(
        parse(&["--auto", "photo été.png"]).unwrap(),
        Command::Process(Operation::Auto(None), "photo été.png".into())
    );
    assert_eq!(
        parse(&["--auto", "1024", "photo.png"]).unwrap(),
        Command::Process(Operation::Auto(Some(1024)), "photo.png".into())
    );
    assert_eq!(
        parse(&["--toggle-overwrite", "photo.png"]).unwrap(),
        Command::ToggleOverwrite
    );
    for args in [
        vec!["--auto"],
        vec!["--downscale", "0", "a.png"],
        vec!["--downscale", "-1", "a.png"],
        vec!["--unknown"],
        vec!["--to-webp", "a.png", "extra"],
    ] {
        assert!(parse(&args).is_err(), "{args:?}");
    }
}
