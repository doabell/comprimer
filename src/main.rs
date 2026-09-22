#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
#![cfg(windows)]

use anyhow::Result;
use comprimer::{
    cli::{self, Command},
    config, diagnostics,
    images::ImageService,
    registry::ExplorerRegistry,
    tools::SystemRunner,
    ui,
};

fn main() {
    let gui = std::env::args_os().len() == 1;
    if let Err(error) = run() {
        tracing::error!("{error:#}");
        eprintln!("Comprimer: {error:#}");
        if gui {
            rfd::MessageDialog::new()
                .set_title("Comprimer")
                .set_description(format!("{error:#}"))
                .set_level(rfd::MessageLevel::Error)
                .show();
        }
        std::process::exit(1);
    }
}

fn run() -> Result<()> {
    let path = config::settings_path()?;
    diagnostics::init(&path)?;
    let args: Vec<_> = std::env::args_os().skip(1).collect();
    let command = cli::parse(&args)?;
    let span = tracing::info_span!("command", command = ?command);
    let _entered = span.enter();
    tracing::info!("Command started");
    if command == Command::Help {
        println!("{}", cli::HELP);
        return Ok(());
    }
    if command == Command::Settings {
        return ui::run(path);
    }
    let mut settings = config::load(&path)?;
    match command {
        Command::Process(operation, input) => {
            ImageService {
                settings: &settings,
                runner: &SystemRunner::default(),
            }
            .process(&input, operation)?;
        }
        Command::ToggleOverwrite => {
            settings.overwrite_original = !settings.overwrite_original;
            let registry = ExplorerRegistry::default();
            if registry.is_registered()? {
                let exe = registry.register(&settings, &std::env::current_exe()?)?;
                settings.comprimer_path = Some(exe.to_string_lossy().into_owned());
            }
            config::save(&path, &settings)?;
        }
        Command::Diagnostics => {
            let report = ui::diagnostic_report(&settings, &path);
            println!("{report}");
            tracing::info!("{report}");
        }
        Command::Settings | Command::Help => unreachable!(),
    }
    Ok(())
}
