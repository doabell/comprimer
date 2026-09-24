use comprimer::{config, settings::Settings};
use image::{GenericImageView, Rgba, RgbaImage};
use std::{
    ffi::OsStr,
    fs,
    os::windows::process::CommandExt,
    path::Path,
    process::{Command, Output, Stdio},
    time::{Duration, Instant},
};

fn headless(args: &[&OsStr], settings: &Path) -> Output {
    let mut child = Command::new(env!("CARGO_BIN_EXE_Comprimer"))
        .arg("--headless")
        .args(args)
        .env("COMPRIMER_CONFIG", settings)
        .env("RUST_LOG", "comprimer=info")
        .creation_flags(0x0800_0000)
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    let deadline = Instant::now() + Duration::from_secs(15);
    while child.try_wait().unwrap().is_none() {
        if Instant::now() >= deadline {
            let _ = child.kill();
            let _ = child.wait();
            panic!("Headless command failed to exit; it may have opened a window");
        }
        std::thread::sleep(Duration::from_millis(10));
    }
    child.wait_with_output().unwrap()
}

#[test]
fn headless_resizes_without_settings_ui_or_saving_preferences() {
    let dir = tempfile::tempdir().unwrap();
    let input = dir.path().join("photo été.png");
    RgbaImage::from_pixel(80, 40, Rgba([10, 20, 30, 128]))
        .save(&input)
        .unwrap();
    let original = fs::read(&input).unwrap();
    let path = dir.path().join("settings.json");
    let mut settings = Settings::default();
    settings.executables.pngquant_path = Some(String::new());
    config::save(&path, &settings).unwrap();
    let preferences = fs::read(&path).unwrap();
    let result = headless(
        &["--downscale".as_ref(), "20".as_ref(), input.as_os_str()],
        &path,
    );
    assert!(
        result.status.success(),
        "{}",
        String::from_utf8_lossy(&result.stderr)
    );
    let output = image::open(dir.path().join("photo été-20px.png")).unwrap();
    assert_eq!(output.dimensions(), (20, 10));
    assert_eq!(output.to_rgba8().get_pixel(0, 0)[3], 128);
    assert_eq!(fs::read(&input).unwrap(), original);
    assert_eq!(fs::read(&path).unwrap(), preferences);
}

#[test]
fn incomplete_headless_calls_exit_with_logged_errors() {
    let dir = tempfile::tempdir().unwrap();
    let settings = dir.path().join("settings.json");
    for args in [
        vec![],
        vec![OsStr::new("--auto")],
        vec![OsStr::new("--auto"), OsStr::new("missing.png")],
    ] {
        let result = headless(&args, &settings);
        assert_eq!(result.status.code(), Some(1));
    }
    let log = fs::read_to_string(dir.path().join("comprimer.log")).unwrap();
    assert!(log.contains("Headless mode requires a command"));
    assert!(log.contains("Invalid command or arguments"));
    assert!(log.contains("Source does not exist"));
    assert!(!settings.exists());
}
