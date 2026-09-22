using Comprimer.Models;
using Comprimer.Services;
using Xunit;

namespace Comprimer.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void Save_KeepsEverySelectedPathIncludingClearedPaths()
    {
        var file = Path.Combine(Path.GetTempPath(), $"comprimer-paths-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigService(file);
            config.Save(new AppSettings
            {
                ComprimerPath = "C:\\My apps\\Comprimer.exe",
                Executables = new ExecutableSettings
                {
                    PngquantPath = "C:\\Encoders\\pngquant.exe",
                    CjpegPath = "C:\\Encoders\\mozjpeg\\cjpeg.exe",
                    Img2WebPPath = "C:\\Encoders\\cwebp.exe",
                    DwebpPath = "",
                },
            });
            var settings = config.Load();
            Assert.Equal("C:\\My apps\\Comprimer.exe", settings.ComprimerPath);
            Assert.Equal("C:\\Encoders\\pngquant.exe", settings.Executables.PngquantPath);
            Assert.Equal("C:\\Encoders\\mozjpeg\\cjpeg.exe", settings.Executables.CjpegPath);
            Assert.Equal("C:\\Encoders\\cwebp.exe", settings.Executables.Img2WebPPath);
            Assert.Equal("", settings.Executables.DwebpPath);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenNoFile()
    {
        var service = new ConfigService(Path.Combine(Path.GetTempPath(), $"comprimer-missing-{Guid.NewGuid():N}.json"));
        var settings = service.Load();

        Assert.NotNull(settings);
        Assert.True(settings.NestedMenu);
        Assert.False(settings.OverwriteOriginal);
        Assert.Equal(1024, settings.DownscaleSize);
        Assert.Equal(DownscaleMode.LongestSide, settings.DownscaleMode);
        Assert.Equal(2, settings.AvailableSizes.Count);
        Assert.Equal(512, settings.AvailableSizes[0]);
        Assert.Equal(1024, settings.AvailableSizes[1]);
        Assert.True(settings.AutoMode);
    }

    [Fact]
    public void DefaultSettings_JpgFormat()
    {
        var settings = new AppSettings();
        Assert.True(settings.Jpg.Downscale);
        Assert.True(settings.Jpg.ConvertToWebP);
        Assert.False(settings.Jpg.ConvertToJpg);
        Assert.True(settings.Jpg.Optimize);
    }

    [Fact]
    public void DefaultSettings_PngFormat()
    {
        var settings = new AppSettings();
        Assert.True(settings.Png.Downscale);
        Assert.True(settings.Png.ConvertToWebP);
        Assert.True(settings.Png.ConvertToJpg);
        Assert.True(settings.Png.Optimize);
    }

    [Fact]
    public void DefaultSettings_WebPFormat()
    {
        var settings = new AppSettings();
        Assert.True(settings.WebP.Downscale);
        Assert.False(settings.WebP.ConvertToWebP);
        Assert.False(settings.WebP.ConvertToJpg);
        Assert.False(settings.WebP.Optimize);
    }

    [Fact]
    public void OldSettings_GetDefaultEncoderQualityAndAuto()
    {
        var file = Path.Combine(Path.GetTempPath(), $"comprimer-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(file, "{\"nestedMenu\":false,\"availableSizes\":[720],\"language\":\"fr\"}");
            var settings = new ConfigService(file).Load();
            Assert.False(settings.NestedMenu);
            Assert.Equal([720], settings.AvailableSizes);
            Assert.Equal("fr", settings.Language);
            Assert.True(settings.AutoMode);
            Assert.Equal(65, settings.Encoders.PngMinQuality);
            Assert.Equal(80, settings.Encoders.PngQuality);
            Assert.Equal(85, settings.Encoders.JpgQuality);
            Assert.Equal(80, settings.Encoders.WebPQuality);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Save_RoundTripsAutoAndEveryEncoderSetting()
    {
        var file = Path.Combine(Path.GetTempPath(), $"comprimer-config-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigService(file);
            config.Save(new AppSettings
            {
                AutoMode = false,
                AvailableSizes = [640, 1536],
                SizeActions = new()
                {
                    [640] = new SizeActionSettings { AutoResize = true, Resize = false },
                    [1536] = new SizeActionSettings { AutoResize = false, Resize = true },
                },
                Encoders = new EncoderSettings { PngMinQuality = 42, PngQuality = 72, JpgQuality = 91, WebPQuality = 63 },
                Executables = new ExecutableSettings { DwebpPath = "C:\\tools\\dwebp.exe" },
            });
            var loaded = config.Load();
            Assert.False(loaded.AutoMode);
            Assert.Equal([640, 1536], loaded.AvailableSizes);
            Assert.True(loaded.GetSizeActions(640).AutoResize);
            Assert.False(loaded.GetSizeActions(640).Resize);
            Assert.False(loaded.GetSizeActions(1536).AutoResize);
            Assert.True(loaded.GetSizeActions(1536).Resize);
            Assert.Equal(42, loaded.Encoders.PngMinQuality);
            Assert.Equal(72, loaded.Encoders.PngQuality);
            Assert.Equal(91, loaded.Encoders.JpgQuality);
            Assert.Equal(63, loaded.Encoders.WebPQuality);
            Assert.Equal("C:\\tools\\dwebp.exe", loaded.Executables.DwebpPath);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacySettings_PreserveExistingAutoAndResizeEntries(bool auto)
    {
        var file = Path.Combine(Path.GetTempPath(), $"comprimer-legacy-sizes-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(file, """{"autoMode":AUTO,"availableSizes":[720,1440],"jpg":{"downscale":false},"png":{"downscale":true},"webP":{"downscale":false}}"""
                .Replace("AUTO", auto ? "true" : "false"));
            var settings = new ConfigService(file).Load();
            var entries = RegistryService.BuildMenuEntries(settings);
            Assert.Equal(auto, settings.AutoMode);
            foreach (var size in settings.AvailableSizes)
            {
                Assert.Equal(auto, settings.GetSizeActions(size).AutoResize);
                Assert.True(settings.GetSizeActions(size).Resize);
                Assert.Equal(auto, entries.Any(entry => entry.Command == $"--auto {size}"));
                Assert.Equal([".png"], Assert.Single(entries, entry => entry.Command == $"--downscale {size}").Extensions);
            }
            settings.AutoMode = !auto;
            Assert.All(settings.AvailableSizes, size => Assert.Equal(auto, settings.GetSizeActions(size).AutoResize));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Quality_StaysInEncoderRangeAndOrdersPngBounds()
    {
        var quality = new EncoderSettings { PngMinQuality = 120, PngQuality = 20, JpgQuality = -1, WebPQuality = 101 };
        Assert.Equal(20, quality.EffectivePngMinQuality);
        Assert.Equal(0, quality.JpgQuality);
        Assert.Equal(100, quality.WebPQuality);
    }
}
