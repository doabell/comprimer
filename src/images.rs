use crate::{
    settings::{DownscaleMode, Settings},
    tools::{self, Runner},
};
use anyhow::{Context, Result, bail, ensure};
use image::{DynamicImage, GenericImageView, ImageFormat, Rgb, RgbImage, imageops::FilterType};
use std::{
    ffi::OsString,
    fs,
    path::{Path, PathBuf},
};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Operation {
    Auto(Option<u32>),
    Resize(u32),
    ToWebp,
    ToJpg,
    OptimizePng,
    OptimizeJpg,
}

pub struct ImageService<'a> {
    pub settings: &'a Settings,
    pub runner: &'a dyn Runner,
}

impl ImageService<'_> {
    pub fn process(&self, input: &Path, operation: Operation) -> Result<Vec<PathBuf>> {
        ensure!(
            input.is_file(),
            "Source does not exist: {}",
            input.display()
        );
        let ext = extension(input);
        ensure!(
            ["png", "jpg", "jpeg", "webp"].contains(&ext.as_str()),
            "Unsupported image format: {ext}"
        );
        let size = match operation {
            Operation::Auto(size) => size,
            Operation::Resize(size) => Some(size),
            _ => None,
        };
        ensure!(size != Some(0), "Resize size must be greater than zero");
        if matches!(operation, Operation::Auto(_)) {
            for format in ["png", "jpg", "webp"] {
                self.encoder(format).with_context(|| {
                    format!("Auto requires the {format} encoder; configure it in Tools")
                })?;
            }
        }
        // Stage on the destination volume so publishing does not rely on cross-volume moves.
        let work = tempfile::Builder::new()
            .prefix(".comprimer-")
            .tempdir_in(parent(input))?;
        let original = self.decode(input, work.path())?;
        let (width, height) = dimensions(
            original.width(),
            original.height(),
            size,
            self.settings.downscale_mode,
        );
        if matches!(operation, Operation::Resize(_)) && original.dimensions() == (width, height) {
            tracing::info!(input = %input.display(), "Image already fits; no output needed");
            return Ok(Vec::new());
        }
        let bitmap = if original.dimensions() == (width, height) {
            original
        } else {
            original.resize_exact(width, height, FilterType::Lanczos3)
        };
        let mut outputs = Vec::new();
        match operation {
            Operation::Auto(_) => {
                for format in ["png", "jpg", "webp"] {
                    self.encode(
                        &bitmap,
                        &work.path().join(format!("candidate.{format}")),
                        format,
                        work.path(),
                        true,
                    )?;
                }
                let png = work.path().join("candidate.png");
                let jpg = work.path().join("candidate.jpg");
                let formats: &[&str] = if fs::metadata(&png)?.len() < fs::metadata(&jpg)?.len() {
                    &["png"]
                } else {
                    &["jpg", "webp"]
                };
                for format in formats {
                    outputs.push((
                        work.path().join(format!("candidate.{format}")),
                        output_path(
                            input,
                            format,
                            size.map(|n| format!("-{n}px")).as_deref().unwrap_or(""),
                            self.settings.overwrite_original,
                        )?,
                    ));
                }
            }
            _ => {
                let (format, suffix, required) = match operation {
                    Operation::Resize(size) => (ext.as_str(), format!("-{size}px"), false),
                    Operation::ToWebp => ("webp", String::new(), true),
                    Operation::ToJpg => ("jpg", String::new(), true),
                    Operation::OptimizePng => ("png", "-fs8".into(), true),
                    Operation::OptimizeJpg => (
                        if ext == "jpeg" { "jpeg" } else { "jpg" },
                        "-moz".into(),
                        true,
                    ),
                    Operation::Auto(_) => unreachable!(),
                };
                let candidate = work.path().join(format!("candidate.{format}"));
                self.encode(&bitmap, &candidate, format, work.path(), required)?;
                outputs.push((
                    candidate,
                    output_path(input, format, &suffix, self.settings.overwrite_original)?,
                ));
            }
        }
        publish(&outputs, self.settings.overwrite_original)?;
        let paths: Vec<_> = outputs.into_iter().map(|(_, dest)| dest).collect();
        tracing::info!(input = %input.display(), ?operation, ?paths, width, height, "Image operation complete");
        Ok(paths)
    }

    fn encoder(&self, format: &str) -> Option<PathBuf> {
        let paths = &self.settings.executables;
        match format {
            "png" => tools::resolve("pngquant", paths.pngquant_path.as_deref()),
            "jpg" | "jpeg" => tools::resolve("cjpeg", paths.cjpeg_path.as_deref()),
            "webp" => tools::resolve("cwebp", paths.cwebp_path.as_deref()),
            _ => None,
        }
    }

    fn decode(&self, input: &Path, work: &Path) -> Result<DynamicImage> {
        let mut decoded = input.to_path_buf();
        if extension(input) == "webp" {
            let paths = &self.settings.executables;
            let decoder = match paths.dwebp_path.as_deref() {
                Some(path) => tools::existing_path(path),
                None => tools::detect_decoder(paths.cwebp_path.as_deref()),
            }.context("WebP sources require dwebp; configure its path in Tools. Animated WebP is unsupported.")?;
            decoded = work.join("decoded.png");
            let result = self.runner.run(
                &decoder,
                &[input.into(), "-o".into(), decoded.as_os_str().into()],
            )?;
            ensure!(
                result.code == 0,
                "dwebp failed ({}): {}",
                result.code,
                result.stderr
            );
        }
        image::open(&decoded).with_context(|| format!("Decode {}", input.display()))
    }

    fn encode(
        &self,
        bitmap: &DynamicImage,
        output: &Path,
        format: &str,
        work: &Path,
        required: bool,
    ) -> Result<()> {
        let quality = &self.settings.encoders;
        let encoder = self.encoder(format);
        if required {
            ensure!(
                encoder.is_some(),
                "Missing {format} encoder; configure its path in Tools"
            );
        }
        let input = if format == "jpg" || format == "jpeg" {
            let opaque = flatten_on_white(bitmap);
            if encoder.is_none() {
                let mut file = fs::File::create(output)?;
                image::codecs::jpeg::JpegEncoder::new_with_quality(
                    &mut file,
                    quality.jpg_quality.clamp(0, 100) as u8,
                )
                .encode_image(&opaque)?;
                return Ok(());
            }
            let path = work.join("encoder-input.bmp");
            opaque.save_with_format(&path, ImageFormat::Bmp)?;
            path
        } else {
            let path = work.join("encoder-input.png");
            bitmap.save_with_format(&path, ImageFormat::Png)?;
            if format == "png" && encoder.is_none() {
                fs::copy(&path, output)?;
                return Ok(());
            }
            path
        };
        let encoder = encoder.context("WebP output requires cwebp; configure its path in Tools")?;
        let args: Vec<OsString> = match format {
            "png" => vec![
                format!(
                    "--quality={}-{}",
                    quality
                        .png_min_quality
                        .clamp(0, 100)
                        .min(quality.png_quality.clamp(0, 100)),
                    quality.png_quality.clamp(0, 100)
                )
                .into(),
                "--output".into(),
                output.into(),
                "--".into(),
                input.as_os_str().into(),
            ],
            "jpg" | "jpeg" => vec![
                "-quality".into(),
                quality.jpg_quality.clamp(0, 100).to_string().into(),
                "-outfile".into(),
                output.into(),
                input.as_os_str().into(),
            ],
            "webp" => vec![
                "-q".into(),
                quality.webp_quality.clamp(0, 100).to_string().into(),
                input.as_os_str().into(),
                "-o".into(),
                output.into(),
            ],
            _ => bail!("Unsupported output format: {format}"),
        };
        let result = self.runner.run(&encoder, &args)?;
        if format == "png" && result.code == 99 {
            tracing::info!("pngquant could not meet quality floor; keeping lossless PNG");
            fs::copy(input, output)?;
        } else {
            ensure!(
                result.code == 0,
                "{} failed ({}): {}",
                encoder.display(),
                result.code,
                result.stderr
            );
        }
        ensure!(
            output.is_file() && fs::metadata(output)?.len() > 0,
            "{} produced no output",
            encoder.display()
        );
        Ok(())
    }
}

