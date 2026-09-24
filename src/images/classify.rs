use image::{DynamicImage, GenericImageView};
use std::{
    fs::File,
    io::{Read, Seek, SeekFrom},
    path::Path,
};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(super) enum Decision {
    Png,
    JpgWebp,
    Uncertain,
}
impl Decision {
    pub(super) fn png(self) -> Option<bool> {
        match self {
            Self::Png => Some(true),
            Self::JpgWebp => Some(false),
            Self::Uncertain => None,
        }
    }
}

#[derive(Debug)]
pub(super) struct Classification {
    pub(super) decision: Decision,
    pub(super) reason: &'static str,
}

#[derive(Default)]
struct SourceHint {
    lossy: bool,
    png_density: Option<f64>,
}

#[derive(Debug)]
struct Features {
    colors: usize,
    dominant: f64,
    flat: f64,
}

pub(super) fn classify(bitmap: &DynamicImage, input: &Path) -> Classification {
    let hint = source_hint(input, bitmap.dimensions()).unwrap_or_default();
    if hint.lossy {
        return Classification {
            decision: Decision::JpgWebp,
            reason: "lossy-source",
        };
    }
    let features = sample(bitmap);
    // Density is supporting evidence, not an intrinsic property of the pixels.
    // Keep a 0.20–0.30 bytes/pixel ambiguity band and require local diversity
    // to corroborate either side. Strong pixel-only evidence can bypass it.
    let palette_png = features.flat >= 0.9 && features.colors <= 64;
    let density_png =
        features.flat >= 0.8 && hint.png_density.is_some_and(|density| density < 0.20);
    let density_jpg = hint.png_density.is_some_and(|density| density > 0.30);
    let detailed = features.flat <= 0.2 && features.colors >= 256 && features.dominant <= 0.1;
    let (decision, reason) = if detailed {
        (Decision::JpgWebp, "varied-detail")
    } else if palette_png || density_png || density_jpg {
        let diverse = diverse_patches(bitmap, if density_jpg { 8 } else { 4 });
        if (palette_png || density_png) && diverse < 4 {
            (
                Decision::Png,
                if palette_png {
                    "flat-palette"
                } else {
                    "flat-compressible"
                },
            )
        } else if density_jpg && diverse >= 8 {
            (Decision::JpgWebp, "diverse-dense")
        } else {
            (Decision::Uncertain, "mixed-patches")
        }
    } else {
        (Decision::Uncertain, "density-ambiguous")
    };
    Classification { decision, reason }
}

fn sample(bitmap: &DynamicImage) -> Features {
    let (width, height) = bitmap.dimensions();
    let columns = width.min(64);
    let rows = height.min(64);
    let mut histogram = [0u16; 4096];
    let mut flat = 0u32;
    let mut pairs = 0u32;

    for row in 0..rows {
        let y = ((u64::from(row) * 2 + 1) * u64::from(height) / (u64::from(rows) * 2)) as u32;
        for column in 0..columns {
            let x =
                ((u64::from(column) * 2 + 1) * u64::from(width) / (u64::from(columns) * 2)) as u32;
            let value = pixel(bitmap, x, y);
            let bin = (usize::from(value[0] >> 4) << 8)
                | (usize::from(value[1] >> 4) << 4)
                | usize::from(value[2] >> 4);
            histogram[bin] += 1;
            for (nx, ny) in [(x.saturating_add(1), y), (x, y.saturating_add(1))] {
                if nx < width && ny < height {
                    pairs += 1;
                    let neighbor = pixel(bitmap, nx, ny);
                    if value.iter().zip(neighbor).all(|(a, b)| a.abs_diff(b) <= 2) {
                        flat += 1;
                    }
                }
            }
        }
    }
    Features {
        colors: histogram.iter().filter(|count| **count > 0).count(),
        dominant: f64::from(*histogram.iter().max().unwrap()) / f64::from((rows * columns).max(1)),
        flat: f64::from(flat) / f64::from(pairs.max(1)),
    }
}

fn pixel(bitmap: &DynamicImage, x: u32, y: u32) -> [u8; 4] {
    let value = bitmap.get_pixel(x, y).0;
    // Hidden RGB must not make transparent areas appear diverse.
    if value[3] == 0 { [0; 4] } else { value }
}

