use anyhow::{Context, Result, bail};
use std::{
    collections::VecDeque,
    ffi::OsString,
    io::Read,
    os::windows::process::CommandExt,
    path::{Path, PathBuf},
    process::{Command, Stdio},
    sync::{Arc, Mutex, mpsc},
    time::{Duration, Instant},
};

pub fn existing_path(value: &str) -> Option<PathBuf> {
    let path = PathBuf::from(value.trim().trim_matches('"'));
    (path.is_absolute() && path.is_file()).then_some(path)
}

pub fn detect(name: &str) -> Option<PathBuf> {
    std::env::split_paths(&std::env::var_os("PATH")?)
        .filter(|p| p.is_absolute())
        .find_map(|dir| {
            let path = dir.join(format!("{name}.exe"));
            path.is_file().then_some(path)
        })
}

// None is an old implicit setting. An explicit empty/missing path never falls back.
pub fn resolve(name: &str, configured: Option<&str>) -> Option<PathBuf> {
    match configured {
        Some(path) => existing_path(path),
        None => detect(name),
    }
}

pub fn detect_decoder(cwebp: Option<&str>) -> Option<PathBuf> {
    cwebp
        .and_then(existing_path)
        .and_then(|p| p.parent().map(|p| p.join("dwebp.exe")))
        .filter(|p| p.is_file())
        .or_else(|| detect("dwebp"))
}

pub struct ProcessOutput {
    pub code: i32,
    pub stderr: String,
}

pub trait Runner {
    fn run(&self, executable: &Path, arguments: &[OsString]) -> Result<ProcessOutput>;
}

pub struct SystemRunner {
    pub timeout: Duration,
}

impl Default for SystemRunner {
    fn default() -> Self {
        Self {
            timeout: Duration::from_secs(60),
        }
    }
}

impl Runner for SystemRunner {
    fn run(&self, executable: &Path, arguments: &[OsString]) -> Result<ProcessOutput> {
        tracing::debug!(executable = %executable.display(), ?arguments, "Starting encoder");
        let mut child = Command::new(executable)
            .args(arguments)
            .creation_flags(0x0800_0000) // CREATE_NO_WINDOW, including debug builds
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::piped())
            .spawn()
            .with_context(|| format!("Start {}", executable.display()))?;
        // Always drain stderr, retaining only a bounded tail in memory.
        let mut stderr = child.stderr.take().context("Encoder stderr unavailable")?;
        let tail = Arc::new(Mutex::new(VecDeque::new()));
        let captured = tail.clone();
        let (finished, receiver) = mpsc::sync_channel(1);
        if let Err(error) = std::thread::Builder::new()
            .name("encoder-stderr".into())
            .spawn(move || {
                let mut chunk = [0_u8; 8192];
                loop {
                    match stderr.read(&mut chunk) {
                        Ok(0) => break,
                        Ok(count) => {
                            let mut tail = captured.lock().unwrap_or_else(|p| p.into_inner());
                            tail.extend(&chunk[..count]);
                            let excess = tail.len().saturating_sub(64 * 1024);
                            tail.drain(..excess);
                        }
                        Err(error) if error.kind() == std::io::ErrorKind::Interrupted => continue,
                        Err(_) => break,
                    }
                }
                let _ = finished.send(());
            })
        {
            let _ = child.kill();
            let _ = child.wait();
            return Err(error).context("Start encoder stderr reader");
        }
        let start = Instant::now();
        loop {
            if let Some(status) = child.try_wait()? {
                let stderr = finish_stderr(&receiver, &tail);
                let code = status.code().unwrap_or(-1);
                tracing::debug!(code, elapsed_ms = start.elapsed().as_millis(), %stderr, "Encoder finished");
                return Ok(ProcessOutput { code, stderr });
            }
            if start.elapsed() >= self.timeout {
                // /T also stops encoder children launched by Scoop shims.
                let _ = Command::new("taskkill.exe")
                    .args(["/PID", &child.id().to_string(), "/T", "/F"])
                    .creation_flags(0x0800_0000)
                    .stdout(Stdio::null())
                    .stderr(Stdio::null())
                    .status();
                let _ = child.kill();
                let _ = child.wait();
                bail!(
                    "{} timed out after {:?}: {}",
                    executable.display(),
                    self.timeout,
                    finish_stderr(&receiver, &tail)
                );
            }
            std::thread::sleep(Duration::from_millis(25));
        }
    }
}

fn finish_stderr(finished: &mpsc::Receiver<()>, tail: &Mutex<VecDeque<u8>>) -> String {
    // A descendant could retain the pipe after the encoder exits. Never hang on it.
    let incomplete = finished.recv_timeout(Duration::from_secs(1)).is_err();
    let mut tail = tail.lock().unwrap_or_else(|p| p.into_inner());
    let mut text = String::from_utf8_lossy(tail.make_contiguous())
        .trim()
        .to_owned();
    if incomplete {
        text.push_str("\n[stderr capture incomplete]");
    }
    text
}
