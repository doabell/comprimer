use super::Timing;
use crate::{
    images::{ImageService, Operation},
    settings::Settings,
    tools::{ProcessOutput, RunControl, RunStopped, Runner},
};
use anyhow::Result;
use image::{GenericImageView, ImageEncoder, Rgba, RgbaImage};
use std::{
    ffi::OsString,
    fs,
    path::{Path, PathBuf},
    sync::Mutex,
    time::{Duration, Instant},
};

type InitialSnapshot = (Duration, Vec<(String, Vec<u8>)>);

fn functional_timing() -> Timing {
    Timing::new(Duration::from_secs(30), Duration::from_secs(45))
}

struct Encoders {
    directory: PathBuf,
    predicted_png: bool,
    actual_png: bool,
    fail_verification: bool,
    expire_verification: bool,
    slow_jpg: bool,
    wait_for_preview: bool,
    preview_delay: Duration,
    preview_completed: std::sync::atomic::AtomicU8,
    started: Instant,
    original: Vec<u8>,
    first: Mutex<Option<InitialSnapshot>>,
    previews: Mutex<Vec<(u32, u32)>>,
}

impl Encoders {
    fn encode(
        &self,
        executable: &Path,
        args: &[OsString],
        control: Option<&RunControl>,
    ) -> Result<ProcessOutput> {
        let name = executable.file_stem().unwrap().to_str().unwrap();
        let output_flag = match name {
            "pngquant" => "--output",
            "cjpeg" => "-outfile",
            _ => "-o",
        };
        let output =
            PathBuf::from(&args[args.iter().position(|arg| arg == output_flag).unwrap() + 1]);
        let input = PathBuf::from(if name == "cwebp" {
            &args[2]
        } else {
            args.last().unwrap()
        });
        let image = image::open(input)?;
        let preview = image.width() <= 512;
        if preview {
            self.previews.lock().unwrap().push(image.dimensions());
            std::thread::sleep(self.preview_delay);
        } else if name == "pngquant" {
            // Hold full PNG verification until a provisional file is actually
            // visible, regardless of when the worker was launched.
            loop {
                if let Some(control) = control {
                    control.check()?;
                }
                if self.wait_for_preview
                    && self
                        .preview_completed
                        .load(std::sync::atomic::Ordering::Acquire)
                        != 3
                {
                    std::thread::sleep(Duration::from_millis(5));
                    continue;
                }
                // Poll metadata, not open file handles, while Windows is
                // replacing the provisional outputs. Read after publication.
                let files: Vec<_> = fs::read_dir(&self.directory)?
                    .filter_map(|entry| {
                        let path = entry.ok()?.path();
                        let name = path.file_name()?.to_string_lossy().into_owned();
                        path.extension().filter(|ext| {
                            ["png", "jpg", "webp"].iter().any(|value| *ext == *value)
                        })?;
                        let bytes = fs::metadata(&path).ok()?.len();
                        Some((name, path, bytes))
                    })
                    .collect();
                let visible = if self.predicted_png || self.slow_jpg {
                    files.iter().any(|(name, _, bytes)| {
                        name.ends_with(".png") && *bytes != self.original.len() as u64
                    })
                } else {
                    [".jpg", ".webp"].iter().all(|extension| {
                        files
                            .iter()
                            .any(|(name, _, bytes)| name.ends_with(extension) && *bytes == 40)
                    })
                };
                if visible {
                    std::thread::sleep(Duration::from_millis(5));
                    let mut snapshot: Vec<_> = files
                        .into_iter()
                        .filter_map(|(name, path, _)| Some((name, fs::read(path).ok()?)))
                        .collect();
                    snapshot.sort_by(|a, b| a.0.cmp(&b.0));
                    *self.first.lock().unwrap() = Some((self.started.elapsed(), snapshot));
                    break;
                }
                std::thread::sleep(Duration::from_millis(5));
            }
            if self.expire_verification {
                let control = control.unwrap();
                while control.check().is_ok() {
                    std::thread::sleep(Duration::from_millis(5));
                }
                return Err(RunStopped.into());
            }
            if self.fail_verification {
                anyhow::bail!("verification failed");
            }
        } else if (name == "cjpeg" || name == "cwebp") && self.slow_jpg {
            while self.first.lock().unwrap().is_none() {
                if let Some(control) = control {
                    control.check()?;
                }
                std::thread::sleep(Duration::from_millis(5));
            }
        }
        let png_wins = if preview {
            self.predicted_png
        } else {
            self.actual_png
        };
        let bytes = match name {
            "pngquant" if png_wins => 20,
            "pngquant" => 80,
            _ => 40,
        };
        fs::write(
            output,
            vec![if name == "pngquant" { b'P' } else { b'J' }; bytes],
        )?;
        if preview {
            self.preview_completed.fetch_or(
                if name == "pngquant" { 1 } else { 2 },
                std::sync::atomic::Ordering::Release,
            );
        }
        Ok(ProcessOutput {
            code: 0,
            stderr: String::new(),
        })
    }
}
impl Runner for Encoders {
    fn run(&self, executable: &Path, args: &[OsString]) -> Result<ProcessOutput> {
        self.encode(executable, args, None)
    }
    fn run_controlled(
        &self,
        executable: &Path,
        args: &[OsString],
        control: &RunControl,
    ) -> Result<ProcessOutput> {
        control.check()?;
        let result = self.encode(executable, args, Some(control));
        control.check()?;
        result
    }
}

