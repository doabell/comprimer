using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Comprimer.Models;

namespace Comprimer.Services;

/// <summary>
/// Handles image downscaling and format conversion operations.
/// </summary>
public sealed class ImageService
{
    private const int MaxAutoIncrementRetries = 1000;

    private readonly ExecutableService _exe;
    private readonly AppSettings _settings;

    public ImageService(ExecutableService exe, AppSettings settings)
    {
        _exe = exe;
        _settings = settings;
    }

    /// <summary>
    /// Downscale an image so its target dimension is at most <paramref name="maxSize"/> pixels.
    /// The target dimension depends on <see cref="AppSettings.DownscaleMode"/>.
    /// Does nothing if the image is already smaller.
    /// </summary>
    public bool Downscale(string inputPath, int maxSize)
    {
        var outputPath = GetOutputPath(inputPath, $"-{maxSize}px");
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();

        using var original = new Bitmap(inputPath);

        // Determine if downscale is needed based on mode
        bool needsDownscale = _settings.DownscaleMode switch
        {
            DownscaleMode.Width => original.Width > maxSize,
            DownscaleMode.Height => original.Height > maxSize,
            _ => original.Width > maxSize || original.Height > maxSize, // LongestSide
        };

        if (!needsDownscale)
            return true;

        // Calculate new dimensions based on mode
        double ratio = _settings.DownscaleMode switch
        {
            DownscaleMode.Width => (double)maxSize / original.Width,
            DownscaleMode.Height => (double)maxSize / original.Height,
            _ => Math.Min((double)maxSize / original.Width, (double)maxSize / original.Height),
        };

        int newWidth = (int)(original.Width * ratio);
        int newHeight = (int)(original.Height * ratio);

        using var resized = new Bitmap(newWidth, newHeight);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(original, 0, 0, newWidth, newHeight);
        }

        if (ext == ".webp")
        {
            // System.Drawing can't save WebP — save temp PNG then use cwebp
            var cwebp = _exe.Resolve("cwebp", _settings.Executables.Img2WebPPath);
            if (cwebp == null) return false;

            var tempPath = Path.Combine(Path.GetTempPath(), $"comprimer-{Guid.NewGuid():N}.png");
            try
            {
                resized.Save(tempPath, ImageFormat.Png);
                return _exe.Run(cwebp, $"-q 80 \"{tempPath}\" -o \"{outputPath}\"");
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        }

        var format = ext == ".png" ? ImageFormat.Png : ImageFormat.Jpeg;
        resized.Save(outputPath, format);
        return true;
    }

    /// <summary>
    /// Convert an image to WebP format using cwebp.
    /// </summary>
    public bool ConvertToWebP(string inputPath)
    {
        var outputPath = GetConversionOutputPath(inputPath, ".webp");
        var cwebp = _exe.Resolve("cwebp", _settings.Executables.Img2WebPPath);

        if (cwebp == null)
            return false;

        return _exe.Run(cwebp, $"-q 80 \"{inputPath}\" -o \"{outputPath}\"");
    }

    /// <summary>
    /// Convert a PNG to JPEG using mozjpeg's cjpeg if available.
    /// </summary>
    public bool ConvertToJpg(string inputPath)
    {
        var outputPath = GetConversionOutputPath(inputPath, ".jpg");
        var cjpeg = _exe.Resolve("cjpeg", _settings.Executables.CjpegPath);

        if (cjpeg == null)
            return false;

        return _exe.Run(cjpeg, $"-quality 85 -outfile \"{outputPath}\" \"{inputPath}\"");
    }

    /// <summary>
    /// Optimize a PNG using pngquant. Output suffix: -fs8.
    /// </summary>
    public bool OptimizePng(string inputPath)
    {
        var pngquant = _exe.Resolve("pngquant", _settings.Executables.PngquantPath);
        if (pngquant == null)
            return false;

        if (_settings.OverwriteOriginal)
        {
            return _exe.Run(pngquant, $"--force --ext .png --quality=65-80 \"{inputPath}\"");
        }

        var outputPath = GetOutputPath(inputPath, "-fs8");
        return _exe.Run(pngquant, $"--quality=65-80 -o \"{outputPath}\" \"{inputPath}\"");
    }

    /// <summary>
    /// Optimize a JPEG using mozjpeg's cjpeg. Output suffix: -moz.
    /// </summary>
    public bool OptimizeJpg(string inputPath)
    {
        var cjpeg = _exe.Resolve("cjpeg", _settings.Executables.CjpegPath);
        if (cjpeg == null)
            return false;

        var outputPath = GetOutputPath(inputPath, "-moz");
        return _exe.Run(cjpeg, $"-quality 85 -outfile \"{outputPath}\" \"{inputPath}\"");
    }

    /// <summary>
    /// Gets the output file path for downscale operations.
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

        for (int i = 1; i < MaxAutoIncrementRetries; i++)
        {
            candidate = Path.Combine(dir, $"{name}{suffix}-{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return candidate;
    }

    /// <summary>
    /// Gets the output file path for format conversion.
    /// Tries the plain filename first, then adds -1, -2 only if it already exists.
    /// </summary>
    internal string GetConversionOutputPath(string inputPath, string newExtension)
    {
        if (_settings.OverwriteOriginal)
            return Path.ChangeExtension(inputPath, newExtension);

        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);

        var candidate = Path.Combine(dir, $"{name}{newExtension}");
        if (!File.Exists(candidate))
            return candidate;

        for (int i = 1; i < MaxAutoIncrementRetries; i++)
        {
            candidate = Path.Combine(dir, $"{name}-{i}{newExtension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return candidate;
    }
}
