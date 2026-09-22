using Comprimer.Services;
using Xunit;

namespace Comprimer.Tests;

public class ExecutableServiceTests
{
    [Fact]
    public void Detection_ReturnsFullCurrentPath()
    {
        var service = new ExecutableService();
        var detected = service.FindInPath("powershell");
        Assert.NotNull(detected);
        Assert.True(Path.IsPathFullyQualified(detected));
        Assert.True(File.Exists(detected));
        Assert.Equal(detected, service.Resolve("powershell", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\missing-comprimer-tool-18b341\\powershell.exe")]
    public void ExplicitPath_DoesNotFallBackToDetectedTool(string chosenPath)
    {
        var service = new ExecutableService();
        Assert.NotNull(service.FindInPath("powershell"));
        Assert.Null(service.Resolve("powershell", chosenPath));
    }

    [Fact]
    public void ExplicitPath_TakesPriorityAndSupportsPastedQuotes()
    {
        var selected = Path.Combine(Path.GetTempPath(), $"chosen-encoder-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllText(selected, "path selection fixture");
            Assert.Equal(selected, new ExecutableService().Resolve("powershell", $" \"{selected}\" "));
        }
        finally { File.Delete(selected); }
    }

    [Fact]
    public void DetectDecoder_PrefersSiblingOfSelectedEncoder()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"comprimer-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var encoder = Path.Combine(directory, "cwebp.exe");
            var decoder = Path.Combine(directory, "dwebp.exe");
            File.WriteAllText(encoder, "fixture");
            File.WriteAllText(decoder, "fixture");
            Assert.Equal(decoder, new ExecutableService().DetectWebPDecoder(encoder));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Run_DrainsBothOutputStreamsAndReturnsExitCode()
    {
        var service = new ExecutableService();
        Assert.Equal(7, service.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"[Console]::Out.Write(('o' * 100000)); [Console]::Error.Write(('e' * 100000)); exit 7\"", 10000));
    }

    [Fact]
    public void Run_TerminatesTimedOutEncoder()
    {
        var service = new ExecutableService();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(service.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"", 200));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void IsInPath_ReturnsFalse_ForNonexistentTool()
    {
        var service = new ExecutableService();
        Assert.False(service.IsInPath("definitely_not_a_real_tool_xyz123"));
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenNotFoundAnywhere()
    {
        var service = new ExecutableService();
        var result = service.Resolve("definitely_not_a_real_tool_xyz123", null);
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenExplicitPathDoesNotExist()
    {
        var service = new ExecutableService();
        var result = service.Resolve("definitely_not_a_real_tool_xyz123", "/nonexistent/path/to/tool");
        Assert.Null(result);
    }
}
