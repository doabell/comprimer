# Comprimer

A Windows 10/11 right-click context menu tool for image compression and conversion. No popup windows or terminal — operations run silently in the background.

## Features

- **Right-click context menu** for JPG, JPEG, PNG, and WebP files
- **Downscale** images to configurable sizes (defaults: 512px, 1024px)
- **Downscale mode**: limit by longest side, width only, or height only
- **Auto mode**: encode PNG, JPG, and WebP, then keep PNG alone if smaller than JPG; otherwise keep JPG and WebP
- **Per-encoder quality**: PNG minimum/target quality, JPG quality, and WebP quality
- **Convert to WebP** from JPG/PNG using cwebp
- **Convert to JPG** from PNG using mozjpeg (cjpeg)
- **One settings page** with all Explorer shortcuts together, encoder paths and quality, scrolling, and English/French labels
- **Explicit executable paths** for Comprimer and every encoder, with separate Detect and Browse buttons
- **Nested or flat** context menu layout
- **Overwrite or suffix** mode (toggle from context menu or settings)
- **Single executable** — no DLLs to manage

## Requirements

- Windows 10 or later
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)

## Installation

1. Download the latest release from [Releases](../../releases)
2. Extract to a permanent location (e.g., `C:\Program Files\Comprimer\`)
3. Run `Comprimer.exe` to open settings
4. Click **Add to Explorer** to register the context menu

## Settings

| Format    | Operations Available                        |
|-----------|---------------------------------------------|
| JPG/JPEG  | Downscale, Convert to WebP                  |
| PNG       | Downscale, Convert to WebP, Convert to JPG  |
| WebP      | Downscale                                   |

Auto is available for all source formats, independently of individual action toggles. Choose **Auto · original size** to compress without resizing, or **Auto + resize 1024px** (and any other configured size) to resize and select output formats together. **Resize only 1024px** keeps the source format. Both actions use the presets and dimension setting under **Sizes (Auto + Resize)**, and never enlarge small images.

Each shortcut has its own switch: **Auto (original size)**, plus separate **Auto + resize** and **Resize only** checkboxes for every size. Disabling Auto at original size leaves the resized Auto shortcuts available. **Resize sources** selects the source formats for Resize only; **Format actions** controls individual optimization and conversion shortcuts. Saved presets and switches survive language changes. Older settings keep their existing enabled shortcuts when upgraded.

### Auto selection and quality

Auto encodes all three candidates at the same dimensions and compares their actual byte sizes:

- PNG smaller than JPG: keep **PNG only**.
- PNG equal to or larger than JPG: keep **JPG and WebP**.

Set quality under **Encoders & quality**, next to each encoder's path. All quality values range from 0 to 100; lower values generally produce smaller files. Defaults are PNG **65–80**, JPG **85**, and WebP **80**. PNG exposes a minimum and target: if pngquant cannot meet the minimum, the lossless PNG candidate is used. JPG places transparent pixels on white; PNG and WebP preserve transparency.

These settings apply to Auto, individual conversions, optimization, and resizing. PNG/JPG resizing can still use the built-in encoder when the external tool is missing (PNG stays lossless; JPG uses the selected quality). Auto requires all three external encoders. WebP source images additionally require `dwebp`; animated WebP is unsupported.

Manage Auto, individual format actions, resize presets, menu style, overwrite behavior, and the Comprimer command path together under **Explorer shortcuts**. Use **Save changes** to save preferences and refresh an installed Explorer menu. Changes also save when the window closes. Saving preferences does not install the menu; use **Add to Explorer** once.

The **Comprimer executable** field controls the path used by every shortcut. Type a path or use **Browse…** to choose a copy. **Detect** selects the currently running executable. A saved choice is preserved when opening another copy, saving settings, or refreshing shortcuts; invalid paths must be corrected before updating an installed menu. Existing registrations are recognized for JPG, JPEG, PNG, and WebP, in both flat and nested menus.

- **Nested menu**: Groups all operations under a "Comprimer" submenu
- **Overwrite original**: Replaces the original file instead of creating a suffixed copy
- **Downscale sizes**: Add/remove target sizes (e.g., 512px, 1024px)
- **Downscale mode**: Longest side (default), width only, or height only

### External Tools

Every tool shows its current full path, availability, **Detect**, and **Browse…**. Type or browse to choose an executable; Detect searches PATH and fills in the result without saving immediately. Saved paths remain fixed. Missing or explicitly cleared paths do not silently fall back to another executable.

When upgrading old settings that have no explicit paths, the form fills in the previously registered Comprimer path and detected encoder paths once. Save to keep these choices. Older settings used directly from Explorer retain PATH lookup until explicit paths are saved.

| Tool     | Purpose                    |
|----------|----------------------------|
| pngquant | PNG optimization           |
| cwebp    | WebP encoding              |
| cjpeg    | JPEG encoding via mozjpeg  |
| dwebp    | Decode WebP source images  |

For `dwebp`, **Detect** first checks beside the selected `cwebp` executable, then PATH.

### Output Naming

When **Overwrite original** is off:
- Downscale: `photo-1024px.jpg`
- Convert: `photo.webp` (adds `-1`, `-2` only if file exists)
- Auto with resize: `photo-1024px.png`, or `photo-1024px.jpg` and `photo-1024px.webp`
- Auto at original size: `photo.png`, or `photo.jpg` and `photo.webp` (adds `-1`, `-2` for existing names, including the source)

All outputs remain in the source folder. Resize collisions also increment: `photo-1024px-1.jpg`. With overwrite enabled, selected outputs use the original basename without a resize suffix; an existing `.jpeg` source keeps its extension when JPG wins. Source files of other formats and existing files for discarded formats are kept. Candidates are encoded in a temporary directory, and encoder failures leave the source and existing outputs untouched.

Silent command-line equivalents:

```powershell
Comprimer.exe --auto "C:\Images\photo.png"
Comprimer.exe --auto 1024 "C:\Images\photo.png"
```

## Building

```bash
dotnet restore
dotnet build --configuration Release
dotnet publish src/Comprimer/Comprimer.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true
```

## Development

- .NET 8.0 SDK
- Windows Forms (Windows only)
- System.Drawing.Common for image processing

```bash
dotnet test
```

Tests cover Auto selection, resize dimensions, quality settings, naming collisions, failure cleanup, overwrite rollback, and explicit path persistence and selection. Encoder integration tests also run when `pngquant`, `cjpeg`, `cwebp`, and `dwebp` are on PATH; otherwise those tests are skipped.

## License

MIT
