use anyhow::Result;
use comprimer::{
    images::{self, ImageService, Operation},
    settings::{DownscaleMode, Settings},
    tools::{ProcessOutput, Runner},
};
use image::{DynamicImage, GenericImageView, Rgba, RgbaImage};
use std::{
    ffi::OsString,
    fs,
    os::windows::fs::OpenOptionsExt,
    path::{Path, PathBuf},
    sync::{Condvar, Mutex},
    time::Duration,
};

type EncoderCall = (String, Vec<OsString>, (u32, u32));

struct FakeEncoders {
    png: usize,
    jpg: usize,
    fail: Option<&'static str>,
    calls: Mutex<Vec<EncoderCall>>,
}

impl Default for FakeEncoders {
    fn default() -> Self {
        Self {
            png: 100,
            jpg: 200,
            fail: None,
            calls: Mutex::new(Vec::new()),
        }
    }
}

impl Runner for FakeEncoders {
    fn run(&self, exe: &Path, args: &[OsString]) -> Result<ProcessOutput> {
        let name = exe.file_stem().unwrap().to_string_lossy().into_owned();
        if self.fail == Some(name.as_str()) {
            return Ok(ProcessOutput {
                code: 2,
                stderr: "test encoder error".into(),
            });
        }
        let flag = if name == "pngquant" {
            "--output"
        } else if name == "cjpeg" {
            "-outfile"
        } else {
            "-o"
        };
        let output = PathBuf::from(&args[args.iter().position(|arg| arg == flag).unwrap() + 1]);
        let input = if name == "cwebp" {
            &args[2]
        } else {
            args.last().unwrap()
        };
        let bitmap = image::open(input)?;
        if name == "cjpeg" {
            assert_eq!(bitmap.to_rgb8().get_pixel(0, 0).0, [255, 255, 255]);
        }
        self.calls
            .lock()
            .unwrap()
            .push((name.clone(), args.to_vec(), bitmap.dimensions()));
        fs::write(
            output,
            vec![
                1;
                if name == "pngquant" {
                    self.png
                } else {
                    self.jpg
                }
            ],
        )?;
        Ok(ProcessOutput {
            code: 0,
            stderr: String::new(),
        })
    }
}

fn setup(dir: &Path) -> (Settings, PathBuf) {
    let mut settings = Settings::default();
    for (name, value) in [
        ("pngquant", &mut settings.executables.pngquant_path),
        ("cjpeg", &mut settings.executables.cjpeg_path),
        ("cwebp", &mut settings.executables.cwebp_path),
    ] {
        let path = dir.join(format!("{name}.exe"));
        fs::write(&path, []).unwrap();
        *value = Some(path.to_string_lossy().into_owned());
    }
    let source = dir.join("photo été.png");
    RgbaImage::from_pixel(160, 80, Rgba([0, 0, 0, 0]))
        .save(&source)
        .unwrap();
    (settings, source)
}

#[test]
fn auto_keeps_both_groups_within_five_percent_including_exact_boundaries() {
    for (png, jpg, formats) in [
        (100, 200, vec!["png"]),
        (200, 100, vec!["jpg", "webp"]),
        (100, 100, vec!["png", "jpg", "webp"]),
        (100, 105, vec!["png", "jpg", "webp"]),
        (105, 100, vec!["png", "jpg", "webp"]),
        (100, 106, vec!["png"]),
        (106, 100, vec!["jpg", "webp"]),
    ] {
        let dir = tempfile::tempdir().unwrap();
        let (settings, source) = setup(dir.path());
        let original = fs::read(&source).unwrap();
        let runner = FakeEncoders {
            png,
            jpg,
            ..Default::default()
        };
        let outputs = ImageService {
            settings: &settings,
            runner: &runner,
        }
        .process(&source, Operation::Auto(Some(40)))
        .unwrap();
        assert_eq!(
            outputs
                .iter()
                .map(|p| p.extension().unwrap().to_str().unwrap())
                .collect::<Vec<_>>(),
            formats
        );
        assert_eq!(fs::read(&source).unwrap(), original);
        let calls = runner.calls.lock().unwrap().len();
        if formats == ["png"] {
            assert!((2..=3).contains(&calls)); // A losing WebP job can be cancelled.
        } else {
            assert_eq!(calls, 3);
        }
        assert!(
            runner
                .calls
                .lock()
                .unwrap()
                .iter()
                .all(|(_, _, dimensions)| *dimensions == (40, 20))
        );
        assert!(!fs::read_dir(dir.path()).unwrap().any(|p| {
            p.unwrap()
                .file_name()
                .to_string_lossy()
                .starts_with(".comprimer-")
        }));
    }
}

