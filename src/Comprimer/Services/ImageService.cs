using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Comprimer.Models;

namespace Comprimer.Services;

/// <summary>Resizes and encodes images, publishing completed outputs beside the source.</summary>
public sealed class ImageService
{
    private const int MaxAutoIncrementRetries = 1000;
    private readonly IExecutableService _exe;
    private readonly AppSettings _settings;

    public ImageService(IExecutableService exe, AppSettings settings)
    {
        _exe = exe;
        _settings = settings;
    }

    public bool Downscale(string inputPath, int maxSize)
    {
        if (maxSize <= 0) return false;
        return InWorkspace(work =>
        {
            using var original = LoadBitmap(inputPath, work);
            if (TargetDimension(original) <= maxSize) return true;
            using var resized = Resize(original, maxSize);
            var extension = Path.GetExtension(inputPath).ToLowerInvariant();
            var candidate = Path.Combine(work, "output" + extension);
            if (!Encode(resized, candidate, extension, work, requireTool: false)) return false;
            Publish([(candidate, GetOutputPath(inputPath, $"-{maxSize}px"))], work);
            return true;
        });
    }

    /// <summary>
    /// Keeps PNG only if it is strictly smaller than JPG; otherwise keeps JPG and WebP.
    /// A null size compresses at original dimensions. Images are never enlarged.
    /// </summary>
    public bool Auto(string inputPath, int? maxSize = null)
    {
        if (maxSize is <= 0) return false;
        return InWorkspace(work =>
        {
            if (ResolveEncoder(".png") == null || ResolveEncoder(".jpg") == null ||
                ResolveEncoder(".webp") == null) return false;
            using var original = LoadBitmap(inputPath, work);
            using var resized = Resize(original, maxSize);
            var png = Path.Combine(work, "candidate.png");
            var jpg = Path.Combine(work, "candidate.jpg");
            var webp = Path.Combine(work, "candidate.webp");
            if (!Encode(resized, png, ".png", work) ||
                !Encode(resized, jpg, ".jpg", work) ||
                !Encode(resized, webp, ".webp", work)) return false;
            var selected = new FileInfo(png).Length < new FileInfo(jpg).Length
                ? new[] { png } : new[] { jpg, webp };
            var outputs = selected.Select(path =>
                (path, GetAutoOutputPath(inputPath, Path.GetExtension(path), maxSize))).ToArray();
            Publish(outputs, work);
            return true;
        });
    }

    public bool ConvertToWebP(string inputPath) => Convert(inputPath, ".webp", null);
    public bool ConvertToJpg(string inputPath) => Convert(inputPath, ".jpg", null);
    public bool OptimizePng(string inputPath) => Convert(inputPath, ".png", "-fs8");
    public bool OptimizeJpg(string inputPath) => Convert(inputPath, ".jpg", "-moz");

    private bool Convert(string inputPath, string extension, string? suffix) => InWorkspace(work =>
    {
        using var bitmap = LoadBitmap(inputPath, work);
        var candidate = Path.Combine(work, "output" + extension);
        if (!Encode(bitmap, candidate, extension, work)) return false;
        var output = suffix == null ? GetConversionOutputPath(inputPath, extension) : GetOutputPath(inputPath, suffix);
        Publish([(candidate, output)], work);
        return true;
    });

    private string? ResolveEncoder(string extension) => extension switch
    {
        ".png" => _exe.Resolve("pngquant", _settings.Executables.PngquantPath),
        ".jpg" or ".jpeg" => _exe.Resolve("cjpeg", _settings.Executables.CjpegPath),
        ".webp" => _exe.Resolve("cwebp", _settings.Executables.Img2WebPPath),
        _ => null,
    };

    private Bitmap LoadBitmap(string inputPath, string work)
    {
        var decodedPath = inputPath;
        if (Path.GetExtension(inputPath).Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            var configuredDecoder = _settings.Executables.DwebpPath;
            if (configuredDecoder == null &&
                !string.IsNullOrWhiteSpace(_settings.Executables.Img2WebPPath))
            {
                var sibling = Path.Combine(Path.GetDirectoryName(
                    Path.GetFullPath(_settings.Executables.Img2WebPPath.Trim().Trim('"')))!, "dwebp.exe");
                if (File.Exists(sibling)) configuredDecoder = sibling;
            }
            var decoder = _exe.Resolve("dwebp", configuredDecoder);
            decodedPath = Path.Combine(work, "decoded.png");
            if (decoder == null || _exe.Run(decoder, $"\"{inputPath}\" -o \"{decodedPath}\"") != 0)
                throw new IOException("WebP decoding failed. Install dwebp or configure its path.");
        }
        // Release the source handle before publishing, including when overwriting.
        using var image = new Bitmap(decodedPath);
        return new Bitmap(image);
    }

