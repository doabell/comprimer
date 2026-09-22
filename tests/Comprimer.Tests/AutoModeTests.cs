using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Comprimer.Models;
using Comprimer.Services;
using Xunit;

namespace Comprimer.Tests;

public sealed class AutoModeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"comprimer-auto-{Guid.NewGuid():N}");
    private readonly FakeEncoders _encoders = new();
    private readonly AppSettings _settings = new();

    public AutoModeTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Source(string name = "photo.png", int width = 80, int height = 40)
    {
        var path = Path.Combine(_directory, name);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Transparent);
        bitmap.Save(path, name.EndsWith(".png") ? ImageFormat.Png : ImageFormat.Jpeg);
        return path;
    }

    [Theory]
    [InlineData(100, 200, true)]
    [InlineData(200, 100, false)]
    [InlineData(100, 100, false)]
    public void Auto_KeepsExactlyTheRequiredOutputs(int pngSize, int jpgSize, bool keepPng)
    {
        var source = Source();
        var original = File.ReadAllBytes(source);
        _encoders.PngBytes = pngSize;
        _encoders.JpgBytes = jpgSize;
        Assert.True(new ImageService(_encoders, _settings).Auto(source, 40));
        Assert.Equal(keepPng, File.Exists(Path.Combine(_directory, "photo-40px.png")));
        Assert.Equal(!keepPng, File.Exists(Path.Combine(_directory, "photo-40px.jpg")));
        Assert.Equal(!keepPng, File.Exists(Path.Combine(_directory, "photo-40px.webp")));
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Equal(3, _encoders.Calls.Count);
        Assert.All(_encoders.Dimensions, dimensions => Assert.Equal(new Size(40, 20), dimensions));
        Assert.All(_encoders.Outputs, output => Assert.False(Directory.Exists(Path.GetDirectoryName(output))));
    }

    [Theory]
    [InlineData(DownscaleMode.LongestSide, 40, 20)]
    [InlineData(DownscaleMode.Width, 40, 20)]
    [InlineData(DownscaleMode.Height, 80, 40)]
    public void Auto_UsesConfiguredDimension(DownscaleMode mode, int width, int height)
    {
        _settings.DownscaleMode = mode;
        Assert.True(new ImageService(_encoders, _settings).Auto(Source(width: 160, height: 80), 40));
        Assert.All(_encoders.Dimensions, size => Assert.Equal(new Size(width, height), size));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1024)]
    public void Auto_DoesNotEnlargeSmallImages(int? maxSize)
    {
        Assert.True(new ImageService(_encoders, _settings).Auto(Source(), maxSize));
        Assert.All(_encoders.Dimensions, size => Assert.Equal(new Size(80, 40), size));
    }

    [Fact]
    public void Auto_PassesSeparateQualityValuesAndFlattensJpgOnWhite()
    {
        _settings.Encoders = new EncoderSettings { PngMinQuality = 12, PngQuality = 34, JpgQuality = 56, WebPQuality = 78 };
        Assert.True(new ImageService(_encoders, _settings).Auto(Source()));
        Assert.Contains("--quality=12-34", _encoders.Calls.Single(c => c.Tool == "pngquant").Arguments);
        Assert.Contains("-quality 56", _encoders.Calls.Single(c => c.Tool == "cjpeg").Arguments);
        Assert.Contains("-q 78", _encoders.Calls.Single(c => c.Tool == "cwebp").Arguments);
        Assert.Equal(Color.White.ToArgb(), _encoders.JpgBackground.ToArgb());
    }

    [Theory]
    [InlineData("pngquant")]
    [InlineData("cjpeg")]
    [InlineData("cwebp")]
    public void Auto_EncoderFailureDoesNotChangeOriginalOrNeighbors(string tool)
    {
        var source = Source();
        var original = File.ReadAllBytes(source);
        var neighbor = Path.Combine(_directory, "photo.jpg");
        File.WriteAllText(neighbor, "existing JPEG");
        _settings.OverwriteOriginal = true;
        _encoders.FailTool = tool;
        Assert.False(new ImageService(_encoders, _settings).Auto(source));
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Equal("existing JPEG", File.ReadAllText(neighbor));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
        Assert.All(_encoders.Outputs, output => Assert.False(Directory.Exists(Path.GetDirectoryName(output))));
    }

    [Fact]
    public void Auto_MissingEncoderDoesNotStartEncoding()
    {
        _encoders.MissingTool = "cwebp";
        Assert.False(new ImageService(_encoders, _settings).Auto(Source()));
        Assert.Empty(_encoders.Calls);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Auto_PngQualityRejectionUsesLosslessCandidate()
    {
        _encoders.PngExitCode = 99;
        _encoders.JpgBytes = 1_000_000;
        Assert.True(new ImageService(_encoders, _settings).Auto(Source()));
        using var output = new Bitmap(Path.Combine(_directory, "photo-1.png"));
        Assert.Equal(80, output.Width);
        Assert.Equal(0, output.GetPixel(0, 0).A);
    }

    [Fact]
    public void Auto_SuffixesCollisionsWithoutTouchingExistingOutputs()
    {
        File.WriteAllText(Path.Combine(_directory, "photo-40px.jpg"), "keep JPG");
        File.WriteAllText(Path.Combine(_directory, "photo-40px.webp"), "keep WebP");
        Assert.True(new ImageService(_encoders, _settings).Auto(Source(), 40));
        Assert.True(File.Exists(Path.Combine(_directory, "photo-40px-1.jpg")));
        Assert.True(File.Exists(Path.Combine(_directory, "photo-40px-1.webp")));
        Assert.Equal("keep JPG", File.ReadAllText(Path.Combine(_directory, "photo-40px.jpg")));
        Assert.Equal("keep WebP", File.ReadAllText(Path.Combine(_directory, "photo-40px.webp")));
    }

    [Fact]
    public void Auto_OverwritePreservesJpegExtensionAndOtherSourceFiles()
    {
        _settings.OverwriteOriginal = true;
        var source = Source("photo.jpeg");
        Assert.True(new ImageService(_encoders, _settings).Auto(source, 40));
        Assert.Equal(_encoders.JpgBytes, new FileInfo(source).Length);
        Assert.False(File.Exists(Path.Combine(_directory, "photo.jpg")));
        Assert.True(File.Exists(Path.Combine(_directory, "photo.webp")));
    }

    [Fact]
    public void Auto_RollsBackFirstOutputIfSecondCannotBePublished()
    {
        _settings.OverwriteOriginal = true;
        var source = Source("photo.jpg");
        var original = File.ReadAllBytes(source);
        Directory.CreateDirectory(Path.Combine(_directory, "photo.webp"));
        Assert.False(new ImageService(_encoders, _settings).Auto(source));
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Fact]
    public void InvalidInputAndInvalidSizesFailWithoutOutput()
    {
        var source = Source();
        var service = new ImageService(_encoders, _settings);
        Assert.False(service.Auto(source, 0));
        Assert.False(service.Auto(source, -1));
        File.WriteAllText(source, "invalid image");
        Assert.False(service.Auto(source));
        Assert.Empty(_encoders.Calls);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("png", "pngquant", "--quality=22-44")]
    [InlineData("jpg", "cjpeg", "-quality 66")]
    [InlineData("webp", "cwebp", "-q 88")]
    public void IndividualActionsUseEncoderQuality(string format, string tool, string arguments)
    {
        _settings.Encoders = new EncoderSettings { PngMinQuality = 22, PngQuality = 44, JpgQuality = 66, WebPQuality = 88 };
        var source = Source(format == "jpg" ? "photo.jpg" : "photo.png");
        var service = new ImageService(_encoders, _settings);
        Assert.True(format switch { "png" => service.OptimizePng(source), "jpg" => service.OptimizeJpg(source), _ => service.ConvertToWebP(source) });
        Assert.Contains(arguments, _encoders.Calls.Single(c => c.Tool == tool).Arguments);
    }

    private sealed class FakeEncoders : IExecutableService
    {
        public int PngBytes { get; set; } = 300;
        public int JpgBytes { get; set; } = 200;
        public string? FailTool { get; set; }
        public string? MissingTool { get; set; }
        public int PngExitCode { get; set; }
        public List<(string Tool, string Arguments)> Calls { get; } = [];
        public List<Size> Dimensions { get; } = [];
        public List<string> Outputs { get; } = [];
        public Color JpgBackground { get; private set; }
        public string? Resolve(string executableName, string? explicitPath) => executableName == MissingTool ? null : executableName;
        public int? Run(string executable, string arguments, int timeoutMs = 60_000)
        {
            Calls.Add((executable, arguments));
            var quoted = Regex.Matches(arguments, "\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();
            var output = executable == "cwebp" ? quoted[1] : quoted[0];
            var input = executable == "cwebp" ? quoted[0] : quoted[1];
            Outputs.Add(output);
            using (var bitmap = new Bitmap(input))
            {
                Dimensions.Add(bitmap.Size);
                if (executable == "cjpeg") JpgBackground = bitmap.GetPixel(0, 0);
            }
            if (executable == FailTool) { File.WriteAllText(output, "partial"); return 1; }
            if (executable == "pngquant" && PngExitCode != 0) return PngExitCode;
            File.WriteAllBytes(output, new byte[executable == "pngquant" ? PngBytes : executable == "cjpeg" ? JpgBytes : 100]);
            return 0;
        }
    }
}
