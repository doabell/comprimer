use comprimer::diagnostics;
use std::{
    fs,
    process::{Command, Stdio},
};

#[test]
#[ignore = "subprocess fixture"]
fn logging_child() {
    let Ok(directory) = std::env::var("COMPRIMER_LOG_TEST_DIR") else {
        return;
    };
    diagnostics::init(&std::path::PathBuf::from(directory).join("settings.json")).unwrap();
    if std::env::var("COMPRIMER_LOG_TEST_MODE").as_deref() == Ok("panic") {
        panic!("intentional panic diagnostic");
    }
    let worker = std::env::var("COMPRIMER_LOG_TEST_WORKER").unwrap();
    for record in 0..25 {
        tracing::info!("concurrent_record {worker}/{record}");
    }
}

fn child(directory: &std::path::Path) -> Command {
    let mut command = Command::new(std::env::current_exe().unwrap());
    command
        .args(["--exact", "logging_child", "--ignored", "--nocapture"])
        .env("COMPRIMER_LOG_TEST_DIR", directory)
        .env("RUST_LOG", "info")
        .stdout(Stdio::null())
        .stderr(Stdio::piped());
    command
}

#[test]
fn concurrent_processes_keep_complete_records() {
    let dir = tempfile::tempdir().unwrap();
    let children: Vec<_> = (0..4)
        .map(|index| {
            child(dir.path())
                .env("COMPRIMER_LOG_TEST_WORKER", index.to_string())
                .spawn()
                .unwrap()
        })
        .collect();
    for process in children {
        let output = process.wait_with_output().unwrap();
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
    }
    let text = fs::read_to_string(dir.path().join("comprimer.log")).unwrap();
    for worker in 0..4 {
        for record in 0..25 {
            let marker = format!("concurrent_record {worker}/{record}");
            assert_eq!(
                text.lines().filter(|line| line.ends_with(&marker)).count(),
                1,
                "{marker}"
            );
        }
    }
    assert!(text.lines().all(|line| line.starts_with("[pid=")));
    assert!(!text.contains('\u{1b}'));
}

#[test]
fn panics_are_logged_even_with_logging_disabled() {
    let dir = tempfile::tempdir().unwrap();
    let output = child(dir.path())
        .env("COMPRIMER_LOG_TEST_MODE", "panic")
        .env("RUST_LOG", "off")
        .output()
        .unwrap();
    assert!(!output.status.success());
    let text = fs::read_to_string(dir.path().join("comprimer.log")).unwrap();
    assert!(text.contains("PANIC:"));
    assert!(text.contains("intentional panic diagnostic"));
}

#[test]
fn cli_messages_use_the_application_log_filter() {
    let dir = tempfile::tempdir().unwrap();
    let settings = dir.path().join("settings.json");
    for (argument, success, message) in [
        ("--diagnostics", true, "Command started"),
        ("--invalid-command", false, "ERROR"),
    ] {
        let output = Command::new(env!("CARGO_BIN_EXE_Comprimer"))
            .arg(argument)
            .env("COMPRIMER_CONFIG", &settings)
            .env("RUST_LOG", "comprimer=info")
            .output()
            .unwrap();
        assert_eq!(output.status.success(), success);
        let log = fs::read_to_string(dir.path().join("comprimer.log")).unwrap();
        assert!(log.contains(message), "{log}");
        assert!(!settings.exists(), "Diagnostics must not save settings");
    }
}
