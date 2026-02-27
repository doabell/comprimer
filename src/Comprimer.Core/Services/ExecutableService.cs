using System.Diagnostics;

namespace Comprimer.Services;

/// <summary>
/// Detects and manages external image processing executables.
/// </summary>
public sealed class ExecutableService
{
    /// <summary>
    /// Checks if a given executable name is found in the system PATH.
    /// </summary>
    public bool IsInPath(string executableName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            return false;

        var paths = pathVar.Split(Path.PathSeparator);
        foreach (var dir in paths)
        {
            var fullPath = Path.Combine(dir, executableName);
            if (File.Exists(fullPath)) return true;
            if (File.Exists(fullPath + ".exe")) return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves the path to an executable. If the explicit path is set, uses that.
    /// Otherwise tries PATH. Returns null if not found.
    /// </summary>
    public string? Resolve(string executableName, string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        if (IsInPath(executableName))
            return executableName;

        return null;
    }

    /// <summary>
    /// Runs an external executable with the given arguments, waiting for completion.
    /// Returns true if process exited with code 0.
    /// </summary>
    public bool Run(string executable, string arguments, int timeoutMs = 60_000)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            process.Start();
            process.WaitForExit(timeoutMs);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
