# Comprimer

A Windows 10/11 image compression and conversion tool. Right-click an image in Explorer to process it silently; open `Comprimer.exe` for the native [egui/eframe](https://github.com/emilk/egui) settings app.

Written in Rust. No .NET runtime is needed. Windows x64 is the supported release target.

## Install

1. Download `Comprimer-…-win-x64.zip` from [Releases](../../releases).
2. Extract it to a permanent location, then run `Comprimer.exe`.
3. Open **Tools** to detect or select encoder executables.
4. Enable **Explorer**, then click **Save**. No administrator privileges are needed.

On Windows 11, the shortcuts appear under **Show more options**. Move the executable only after updating its selected path and refreshing the menu.

## Settings

- **Sizes:** enable Auto or resizing for each preset. Original-size Auto has its own switch.
- **Quality:** adjust PNG, JPG, and WebP quality.
- **Formats:** choose individual actions for each source format.
- **Tools:** edit executable paths. The button shows how many encoders are available.
- **Log:** view activity, copy diagnostics, or open egui's inspector.

The single settings screen supports English/French, dark/light themes, DPI scaling, and scrolling. Only clicking **Save** applies preferences and the selected Explorer menu state. Closing the window discards unsaved edits; keyboard shortcuts do not save. Enable **Explorer** to install or refresh the menu, or clear it to remove the menu, then save. Errors appear under **Log**.

Settings remain in `%APPDATA%\Comprimer\settings.json`. Existing C# settings, encoder paths, language, quality values, and per-size switches are read directly. Old Auto switches are migrated without enabling previously disabled shortcuts. Explicitly cleared or missing encoder paths never silently fall back to PATH. **Detect** updates the selected path; **Browse** opens a native file picker.

| Source | Individual actions |
| --- | --- |
| JPG / JPEG | Resize, optimize with mozjpeg, convert to WebP |
| PNG | Resize, optimize with pngquant, convert to JPG or WebP |
| WebP | Resize |

Auto is available for every source format, independently of those switches. Each size has separate **Auto** and **Resize** controls. Sizes can limit the longest side, width, or height; small images are never enlarged. Resize skips images that already fit.

## Auto and quality

Auto encodes PNG, JPG, and WebP at the same dimensions and compares actual file sizes:

- PNG smaller than JPG: keep **PNG only**.
- PNG equal to or larger than JPG: keep **JPG and WebP**.

Defaults are PNG minimum/target **65–80**, JPG **85**, and WebP **80**. If pngquant cannot meet the quality floor, the lossless PNG is retained. JPG composites transparency on white; PNG and WebP preserve it. Quality controls apply to Auto, resizing, optimization, and conversion. PNG/JPG resizing can use built-in encoders when the external tool is missing. Auto requires all three external encoders.

| Executable | Purpose |
| --- | --- |
| `pngquant.exe` | PNG optimization |
| `cjpeg.exe` (mozjpeg) | JPG encoding |
| `cwebp.exe` (libwebp) | WebP encoding |
| `dwebp.exe` (libwebp) | Decode WebP sources |

`dwebp` detection checks beside the selected `cwebp`, then PATH. Animated WebP is unsupported. External tools are not bundled.

## Output files

Outputs stay beside the source. Without overwrite:

- Resize: `photo-1024px.jpg`
- Conversion: `photo.webp`
- PNG / JPG optimization: `photo-fs8.png` / `photo-moz.jpg`
- Auto with resize: `photo-1024px.png`, or `photo-1024px.jpg` and `photo-1024px.webp`
- Auto at original size: `photo.png`, or `photo.jpg` and `photo.webp`

Collisions, including the source itself, receive `-1`, `-2`, etc. With overwrite enabled, outputs use the original basename without the resize suffix; `.jpeg` sources keep that extension when JPG replaces them. Files belonging to unselected formats are kept. Candidates are staged before publication; encoder errors leave originals untouched, and publication failures roll back earlier outputs. A process crash or power loss during a multi-file publication is not a filesystem transaction.

## Command line and debugging

Explorer uses the same commands as before:

```powershell
.\Comprimer.exe --auto "C:\Images\photo.png"
.\Comprimer.exe --auto 1024 "C:\Images\photo.png"
.\Comprimer.exe --downscale 512 "C:\Images\photo.jpg"
.\Comprimer.exe --to-webp "C:\Images\photo.png"
.\Comprimer.exe --to-jpg "C:\Images\photo.png"
.\Comprimer.exe --pngquant "C:\Images\photo.png"
.\Comprimer.exe --mozjpeg "C:\Images\photo.jpg"
.\Comprimer.exe --toggle-overwrite
.\Comprimer.exe --diagnostics
```

Release builds have no console window. Failures return a nonzero exit code and log a reason to `%APPDATA%\Comprimer\comprimer.log`, including encoder stderr and timeouts. Logs rotate during writes at 2 MiB and retain two backups. A process lock keeps concurrent Explorer commands from mixing records or racing rotation. Records include process IDs; panic reports include backtraces even when `RUST_LOG=off`.

If the normal log cannot be written, logging falls back to `%TEMP%\Comprimer\logs-…`; if both locations fail, a bounded in-memory buffer keeps recent activity while the app remains usable. **Log** reports the fallback, **Folder** opens its location, and **Copy** includes it in diagnostics. Recent log reads, individual records, and encoder stderr are bounded. Use **Log → Refresh** for recent activity. Logs contain local paths; review reports before sharing them.

Debug builds also write to the terminal:

```powershell
$env:RUST_LOG = 'comprimer=debug'
$env:RUST_BACKTRACE = '1'
cargo run
cargo run -- --auto 1024 "C:\Images\photo.png"
```

To test with separate preferences and logs, set `$env:COMPRIMER_CONFIG = "$PWD\out\settings.json"`. This changes the config/log location; Explorer registration still targets your normal per-user menu. Automated registry tests use separate temporary registry keys.

## Build and test

Install Rust **1.95+** with the MSVC toolchain and Visual Studio Build Tools (**Desktop development with C++**, including the Windows SDK). From a Windows terminal:

```powershell
cargo fmt --all -- --check
cargo clippy --locked --all-targets -- -D warnings
cargo test --locked --all-targets
cargo build --locked --release
```

The executable is `target\release\Comprimer.exe`; matching debug symbols are in `Comprimer.pdb`. Use a Rust-capable debugger such as CodeLLDB or the Visual Studio debugger to set breakpoints in a debug build.

Unit and regression tests cover settings compatibility, explicit saving, CLI validation, quality, Auto selection, dimensions, collisions, encoder failures, publication rollback, isolated Explorer registration, and egui rendering in both languages/themes. Logging tests cover rotation, concurrent processes, fallback, bounded buffers, and panic reports. Run the real encoder integration suite after installing all four tools on PATH:

```powershell
cargo test --locked --test real_encoders -- --ignored
```

One GitHub Actions workflow runs formatting, Clippy, tests, and a release build on Windows. CI uploads the executable and symbols separately; `v*` tags publish those same artifacts as a release ZIP and a symbols ZIP. Real encoder tests are opt-in and do not run in CI.

For an isolated native UI screenshot (no settings or Explorer changes), use `cargo run --example ui_snapshot -- main en dark 820 570 out/main.png`. Panels are `main`, `tools`, and `log`; choose `en`/`fr` and `dark`/`light`.

## Code layout

`src/ui.rs` owns the egui settings UI. `settings.rs`/`config.rs` handle compatible JSON persistence; `registry.rs` generates and installs Explorer menus; `images.rs` stages and processes images; `tools.rs` runs encoders; `cli.rs` parses commands; `diagnostics.rs` sets up logging. Image processing and registry behavior are testable without launching a window.

## License

MIT
