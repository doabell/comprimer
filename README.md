# Comprimer

A Windows 10/11 right-click context menu tool for image compression and conversion. No popup windows or terminal — operations run silently in the background.

## Features

- **Right-click context menu** for JPG, JPEG, PNG, and WebP files
- **Downscale** images to configurable sizes (default 1000px) — only shrinks if larger
- **Convert to WebP** from JPG/PNG using cwebp
- **Convert to JPG** from PNG using mozjpeg (cjpeg)
- **Settings GUI** to configure operations, menu style, and executable paths
- **Nested or flat** context menu layout
- **Overwrite or suffix** mode (toggle from context menu or settings)

## Installation

1. Download the latest release from [Releases](../../releases)
2. Extract to a permanent location (e.g., `C:\Program Files\Comprimer\`)
3. Run `Comprimer.exe` to open settings
4. Configure your preferences and click **Add to Explorer**

## Settings

### Operations Tab

| Format    | Operations Available                        |
|-----------|---------------------------------------------|
| JPG/JPEG  | Downscale, Convert to WebP                  |
| PNG       | Downscale, Convert to WebP, Convert to JPG  |
| WebP      | Downscale                                   |

- **Nested menu**: Groups all operations under a "Comprimer" submenu
- **Overwrite original**: Replaces the original file instead of creating a suffixed copy
- **Downscale sizes**: Add/remove target sizes (e.g., 1000px, 800px, 500px)

### Executables Tab

External tools for optimized encoding. If detected in PATH, they're used automatically.

| Tool     | Purpose                    |
|----------|----------------------------|
| pngquant | PNG optimization           |
| cwebp    | WebP encoding              |
| cjpeg    | JPEG encoding via mozjpeg  |

If a tool isn't in PATH, provide the full path in settings.

### Output Naming

When **Overwrite original** is off:
- Downscale: `photo-1000px.jpg`
- Convert: `photo-1.webp`, `photo-2.webp`, ...

## Building

```bash
dotnet restore
dotnet build --configuration Release
dotnet publish src/Comprimer/Comprimer.csproj -c Release -r win-x64 --self-contained
```

## Development

- .NET 8.0 SDK
- Windows Forms (Windows only)
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) for image processing

```bash
dotnet test
```

## License

MIT