fn setup(directory: &Path, predicted_png: bool, actual_png: bool) -> (Settings, PathBuf, Encoders) {
    let mut settings = Settings {
        auto_preview: true,
        ..Default::default()
    };
    for (name, value) in [
        ("pngquant", &mut settings.executables.pngquant_path),
        ("cjpeg", &mut settings.executables.cjpeg_path),
        ("cwebp", &mut settings.executables.cwebp_path),
    ] {
        let executable = directory.join(format!("{name}.exe"));
        fs::write(&executable, []).unwrap();
        *value = Some(executable.to_string_lossy().into_owned());
    }
    let input = directory.join("photo.png");
    // Deliberately mixed content stays uncertain, exercising the preview path.
    let mut state = 42u32;
    let bitmap = RgbaImage::from_fn(600, 300, |x, y| {
        if x < 450 || y < 225 {
            Rgba([40, 80, 120, 128])
        } else {
            state = state.wrapping_mul(1664525).wrapping_add(1013904223);
            let bytes = state.to_le_bytes();
            Rgba([bytes[1], bytes[2], bytes[3], 128])
        }
    });
    // Different lossless encoding makes provisional overwrites observable.
    image::codecs::png::PngEncoder::new_with_quality(
        fs::File::create(&input).unwrap(),
        image::codecs::png::CompressionType::Best,
        image::codecs::png::FilterType::Adaptive,
    )
    .write_image(bitmap.as_raw(), 600, 300, image::ExtendedColorType::Rgba8)
    .unwrap();
    let encoders = Encoders {
        directory: directory.to_owned(),
        predicted_png,
        actual_png,
        fail_verification: false,
        expire_verification: false,
        slow_jpg: false,
        wait_for_preview: false,
        preview_delay: Duration::ZERO,
        preview_completed: std::sync::atomic::AtomicU8::new(0),
        started: Instant::now(),
        original: fs::read(&input).unwrap(),
        first: Mutex::new(None),
        previews: Mutex::new(Vec::new()),
    };
    (settings, input, encoders)
}

