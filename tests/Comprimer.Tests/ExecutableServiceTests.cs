using Comprimer.Services;
using Xunit;

namespace Comprimer.Tests;

public class ExecutableServiceTests
{
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
