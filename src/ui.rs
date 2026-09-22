use crate::{
    config, diagnostics,
    registry::ExplorerRegistry,
    settings::{DownscaleMode, Settings},
    tools,
};
use anyhow::Result;
use eframe::egui::{self, Color32, RichText, Vec2};
use std::{
    path::{Path, PathBuf},
    process::Command,
};

const ACCENT: Color32 = Color32::from_rgb(93, 207, 180);

pub struct SettingsApp {
    settings: Settings,
    saved: Settings,
    path: PathBuf,
    registry: ExplorerRegistry,
    installed: bool,
    explorer_enabled: bool,
    new_size: u32,
    status: String,
    error: Option<String>,
    log: String,
    tools_open: bool,
    log_open: bool,
    inspect: bool,
    logo: Option<egui::TextureHandle>,
}

fn t<'a>(fr: bool, en: &'a str, french: &'a str) -> &'a str {
    if fr { french } else { en }
}

pub fn run(path: PathBuf) -> Result<()> {
    let app = SettingsApp::new(config::load(&path)?, path, ExplorerRegistry::default())?;
    let icon = image::load_from_memory(include_bytes!("../assets/icon-C.png"))?.to_rgba8();
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([820.0, 570.0])
            .with_min_inner_size([720.0, 480.0])
            .with_icon(egui::IconData {
                width: icon.width(),
                height: icon.height(),
                rgba: icon.into_raw(),
            }),
        centered: true,
        ..Default::default()
    };
    eframe::run_native(
        "Comprimer",
        options,
        Box::new(move |cc| {
            apply_style(&cc.egui_ctx, app.settings.dark_mode);
            Ok(Box::new(app))
        }),
    )
    .map_err(|error| anyhow::anyhow!("Open settings window: {error}"))
}

impl SettingsApp {
    /// Open an auxiliary panel for isolated UI snapshots.
    pub fn with_panel(mut self, panel: &str) -> Self {
        self.tools_open = panel == "tools";
        self.log_open = panel == "log";
        self
    }

    pub fn new(mut settings: Settings, path: PathBuf, registry: ExplorerRegistry) -> Result<Self> {
        let saved = settings.clone();
        settings.comprimer_path.get_or_insert_with(|| {
            registry
                .registered_path()
                .ok()
                .flatten()
                .or_else(|| std::env::current_exe().ok())
                .map(|p| p.to_string_lossy().into_owned())
                .unwrap_or_default()
        });
        let paths = &mut settings.executables;
        paths
            .pngquant_path
            .get_or_insert_with(|| detected("pngquant"));
        paths.cjpeg_path.get_or_insert_with(|| detected("cjpeg"));
        paths.cwebp_path.get_or_insert_with(|| detected("cwebp"));
        if paths.dwebp_path.is_none() {
            paths.dwebp_path = Some(
                tools::detect_decoder(paths.cwebp_path.as_deref())
                    .map(|p| p.to_string_lossy().into_owned())
                    .unwrap_or_default(),
            );
        }
        for size in &settings.available_sizes {
            settings.size_actions.entry(*size).or_default();
        }
        let installed = registry.is_registered()?;
        let log = diagnostics::recent_log(&path).unwrap_or_default();
        Ok(Self {
            settings,
            saved,
            path,
            registry,
            installed,
            explorer_enabled: installed,
            new_size: 1536,
            status: String::new(),
            error: None,
            log,
            tools_open: false,
            log_open: false,
            inspect: false,
            logo: None,
        })
    }

    fn dirty(&self) -> bool {
        self.settings != self.saved || self.explorer_enabled != self.installed
    }

    fn save(&mut self) -> Result<()> {
        self.settings.normalize();
        if self.explorer_enabled {
            self.registry
                .registration_path(&self.settings, &std::env::current_exe()?)?;
        }
        config::save(&self.path, &self.settings)?;
        if self.explorer_enabled {
            self.registry
                .register(&self.settings, &std::env::current_exe()?)?;
        } else if self.registry.is_registered()? {
            self.registry.unregister()?;
        }
        self.installed = self.registry.is_registered()?;
        self.saved = self.settings.clone();
        tracing::info!("Settings saved");
        Ok(())
    }