#[test]
fn full_size_verification_corrects_both_preview_directions_and_restores_originals() {
    for (predicted_png, actual_png) in [(true, false), (false, true), (true, true), (false, false)]
    {
        for overwrite in [false, true] {
            let dir = tempfile::tempdir().unwrap();
            let (mut settings, input, mut encoders) = setup(dir.path(), predicted_png, actual_png);
            encoders.wait_for_preview = true;
            if predicted_png && !actual_png && !overwrite {
                // Reproduce a preview exceeding the production initial target.
                // Correctness must not depend on a shared runner finishing it in 850ms.
                encoders.preview_delay = Duration::from_millis(1100);
            }
            settings.overwrite_original = overwrite;
            let original = fs::read(&input).unwrap();
            fs::write(input.with_extension("jpg"), b"original JPG").unwrap();
            fs::write(input.with_extension("webp"), b"original WebP").unwrap();
            let paths = ImageService {
                settings: &settings,
                runner: &encoders,
            }
            .process_with_timing(&input, Operation::Auto(None), functional_timing())
            .unwrap();
            let first = encoders.first.lock().unwrap();
            let (elapsed, files) = first.as_ref().unwrap_or_else(|| {
                panic!("No provisional output: predicted_png={predicted_png}, actual_png={actual_png}, overwrite={overwrite}")
            });
            if !encoders.preview_delay.is_zero() {
                assert!(*elapsed >= encoders.preview_delay);
            }
            if predicted_png {
                let name = if overwrite {
                    "photo.png"
                } else {
                    "photo-1.png"
                };
                let bytes = &files.iter().find(|(path, _)| path == name).unwrap().1;
                let image = image::load_from_memory(bytes).unwrap();
                assert_eq!(
                    image.dimensions(),
                    (600, 300),
                    "Never publish the 512px preview"
                );
                assert_eq!(image.to_rgba8().get_pixel(0, 0)[3], 128);
            } else {
                let name = if overwrite {
                    "photo.jpg"
                } else {
                    "photo-1.jpg"
                };
                assert_eq!(
                    files.iter().find(|(path, _)| path == name).unwrap().1,
                    vec![b'J'; 40]
                );
            }
            assert_eq!(
                encoders.previews.lock().unwrap().as_slice(),
                &[(512, 256), (512, 256)]
            );
            assert_eq!(paths.len(), if actual_png { 1 } else { 2 });
            for path in &paths {
                assert_eq!(
                    fs::read(path).unwrap(),
                    vec![if actual_png { b'P' } else { b'J' }; if actual_png { 20 } else { 40 }]
                );
            }
            if !overwrite || !actual_png {
                assert_eq!(fs::read(&input).unwrap(), original);
            }
            if !overwrite || actual_png {
                assert_eq!(
                    fs::read(input.with_extension("jpg")).unwrap(),
                    b"original JPG"
                );
                assert_eq!(
                    fs::read(input.with_extension("webp")).unwrap(),
                    b"original WebP"
                );
            }
            assert_eq!(
                fs::read_dir(dir.path()).unwrap().count(),
                6 + if overwrite { 0 } else { paths.len() }
            );
        }
    }
}

#[test]
fn failed_verification_reverts_provisional_overwrites() {
    let dir = tempfile::tempdir().unwrap();
    let (mut settings, input, mut encoders) = setup(dir.path(), false, true);
    settings.overwrite_original = true;
    encoders.fail_verification = true;
    let original = fs::read(&input).unwrap();
    fs::write(input.with_extension("jpg"), b"original JPG").unwrap();
    fs::write(input.with_extension("webp"), b"original WebP").unwrap();
    let error = ImageService {
        settings: &settings,
        runner: &encoders,
    }
    .process_with_timing(&input, Operation::Auto(None), functional_timing())
    .unwrap_err();
    assert!(error.to_string().contains("verification failed"));
    assert!(encoders.first.lock().unwrap().is_some());
    assert_eq!(fs::read(input).unwrap(), original);
    assert_eq!(
        fs::read(dir.path().join("photo.jpg")).unwrap(),
        b"original JPG"
    );
    assert_eq!(
        fs::read(dir.path().join("photo.webp")).unwrap(),
        b"original WebP"
    );
    assert_eq!(fs::read_dir(dir.path()).unwrap().count(), 6);
}

#[test]
fn five_second_deadline_keeps_the_valid_initial_result() {
    let dir = tempfile::tempdir().unwrap();
    let (settings, input, mut encoders) = setup(dir.path(), false, true);
    encoders.expire_verification = true;
    encoders.slow_jpg = true;
    let started = Instant::now();
    let paths = ImageService {
        settings: &settings,
        runner: &encoders,
    }
    .process(&input, Operation::Auto(None))
    .unwrap();
    assert!(started.elapsed() >= Duration::from_secs(4));
    assert!(started.elapsed() < Duration::from_secs(5));
    assert_eq!(paths.len(), 3);
    for path in paths {
        if path.extension().unwrap() == "png" {
            assert_eq!(image::open(path).unwrap().dimensions(), (600, 300));
        } else {
            assert_eq!(fs::read(path).unwrap(), vec![b'J'; 40]);
        }
    }
}

#[test]
fn initial_deadline_publishes_full_size_png_when_compression_is_still_busy() {
    let dir = tempfile::tempdir().unwrap();
    let (settings, input, mut encoders) = setup(dir.path(), false, false);
    encoders.slow_jpg = true;
    let paths = ImageService {
        settings: &settings,
        runner: &encoders,
    }
    .process(&input, Operation::Auto(None))
    .unwrap();
    let first = encoders.first.lock().unwrap();
    let (elapsed, files) = first.as_ref().unwrap();
    assert!(*elapsed >= Duration::from_millis(800));
    assert!(*elapsed < Duration::from_millis(1200));
    let png = &files
        .iter()
        .find(|(name, _)| name == "photo-1.png")
        .unwrap()
        .1;
    assert_eq!(
        image::load_from_memory(png).unwrap().dimensions(),
        (600, 300)
    );
    assert_eq!(paths.len(), 2);
    assert!(!dir.path().join("photo-1.png").exists());
}

