using System.Drawing;
using System.Drawing.Imaging;
using Comprimer.Models;
using Comprimer.Services;
using Xunit;

namespace Comprimer.Tests;

/// <summary>Runs when the external tools are installed; regular unit tests do not require them.</summary>
public sealed class EncoderIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"comprimer-encoders-{Guid.NewGuid():N}");
    private readonly ExecutableService _tools = new();
    private readonly AppSettings _settings = new() { Encoders = new EncoderSettings { PngMinQuality = 0 } };

    public EncoderIntegrationTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private string CreateImage(string name, bool noisy)
    {
        var path = Path.Combine(_directory, name);
        using var image = new Bitmap(256, 128);
        if (noisy)
        {
            var random = new Random(42);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    image.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
        }
        else
        {
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.Transparent);
            graphics.FillRectangle(Brushes.CornflowerBlue, 20, 20, 80, 60);
        }
        image.Save(path, Path.GetExtension(name) == ".jpg" ? ImageFormat.Jpeg : ImageFormat.Png);
        return path;
    }

    [EncoderFact]
    public void RealAuto_FlatGraphicKeepsPngAndTransparency()
    {
        var source = CreateImage("graphic.png", false);
        var original = File.ReadAllBytes(source);
        Assert.True(new ImageService(_tools, _settings).Auto(source, 128));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
        using var output = new Bitmap(Path.Combine(_directory, "graphic-128px.png"));
        Assert.Equal(new Size(128, 64), output.Size);
        Assert.Equal(0, output.GetPixel(0, 0).A);
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [EncoderFact]
    public void RealAuto_DetailedImageKeepsValidJpgAndWebP()
    {
        var source = CreateImage("detail.png", true);
        Assert.True(new ImageService(_tools, _settings).Auto(source, 128));
        Assert.False(File.Exists(Path.Combine(_directory, "detail-128px.png")));
        using var jpg = new Bitmap(Path.Combine(_directory, "detail-128px.jpg"));
        Assert.Equal(new Size(128, 64), jpg.Size);
        var webp = Path.Combine(_directory, "detail-128px.webp");
        var decoded = Path.Combine(_directory, "decoded.png");
        Assert.Equal(0, _tools.Run("dwebp", $"\"{webp}\" -o \"{decoded}\""));
        using var image = new Bitmap(decoded);
        Assert.Equal(jpg.Size, image.Size);
    }

    [EncoderFact]
    public void WebPSource_CanBeResizedAndUsedInAuto()
    {
        var source = CreateImage("source.png", true);
        var service = new ImageService(_tools, _settings);
        Assert.True(service.ConvertToWebP(source));
        var webp = Path.Combine(_directory, "source.webp");
        Assert.True(service.Downscale(webp, 64));
        Assert.True(File.Exists(Path.Combine(_directory, "source-64px.webp")));
        Assert.True(service.Auto(webp, 128));
        Assert.True(File.Exists(Path.Combine(_directory, "source-128px.jpg")));
        Assert.True(File.Exists(Path.Combine(_directory, "source-128px.webp")));
    }

    [EncoderFact]
    public void JpegOptimizationAndResizeCanOverwriteWithoutLockingSource()
    {
        _settings.OverwriteOriginal = true;
        var source = CreateImage("source.jpg", true);
        var service = new ImageService(_tools, _settings);
        Assert.True(service.OptimizeJpg(source));
        Assert.True(service.Downscale(source, 64));
        using var image = new Bitmap(source);
        Assert.Equal(new Size(64, 32), image.Size);
    }

    [EncoderFact]
    public void QualityChangesAffectActualJpgAndWebPFiles()
    {
        var source = CreateImage("quality.png", true);
        var service = new ImageService(_tools, _settings);
        _settings.Encoders.JpgQuality = 25;
        _settings.Encoders.WebPQuality = 25;
        Assert.True(service.ConvertToJpg(source));
        Assert.True(service.ConvertToWebP(source));
        _settings.Encoders.JpgQuality = 95;
        _settings.Encoders.WebPQuality = 95;
        Assert.True(service.ConvertToJpg(source));
        Assert.True(service.ConvertToWebP(source));
        foreach (var extension in new[] { "jpg", "webp" })
            Assert.True(new FileInfo(Path.Combine(_directory, $"quality-1.{extension}")).Length >
                new FileInfo(Path.Combine(_directory, $"quality.{extension}")).Length);
    }
}

public sealed class EncoderFactAttribute : FactAttribute
{
    public EncoderFactAttribute()
    {
        var tools = new ExecutableService();
        if (new[] { "pngquant", "cjpeg", "cwebp", "dwebp" }.Any(tool => !tools.IsInPath(tool)))
            Skip = "Requires pngquant, mozjpeg and libwebp on PATH.";
    }
}
