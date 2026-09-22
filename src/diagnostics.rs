use anyhow::Result;
use std::{
    collections::VecDeque,
    fs::{self, File, TryLockError},
    hash::{DefaultHasher, Hash, Hasher},
    io::{self, Read, Seek, SeekFrom, Write},
    path::{Path, PathBuf},
    sync::{Arc, Mutex, OnceLock},
    time::{Duration, Instant},
};
use tracing_subscriber::{
    EnvFilter, fmt::MakeWriter, layer::SubscriberExt, util::SubscriberInitExt,
};

const LOG_LIMIT: u64 = 2 * 1024 * 1024;
const RECORD_LIMIT: usize = 64 * 1024;
const VIEW_LIMIT: usize = 64 * 1024;
const BACKUPS: usize = 2;
static LOGGER: OnceLock<RollingLog> = OnceLock::new();

pub fn log_path(config: &Path) -> PathBuf {
    config.with_file_name("comprimer.log")
}

pub fn active_log_path(config: &Path) -> PathBuf {
    LOGGER
        .get()
        .filter(|log| log.0.primary == log_path(config))
        .map(|log| log.state().active.clone())
        .unwrap_or_else(|| log_path(config))
}

pub fn init(config: &Path) -> Result<()> {
    let primary = log_path(config);
    let mut hash = DefaultHasher::new();
    std::path::absolute(&primary)
        .unwrap_or_else(|_| primary.clone())
        .hash(&mut hash);
    let fallback = std::env::temp_dir()
        .join("Comprimer")
        .join(format!("logs-{:x}", hash.finish()))
        .join("comprimer.log");
    let logger = RollingLog::new(primary, fallback, LOG_LIMIT);
    // Probe early, even if RUST_LOG disables informational events.
    logger.record(b"");
    let _ = LOGGER.set(logger.clone());
    let requested_filter = EnvFilter::try_from_default_env();
    let invalid_filter = std::env::var_os("RUST_LOG").is_some() && requested_filter.is_err();
    let filter = requested_filter.unwrap_or_else(|_| EnvFilter::new("warn,comprimer=info"));
    tracing_subscriber::registry()
        .with(filter)
        .with(
            tracing_subscriber::fmt::layer()
                .with_ansi(false)
                .with_thread_ids(true)
                .with_writer(logger.clone()),
        )
        .with(cfg!(debug_assertions).then(|| {
            tracing_subscriber::fmt::layer()
                .with_ansi(false)
                .with_writer(std::io::stderr)
        }))
        .try_init()?;
    std::panic::set_hook(Box::new(move |info| {
        // Bypass subscriber filters and the in-process mutex in the panic path.
        let message = format!(
            "PANIC: {info}\n{}\n",
            std::backtrace::Backtrace::force_capture()
        );
        logger.emergency(message.as_bytes());
        let _ = writeln!(std::io::stderr(), "{message}");
    }));
    if invalid_filter {
        tracing::warn!("Invalid RUST_LOG; using defaults");
    }
    tracing::info!(version = env!("CARGO_PKG_VERSION"), log = %active_log_path(config).display(), "Application started");
    Ok(())
}

pub fn recent_log(config: &Path) -> Result<String> {
    if let Some(logger) = LOGGER.get().filter(|log| log.0.primary == log_path(config)) {
        let state = logger.state();
        let mut text = state
            .warning
            .as_ref()
            .map(|warning| format!("Logging: {warning}\n"))
            .unwrap_or_default();
        match read_tail(&state.active) {
            Ok(recent) => text.push_str(&recent),
            Err(error) if !state.memory.is_empty() => {
                text.push_str(&format!("Log read failed: {error}\n"))
            }
            Err(error) => return Err(error.into()),
        }
        for record in &state.memory {
            text.push_str(record);
        }
        return Ok(text);
    }
    Ok(read_tail(&log_path(config))?)
}

#[derive(Clone)]
struct RollingLog(Arc<LogInner>);
struct LogInner {
    primary: PathBuf,
    fallback: PathBuf,
    max_bytes: u64,
    state: Mutex<LogState>,
}
struct LogState {
    active: PathBuf,
    warning: Option<String>,
    memory: VecDeque<String>,
    memory_bytes: usize,
}

impl RollingLog {
    fn new(primary: PathBuf, fallback: PathBuf, max_bytes: u64) -> Self {
        Self(Arc::new(LogInner {
            state: Mutex::new(LogState {
                active: primary.clone(),
                warning: None,
                memory: VecDeque::new(),
                memory_bytes: 0,
            }),
            primary,
            fallback,
            max_bytes,
        }))
    }

