use super::{ImageService, classify, publication::Publication, selection::Selection};
use crate::tools::{ControlledRunner, RunControl, RunStopped};
use anyhow::{Context, Result, bail};
use image::{DynamicImage, ImageFormat};
use std::{
    fs,
    panic::{AssertUnwindSafe, catch_unwind},
    path::{Path, PathBuf},
    sync::{OnceLock, mpsc},
    thread,
    time::{Duration, Instant},
};

const INITIAL_BUDGET: Duration = Duration::from_secs(1);
const TOTAL_BUDGET: Duration = Duration::from_secs(5);
// Reserve time inside the visible-result and total budgets for atomic publication,
// process startup, and the bounded process-tree termination in SystemRunner.
const PUBLICATION_RESERVE: Duration = Duration::from_millis(150);
const CLEANUP_RESERVE: Duration = Duration::from_millis(750);
const PREVIEW_SIZE: u32 = 512;

#[cfg(test)]
mod tests;

/// Share the same start and deadlines across decoding and every encoder.
/// Tests of ordering can use a generous allowance independently of the tests
/// that exercise the production one/five-second timing targets.
pub(super) struct Timing {
    pub started: Instant,
    pub initial_deadline: Instant,
    pub control: RunControl,
}

impl Timing {
    pub fn new(initial: Duration, total: Duration) -> Self {
        let started = Instant::now();
        Self {
            started,
            initial_deadline: started + initial - PUBLICATION_RESERVE,
            control: RunControl::new(started + total - CLEANUP_RESERVE),
        }
    }
}

impl Default for Timing {
    fn default() -> Self {
        Self::new(INITIAL_BUDGET, TOTAL_BUDGET)
    }
}

enum Event {
    InputReady,
    Preview(Result<Selection>),
    Encoded(&'static str, Result<()>),
    Failed(anyhow::Error),
}

fn spawn<'scope, 'env: 'scope>(
    scope: &'scope thread::Scope<'scope, 'env>,
    name: &str,
    sender: &mpsc::Sender<Event>,
    job: impl FnOnce() -> Event + Send + 'scope,
) -> Result<()> {
    let sender = sender.clone();
    let span = tracing::Span::current();
    thread::Builder::new()
        .name(name.into())
        .spawn_scoped(scope, move || {
            let _entered = span.enter();
            let event = catch_unwind(AssertUnwindSafe(job))
                .unwrap_or_else(|_| Event::Failed(anyhow::anyhow!("Auto worker panicked")));
            let _ = sender.send(event);
        })
        .with_context(|| format!("Start {name} worker"))?;
    Ok(())
}

fn selection(work: &Path) -> Result<Selection> {
    Ok(Selection::from_sizes(
        fs::metadata(work.join("candidate.png"))?.len(),
        fs::metadata(work.join("candidate.jpg"))?.len(),
    ))
}

fn candidates(work: &Path, selection: Selection, webp_ready: bool) -> Vec<(&'static str, PathBuf)> {
    selection
        .formats()
        .iter()
        .filter(|format| **format != "webp" || webp_ready)
        .map(|format| (*format, work.join(format!("candidate.{format}"))))
        .collect()
}

fn provisional_candidates(
    work: &Path,
    ready: &[bool; 3],
    png_input: Option<&PathBuf>,
) -> Vec<(&'static str, PathBuf)> {
    Selection::Both
        .formats()
        .iter()
        .filter_map(|format| {
            let source = match *format {
                "png" if ready[0] => work.join("candidate.png"),
                "png" => png_input?.clone(),
                "jpg" if ready[1] => work.join("candidate.jpg"),
                "webp" if ready[2] => work.join("candidate.webp"),
                _ => return None,
            };
            Some((*format, source))
        })
        .collect()
}