struct RacingEncoders {
    png_bytes: usize,
    expected_jobs: u8,
    directory: PathBuf,
    arrived: std::sync::atomic::AtomicU8,
    overlapped: std::sync::atomic::AtomicU8,
    preview_completed: std::sync::atomic::AtomicU8,
}

impl Runner for RacingEncoders {
    fn run(&self, _: &Path, _: &[OsString]) -> Result<ProcessOutput> {
        unreachable!("Auto encoders must share a deadline");
    }

    fn run_controlled(
        &self,
        executable: &Path,
        args: &[OsString],
        control: &RunControl,
    ) -> Result<ProcessOutput> {
        use std::sync::atomic::Ordering::SeqCst;
        let name = executable.file_stem().unwrap().to_str().unwrap();
        let input = PathBuf::from(if name == "cwebp" {
            &args[2]
        } else {
            args.last().unwrap()
        });
        let preview = image::open(input)?.width() <= 512;
        let flag = match (preview, name) {
            (false, "pngquant") => 1,
            (false, "cjpeg") => 2,
            (false, "cwebp") => 4,
            (true, "pngquant") => 8,
            (true, "cjpeg") => 16,
            _ => unreachable!(),
        };
        self.arrived.fetch_or(flag, SeqCst);
        while self.arrived.load(SeqCst) != self.expected_jobs {
            control.check()?;
            std::thread::sleep(Duration::from_millis(1));
        }
        control.check()?;
        self.overlapped.fetch_or(flag, SeqCst);
        while self.overlapped.load(SeqCst) != self.expected_jobs {
            control.check()?;
            std::thread::sleep(Duration::from_millis(1));
        }
        let near_tie = self.png_bytes == 42;
        if (preview && !near_tie) || (name == "cwebp" && self.png_bytes == 20) {
            // Only publication of the full-size PNG winner can cancel these.
            loop {
                control.check()?;
                std::thread::sleep(Duration::from_millis(1));
            }
        }
        if name == "cwebp" || (preview && near_tie) {
            // Verified JPG (and PNG for a near tie) must appear before WebP.
            while fs::metadata(self.directory.join("photo.jpg"))
                .map(|m| m.len())
                .ok()
                != Some(40)
            {
                control.check()?;
                std::thread::sleep(Duration::from_millis(1));
            }
        }
        if near_tie && (preview || name == "cwebp") {
            while fs::metadata(self.directory.join("photo-1.png"))
                .map(|m| m.len())
                .ok()
                != Some(42)
            {
                control.check()?;
                std::thread::sleep(Duration::from_millis(1));
            }
        }
        if name == "cwebp" && near_tie {
            if self.expected_jobs == 31 {
                while self.preview_completed.load(SeqCst) != 24 {
                    control.check()?;
                    std::thread::sleep(Duration::from_millis(1));
                }
            }
            // A late, disagreeing preview must not remove the verified PNG
            // while its WebP companion is still running.
            for _ in 0..30 {
                assert_eq!(fs::metadata(self.directory.join("photo-1.png"))?.len(), 42);
                std::thread::sleep(Duration::from_millis(1));
            }
        }
        let output_flag = match name {
            "pngquant" => "--output",
            "cjpeg" => "-outfile",
            _ => "-o",
        };
        let output =
            PathBuf::from(&args[args.iter().position(|arg| arg == output_flag).unwrap() + 1]);
        fs::write(
            output,
            match name {
                "pngquant" => vec![b'P'; if preview { 80 } else { self.png_bytes }],
                _ => vec![b'J'; 40],
            },
        )?;
        if preview {
            self.preview_completed.fetch_or(flag, SeqCst);
        }
        Ok(ProcessOutput {
            code: 0,
            stderr: String::new(),
        })
    }
}