    fn feedback(&mut self, result: Result<()>, success: &str, failure: &str) {
        match result {
            Ok(()) => {
                self.status = success.into();
                self.error = None;
            }
            Err(error) => {
                self.status = failure.into();
                self.error = Some(format!("{error:#}"));
                tracing::error!("{error:#}");
            }
        }
    }

    fn save_feedback(&mut self, fr: bool) {
        let result = self.save();
        self.feedback(
            result,
            t(fr, "Saved", "Enregistré"),
            t(fr, "Save failed", "Échec d'enregistrement"),
        );
    }

    pub fn draw(&mut self, ui: &mut egui::Ui) {
        let ctx = ui.ctx().clone();
        let fr = self.settings.language == "fr";
        self.load_logo(&ctx);

        egui::Panel::top("header")
            .resizable(false)
            .frame(
                egui::Frame::new()
                    .fill(ui.visuals().panel_fill)
                    .inner_margin(18),
            )
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    if let Some(logo) = &self.logo {
                        ui.image((logo.id(), Vec2::splat(28.0)));
                    }
                    ui.label(RichText::new("Comprimer").size(22.0).strong());
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        if ui.button(if fr { "EN" } else { "FR" }).clicked() {
                            self.settings.language = if fr { "en" } else { "fr" }.into();
                        }
                        if ui
                            .button(if self.settings.dark_mode {
                                t(fr, "Light", "Clair")
                            } else {
                                t(fr, "Dark", "Sombre")
                            })
                            .clicked()
                        {
                            self.settings.dark_mode = !self.settings.dark_mode;
                            apply_style(&ctx, self.settings.dark_mode);
                        }
                    });
                });
            });

        egui::Panel::bottom("footer")
            .resizable(false)
            .frame(
                egui::Frame::new()
                    .fill(ui.visuals().panel_fill)
                    .inner_margin(16),
            )
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    let ready = self.ready_tools();
                    if ui
                        .button(format!("{} {ready}/4", t(fr, "Tools", "Outils")))
                        .clicked()
                    {
                        self.tools_open = true;
                    }
                    if ui.button(t(fr, "Log", "Journal")).clicked() {
                        self.refresh_log(fr);
                        self.log_open = true;
                    }
                    if self.error.is_some() {
                        ui.colored_label(ui.visuals().error_fg_color, &self.status);
                    }
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        if ui
                            .add_sized(
                                [86.0, 32.0],
                                egui::Button::new(
                                    RichText::new(t(fr, "Save", "Enregistrer"))
                                        .color(Color32::from_rgb(15, 35, 29)),
                                )
                                .fill(ACCENT),
                            )
                            .clicked()
                        {
                            self.save_feedback(fr);
                        }
                        if self.dirty() {
                            ui.weak(t(fr, "Unsaved", "Modifié"));
                        } else if self.error.is_none() {
                            ui.weak(&self.status);
                        }
                    });
                });
            });

        egui::CentralPanel::default()
            .frame(
                egui::Frame::new()
                    .fill(ui.visuals().window_fill())
                    .inner_margin(22),
            )
            .show(ui, |ui| {
                egui::ScrollArea::vertical()
                    .auto_shrink([false, false])
                    .show(ui, |ui| {
                        ui.horizontal_wrapped(|ui| {
                            ui.checkbox(&mut self.explorer_enabled, "Explorer")
                                .on_hover_text(t(
                                    fr,
                                    "Apply with Save",
                                    "Appliquer avec Enregistrer",
                                ));
                            ui.add_space(14.0);
                            ui.checkbox(
                                &mut self.settings.nested_menu,
                                t(fr, "Submenu", "Sous-menu"),
                            );
                            ui.checkbox(
                                &mut self.settings.overwrite_original,
                                t(fr, "Overwrite", "Remplacer"),
                            );
                        });
                        ui.add_space(18.0);
                        ui.columns(2, |columns| {
                            section(&mut columns[0], t(fr, "Sizes", "Tailles"), |ui| {
                                self.sizes(ui, fr)
                            });
                            section(&mut columns[1], t(fr, "Quality", "Qualité"), |ui| {
                                self.quality(ui, fr)
                            });
                            columns[1].add_space(16.0);
                            section(&mut columns[1], t(fr, "Formats", "Formats"), |ui| {
                                self.formats(ui, fr)
                            });
                        });
                    });
            });

        self.tool_window(&ctx, fr);
        self.log_window(&ctx, fr);
        if self.inspect {
            egui::Window::new("egui")
                .open(&mut self.inspect)
                .vscroll(true)
                .show(&ctx, |ui| ctx.inspection_ui(ui));
        }
    }

    fn sizes(&mut self, ui: &mut egui::Ui, fr: bool) {
        let mut remove = None;
        egui::Grid::new("sizes")
            .num_columns(4)
            .spacing([16.0, 8.0])
            .min_col_width(34.0)
            .show(ui, |ui| {
                ui.weak(t(fr, "Size", "Taille"));
                ui.weak("Auto")
                    .on_hover_text(t(fr, "Compare formats", "Comparer les formats"));
                ui.weak(t(fr, "Resize", "Réduire")).on_hover_text(t(
                    fr,
                    "Keep source format",
                    "Conserver le format source",
                ));
                ui.label("");
                ui.end_row();
                ui.label(t(fr, "Original", "Original"));
                ui.checkbox(&mut self.settings.auto_mode, "");
                ui.weak("-");
                ui.label("");
                ui.end_row();
                for &size in &self.settings.available_sizes {
                    ui.label(format!("{size} px"));
                    let actions = self.settings.size_actions.entry(size).or_default();
                    ui.checkbox(&mut actions.auto_resize, "");
                    ui.checkbox(&mut actions.resize, "");
                    if ui
                        .small_button("x")
                        .on_hover_text(t(fr, "Remove", "Retirer"))
                        .clicked()
                    {
                        remove = Some(size);
                    }
                    ui.end_row();
                }
            });
        if let Some(size) = remove {
            self.settings.available_sizes.retain(|n| *n != size);
            self.settings.size_actions.remove(&size);
        }
        ui.add_space(12.0);
        ui.horizontal(|ui| {
            ui.add(
                egui::DragValue::new(&mut self.new_size)
                    .range(1..=32768)
                    .suffix(" px"),
            );
            if ui
                .button("+")
                .on_hover_text(t(fr, "Add size", "Ajouter une taille"))
                .clicked()
                && !self.settings.available_sizes.contains(&self.new_size)
            {
                self.settings.available_sizes.push(self.new_size);
                self.settings
                    .size_actions
                    .insert(self.new_size, Default::default());
                self.settings.normalize();
            }
        });
        ui.add_space(12.0);
        ui.horizontal(|ui| {
            ui.weak(t(fr, "Limit", "Limite"));
            egui::ComboBox::from_id_salt("dimension")
                .width(135.0)
                .selected_text(mode_label(self.settings.downscale_mode, fr))
                .show_ui(ui, |ui| {
                    for mode in [
                        DownscaleMode::LongestSide,
                        DownscaleMode::Width,
                        DownscaleMode::Height,
                    ] {
                        ui.selectable_value(
                            &mut self.settings.downscale_mode,
                            mode,
                            mode_label(mode, fr),
                        );
                    }
                });
        });
    }

    fn quality(&mut self, ui: &mut egui::Ui, fr: bool) {
        ui.spacing_mut().slider_width = (ui.available_width() - 132.0).max(70.0);
        egui::Grid::new("quality")
            .num_columns(2)
            .spacing([14.0, 6.0])
            .show(ui, |ui| {
                for (label, value) in [
                    ("PNG min", &mut self.settings.encoders.png_min_quality),
                    (
                        t(fr, "PNG target", "PNG cible"),
                        &mut self.settings.encoders.png_quality,
                    ),
                    ("JPG", &mut self.settings.encoders.jpg_quality),
                    ("WebP", &mut self.settings.encoders.webp_quality),
                ] {
                    ui.label(label);
                    ui.add(egui::Slider::new(value, 0..=100));
                    ui.end_row();
                }
            });
    }

    fn formats(&mut self, ui: &mut egui::Ui, fr: bool) {
        egui::Grid::new("formats")
            .num_columns(5)
            .spacing([12.0, 6.0])
            .show(ui, |ui| {
                for label in [
                    "",
                    t(fr, "Resize", "Réduire"),
                    t(fr, "Optimize", "Optimiser"),
                    t(fr, "To WebP", "Vers WebP"),
                    t(fr, "To JPG", "Vers JPG"),
                ] {
                    ui.weak(label);
                }
                ui.end_row();
                for (name, format, optimize, webp, jpg) in [
                    ("JPG", &mut self.settings.jpg, true, true, false),
                    ("PNG", &mut self.settings.png, true, true, true),
                    ("WebP", &mut self.settings.webp, false, false, false),
                ] {
                    ui.label(name);
                    ui.checkbox(&mut format.downscale, "");
                    if optimize {
                        ui.checkbox(&mut format.optimize, "");
                    } else {
                        ui.weak("-");
                    }
                    if webp {
                        ui.checkbox(&mut format.convert_to_webp, "");
                    } else {
                        ui.weak("-");
                    }
                    if jpg {
                        ui.checkbox(&mut format.convert_to_jpg, "");
                    } else {
                        ui.weak("-");
                    }
                    ui.end_row();
                }
            });
    }

    fn tool_window(&mut self, ctx: &egui::Context, fr: bool) {
        let mut open = self.tools_open;
        let mut done = false;
        egui::Window::new(t(fr, "Tools", "Outils"))
            .id(egui::Id::new("tools"))
            .open(&mut open)
            .collapsible(false)
            .resizable(true)
            .default_size([650.0, 330.0])
            .anchor(egui::Align2::CENTER_CENTER, Vec2::ZERO)
            .vscroll(true)
            .show(ctx, |ui| {
                path_field(
                    ui,
                    fr,
                    "Comprimer",
                    self.settings.comprimer_path.as_mut().unwrap(),
                    || std::env::current_exe().ok(),
                );
                ui.separator();
                path_field(
                    ui,
                    fr,
                    "pngquant",
                    self.settings.executables.pngquant_path.as_mut().unwrap(),
                    || tools::detect("pngquant"),
                );
                path_field(
                    ui,
                    fr,
                    "cjpeg",
                    self.settings.executables.cjpeg_path.as_mut().unwrap(),
                    || tools::detect("cjpeg"),
                );
                path_field(
                    ui,
                    fr,
                    "cwebp",
                    self.settings.executables.cwebp_path.as_mut().unwrap(),
                    || tools::detect("cwebp"),
                );
                let encoder = self.settings.executables.cwebp_path.clone();
                path_field(
                    ui,
                    fr,
                    "dwebp",
                    self.settings.executables.dwebp_path.as_mut().unwrap(),
                    || tools::detect_decoder(encoder.as_deref()),
                );
                ui.horizontal(|ui| {
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        done = ui.button(t(fr, "Done", "Terminé")).clicked();
                    });
                });
            });
        self.tools_open = open && !done;
    }

    fn log_window(&mut self, ctx: &egui::Context, fr: bool) {
        let mut open = self.log_open;
        egui::Window::new(t(fr, "Log", "Journal"))
            .id(egui::Id::new("log-window"))
            .open(&mut open)
            .default_size([660.0, 350.0])
            .resizable(true)
            .show(ctx, |ui| {
                ui.horizontal(|ui| {
                    if ui.button(t(fr, "Refresh", "Actualiser")).clicked() {
                        self.refresh_log(fr);
                    }
                    if ui.button(t(fr, "Copy", "Copier")).clicked() {
                        ui.ctx().copy_text(format!(
                            "{}\n{}\n{}",
                            diagnostic_report(&self.settings, &self.path),
                            self.error.as_deref().unwrap_or_default(),
                            self.log
                        ));
                    }
                    if ui.button(t(fr, "Folder", "Dossier")).clicked() {
                        let result = Command::new("explorer.exe")
                            .arg(
                                diagnostics::active_log_path(&self.path)
                                    .parent()
                                    .unwrap_or(Path::new(".")),
                            )
                            .spawn()
                            .map(|_| ())
                            .map_err(Into::into);
                        self.feedback(result, "", t(fr, "Open failed", "Ouverture impossible"));
                    }
                    ui.checkbox(&mut self.inspect, t(fr, "Inspect", "Inspecter"));
                });
                if let Some(error) = &self.error {
                    ui.colored_label(ui.visuals().error_fg_color, error);
                }
                egui::ScrollArea::both().id_salt("log-text").show(ui, |ui| {
                    if self.log.is_empty() {
                        ui.weak(t(fr, "No activity", "Aucune activité"));
                    } else {
                        ui.add(
                            egui::Label::new(RichText::new(&self.log).monospace().size(12.0))
                                .selectable(true),
                        );
                    }
                });
            });
        self.log_open = open;
    }

    fn ready_tools(&self) -> usize {
        [
            &self.settings.executables.pngquant_path,
            &self.settings.executables.cjpeg_path,
            &self.settings.executables.cwebp_path,
            &self.settings.executables.dwebp_path,
        ]
        .into_iter()
        .filter(|path| path.as_deref().and_then(tools::existing_path).is_some())
        .count()
    }

    fn refresh_log(&mut self, fr: bool) {
        match diagnostics::recent_log(&self.path) {
            Ok(log) => self.log = log,
            Err(error) => self.feedback(Err(error), "", t(fr, "Read failed", "Lecture impossible")),
        }
    }

    fn load_logo(&mut self, ctx: &egui::Context) {
        if self.logo.is_none()
            && let Ok(icon) = image::load_from_memory(include_bytes!("../assets/icon-C.png"))
        {
            let icon = icon.to_rgba8();
            self.logo = Some(ctx.load_texture(
                "logo",
                egui::ColorImage::from_rgba_unmultiplied(
                    [icon.width() as usize, icon.height() as usize],
                    icon.as_raw(),
                ),
                Default::default(),
            ));
        }
    }
}