#[test]
fn resizing_obeys_dimension_limits_and_does_not_enlarge() {
    for (mode, expected) in [
        (DownscaleMode::LongestSide, (40, 20)),
        (DownscaleMode::Width, (40, 20)),
        (DownscaleMode::Height, (80, 40)),
    ] {
        assert_eq!(images::dimensions(160, 80, Some(40), mode), expected);
        assert_eq!(images::dimensions(160, 80, Some(1024), mode), (160, 80));
        assert_eq!(images::dimensions(160, 80, None, mode), (160, 80));
    }
    assert_eq!(
        images::dimensions(1000, 1, Some(1), DownscaleMode::LongestSide),
        (1, 1)
    );
}

#[test]
fn quality_values_reach_encoders_and_jpg_is_flattened() {
    let dir = tempfile::tempdir().unwrap();
    let (mut settings, source) = setup(dir.path());
    settings.encoders.png_min_quality = 12;
    settings.encoders.png_quality = 34;
    settings.encoders.jpg_quality = 56;
    settings.encoders.webp_quality = 78;
    let runner = FakeEncoders {
        png: 300, // A JPG win requires all three configured encoders.
        ..Default::default()
    };
    ImageService {
        settings: &settings,
        runner: &runner,
    }
    .process(&source, Operation::Auto(None))
    .unwrap();
    let calls = runner.calls.lock().unwrap();
    for (encoder, argument) in [
        ("pngquant", "--quality=12-34"),
        ("cjpeg", "56"),
        ("cwebp", "78"),
    ] {
        let call = calls.iter().find(|(name, _, _)| name == encoder).unwrap();
        assert!(call.1.contains(&argument.into()));
    }
}

#[test]
fn encoder_failures_leave_source_and_existing_outputs_untouched() {
    for tool in ["pngquant", "cjpeg", "cwebp"] {
        let dir = tempfile::tempdir().unwrap();
        let (mut settings, source) = setup(dir.path());
        settings.overwrite_original = true;
        let jpg = source.with_extension("jpg");
        fs::write(&jpg, b"existing JPG").unwrap();
        let original = fs::read(&source).unwrap();
        let runner = FakeEncoders {
            png: 300, // WebP failure matters when JPG/WebP would win.
            fail: Some(tool),
            ..Default::default()
        };
        let error = ImageService {
            settings: &settings,
            runner: &runner,
        }
        .process(&source, Operation::Auto(None))
        .unwrap_err();
        assert!(error.to_string().contains("test encoder error"));
        assert_eq!(fs::read(&source).unwrap(), original);
        assert_eq!(fs::read(&jpg).unwrap(), b"existing JPG");
    }
}

#[test]
fn collisions_include_directories_and_preserve_jpeg_extension_on_overwrite() {
    let dir = tempfile::tempdir().unwrap();
    let source = dir.path().join("photo.jpeg");
    fs::write(&source, b"original").unwrap();
    fs::create_dir(dir.path().join("photo-40px.jpeg")).unwrap();
    assert_eq!(
        images::output_path(&source, "jpeg", "-40px", false).unwrap(),
        dir.path().join("photo-40px-1.jpeg")
    );
    assert_eq!(
        images::output_path(&source, "jpg", "-40px", false).unwrap(),
        dir.path().join("photo-40px.jpg")
    );
    assert_eq!(
        images::output_path(&source, "jpg", "-40px", true).unwrap(),
        source
    );
}

#[test]
fn publish_rolls_back_overwritten_files_when_later_output_is_locked() {
    let dir = tempfile::tempdir().unwrap();
    let a = dir.path().join("a.jpg");
    let b = dir.path().join("b.webp");
    let candidate = dir.path().join("candidate");
    fs::write(&a, b"original A").unwrap();
    fs::write(&b, b"original B").unwrap();
    fs::write(&candidate, b"new").unwrap();
    // Reading is allowed (so preparation succeeds), but replacing b is not.
    let _locked = fs::OpenOptions::new()
        .read(true)
        .share_mode(1)
        .open(&b)
        .unwrap();
    assert!(
        images::publish(
            &[(candidate.clone(), a.clone()), (candidate, b.clone())],
            true
        )
        .is_err()
    );
    assert_eq!(fs::read(&a).unwrap(), b"original A");
    assert_eq!(fs::read(&b).unwrap(), b"original B");
}

#[test]
fn publish_rolls_back_new_output_on_collision_race() {
    let dir = tempfile::tempdir().unwrap();
    let candidate = dir.path().join("candidate");
    let output = dir.path().join("output");
    fs::write(&candidate, b"new").unwrap();
    assert!(
        images::publish(
            &[
                (candidate.clone(), output.clone()),
                (candidate, output.clone())
            ],
            false
        )
        .is_err()
    );
    assert!(!output.exists());
}

