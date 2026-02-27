using Comprimer.Models;
using Comprimer.Services;
using Xunit;

namespace Comprimer.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void Load_ReturnsDefaults_WhenNoFile()
    {
        var service = new ConfigService();
        // On a clean system with no settings file, should return defaults
        var settings = service.Load();

        Assert.NotNull(settings);
        Assert.True(settings.NestedMenu);
        Assert.False(settings.OverwriteOriginal);
        Assert.Equal(1024, settings.DownscaleSize);
        Assert.Equal(DownscaleMode.LongestSide, settings.DownscaleMode);
        Assert.Equal(2, settings.AvailableSizes.Count);
        Assert.Equal(512, settings.AvailableSizes[0]);
        Assert.Equal(1024, settings.AvailableSizes[1]);
    }

    [Fact]
    public void DefaultSettings_JpgFormat()
    {
        var settings = new AppSettings();
        Assert.True(settings.Jpg.Downscale);
        Assert.True(settings.Jpg.ConvertToWebP);
        Assert.False(settings.Jpg.ConvertToJpg);
    }

    [Fact]
    public void DefaultSettings_PngFormat()
    {
        var settings = new AppSettings();
        Assert.True(settings.Png.Downscale);
        Assert.True(settings.Png.ConvertToWebP);
        Assert.True(settings.Png.ConvertToJpg);
    }

    [Fact]
    public void DefaultSettings_WebPFormat()
    {
        var settings = new AppSettings();
        Assert.True(settings.WebP.Downscale);
        Assert.False(settings.WebP.ConvertToWebP);
        Assert.False(settings.WebP.ConvertToJpg);
    }
}