impl eframe::App for SettingsApp {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        self.draw(ui);
    }
}

pub fn apply_style(ctx: &egui::Context, dark: bool) {
    ctx.set_theme(if dark {
        egui::Theme::Dark
    } else {
        egui::Theme::Light
    });
    let mut style = (*ctx.global_style()).clone();
    style.visuals = if dark {
        egui::Visuals::dark()
    } else {
        egui::Visuals::light()
    };
    if dark {
        style.visuals.panel_fill = Color32::from_rgb(24, 29, 34);
        style.visuals.window_fill = Color32::from_rgb(17, 21, 25);
        style.visuals.extreme_bg_color = Color32::from_rgb(14, 18, 21);
        style.visuals.widgets.noninteractive.fg_stroke.color = Color32::from_rgb(224, 230, 235);
        style.visuals.widgets.inactive.fg_stroke.color = Color32::from_rgb(224, 230, 235);
        style.visuals.weak_text_color = Some(Color32::from_rgb(150, 163, 174));
    } else {
        style.visuals.weak_text_color = Some(Color32::from_rgb(92, 104, 112));
    }
    style.visuals.selection.bg_fill = Color32::from_rgb(38, 103, 88);
    style.visuals.selection.stroke = egui::Stroke::new(1.0, Color32::WHITE);
    style.spacing.item_spacing = Vec2::new(10.0, 8.0);
    style.spacing.button_padding = Vec2::new(10.0, 5.0);
    style.spacing.interact_size.y = 24.0;
    style
        .text_styles
        .insert(egui::TextStyle::Body, egui::FontId::proportional(14.0));
    style
        .text_styles
        .insert(egui::TextStyle::Button, egui::FontId::proportional(14.0));
    ctx.set_global_style(style);
}

