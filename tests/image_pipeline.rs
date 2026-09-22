use anyhow::Result;
use comprimer::{
    images::{self, ImageService, Operation},
    settings::{DownscaleMode, Settings},
    tools::{ProcessOutput, Runner},
};
use image::{DynamicImage, GenericImageView, Rgba, RgbaImage};
use std::{
    cell::RefCell,
    ffi::OsString,
    fs,
    os::windows::fs::OpenOptionsExt,
    path::{Path, PathBuf},
};

type EncoderCall = (String, Vec<OsString>, (u32, u32));

struct FakeEncoders {
    png: usize,
    jpg: usize,
    fail: Option<&'static str>,
    calls: RefCell<Vec<EncoderCall>>,
}

impl Default for FakeEncoders {
    fn default() -> Self {
        Self {
            png: 100,
            jpg: 200,
            fail: None,
            calls: RefCell::new(Vec::new()),
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
            .borrow_mut()
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
fn auto_selects_png_only_or_jpg_and_webp_including_ties() {
    for (png, jpg, formats) in [
        (100, 200, vec!["png"]),
        (200, 100, vec!["jpg", "webp"]),
        (100, 100, vec!["jpg", "webp"]),
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
        assert_eq!(runner.calls.borrow().len(), 3);
        assert!(
            runner
                .calls
                .borrow()
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
    let runner = FakeEncoders::default();
    ImageService {
        settings: &settings,
        runner: &runner,
    }
    .process(&source, Operation::Auto(None))
    .unwrap();
    let calls = runner.calls.borrow();
    assert!(calls[0].1.contains(&"--quality=12-34".into()));
    assert!(calls[1].1.contains(&"56".into()));
    assert!(calls[2].1.contains(&"78".into()));
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
    assert!(runner.calls.borrow().is_empty());
}

#[test]
fn partial_alpha_is_composited_on_white() {
    let image = DynamicImage::ImageRgba8(RgbaImage::from_pixel(1, 1, Rgba([0, 0, 0, 128])));
    assert_eq!(
        images::flatten_on_white(&image).get_pixel(0, 0).0,
        [127, 127, 127]
    );
}
