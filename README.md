# Comprimer

Compress, resize, and convert images from Windows Explorer. Image actions run quietly; open `Comprimer.exe` to change settings.

For Windows 10/11, x64. Written in Rust; no .NET runtime needed.

## Setup

1. Download the Windows ZIP from [Releases](../../releases) and extract it to a permanent folder.
2. Open `Comprimer.exe`, then **Tools** to detect or select the programs below.
3. Enable **Explorer** and click **Save**. No administrator access is needed.

| Program | Used for |
| --- | --- |
| `pngquant.exe` | Compress PNG |
| `cjpeg.exe` from mozjpeg | Create JPG |
| `cwebp.exe` from libwebp | Create WebP |
| `dwebp.exe` from libwebp | Read WebP |

These programs are installed separately. Auto needs the first three; WebP inputs also need `dwebp`. Animated WebP is unsupported.

On Windows 11, the menu is under **Show more options**. If you move Comprimer, update its path in **Tools** and save again.

## Everyday use

Right-click a PNG, JPG/JPEG, or WebP image and choose **Auto**, a resize preset, or an available format action.

- **Auto** chooses which formats to keep, with optional resizing.
- **Resize** limits the longest side, width, or height. It never enlarges an image and skips images that already fit.
- **Format actions** compress PNG/JPG or convert to JPG/WebP, depending on the source format.

Open Comprimer to change sizes, quality, menu actions, or overwrite behavior. **Save** or **Ctrl+S** applies changes; closing without saving discards them. Clear **Explorer** and save to remove the menu.

## How Auto works

PNG, JPG, and WebP are processed at the same time. Finished files appear as they become ready, then Auto compares the PNG and JPG sizes:

- Within **5% of the smaller file**: keep **PNG, JPG, and WebP**.
- Otherwise, if PNG is smaller: keep **PNG only**.
- Otherwise: keep **JPG and WebP**.

Early files may be replaced or removed as the comparison finishes. Outputs always have the requested dimensions.

Auto targets a first file within **1 second** and a **5-second** total budget. At the deadline it stops unfinished encoders and keeps available results. Large images or slow disks can exceed these targets; the log reports incomplete verification.

Default quality: PNG **65–80** (minimum–target), JPG **85**, WebP **80**. PNG falls back to a lossless copy when needed to meet its minimum quality. JPG replaces transparency with white; PNG and WebP preserve it.

## Files and logs

Outputs go beside the source. **Overwrite is off by default**: resized names include the size, such as `photo-1024px.jpg`, and name conflicts get `-1`, `-2`, and so on. With overwrite enabled, outputs reuse the original basename and can replace existing files. Auto restores replaced files for formats it later drops.

Temporary work uses the system temp folder, falling back to the image folder only if necessary. Temporary files are cleaned up when processing ends.

Settings are stored in `%APPDATA%\Comprimer\settings.json`; settings from the earlier C# version remain compatible. For problems, open **Log** in the app or check `%APPDATA%\Comprimer\comprimer.log`. The app shows an alternate log location if that folder is unavailable.

## Command line

Image commands run without opening the settings window. The optional `--headless` prefix makes this explicit; older commands still work.

```powershell
.\Comprimer.exe --headless --auto "photo.png"
.\Comprimer.exe --auto 1024 "photo.png"
.\Comprimer.exe --downscale 512 "photo.jpg"
.\Comprimer.exe --to-webp "photo.png"
.\Comprimer.exe --to-jpg "photo.png"
.\Comprimer.exe --pngquant "photo.png"
.\Comprimer.exe --mozjpeg "photo.jpg"
.\Comprimer.exe --toggle-overwrite
.\Comprimer.exe --diagnostics
```

## Development

Use Windows, Rust through rustup, and Visual Studio Build Tools with **Desktop development with C++** and the Windows SDK. The repository pins the Rust toolchain.

```powershell
cargo fmt --all -- --check
cargo clippy --locked --all-targets -- -D warnings
cargo test --locked --all-targets
cargo build --locked --release
```

The build produces `target\release\Comprimer.exe` and a separate `Comprimer.pdb` for debugging.

Optional checks:

- Installed encoders on PATH: `cargo test --locked --test real_encoders -- --ignored`
- CI scripts: `node --test scripts/ci.test.mjs`

For separate test settings and logs, set `$env:COMPRIMER_CONFIG = "$PWD\out\settings.json"`. This does not isolate Explorer registration. For debug output, set `$env:RUST_LOG = 'comprimer=debug'` and run `cargo run`.

See the [CI workflow](.github/workflows/ci.yml) for checks and packaging. Version tags create draft releases; publishing is manual.

## License

MIT