fn preview(
    service: &ImageService<'_>,
    bitmap: &DynamicImage,
    work: &Path,
    control: &RunControl,
) -> Result<Selection> {
    control.check()?;
    let preview = bitmap.thumbnail(PREVIEW_SIZE, PREVIEW_SIZE);
    let work = work.join("preview");
    fs::create_dir(&work)?;
    let runner = ControlledRunner {
        inner: service.runner,
        control,
    };
    let service = ImageService {
        settings: service.settings,
        runner: &runner,
    };
    thread::scope(|scope| -> Result<()> {
        let png = thread::Builder::new()
            .name("preview-png".into())
            .spawn_scoped(scope, || {
                service.encode(&preview, &work.join("candidate.png"), "png", &work, true)
            })?;
        let jpg = service.encode(&preview, &work.join("candidate.jpg"), "jpg", &work, true);
        let png = png
            .join()
            .map_err(|_| anyhow::anyhow!("Preview encoder panicked"))?;
        png?;
        jpg?;
        Ok(())
    })?;
    control.check()?;
    selection(&work)
}

pub(super) fn process(
    service: &ImageService<'_>,
    bitmap: &DynamicImage,
    input: &Path,
    work: &Path,
    size: Option<u32>,
    timing: &Timing,
) -> Result<Vec<PathBuf>> {
    let started = timing.started;
    let control = &timing.control;
    let initial_budget = timing.initial_deadline.duration_since(started);
    let mut published = Publication::new(service.settings.overwrite_original);
    let suffix = size.map(|size| format!("-{size}px")).unwrap_or_default();
    let small = bitmap.width().max(bitmap.height()) <= PREVIEW_SIZE;
    let (sender, receiver) = mpsc::channel();
    let png_input: OnceLock<Result<PathBuf>> = OnceLock::new();
    let runner = ControlledRunner {
        inner: service.runner,
        control,
    };
    let bounded = ImageService {
        settings: service.settings,
        runner: &runner,
    };
    let prepare_png = || -> Result<&Path> {
        let result = png_input.get_or_init(|| {
            let result = (|| -> Result<PathBuf> {
                control.check()?;
                let path = work.join("encoder-input.png");
                bitmap.save_with_format(&path, ImageFormat::Png)?;
                control.check()?;
                Ok(path)
            })();
            let _ = sender.send(Event::InputReady);
            result
        });
        result.as_ref().map(PathBuf::as_path).map_err(|error| {
            if error.is::<RunStopped>() {
                RunStopped.into()
            } else {
                anyhow::anyhow!("{error:#}")
            }
        })
    };
    let encode = |format: &'static str| -> Result<()> {
        control.check()?;
        let output = work.join(format!("candidate.{format}"));
        if format == "jpg" {
            bounded.encode(bitmap, &output, format, work, true)
        } else {
            let input = prepare_png()?;
            let encoder = bounded
                .encoder(format)
                .with_context(|| format!("Missing {format} encoder"))?;
            bounded.encode_external(input, &output, format, &encoder)
        }
    };
    let result = thread::scope(|scope| {
        let result = (|| -> Result<Vec<PathBuf>> {
            spawn(scope, "auto-jpg", &sender, || {
                Event::Encoded("jpg", encode("jpg"))
            })?;
            spawn(scope, "auto-webp", &sender, || {
                Event::Encoded("webp", encode("webp"))
            })?;
            spawn(scope, "auto-png", &sender, || {
                Event::Encoded("png", encode("png"))
            })?;
            let classification_started = Instant::now();
            let classification = (!small).then(|| classify::classify(bitmap, input));
            let mut prediction = classification
                .as_ref()
                .and_then(|value| value.decision.png())
                .map(|png| {
                    if png {
                        Selection::Png
                    } else {
                        Selection::JpgWebp
                    }
                });
            tracing::info!(
                elapsed_us = classification_started.elapsed().as_micros(),
                prediction = ?prediction,
                preview_enabled = service.settings.auto_preview,
                reason = classification.as_ref().map_or("small-image", |value| value.reason),
                "Auto classification complete"
            );
            if !small && prediction.is_none() && service.settings.auto_preview {
                let preview_control = control.until(started + initial_budget);
                spawn(scope, "auto-preview", &sender, move || {
                    Event::Preview(preview(service, bitmap, work, &preview_control))
                })?;
            }
            let mut ready = [false; 3]; // PNG, JPG, WebP
            let mut provisional_png = false;
            loop {
                let verified = if ready[0] && ready[1] {
                    Some(selection(work)?)
                } else {
                    None
                };
                if let Some(selection) = verified {
                    // Publish the full PNG/JPG decision immediately, including
                    // both files for a near tie. WebP may join them later.
                    let selected = candidates(work, selection, ready[2]);
                    if !published.matches(&selected) {
                        published.set(input, &suffix, &selected)?;
                    }
                    if selection == Selection::Png || ready[2] {
                        let corrected =
                            prediction.is_some_and(|prediction| prediction != selection);
                        let paths = published.paths();
                        tracing::info!(
                            elapsed_ms = started.elapsed().as_millis(),
                            corrected,
                            ?selection,
                            ?paths,
                            "Auto verified result published"
                        );
                        return Ok(paths);
                    }
                } else {
                    let input_ready = png_input.get().and_then(|result| result.as_ref().ok());
                    provisional_png |= matches!(prediction, Some(Selection::Png | Selection::Both))
                        || (published.is_empty()
                            && !ready.iter().any(|ready| *ready)
                            && started.elapsed() >= initial_budget);
                    // Every finished encoder is independently publishable while
                    // the PNG/JPG decision is pending, including WebP first.
                    let initial = provisional_candidates(
                        work,
                        &ready,
                        input_ready.filter(|_| provisional_png),
                    );
                    // A full-size decision always outranks a preview or hint.
                    // Never let a late preview remove a verified near-tie PNG.
                    if !initial.is_empty() && !published.matches(&initial) {
                        let first = published.is_empty();
                        published.set(input, &suffix, &initial)?;
                        tracing::info!(elapsed_ms = started.elapsed().as_millis(), first,
                            paths = ?published.paths(), "Auto provisional result published");
                    }
                }

                if control.check().is_err() {
                    if published.is_empty() {
                        bail!("Auto time budget exhausted before an output was ready");
                    }
                    tracing::warn!(elapsed_ms = started.elapsed().as_millis(), paths = ?published.paths(),
                        "Auto verification deadline reached; keeping provisional output");
                    return Ok(published.paths());
                }

                let wake = if started.elapsed() < initial_budget {
                    (started + initial_budget).min(control.deadline)
                } else {
                    control.deadline
                };
                match receiver.recv_timeout(wake.saturating_duration_since(Instant::now())) {
                    Ok(Event::InputReady) => {}
                    Ok(Event::Preview(Ok(selection))) => {
                        prediction = Some(selection);
                        tracing::info!(prediction = ?selection, "Auto preview prediction ready");
                    }
                    Ok(Event::Preview(Err(error))) => {
                        tracing::debug!(%error, "Preview unavailable; using full-size verification");
                    }
                    Ok(Event::Encoded(format, result)) => match result {
                        Ok(()) => {
                            ready[match format {
                                "png" => 0,
                                "jpg" => 1,
                                _ => 2,
                            }] = true
                        }
                        Err(error) if error.is::<RunStopped>() => {}
                        Err(error) => return Err(error),
                    },
                    Ok(Event::Failed(error)) => return Err(error),
                    Err(mpsc::RecvTimeoutError::Timeout) => {}
                    Err(mpsc::RecvTimeoutError::Disconnected) => bail!("Auto workers disconnected"),
                }
            }
        })();
        control.cancel();
        result
    });
    if let Err(error) = result {
        published
            .rollback()
            .with_context(|| format!("Auto failed: {error:#}"))?;
        return Err(error);
    }
    result
}
