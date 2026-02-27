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

        var result = service.GetOutputPath("/path/to/image.jpg", "-1000px");
        Assert.Equal("/path/to/image.jpg", result);
    }

    [Fact]
    public void GetOutputPath_AppendsSuffix_WhenOverwriteDisabled()
    {
        var settings = new AppSettings { OverwriteOriginal = false };
        var exe = new ExecutableService();
        var service = new ImageService(exe, settings);

        // The file doesn't exist on disk, so first candidate should be used
        var result = service.GetOutputPath("/nonexistent/path/image.png", "-1000px");
        Assert.Equal("/nonexistent/path/image-1000px.png", result);
    }

    [Fact]
    public void GetOutputPath_PreservesExtension()
    {
        var settings = new AppSettings { OverwriteOriginal = false };
        var exe = new ExecutableService();
        var service = new ImageService(exe, settings);

        var result = service.GetOutputPath("/path/photo.webp", "-500px");
        Assert.Equal("/path/photo-500px.webp", result);
    }

    [Fact]
    public void GetOutputPath_ConvertSuffix()
    {
        var settings = new AppSettings { OverwriteOriginal = false };
        var exe = new ExecutableService();
        var service = new ImageService(exe, settings);

        var result = service.GetOutputPath("/path/photo.webp", "-1");
        Assert.Equal("/path/photo-1.webp", result);
    }
}
