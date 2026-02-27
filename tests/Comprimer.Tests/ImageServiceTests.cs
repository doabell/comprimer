using Comprimer.Models;
using Comprimer.Services;
using Xunit;

namespace Comprimer.Tests;

public class ImageServiceTests
{
    [Fact]
    public void GetOutputPath_ReturnsOriginal_WhenOverwriteEnabled()
    {
        var settings = new AppSettings { OverwriteOriginal = true };
        var exe = new ExecutableService();
        var service = new ImageService(exe, settings);

        var input = Path.Combine("path", "to", "image.jpg");
        var result = service.GetOutputPath(input, "-1000px");
        Assert.Equal(input, result);
    }

    [Fact]
    public void GetOutputPath_AppendsSuffix_WhenOverwriteDisabled()
    {
        var settings = new AppSettings { OverwriteOriginal = false };
        var exe = new ExecutableService();
        var service = new ImageService(exe, settings);

        var dir = Path.Combine(Path.GetTempPath(), $"comprimer-test-{Guid.NewGuid():N}");
        var input = Path.Combine(dir, "image.png");
        var expected = Path.Combine(dir, "image-1000px.png");

        var result = service.GetOutputPath(input, "-1000px");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetOutputPath_PreservesExtension()
    {
        var settings = new AppSettings { OverwriteOriginal = false };
        var exe = new ExecutableService();
        var service = new ImageService(exe, settings);

        var dir = Path.Combine(Path.GetTempPath(), $"comprimer-test-{Guid.NewGuid():N}");
        var input = Path.Combine(dir, "photo.webp");
        var expected = Path.Combine(dir, "photo-500px.webp");

        var result = service.GetOutputPath(input, "-500px");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetConversionOutputPath_UsesPlainName_WhenNoCollision()
    {
        var settings = new AppSettings { OverwriteOriginal = false };
        var exe = new ExecutableService();
        var service = new ImageService(exe, settings);

        var dir = Path.Combine(Path.GetTempPath(), $"comprimer-test-{Guid.NewGuid():N}");
        var input = Path.Combine(dir, "photo.png");
        var expected = Path.Combine(dir, "photo.webp");

        var result = service.GetConversionOutputPath(input, ".webp");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetConversionOutputPath_ReturnsChangedExtension_WhenOverwrite()
    {
        var settings = new AppSettings { OverwriteOriginal = true };
        var exe = new ExecutableService();
        var service = new ImageService(exe, settings);

        var dir = Path.Combine(Path.GetTempPath(), $"comprimer-test-{Guid.NewGuid():N}");
        var input = Path.Combine(dir, "photo.png");
        var expected = Path.Combine(dir, "photo.webp");

        var result = service.GetConversionOutputPath(input, ".webp");
        Assert.Equal(expected, result);
    }
}

