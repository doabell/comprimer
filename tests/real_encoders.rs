//! Explicit integration suite: cargo test --test real_encoders -- --ignored
use comprimer::{
    images::{ImageService, Operation},
    settings::Settings,
    tools::{self, SystemRunner},
};
use image::{GenericImageView, Rgba, RgbaImage};
use std::{
    fs,
    path::{Path, PathBuf},
};

fn setup() -> (tempfile::TempDir, Settings, SystemRunner) {
    for tool in ["pngquant", "cjpeg", "cwebp", "dwebp"] {
        assert!(
            tools::detect(tool).is_some(),
            "Install {tool} on PATH before running this suite"
        );
    }
    let mut settings = Settings::default();
    settings.encoders.png_min_quality = 0;
    (
        tempfile::tempdir().unwrap(),
        settings,
        SystemRunner::default(),
    )
}

fn source(dir: &Path, noisy: bool) -> PathBuf {
    let mut state = 42_u32;
    let image = RgbaImage::from_fn(256, 128, |x, y| {
        if noisy {
            let mut channel = || {
                state = state.wrapping_mul(1664525).wrapping_add(1013904223);
                (state >> 24) as u8
            };
            Rgba([channel(), channel(), channel(), 255])
        } else if (20..100).contains(&x) && (20..80).contains(&y) {
            Rgba([100, 149, 237, 255])
        } else {
            Rgba([0, 0, 0, 0])
        }
    });
    let path = dir.join("source été.png");
    image.save(&path).unwrap();
    path
}

#[test]
#[ignore = "requires pngquant, mozjpeg and libwebp on PATH"]
fn real_auto_graphic_keeps_png_and_transparency() {
    let (dir, settings, runner) = setup();
    let input = source(dir.path(), false);
    let original = fs::read(&input).unwrap();
    let paths = ImageService {
        settings: &settings,
        runner: &runner,
    }
    .process(&input, Operation::Auto(Some(128)))
    .unwrap();
    assert_eq!(paths.len(), 1);
    assert_eq!(paths[0].extension().unwrap(), "png");
    let output = image::open(&paths[0]).unwrap();
    assert_eq!(output.dimensions(), (128, 64));
    assert_eq!(output.to_rgba8().get_pixel(0, 0)[3], 0);
    assert_eq!(fs::read(input).unwrap(), original);
}

#[test]
#[ignore = "requires pngquant, mozjpeg and libwebp on PATH"]
fn real_auto_detail_keeps_jpg_webp_and_can_process_webp_sources() {
    let (dir, settings, runner) = setup();
    let input = source(dir.path(), true);
    let service = ImageService {
        settings: &settings,
        runner: &runner,
    };
    let paths = service.process(&input, Operation::Auto(Some(128))).unwrap();
    assert_eq!(paths.len(), 2);
    for path in &paths {
        assert_eq!(image::open(path).unwrap().dimensions(), (128, 64));
    }
    let webp = paths
        .iter()
        .find(|p| p.extension().unwrap() == "webp")
        .unwrap();
    let resized = service.process(webp, Operation::Resize(64)).unwrap();
    assert_eq!(image::open(&resized[0]).unwrap().dimensions(), (64, 32));
    assert!(
        !service
            .process(webp, Operation::Auto(None))
            .unwrap()
            .is_empty()
    );
}

#[test]
#[ignore = "requires pngquant, mozjpeg and libwebp on PATH"]
fn real_quality_and_overwrite_behave_as_configured() {
    let (dir, mut settings, runner) = setup();
    let input = source(dir.path(), true);
    let mut sizes = Vec::new();
    for quality in [25, 95] {
        settings.encoders.jpg_quality = quality;
        settings.encoders.webp_quality = quality;
        let service = ImageService {
            settings: &settings,
            runner: &runner,
        };
        sizes.push([Operation::ToJpg, Operation::ToWebp].map(|op| {
            fs::metadata(&service.process(&input, op).unwrap()[0])
                .unwrap()
                .len()
        }));
    }
    assert!(sizes[1][0] > sizes[0][0]);
    assert!(sizes[1][1] > sizes[0][1]);
    settings.overwrite_original = true;
    let jpg = input.with_extension("jpg");
    let service = ImageService {
        settings: &settings,
        runner: &runner,
    };
    service.process(&jpg, Operation::OptimizeJpg).unwrap();
    assert_eq!(
        service.process(&jpg, Operation::Resize(64)).unwrap(),
        std::slice::from_ref(&jpg)
    );
    assert_eq!(image::open(jpg).unwrap().dimensions(), (64, 32));
}