#[test]
fn auto_jobs_overlap_and_full_results_outrank_preview_including_near_ties() {
    for preview in [true, false] {
        for png_bytes in [20, 42, 80] {
            let dir = tempfile::tempdir().unwrap();
            let (mut settings, input, _) = setup(dir.path(), false, false);
            settings.auto_preview = preview;
            let expected_jobs = if preview { 31 } else { 7 };
            let encoders = RacingEncoders {
                png_bytes,
                expected_jobs,
                directory: dir.path().to_owned(),
                arrived: std::sync::atomic::AtomicU8::new(0),
                overlapped: std::sync::atomic::AtomicU8::new(0),
                preview_completed: std::sync::atomic::AtomicU8::new(0),
            };
            let paths = ImageService {
                settings: &settings,
                runner: &encoders,
            }
            .process_with_timing(&input, Operation::Auto(None), functional_timing())
            .unwrap();
            assert_eq!(
                encoders
                    .overlapped
                    .load(std::sync::atomic::Ordering::SeqCst),
                expected_jobs
            );
            assert_eq!(
                paths.len(),
                match png_bytes {
                    20 => 1,
                    42 => 3,
                    _ => 2,
                }
            );
            for path in paths {
                let png = path.extension().unwrap() == "png";
                assert_eq!(
                    fs::read(path).unwrap(),
                    if png {
                        vec![b'P'; png_bytes]
                    } else {
                        vec![b'J'; 40]
                    }
                );
            }
        }
    }
}

#[test]
fn cheap_prediction_skips_preview_and_full_size_verification_can_reverse_it() {
    let dir = tempfile::tempdir().unwrap();
    let (settings, input, mut encoders) = setup(dir.path(), true, false);
    let bitmap = RgbaImage::from_pixel(600, 300, Rgba([40, 80, 120, 128]));
    image::codecs::png::PngEncoder::new_with_quality(
        fs::File::create(&input).unwrap(),
        image::codecs::png::CompressionType::Best,
        image::codecs::png::FilterType::Adaptive,
    )
    .write_image(bitmap.as_raw(), 600, 300, image::ExtendedColorType::Rgba8)
    .unwrap();
    encoders.original = fs::read(&input).unwrap();
    let paths = ImageService {
        settings: &settings,
        runner: &encoders,
    }
    .process_with_timing(&input, Operation::Auto(None), functional_timing())
    .unwrap();
    assert!(encoders.previews.lock().unwrap().is_empty());
    assert!(encoders.first.lock().unwrap().is_some());
    assert_eq!(paths.len(), 2);
    assert!(!dir.path().join("photo-1.png").exists());
    assert_eq!(fs::read(input).unwrap(), encoders.original);
    for path in paths {
        assert_eq!(fs::read(path).unwrap(), vec![b'J'; 40]);
    }
}

struct FirstEncoder {
    directory: PathBuf,
    first: &'static str,
}

impl Runner for FirstEncoder {
    fn run(&self, _: &Path, _: &[OsString]) -> Result<ProcessOutput> {
        unreachable!();
    }

    fn run_controlled(
        &self,
        executable: &Path,
        args: &[OsString],
        control: &RunControl,
    ) -> Result<ProcessOutput> {
        let name = executable.file_stem().unwrap().to_str().unwrap();
        if name != self.first {
            let (file, bytes) = match self.first {
                "pngquant" => ("photo-1.png", 42),
                "cjpeg" => ("photo.jpg", 40),
                _ => ("photo.webp", 40),
            };
            while fs::metadata(self.directory.join(file))
                .map(|m| m.len())
                .ok()
                != Some(bytes)
            {
                control.check()?;
                std::thread::sleep(Duration::from_millis(1));
            }
        }
        let flag = match name {
            "pngquant" => "--output",
            "cjpeg" => "-outfile",
            _ => "-o",
        };
        let output = PathBuf::from(&args[args.iter().position(|arg| arg == flag).unwrap() + 1]);
        fs::write(output, vec![1; if name == "pngquant" { 42 } else { 40 }])?;
        Ok(ProcessOutput {
            code: 0,
            stderr: String::new(),
        })
    }
}

#[test]
fn any_encoder_can_publish_first_without_waiting_for_the_other_two() {
    for first in ["pngquant", "cjpeg", "cwebp"] {
        let dir = tempfile::tempdir().unwrap();
        let (mut settings, input, _) = setup(dir.path(), false, false);
        settings.auto_preview = false;
        let runner = FirstEncoder {
            directory: dir.path().to_owned(),
            first,
        };
        let timing = functional_timing();
        let initial_deadline = timing.initial_deadline;
        let outputs = ImageService {
            settings: &settings,
            runner: &runner,
        }
        .process_with_timing(&input, Operation::Auto(None), timing)
        .unwrap();
        assert_eq!(
            outputs.len(),
            3,
            "All three near-tie outputs must finish: {first}"
        );
        assert!(
            Instant::now() < initial_deadline,
            "{first} waited for a deadline"
        );
        for output in outputs {
            let count = if output.extension().unwrap() == "png" {
                42
            } else {
                40
            };
            assert_eq!(fs::read(output).unwrap(), vec![1; count]);
        }
    }
}