#[test]
fn built_in_png_resize_works_without_tools() {
    let dir = tempfile::tempdir().unwrap();
    let (mut settings, source) = setup(dir.path());
    settings.executables.pngquant_path = Some(String::new());
    let runner = FakeEncoders::default();
    let service = ImageService {
        settings: &settings,
        runner: &runner,
    };
    let outputs = service.process(&source, Operation::Resize(40)).unwrap();
    assert_eq!(image::open(&outputs[0]).unwrap().dimensions(), (40, 20));
    assert!(
        service
            .process(&source, Operation::Resize(1024))
            .unwrap()
            .is_empty()
    );
    assert!(service.process(&source, Operation::Auto(None)).is_err());
    assert!(runner.calls.lock().unwrap().is_empty());
}

#[test]
fn partial_alpha_is_composited_on_white() {
    let image = DynamicImage::ImageRgba8(RgbaImage::from_pixel(1, 1, Rgba([0, 0, 0, 128])));
    assert_eq!(
        images::flatten_on_white(&image).get_pixel(0, 0).0,
        [127, 127, 127]
    );
}

struct ConcurrentEncoders {
    inner: FakeEncoders,
    started: Mutex<usize>,
    ready: Condvar,
    completed: std::sync::atomic::AtomicUsize,
}

impl Runner for ConcurrentEncoders {
    fn run(&self, exe: &Path, args: &[OsString]) -> Result<ProcessOutput> {
        // All three jobs must start before any may finish. A sequential
        // implementation fails with a bounded timeout instead of hanging.
        let mut started = self.started.lock().unwrap();
        *started += 1;
        self.ready.notify_all();
        let (started, _) = self
            .ready
            .wait_timeout_while(started, Duration::from_secs(5), |started| *started < 3)
            .unwrap();
        anyhow::ensure!(*started == 3, "Auto encoders did not overlap");
        drop(started);
        let result = self.inner.run(exe, args);
        self.completed
            .fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        result
    }
}

#[test]
fn auto_overlaps_encoders_shares_png_input_and_joins_on_failure() {
    for fail in [None, Some("pngquant"), Some("cjpeg"), Some("cwebp")] {
        let dir = tempfile::tempdir().unwrap();
        let (settings, source) = setup(dir.path());
        let original = fs::read(&source).unwrap();
        let runner = ConcurrentEncoders {
            inner: FakeEncoders {
                png: 300,
                fail,
                ..Default::default()
            },
            started: Mutex::new(0),
            ready: Condvar::new(),
            completed: std::sync::atomic::AtomicUsize::new(0),
        };
        let result = ImageService {
            settings: &settings,
            runner: &runner,
        }
        .process(&source, Operation::Auto(None));
        assert_eq!(result.is_ok(), fail.is_none(), "{result:?}");
        assert_eq!(
            runner.completed.load(std::sync::atomic::Ordering::SeqCst),
            3
        );
        assert_eq!(fs::read(&source).unwrap(), original);
        let calls = runner.inner.calls.lock().unwrap();
        let input = |name: &str| -> PathBuf {
            let call = calls
                .iter()
                .find(|(encoder, _, _)| encoder == name)
                .unwrap();
            PathBuf::from(if name == "cwebp" {
                &call.1[2]
            } else {
                call.1.last().unwrap()
            })
        };
        if fail.is_none() {
            assert_eq!(input("pngquant"), input("cwebp"));
        } else {
            // Failed operations must not publish any candidate.
            assert_eq!(fs::read_dir(dir.path()).unwrap().count(), 4);
        }
        for (name, _, _) in calls.iter() {
            let path = input(name);
            let work = path.parent().unwrap();
            assert_eq!(
                work.parent().unwrap().canonicalize().unwrap(),
                std::env::temp_dir().canonicalize().unwrap()
            );
            assert!(!work.exists(), "Workspace leaked: {}", work.display());
        }
    }
}

#[test]
fn publication_accepts_candidates_from_a_separate_workspace() {
    let work = tempfile::tempdir().unwrap();
    let images = tempfile::tempdir().unwrap();
    let candidate = work.path().join("candidate.png");
    let destination = images.path().join("image.png");
    fs::write(&candidate, b"encoded image").unwrap();
    images::publish(&[(candidate, destination.clone())], false).unwrap();
    assert_eq!(fs::read(destination).unwrap(), b"encoded image");
    assert_eq!(fs::read_dir(images.path()).unwrap().count(), 1);
}