    private int TargetDimension(Bitmap bitmap) => _settings.DownscaleMode switch
    {
        DownscaleMode.Width => bitmap.Width,
        DownscaleMode.Height => bitmap.Height,
        _ => Math.Max(bitmap.Width, bitmap.Height),
    };

    private Bitmap Resize(Bitmap original, int? maxSize)
    {
        double ratio = maxSize.HasValue ? Math.Min(1d, (double)maxSize.Value / TargetDimension(original)) : 1d;
        int width = Math.Max(1, (int)(original.Width * ratio));
        int height = Math.Max(1, (int)(original.Height * ratio));
        if (width == original.Width && height == original.Height) return new Bitmap(original);
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(original, new Rectangle(0, 0, width, height),
            0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
        return resized;
    }

    private bool Encode(Bitmap bitmap, string output, string extension, string work, bool requireTool = true)
    {
        var encoder = ResolveEncoder(extension);
        var quality = _settings.Encoders;
        if (extension is ".jpg" or ".jpeg")
        {
            // cjpeg reads BMP, not JPEG. Flatten transparency on white consistently.
            using var opaque = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(opaque))
            {
                graphics.Clear(Color.White);
                graphics.DrawImageUnscaled(bitmap, 0, 0);
            }
            if (encoder == null)
            {
                if (requireTool) return false;
                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality.JpgQuality);
                opaque.Save(output, codec, parameters);
                return true;
            }
            var bmp = Path.Combine(work, "encoder-input.bmp");
            opaque.Save(bmp, ImageFormat.Bmp);
            return _exe.Run(encoder, $"-quality {quality.JpgQuality} -outfile \"{output}\" \"{bmp}\"") == 0 && HasOutput(output);
        }
        var png = Path.Combine(work, "encoder-input.png");
        bitmap.Save(png, ImageFormat.Png);
        if (extension == ".png")
        {
            if (encoder == null)
            {
                if (requireTool) return false;
                File.Copy(png, output);
                return true;
            }
            var result = _exe.Run(encoder, $"--quality={quality.EffectivePngMinQuality}-{quality.PngQuality} --output \"{output}\" -- \"{png}\"");
            // If pngquant cannot meet the quality floor, retain the lossless candidate.
            if (result == 99)
            {
                File.Copy(png, output, overwrite: true);
                return true;
            }
            return result == 0 && HasOutput(output);
        }
        return extension == ".webp" && encoder != null &&
            _exe.Run(encoder, $"-q {quality.WebPQuality} \"{png}\" -o \"{output}\"") == 0 && HasOutput(output);
    }

    private static bool HasOutput(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    private static bool InWorkspace(Func<string, bool> action)
    {
        var work = Path.Combine(Path.GetTempPath(), $"comprimer-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(work);
            return action(work);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
        {
            return false;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void Publish((string Source, string Destination)[] outputs, string work)
    {
        var published = new List<(string Destination, string? Backup)>();
        try
        {
            foreach (var (source, destination) in outputs)
            {
                string? backup = null;
                if (_settings.OverwriteOriginal && File.Exists(destination))
                {
                    backup = Path.Combine(work, $"backup-{published.Count}");
                    File.Copy(destination, backup);
                }
                File.Move(source, destination, overwrite: _settings.OverwriteOriginal);
                published.Add((destination, backup));
            }
        }
        catch
        {
            foreach (var (destination, backup) in published.AsEnumerable().Reverse())
            {
                if (backup == null) File.Delete(destination);
                else File.Copy(backup, destination, overwrite: true);
            }
            throw;
        }
    }

    internal string GetAutoOutputPath(string inputPath, string extension, int? maxSize)
    {
        if (_settings.OverwriteOriginal)
        {
            var inputExtension = Path.GetExtension(inputPath);
            if (inputExtension.Equals(extension, StringComparison.OrdinalIgnoreCase) ||
                (extension == ".jpg" && inputExtension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))) return inputPath;
            return Path.ChangeExtension(inputPath, extension);
        }
        return UniquePath(inputPath, maxSize.HasValue ? $"-{maxSize}px" : "", extension);
    }

    internal string GetOutputPath(string inputPath, string suffix) => _settings.OverwriteOriginal
        ? inputPath : UniquePath(inputPath, suffix, Path.GetExtension(inputPath));

    internal string GetConversionOutputPath(string inputPath, string newExtension) => _settings.OverwriteOriginal
        ? Path.ChangeExtension(inputPath, newExtension) : UniquePath(inputPath, "", newExtension);

    private static string UniquePath(string inputPath, string suffix, string extension)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputPath);
        for (int i = 0; i < MaxAutoIncrementRetries; i++)
        {
            var increment = i == 0 ? "" : $"-{i}";
            var candidate = Path.Combine(dir, $"{name}{suffix}{increment}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("No unused output filename is available.");
    }
}
