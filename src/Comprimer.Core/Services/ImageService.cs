using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Comprimer.Models;

namespace Comprimer.Services;

/// <summary>
/// Handles image downscaling and format conversion operations.
/// </summary>
public sealed class ImageService
{
    private const string ConversionSuffix = "-1";
    private const int MaxAutoIncrementRetries = 1000;

    private readonly ExecutableService _exe;
    private readonly AppSettings _settings;

    public ImageService(ExecutableService exe, AppSettings settings)
    {
        _exe = exe;
        _settings = settings;
    }

    /// <summary>
    /// Downscale an image so its longest side is at most <paramref name="maxSize"/> pixels.
    /// Does nothing if the image is already smaller.
    /// </summary>
    public bool Downscale(string inputPath, int maxSize)
    {
        var outputPath = GetOutputPath(inputPath, $"-{maxSize}px");
        using var image = Image.Load(inputPath);

        if (image.Width <= maxSize && image.Height <= maxSize)
            return true; // already small enough

        var options = new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxSize, maxSize),
        };
        image.Mutate(x => x.Resize(options));
        image.Save(outputPath);
        return true;
    }

    /// <summary>
    /// Convert an image to WebP format using img2webp/cwebp.
    /// Falls back to ImageSharp if no external tool is available.
    /// </summary>
    public bool ConvertToWebP(string inputPath)
    {
        var outputPath = GetOutputPath(Path.ChangeExtension(inputPath, ".webp"), ConversionSuffix);
        var cwebp = _exe.Resolve("cwebp", _settings.Executables.Img2WebPPath);

        if (cwebp != null)
        {
            return _exe.Run(cwebp, $"-q 80 \"{inputPath}\" -o \"{outputPath}\"");
        }

        // Fallback: use ImageSharp to save as WebP
        using var image = Image.Load(inputPath);
        image.SaveAsWebp(outputPath);
        return true;
    }

    /// <summary>
    /// Convert a PNG to JPEG using mozjpeg's cjpeg if available, otherwise ImageSharp.
    /// </summary>
    public bool ConvertToJpg(string inputPath)
    {
        var outputPath = GetOutputPath(Path.ChangeExtension(inputPath, ".jpg"), ConversionSuffix);
        var cjpeg = _exe.Resolve("cjpeg", _settings.Executables.CjpegPath);

        if (cjpeg != null)
        {
            return _exe.Run(cjpeg, $"-quality 85 -outfile \"{outputPath}\" \"{inputPath}\"");
        }

        // Fallback: use ImageSharp
        using var image = Image.Load(inputPath);
        image.SaveAsJpeg(outputPath);
        return true;
    }

    /// <summary>
    /// Optimize a PNG using pngquant.
    /// </summary>
    public bool OptimizePng(string inputPath)
    {
        var pngquant = _exe.Resolve("pngquant", _settings.Executables.PngquantPath);
        if (pngquant == null)
            return false;

        var outputPath = _settings.OverwriteOriginal
            ? inputPath
            : GetOutputPath(inputPath, "-opt");

        if (_settings.OverwriteOriginal)
        {
            return _exe.Run(pngquant, $"--force --ext .png --quality=65-80 \"{inputPath}\"");
        }
        else
        {
            return _exe.Run(pngquant, $"--quality=65-80 -o \"{outputPath}\" \"{inputPath}\"");
        }
    }

    /// <summary>
    /// Gets the output file path, respecting overwrite settings.
    /// When not overwriting, appends a suffix and auto-increments to avoid collisions.
    /// </summary>
    internal string GetOutputPath(string inputPath, string suffix)
    {
        if (_settings.OverwriteOriginal)
            return inputPath;

        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var ext = Path.GetExtension(inputPath);

        var candidate = Path.Combine(dir, $"{name}{suffix}{ext}");
        if (!File.Exists(candidate))
            return candidate;

        // Auto-increment: -1, -2, -3, ...
        for (int i = 1; i < MaxAutoIncrementRetries; i++)
        {
            candidate = Path.Combine(dir, $"{name}{suffix}-{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return candidate;
    }
}