pub fn dimensions(width: u32, height: u32, size: Option<u32>, mode: DownscaleMode) -> (u32, u32) {
    let dimension = match mode {
        DownscaleMode::LongestSide => width.max(height),
        DownscaleMode::Width => width,
        DownscaleMode::Height => height,
    };
    let ratio = size
        .map(|n| (f64::from(n) / f64::from(dimension)).min(1.0))
        .unwrap_or(1.0);
    (
        (f64::from(width) * ratio).max(1.0) as u32,
        (f64::from(height) * ratio).max(1.0) as u32,
    )
}

pub fn flatten_on_white(bitmap: &DynamicImage) -> RgbImage {
    let rgba = bitmap.to_rgba8();
    RgbImage::from_fn(rgba.width(), rgba.height(), |x, y| {
        let pixel = rgba.get_pixel(x, y);
        let alpha = u32::from(pixel[3]);
        Rgb([0, 1, 2]
            .map(|c| ((u32::from(pixel[c]) * alpha + 255 * (255 - alpha) + 127) / 255) as u8))
    })
}

pub fn output_path(input: &Path, format: &str, suffix: &str, overwrite: bool) -> Result<PathBuf> {
    let input_ext = extension(input);
    if overwrite {
        return Ok(
            if input_ext == format || (input_ext == "jpeg" && format == "jpg") {
                input.to_owned()
            } else {
                input.with_extension(format)
            },
        );
    }
    let stem = input.file_stem().context("Image has no filename")?;
    for i in 0..1000 {
        let mut name = stem.to_os_string();
        name.push(suffix);
        if i > 0 {
            name.push(format!("-{i}"));
        }
        name.push(format!(".{format}"));
        let path = parent(input).join(name);
        if !path.try_exists()? {
            return Ok(path);
        }
    }
    bail!(
        "No unused output filename is available for {}",
        input.display()
    )
}

