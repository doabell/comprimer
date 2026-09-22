use comprimer::tools::{Runner, SystemRunner};
use std::{
    ffi::OsString,
    io::Write,
    time::{Duration, Instant},
};

#[test]
#[ignore = "subprocess fixture"]
fn noisy_encoder_helper() {
    if !std::env::args().any(|arg| arg == "noisy_encoder_helper") {
        return;
    }
    let mut stderr = std::io::stderr().lock();
    for _ in 0..1024 {
        stderr.write_all(&[b'x'; 1024]).unwrap();
    }
    stderr.write_all(b"\nstderr-tail-marker\n").unwrap();
    stderr.flush().unwrap();
    std::process::exit(7);
}

#[test]
#[ignore = "subprocess fixture"]
fn slow_encoder_helper() {
    if !std::env::args().any(|arg| arg == "slow_encoder_helper") {
        return;
    }
    std::thread::sleep(Duration::from_secs(30));
}

fn arguments(helper: &str) -> Vec<OsString> {
    ["--exact", helper, "--ignored", "--nocapture"]
        .into_iter()
        .map(OsString::from)
        .collect()
}

#[test]
fn large_stderr_does_not_deadlock_or_grow_without_bound() {
    let result = SystemRunner::default()
        .run(
            &std::env::current_exe().unwrap(),
            &arguments("noisy_encoder_helper"),
        )
        .unwrap();
    assert_eq!(result.code, 7);
    assert!(result.stderr.len() <= 64 * 1024);
    assert!(result.stderr.ends_with("stderr-tail-marker"));
}

#[test]
fn timeout_stops_the_encoder_and_reports_the_reason() {
    let runner = SystemRunner {
        timeout: Duration::from_millis(150),
    };
    let start = Instant::now();
    let error = match runner.run(
        &std::env::current_exe().unwrap(),
        &arguments("slow_encoder_helper"),
    ) {
        Ok(_) => panic!("Expected timeout"),
        Err(error) => error,
    };
    assert!(error.to_string().contains("timed out"));
    assert!(start.elapsed() < Duration::from_secs(5));
}