fn section(ui: &mut egui::Ui, title: &str, contents: impl FnOnce(&mut egui::Ui)) {
    ui.push_id(title, |ui| {
        ui.label(RichText::new(title).size(16.0).strong());
        ui.add_space(10.0);
        contents(ui);
    });
}

fn readiness(ui: &mut egui::Ui, ready: bool, fr: bool) {
    let (rect, response) = ui.allocate_exact_size(Vec2::splat(14.0), egui::Sense::hover());
    let color = if ready {
        if ui.visuals().dark_mode {
            ACCENT
        } else {
            Color32::from_rgb(24, 115, 85)
        }
    } else {
        ui.visuals().warn_fg_color
    };
    let center = rect.center();
    if ready {
        ui.painter().add(egui::Shape::line(
            vec![
                center + Vec2::new(-5.0, 0.0),
                center + Vec2::new(-1.0, 4.0),
                center + Vec2::new(5.0, -4.0),
            ],
            egui::Stroke::new(1.8, color),
        ));
    } else {
        ui.painter().line_segment(
            [center + Vec2::new(-4.0, 0.0), center + Vec2::new(4.0, 0.0)],
            egui::Stroke::new(1.8, color),
        );
    }
    response.on_hover_text(if ready {
        t(fr, "Ready", "Prêt")
    } else {
        t(fr, "Missing", "Absent")
    });
}