    fn state(&self) -> std::sync::MutexGuard<'_, LogState> {
        self.0
            .state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    fn record_limit(&self) -> usize {
        RECORD_LIMIT
            .min(VIEW_LIMIT - 96)
            .min(self.0.max_bytes.saturating_sub(96) as usize)
    }

    fn record(&self, bytes: &[u8]) {
        let record = bounded_record(bytes, self.record_limit());
        let mut state = self.state();
        if let Err(error) = append_file(&state.active, record.as_bytes(), self.0.max_bytes) {
            let mut warning = format!("{}: {error}", state.active.display());
            if state.active != self.0.fallback {
                match append_file(&self.0.fallback, record.as_bytes(), self.0.max_bytes) {
                    Ok(()) => {
                        warning.push_str(&format!("; using {}", self.0.fallback.display()));
                        state.active = self.0.fallback.clone();
                        Self::warn(&mut state, warning);
                        return;
                    }
                    Err(fallback_error) => {
                        warning.push_str(&format!("; fallback: {fallback_error}"))
                    }
                }
            }
            Self::warn(&mut state, warning);
            state.memory_bytes += record.len();
            state.memory.push_back(record);
            while state.memory_bytes > VIEW_LIMIT {
                if let Some(oldest) = state.memory.pop_front() {
                    state.memory_bytes -= oldest.len();
                }
            }
        }
    }

    fn warn(state: &mut LogState, warning: String) {
        if state.warning.as_ref() != Some(&warning) {
            let _ = writeln!(std::io::stderr(), "Comprimer logging: {warning}");
        }
        state.warning = Some(warning);
    }

    fn emergency(&self, bytes: &[u8]) {
        let record = bounded_record(bytes, self.record_limit());
        if append_file(&self.0.primary, record.as_bytes(), self.0.max_bytes).is_err() {
            let _ = append_file(&self.0.fallback, record.as_bytes(), self.0.max_bytes);
        }
    }
}

struct RecordWriter {
    log: RollingLog,
    bytes: Vec<u8>,
    truncated: bool,
}
impl Write for RecordWriter {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        let take = bytes
            .len()
            .min(self.log.record_limit().saturating_sub(self.bytes.len()));
        self.bytes.extend_from_slice(&bytes[..take]);
        self.truncated |= take < bytes.len();
        Ok(bytes.len())
    }
    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}
impl Drop for RecordWriter {
    fn drop(&mut self) {
        if self.truncated {
            self.bytes.extend_from_slice(b" [truncated]\n");
        }
        self.log.record(&self.bytes);
    }
}
impl<'a> MakeWriter<'a> for RollingLog {
    type Writer = RecordWriter;
    fn make_writer(&'a self) -> Self::Writer {
        RecordWriter {
            log: self.clone(),
            bytes: Vec::new(),
            truncated: false,
        }
    }
}

fn bounded_record(bytes: &[u8], limit: usize) -> String {
    if bytes.is_empty() {
        return String::new();
    }
    let clipped = &bytes[..bytes.len().min(limit)];
    let text = String::from_utf8_lossy(clipped);
    // Strip terminal control characters, preserving readable multiline diagnostics.
    let text: String = text
        .chars()
        .filter(|ch| !ch.is_control() || *ch == '\n' || *ch == '\t')
        .collect();
    format!(
        "[pid={}] {}{}\n",
        std::process::id(),
        text.trim_end(),
        if bytes.len() > limit {
            " [truncated]"
        } else {
            ""
        }
    )
}

fn suffixed(path: &Path, suffix: &str) -> PathBuf {
    let mut name = path.as_os_str().to_os_string();
    name.push(suffix);
    PathBuf::from(name)
}

fn lock(path: &Path) -> io::Result<File> {
    let file = File::options()
        .read(true)
        .write(true)
        .create(true)
        .truncate(false)
        .open(suffixed(path, ".lock"))?;
    let start = Instant::now();
    loop {
        match file.try_lock() {
            Ok(()) => return Ok(file),
            Err(TryLockError::WouldBlock) if start.elapsed() < Duration::from_millis(250) => {
                std::thread::sleep(Duration::from_millis(5))
            }
            Err(TryLockError::WouldBlock) => {
                return Err(io::Error::new(
                    io::ErrorKind::TimedOut,
                    "Log lock timed out",
                ));
            }
            Err(TryLockError::Error(error)) => return Err(error),
        }
    }
}

