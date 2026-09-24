use crate::images::Operation;
use anyhow::{Result, bail, ensure};
use std::{ffi::OsString, path::PathBuf};

pub const HELP: &str = "Comprimer — Windows image compression\n\nNo arguments: open settings\n--headless COMMAND: process without a window (used by Explorer)\n--auto [SIZE] FILE\n--downscale SIZE FILE\n--to-webp FILE\n--to-jpg FILE\n--pngquant FILE\n--mozjpeg FILE\n--toggle-overwrite\n--diagnostics\n--help\n\nFailures return a nonzero exit code and are recorded in comprimer.log.\nUse RUST_LOG=comprimer=debug for encoder arguments and stderr.\nCOMPRIMER_CONFIG overrides the settings file for isolated development.\n";

#[derive(Debug, PartialEq, Eq)]
pub enum Command {
    Settings,
    Process(Operation, PathBuf),
    ToggleOverwrite,
    Diagnostics,
    Help,
}

pub fn parse(args: &[OsString]) -> Result<Command> {
    let headless = args.first().is_some_and(|arg| {
        arg.to_str()
            .is_some_and(|arg| arg.eq_ignore_ascii_case("--headless"))
    });
    let args = if headless { &args[1..] } else { args };
    if args.is_empty() {
        ensure!(
            !headless,
            "Headless mode requires a command. Use --help for usage."
        );
        return Ok(Command::Settings);
    }
    let command = args[0].to_str().unwrap_or("").to_ascii_lowercase();
    let size = |value: &OsString| -> Result<u32> {
        let n = value.to_str().unwrap_or("").parse::<u32>()?;
        ensure!(n > 0, "Resize size must be greater than zero");
        Ok(n)
    };
    let operation = match (command.as_str(), args.len()) {
        ("--auto", 2) => Operation::Auto(None),
        ("--auto", 3) => Operation::Auto(Some(size(&args[1])?)),
        ("--downscale", 3) => Operation::Resize(size(&args[1])?),
        ("--to-webp", 2) => Operation::ToWebp,
        ("--to-jpg", 2) => Operation::ToJpg,
        ("--pngquant", 2) => Operation::OptimizePng,
        ("--mozjpeg", 2) => Operation::OptimizeJpg,
        // Legacy Explorer commands may pass the selected image, which is unused here.
        ("--toggle-overwrite", 1 | 2) => return Ok(Command::ToggleOverwrite),
        ("--diagnostics", 1) => return Ok(Command::Diagnostics),
        ("--help" | "-h", 1) => return Ok(Command::Help),
        _ => bail!("Invalid command or arguments. Use --help for usage."),
    };
    Ok(Command::Process(
        operation,
        PathBuf::from(args.last().unwrap()),
    ))
}