// Prepare all outputs and backups before publishing any. Roll back earlier outputs on failure.
pub fn publish(outputs: &[(PathBuf, PathBuf)], overwrite: bool) -> Result<()> {
    let mut prepared = Vec::new();
    for (source, destination) in outputs {
        ensure!(
            !destination.is_dir(),
            "Output is a directory: {}",
            destination.display()
        );
        let backup = if destination.try_exists()? {
            ensure!(
                overwrite,
                "Output already exists: {}",
                destination.display()
            );
            ensure!(
                !fs::metadata(destination)?.permissions().readonly(),
                "Output is read-only: {}",
                destination.display()
            );
            Some(stage(destination, parent(destination))?)
        } else {
            None
        };
        prepared.push((
            stage(source, parent(destination))?,
            destination.clone(),
            backup,
        ));
    }
    let mut published: Vec<(PathBuf, Option<tempfile::NamedTempFile>)> = Vec::new();
    for (file, destination, backup) in prepared {
        let result = if overwrite {
            file.persist(&destination)
        } else {
            file.persist_noclobber(&destination)
        };
        if let Err(error) = result {
            let mut failures = Vec::new();
            for (path, backup) in published.into_iter().rev() {
                if let Some(backup) = backup {
                    if let Err(error) = backup.persist(&path) {
                        let reason = error.error.to_string();
                        let recovery = error.file.keep().map(|(_, p)| p);
                        failures.push(format!(
                            "{}: {reason}; recovery file: {recovery:?}",
                            path.display()
                        ));
                    }
                } else if let Err(error) = fs::remove_file(&path) {
                    failures.push(format!("{}: {error}", path.display()));
                }
            }
            bail!(
                "Could not publish {}: {}{}",
                destination.display(),
                error.error,
                if failures.is_empty() {
                    String::new()
                } else {
                    format!(". Rollback needs attention: {}", failures.join("; "))
                }
            );
        }
        published.push((destination, backup));
    }
    Ok(())
}

fn stage(source: &Path, directory: &Path) -> Result<tempfile::NamedTempFile> {
    let mut staged = tempfile::NamedTempFile::new_in(directory)?;
    std::io::copy(&mut fs::File::open(source)?, &mut staged)?;
    staged.as_file().sync_all()?;
    Ok(staged)
}

fn extension(path: &Path) -> String {
    path.extension()
        .unwrap_or_default()
        .to_string_lossy()
        .to_ascii_lowercase()
}
fn parent(path: &Path) -> &Path {
    path.parent()
        .filter(|p| !p.as_os_str().is_empty())
        .unwrap_or(Path::new("."))
}