fn append_file(path: &Path, record: &[u8], max_bytes: u64) -> io::Result<()> {
    if let Some(parent) = path.parent().filter(|p| !p.as_os_str().is_empty()) {
        fs::create_dir_all(parent)?;
    }
    // A stable, separate lock file protects rotation and complete records across processes.
    // No log handle survives releasing this lock, so Windows can always rename old files.
    let _lock = lock(path)?;
    let len = match fs::metadata(path) {
        Ok(meta) => meta.len(),
        Err(error) if error.kind() == io::ErrorKind::NotFound => 0,
        Err(error) => return Err(error),
    };
    if len > 0 && len.saturating_add(record.len() as u64) > max_bytes {
        for index in (1..=BACKUPS).rev() {
            let destination = suffixed(path, &format!(".{index}"));
            match fs::remove_file(&destination) {
                Ok(()) => {}
                Err(error) if error.kind() == io::ErrorKind::NotFound => {}
                Err(error) => return Err(error),
            }
            let source = if index == 1 {
                path.to_owned()
            } else {
                suffixed(path, &format!(".{}", index - 1))
            };
            match fs::rename(source, destination) {
                Ok(()) => {}
                Err(error) if error.kind() == io::ErrorKind::NotFound => {}
                Err(error) => return Err(error),
            }
        }
    }
    let mut file = File::options().create(true).append(true).open(path)?;
    file.write_all(record)?;
    file.flush()?;
    Ok(())
}

fn read_tail(path: &Path) -> io::Result<String> {
    if !path.try_exists()? {
        return Ok(String::new());
    }
    let _lock = lock(path)?;
    let mut file = File::open(path)?;
    let len = file.metadata()?.len();
    let start = len.saturating_sub(VIEW_LIMIT as u64);
    file.seek(SeekFrom::Start(start))?;
    let mut bytes = Vec::new();
    file.take(VIEW_LIMIT as u64).read_to_end(&mut bytes)?;
    if start > 0
        && let Some(newline) = bytes.iter().position(|byte| *byte == b'\n')
    {
        bytes.drain(..=newline);
    }
    Ok(String::from_utf8_lossy(&bytes).into_owned())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rotation_bounds_size_and_retention_during_writes() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("comprimer.log");
        let log = RollingLog::new(path.clone(), dir.path().join("fallback.log"), 512);
        for i in 0..200 {
            log.record(format!("record {i} {}", "x".repeat(80)).as_bytes());
        }
        for suffix in ["", ".1", ".2"] {
            assert!(fs::metadata(suffixed(&path, suffix)).unwrap().len() <= 512);
        }
        assert!(!suffixed(&path, ".3").exists());
        assert!(read_tail(&path).unwrap().contains("record 199"));
        log.record("é".repeat(20_000).as_bytes());
        assert!(fs::metadata(&path).unwrap().len() <= 512);
        assert!(read_tail(&path).unwrap().contains("truncated"));
    }

    #[test]
    fn unavailable_primary_uses_fallback_without_panicking() {
        let dir = tempfile::tempdir().unwrap();
        let blocked = dir.path().join("blocked");
        fs::write(&blocked, b"file").unwrap();
        let fallback = dir.path().join("fallback.log");
        let log = RollingLog::new(blocked.join("comprimer.log"), fallback.clone(), LOG_LIMIT);
        log.record(b"still operational");
        assert_eq!(log.state().active, fallback);
        assert!(log.state().warning.is_some());
        assert!(read_tail(&fallback).unwrap().contains("still operational"));
    }

    #[test]
    fn lock_contention_has_a_bounded_fallback() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("comprimer.log");
        let _held = lock(&path).unwrap();
        let log = RollingLog::new(path, dir.path().join("fallback.log"), LOG_LIMIT);
        let start = Instant::now();
        log.record(b"contended");
        assert!(start.elapsed() < Duration::from_secs(2));
        assert!(
            read_tail(&log.state().active)
                .unwrap()
                .contains("contended")
        );
    }

    #[test]
    fn failure_buffer_and_records_are_bounded() {
        let dir = tempfile::tempdir().unwrap();
        let blocked = dir.path().join("blocked");
        fs::write(&blocked, b"file").unwrap();
        let log = RollingLog::new(blocked.join("a.log"), blocked.join("b.log"), LOG_LIMIT);
        for _ in 0..12 {
            log.record(&vec![b'x'; 12_000]);
        }
        assert!(log.state().memory_bytes <= VIEW_LIMIT);
        assert!(!log.state().memory.is_empty());
    }
}
