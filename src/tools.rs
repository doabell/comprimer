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

/// Implementations may be called concurrently by Auto's three encoder jobs.
pub trait Runner: Sync {
    fn run(&self, executable: &Path, arguments: &[OsString]) -> Result<ProcessOutput>;

    fn run_controlled(
        &self,
        executable: &Path,
        arguments: &[OsString],
        control: &RunControl,
    ) -> Result<ProcessOutput> {
        control.check()?;
        let result = self.run(executable, arguments);
        control.check()?;
        result
    }
}

#[derive(Clone)]
pub struct RunControl {
    pub deadline: Instant,
    cancelled: Arc<std::sync::atomic::AtomicBool>,
}

#[derive(Debug)]
pub struct RunStopped;

impl std::fmt::Display for RunStopped {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("Encoder cancelled or time budget exhausted")
    }
}
impl std::error::Error for RunStopped {}

impl RunControl {
    pub fn new(deadline: Instant) -> Self {
        Self {
            deadline,
            cancelled: Arc::new(std::sync::atomic::AtomicBool::new(false)),
        }
    }
    pub fn until(&self, deadline: Instant) -> Self {
        Self {
            deadline: deadline.min(self.deadline),
            cancelled: self.cancelled.clone(),
        }
    }
    pub fn cancel(&self) {
        self.cancelled
            .store(true, std::sync::atomic::Ordering::Relaxed);
    }
    pub fn check(&self) -> Result<()> {
        if Instant::now() >= self.deadline
            || self.cancelled.load(std::sync::atomic::Ordering::Relaxed)
        {
            return Err(RunStopped.into());
        }
        Ok(())
    }
}

pub(crate) struct ControlledRunner<'a> {
    pub inner: &'a dyn Runner,
    pub control: &'a RunControl,
}

impl Runner for ControlledRunner<'_> {
    fn run(&self, executable: &Path, arguments: &[OsString]) -> Result<ProcessOutput> {
        self.inner
            .run_controlled(executable, arguments, self.control)
    }
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
        self.run_inner(executable, arguments, None)
    }

    fn run_controlled(
        &self,
        executable: &Path,
        arguments: &[OsString],
        control: &RunControl,
    ) -> Result<ProcessOutput> {
        self.run_inner(executable, arguments, Some(control))
    }
}

impl SystemRunner {
    fn run_inner(
        &self,
        executable: &Path,
        arguments: &[OsString],
        control: Option<&RunControl>,
    ) -> Result<ProcessOutput> {
        if let Some(control) = control {
            control.check()?;
        }
        let start = Instant::now();
        let capture_budget = || {
            control.map_or(Duration::from_secs(1), |c| {
                c.deadline
                    .saturating_duration_since(Instant::now())
                    .min(Duration::from_secs(1))
            })
        };
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
        loop {
            if let Some(status) = child.try_wait()? {
                let stderr = finish_stderr(&receiver, &tail, capture_budget());
                let code = status.code().unwrap_or(-1);
                tracing::debug!(code, elapsed_ms = start.elapsed().as_millis(), %stderr, "Encoder finished");
                if let Some(control) = control {
                    control.check()?;
                }
                return Ok(ProcessOutput { code, stderr });
            }
            if start.elapsed() >= self.timeout || control.is_some_and(|c| c.check().is_err()) {
                // /T also stops encoder children launched by Scoop shims.
                if let Ok(mut killer) = Command::new("taskkill.exe")
                    .args(["/PID", &child.id().to_string(), "/T", "/F"])
                    .creation_flags(0x0800_0000)
                    .stdout(Stdio::null())
                    .stderr(Stdio::null())
                    .spawn()
                {
                    let cleanup = Instant::now();
                    while matches!(killer.try_wait(), Ok(None)) {
                        if cleanup.elapsed() >= Duration::from_millis(500) {
                            let _ = killer.kill();
                            break;
                        }
                        std::thread::sleep(Duration::from_millis(5));
                    }
                    let _ = killer.wait();
                }
                let _ = child.kill();
                let _ = child.wait();
                if control.is_some_and(|c| c.check().is_err()) {
                    return Err(RunStopped.into());
                }
                bail!(
                    "{} timed out after {:?}: {}",
                    executable.display(),
                    self.timeout,
                    finish_stderr(&receiver, &tail, capture_budget())
                );
            }
            std::thread::sleep(Duration::from_millis(25));
        }
    }
}

fn finish_stderr(
    finished: &mpsc::Receiver<()>,
    tail: &Mutex<VecDeque<u8>>,
    timeout: Duration,
) -> String {
    // A descendant could retain the pipe after the encoder exits. Never hang on it.
    let incomplete = finished.recv_timeout(timeout).is_err();
    let mut tail = tail.lock().unwrap_or_else(|p| p.into_inner());
    let mut text = String::from_utf8_lossy(tail.make_contiguous())
        .trim()
        .to_owned();
    if incomplete {
        text.push_str("\n[stderr capture incomplete]");
    }
    text
}
