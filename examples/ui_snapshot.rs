//! Render an isolated native UI screenshot without touching user settings or Explorer.
//! cargo run --example ui_snapshot -- main en dark 820 570 out/main.png
use comprimer::{
    registry::ExplorerRegistry,
    settings::Settings,
    ui::{self, SettingsApp},
};
use eframe::egui;
use std::path::PathBuf;

struct Capture {
    app: SettingsApp,
    output: PathBuf,
    frames: usize,
    requested: bool,
    finished: bool,
}

impl eframe::App for Capture {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        if self.finished {
            ui.ctx().send_viewport_cmd(egui::ViewportCommand::Close);
            return;
        }
        let screenshot = ui.input(|input| {
            input.events.iter().find_map(|event| match event {
                egui::Event::Screenshot { image, .. } => Some(image.clone()),
                _ => None,
            })
        });
        if let Some(image) = screenshot {
            if let Some(parent) = self.output.parent() {
                std::fs::create_dir_all(parent).unwrap();
            }
            image::save_buffer(
                &self.output,
                image.as_raw(),
                image.width() as u32,
                image.height() as u32,
                image::ColorType::Rgba8,
            )
            .unwrap();
            println!("Saved {}", self.output.display());
            self.finished = true;
            // Skip SettingsApp::draw during close: preview never saves or registers anything.
            ui.ctx().send_viewport_cmd(egui::ViewportCommand::Close);
            return;
        }
        self.app.draw(ui);
        self.frames += 1;
        if self.frames >= 6 && !self.requested {
            ui.ctx()
                .send_viewport_cmd(egui::ViewportCommand::Screenshot(Default::default()));
            self.requested = true;
        }
        ui.ctx().request_repaint();
    }
}

fn main() -> eframe::Result {
    let args: Vec<_> = std::env::args().skip(1).collect();
    assert_eq!(
        args.len(),
        6,
        "Expected PANEL LANGUAGE THEME WIDTH HEIGHT OUTPUT.png"
    );
    assert!(
        ["main", "tools", "log"].contains(&args[0].as_str()),
        "Unknown panel"
    );
    let settings = Settings {
        language: args[1].clone(),
        dark_mode: args[2] == "dark",
        ..Default::default()
    };
    let dark = settings.dark_mode;
    let dir = tempfile::tempdir().unwrap();
    let app = SettingsApp::new(
        settings,
        dir.path().join("settings.json"),
        ExplorerRegistry::at(format!(r"Software\ComprimerPreview\{}", std::process::id())),
    )
    .unwrap()
    .with_panel(&args[0]);
    let width: f32 = args[3].parse().unwrap();
    let height: f32 = args[4].parse().unwrap();
    eframe::run_native(
        "Comprimer UI snapshot",
        eframe::NativeOptions {
            viewport: egui::ViewportBuilder::default()
                .with_inner_size([width, height])
                .with_resizable(false),
            ..Default::default()
        },
        Box::new(move |cc| {
            ui::apply_style(&cc.egui_ctx, dark);
            Ok(Box::new(Capture {
                app,
                output: args[5].clone().into(),
                frames: 0,
                requested: false,
                finished: false,
            }))
        }),
    )
}