fn diverse_patches(bitmap: &DynamicImage, stop_after: usize) -> usize {
    let (width, height) = bitmap.dimensions();
    let mut diverse = 0;
    for row in 0..8u64 {
        for column in 0..8u64 {
            let x = (((2 * column + 1) * u64::from(width) / 16) as u32)
                .saturating_sub(8)
                .min(width.saturating_sub(16));
            let y = (((2 * row + 1) * u64::from(height) / 16) as u32)
                .saturating_sub(8)
                .min(height.saturating_sub(16));
            let mut colors = [0u32; 256];
            let mut count = 0;
            for py in y..y.saturating_add(16).min(height) {
                for px in x..x.saturating_add(16).min(width) {
                    colors[count] = u32::from_le_bytes(pixel(bitmap, px, py));
                    count += 1;
                }
            }
            colors[..count].sort_unstable();
            let unique = usize::from(count > 0)
                + colors[..count]
                    .windows(2)
                    .filter(|pair| pair[0] != pair[1])
                    .count();
            if unique > 128 {
                diverse += 1;
                if diverse >= stop_after {
                    return diverse;
                }
            }
        }
    }
    diverse
}

// Read signatures and chunk headers only. Bound the scan, validate offsets,
// and treat malformed, unsupported, or expensive metadata as no hint.
fn source_hint(input: &Path, dimensions: (u32, u32)) -> std::io::Result<SourceHint> {
    let mut file = File::open(input)?;
    let length = file.metadata()?.len();
    let mut header = [0u8; 16];
    file.read_exact(&mut header)?;
    if header[..3] == [0xff, 0xd8, 0xff] {
        return Ok(SourceHint {
            lossy: true,
            ..Default::default()
        });
    }
    if &header[..4] == b"RIFF" && &header[8..12] == b"WEBP" {
        let end = (u64::from(u32::from_le_bytes(header[4..8].try_into().unwrap())) + 8).min(length);
        let mut position = 12u64;
        let mut opaque = true;
        for _ in 0..256 {
            if position + 8 > end {
                break;
            }
            file.seek(SeekFrom::Start(position))?;
            let mut chunk = [0u8; 8];
            file.read_exact(&mut chunk)?;
            let size = u64::from(u32::from_le_bytes(chunk[4..8].try_into().unwrap()));
            let next = position + 8 + size + (size & 1);
            if next > end {
                break;
            }
            match &chunk[..4] {
                b"VP8X" => {
                    if size != 10 {
                        break;
                    }
                    let mut flags = [0];
                    file.read_exact(&mut flags)?;
                    opaque &= flags[0] & (0x10 | 0x02) == 0;
                }
                b"ALPH" | b"ANIM" => opaque = false,
                b"VP8 " if size > 0 => {
                    return Ok(SourceHint {
                        lossy: opaque,
                        ..Default::default()
                    });
                }
                b"VP8L" => return Ok(SourceHint::default()),
                _ => {}
            }
            position = next;
        }
    } else if &header[..8] == b"\x89PNG\r\n\x1a\n" {
        let mut position = 8u64;
        let mut density_allowed = false;
        let mut compressed = 0u64;
        for _ in 0..256 {
            if position + 12 > length {
                break;
            }
            file.seek(SeekFrom::Start(position))?;
            let mut chunk = [0u8; 8];
            file.read_exact(&mut chunk)?;
            let size = u64::from(u32::from_be_bytes(chunk[..4].try_into().unwrap()));
            let next = position + 12 + size;
            if next > length {
                break;
            }
            match &chunk[4..8] {
                b"IHDR" if position == 8 && size == 13 => {
                    let mut data = [0; 13];
                    file.read_exact(&mut data)?;
                    let width = u32::from_be_bytes(data[..4].try_into().unwrap());
                    let height = u32::from_be_bytes(data[4..8].try_into().unwrap());
                    density_allowed =
                        (width, height) == dimensions && data[8] == 8 && matches!(data[9], 2 | 6);
                }
                b"IDAT" => compressed += size,
                b"IEND"
                    if density_allowed
                        && compressed > 0
                        && dimensions.0 > 0
                        && dimensions.1 > 0 =>
                {
                    return Ok(SourceHint {
                        lossy: false,
                        png_density: Some(
                            compressed as f64 / (f64::from(dimensions.0) * f64::from(dimensions.1)),
                        ),
                    });
                }
                b"acTL" => return Ok(SourceHint::default()),
                _ => {}
            }
            position = next;
        }
    }
    Ok(SourceHint::default())
}

#[cfg(test)]
mod tests {
    use super::*;
    use image::{Rgba, RgbaImage};