fn path_field(
    ui: &mut egui::Ui,
    fr: bool,
    label: &str,
    value: &mut String,
    detect: impl FnOnce() -> Option<PathBuf>,
) {
    ui.push_id(label, |ui| {
        ui.horizontal(|ui| {
            ui.allocate_ui_with_layout(
                Vec2::new(110.0, ui.spacing().interact_size.y),
                egui::Layout::left_to_right(egui::Align::Center),
                |ui| {
                    ui.set_min_width(110.0);
                    ui.strong(label);
                    readiness(ui, tools::existing_path(value).is_some(), fr);
                },
            );
            ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                if ui.button(t(fr, "Browse", "Parcourir")).clicked()
                    && let Some(path) = rfd::FileDialog::new()
                        .add_filter("Executable", &["exe"])
                        .pick_file()
                {
                    *value = path.to_string_lossy().into_owned();
                }
                if ui.button(t(fr, "Detect", "Détecter")).clicked() {
                    *value = detect()
                        .map(|p| p.to_string_lossy().into_owned())
                        .unwrap_or_default();
                }
                ui.add_sized(
                    [ui.available_width(), ui.spacing().interact_size.y],
                    egui::TextEdit::singleline(value),
                );
            });
        });
        ui.add_space(5.0);
    });
}

fn mode_label(mode: DownscaleMode, fr: bool) -> &'static str {
    match mode {
        DownscaleMode::LongestSide => t(fr, "Longest side", "Grand côté"),
        DownscaleMode::Width => t(fr, "Width", "Largeur"),
        DownscaleMode::Height => t(fr, "Height", "Hauteur"),
    }
}

