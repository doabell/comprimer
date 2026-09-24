use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum DownscaleMode {
    #[default]
    LongestSide,
    Width,
    Height,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default, rename_all = "camelCase")]
pub struct Settings {
    pub nested_menu: bool,
    pub overwrite_original: bool,
    pub downscale_size: u32,
    pub downscale_mode: DownscaleMode,
    pub available_sizes: Vec<u32>,
    pub size_actions: BTreeMap<u32, SizeActions>,
    pub language: String,
    pub dark_mode: bool,
    pub auto_mode: bool,
    /// Use a 512px codec preview when the inexpensive classifier abstains.
    pub auto_preview: bool,
    pub comprimer_path: Option<String>,
    pub encoders: Encoders,
    pub jpg: FormatSettings,
    pub png: FormatSettings,
    #[serde(rename = "webP")]
    pub webp: FormatSettings,
    pub executables: Executables,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            nested_menu: true,
            overwrite_original: false,
            downscale_size: 1024,
            downscale_mode: DownscaleMode::LongestSide,
            available_sizes: vec![512, 1024],
            size_actions: BTreeMap::new(),
            language: "en".into(),
            dark_mode: true,
            auto_mode: true,
            auto_preview: false,
            comprimer_path: None,
            encoders: Encoders::default(),
            jpg: FormatSettings {
                downscale: true,
                convert_to_webp: true,
                optimize: true,
                ..Default::default()
            },
            png: FormatSettings {
                downscale: true,
                convert_to_webp: true,
                convert_to_jpg: true,
                optimize: true,
            },
            webp: FormatSettings {
                downscale: true,
                ..Default::default()
            },
            executables: Executables::default(),
        }
    }
}

impl Settings {
    pub fn normalize(&mut self) {
        self.available_sizes.retain(|size| *size > 0);
        self.available_sizes.sort_unstable();
        self.available_sizes.dedup();
        self.encoders.png_min_quality = self.encoders.png_min_quality.clamp(0, 100);
        self.encoders.png_quality = self.encoders.png_quality.clamp(0, 100);
        self.encoders.jpg_quality = self.encoders.jpg_quality.clamp(0, 100);
        self.encoders.webp_quality = self.encoders.webp_quality.clamp(0, 100);
    }

    pub fn actions(&self, size: u32) -> SizeActions {
        self.size_actions.get(&size).cloned().unwrap_or_default()
    }
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default, rename_all = "camelCase")]
pub struct SizeActions {
    pub auto_resize: bool,
    pub resize: bool,
}

impl Default for SizeActions {
    fn default() -> Self {
        Self {
            auto_resize: true,
            resize: true,
        }
    }
}

#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default, rename_all = "camelCase")]
pub struct FormatSettings {
    pub downscale: bool,
    #[serde(rename = "convertToWebP")]
    pub convert_to_webp: bool,
    pub convert_to_jpg: bool,
    pub optimize: bool,
}

#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default, rename_all = "camelCase")]
pub struct Executables {
    pub pngquant_path: Option<String>,
    #[serde(rename = "img2WebPPath")]
    pub cwebp_path: Option<String>,
    pub cjpeg_path: Option<String>,
    pub dwebp_path: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default, rename_all = "camelCase")]
pub struct Encoders {
    pub png_min_quality: i32,
    pub png_quality: i32,
    pub jpg_quality: i32,
    #[serde(rename = "webPQuality")]
    pub webp_quality: i32,
}

impl Default for Encoders {
    fn default() -> Self {
        Self {
            png_min_quality: 65,
            png_quality: 80,
            jpg_quality: 85,
            webp_quality: 80,
        }
    }
}