    #[test]
    fn flat_images_predict_png_and_hidden_rgb_does_not_add_complexity() {
        let image = DynamicImage::ImageRgba8(RgbaImage::from_fn(600, 300, |x, y| {
            Rgba([x as u8, y as u8, (x ^ y) as u8, 0])
        }));
        assert_eq!(
            classify(&image, Path::new("missing.png")).decision,
            Decision::Png
        );
    }

    #[test]
    fn detailed_images_predict_jpg_and_mixed_images_keep_preview() {
        let mut state = 42u32;
        let noise = RgbaImage::from_fn(600, 300, |_, _| {
            state = state.wrapping_mul(1664525).wrapping_add(1013904223);
            let b = state.to_le_bytes();
            Rgba([b[1], b[2], b[3], 255])
        });
        let image = DynamicImage::ImageRgba8(noise.clone());
        assert_eq!(
            classify(&image, Path::new("missing.png")).decision,
            Decision::JpgWebp
        );
        let mixed = DynamicImage::ImageRgba8(RgbaImage::from_fn(600, 300, |x, y| {
            if x < 300 {
                Rgba([40, 80, 120, 128])
            } else {
                *noise.get_pixel(x, y)
            }
        }));
        assert_eq!(
            classify(&mixed, Path::new("missing.png")).decision,
            Decision::Uncertain
        );
    }

    #[test]
    fn source_hints_use_signatures_and_distinguish_webp_modes() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("misleading.png");
        let mut jpg = vec![0; 16];
        jpg[..3].copy_from_slice(&[0xff, 0xd8, 0xff]);
        std::fs::write(&path, jpg).unwrap();
        assert!(source_hint(&path, (600, 300)).unwrap().lossy);
        for (kind, flags, expected) in [
            (b"VP8 ", None, true),
            (b"VP8L", None, false),
            (b"VP8 ", Some(0), true),
            (b"VP8 ", Some(0x10), false),
            (b"VP8 ", Some(0x02), false),
        ] {
            let mut data = b"RIFF\0\0\0\0WEBP".to_vec();
            if let Some(flags) = flags {
                data.extend_from_slice(b"VP8X\x0a\0\0\0");
                data.extend_from_slice(&[flags, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
            }
            data.extend_from_slice(kind);
            data.extend_from_slice(&2u32.to_le_bytes());
            data.extend_from_slice(&[0, 0]);
            let length = data.len() as u32 - 8;
            data[4..8].copy_from_slice(&length.to_le_bytes());
            std::fs::write(&path, data).unwrap();
            assert_eq!(source_hint(&path, (600, 300)).unwrap().lossy, expected);
        }
        std::fs::write(&path, b"RIFF\xff\xff\xff\xffWEBPVP8 \xff\xff\xff\xff").unwrap();
        assert!(!source_hint(&path, (600, 300)).unwrap().lossy);
    }

    #[test]
    fn png_density_is_not_reused_after_resizing() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("input.png");
        image::RgbImage::from_pixel(600, 300, image::Rgb([40, 80, 120]))
            .save(&path)
            .unwrap();
        assert!(
            source_hint(&path, (600, 300))
                .unwrap()
                .png_density
                .is_some()
        );
        assert!(
            source_hint(&path, (300, 150))
                .unwrap()
                .png_density
                .is_none()
        );
    }

    #[test]
    fn sparse_diverse_patches_veto_a_flat_background_and_ignore_hidden_rgb() {
        let mut bitmap = image::RgbaImage::from_pixel(2400, 1200, Rgba([40, 80, 120, 255]));
        let mut state = 42u32;
        for column in 0..4 {
            let x = (2 * column + 1) * 2400 / 16 - 8;
            let y = 1200 / 16 - 8;
            for py in y..y + 16 {
                for px in x..x + 16 {
                    state = state.wrapping_mul(1664525).wrapping_add(1013904223);
                    let bytes = state.to_le_bytes();
                    bitmap.put_pixel(px, py, Rgba([bytes[1], bytes[2], bytes[3], 255]));
                }
            }
        }
        let image = DynamicImage::ImageRgba8(bitmap.clone());
        assert!(sample(&image).flat >= 0.9);
        assert!(sample(&image).colors <= 64);
        assert_eq!(diverse_patches(&image, 4), 4);
        assert_eq!(
            classify(&image, Path::new("missing.png")).decision,
            Decision::Uncertain
        );
        for value in bitmap.pixels_mut() {
            value[3] = 0;
        }
        let hidden = DynamicImage::ImageRgba8(bitmap);
        assert_eq!(diverse_patches(&hidden, 4), 0);
        assert_eq!(
            classify(&hidden, Path::new("missing.png")).decision,
            Decision::Png
        );
    }
}