fn detected(name: &str) -> String {
    tools::detect(name)
        .map(|p| p.to_string_lossy().into_owned())
        .unwrap_or_default()
}

pub fn diagnostic_report(settings: &Settings, path: &Path) -> String {
    let mut report = format!(
        "Comprimer {} · Windows {}\nSettings: {}\nLog: {}\nExecutable: {}\n",
        env!("CARGO_PKG_VERSION"),
        std::env::consts::ARCH,
        path.display(),
        diagnostics::active_log_path(path).display(),
        settings
            .comprimer_path
            .as_deref()
            .unwrap_or("(not selected)")
    );
    for (name, configured) in [
        ("pngquant", &settings.executables.pngquant_path),
        ("cjpeg", &settings.executables.cjpeg_path),
        ("cwebp", &settings.executables.cwebp_path),
        ("dwebp", &settings.executables.dwebp_path),
    ] {
        let resolved = if name == "dwebp" && configured.is_none() {
            tools::detect_decoder(settings.executables.cwebp_path.as_deref())
        } else {
            tools::resolve(name, configured.as_deref())
        };
        report.push_str(&format!(
            "{name}: {}\n",
            resolved
                .map(|p| p.to_string_lossy().into_owned())
                .unwrap_or_else(|| "MISSING".into())
        ));
    }
    report
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn closing_and_keyboard_shortcuts_do_not_save_edits() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("settings.json");
        let initial = Settings::default();
        config::save(&path, &initial).unwrap();
        let original = std::fs::read(&path).unwrap();
        let mut app = SettingsApp::new(
            initial,
            path.clone(),
            ExplorerRegistry::at(r"Software\ComprimerUiReadOnlyTest".into()),
        )
        .unwrap();
        app.settings.encoders.jpg_quality = 12;
        app.explorer_enabled = true;
        let ctx = egui::Context::default();
        let mut input = egui::RawInput::default();
        input.events.push(egui::Event::Key {
            key: egui::Key::S,
            physical_key: None,
            pressed: true,
            repeat: false,
            modifiers: egui::Modifiers::CTRL,
        });
        input
            .viewports
            .get_mut(&egui::ViewportId::ROOT)
            .unwrap()
            .events
            .push(egui::ViewportEvent::Close);
        let mut output = ctx.run_ui(input, |ui| app.draw(ui));
        output.textures_delta.clear();
        drop(app);
        assert_eq!(std::fs::read(&path).unwrap(), original);
    }

    #[test]
    fn save_applies_preferences_and_explorer_together() {
        struct Cleanup(String);
        impl Drop for Cleanup {
            fn drop(&mut self) {
                let _ = winreg::RegKey::predef(winreg::enums::HKEY_CURRENT_USER)
                    .delete_subkey_all(&self.0);
            }
        }
        let dir = tempfile::tempdir().unwrap();
        let root = format!(
            r"Software\ComprimerSaveTests\{}",
            dir.path().file_name().unwrap().to_string_lossy()
        );
        let _cleanup = Cleanup(root.clone());
        let path = dir.path().join("settings.json");
        let mut app = SettingsApp::new(
            Settings::default(),
            path.clone(),
            ExplorerRegistry::at(root),
        )
        .unwrap();
        app.explorer_enabled = true;
        app.settings.encoders.jpg_quality = 72;
        assert!(!path.exists());
        assert!(!app.registry.is_registered().unwrap());
        app.save().unwrap();
        assert_eq!(config::load(&path).unwrap().encoders.jpg_quality, 72);
        assert!(app.registry.is_registered().unwrap());
        assert!(!app.dirty());
        app.explorer_enabled = false;
        assert!(app.registry.is_registered().unwrap());
        app.save().unwrap();
        assert!(!app.registry.is_registered().unwrap());
    }

    #[test]
    fn compact_screen_renders_in_both_languages_and_themes() {
        let dir = tempfile::tempdir().unwrap();
        for fr in [false, true] {
            for dark in [false, true] {
                let settings = Settings {
                    language: if fr { "fr" } else { "en" }.into(),
                    dark_mode: dark,
                    ..Default::default()
                };
                for panel in ["main", "tools", "log"] {
                    let mut app = SettingsApp::new(
                        settings.clone(),
                        dir.path().join("settings.json"),
                        ExplorerRegistry::at(r"Software\ComprimerUiReadOnlyTest".into()),
                    )
                    .unwrap()
                    .with_panel(panel);
                    let ctx = egui::Context::default();
                    apply_style(&ctx, dark);
                    let mut output = ctx.run_ui(
                        egui::RawInput {
                            screen_rect: Some(egui::Rect::from_min_size(
                                egui::Pos2::ZERO,
                                Vec2::new(720.0, 480.0),
                            )),
                            ..Default::default()
                        },
                        |ui| app.draw(ui),
                    );
                    assert!(!output.shapes.is_empty());
                    output.textures_delta.clear();
                    assert!(!app.path.exists(), "Rendering must not save settings");
                }
            }
        }
    }
}
