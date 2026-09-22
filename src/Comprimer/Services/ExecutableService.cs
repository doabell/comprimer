using System.Diagnostics;

namespace Comprimer.Services;

/// <summary>
/// Detects and manages external image processing executables.
/// </summary>
public interface IExecutableService
{
    string? Resolve(string executableName, string? explicitPath);
    int? Run(string executable, string arguments, int timeoutMs = 60_000);
}

public sealed class ExecutableService : IExecutableService
{
    /// <summary>
    /// Checks if a given executable name is found in the system PATH.
    /// </summary>
    public bool IsInPath(string executableName) => FindInPath(executableName) != null;

    /// <summary>Detects a tool on PATH and returns its actual absolute path.</summary>
    public string? FindInPath(string executableName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            return null;

        var paths = pathVar.Split(Path.PathSeparator);
        foreach (var dir in paths)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(dir.Trim().Trim('"'), executableName));
                if (File.Exists(fullPath)) return fullPath;
                if (File.Exists(fullPath + ".exe")) return fullPath + ".exe";
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { }
        }
        return null;
    }

    /// <summary>
    /// A configured path is authoritative, even when missing or explicitly cleared.
    /// Null retains PATH lookup for settings created before explicit paths were saved.
    /// </summary>
    public string? Resolve(string executableName, string? explicitPath)
    {
        if (explicitPath == null) return FindInPath(executableName);
        return ExistingPath(explicitPath);
    }

    internal static string? ExistingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var cleaned = path.Trim().Trim('"');
        return Path.IsPathFullyQualified(cleaned) && File.Exists(cleaned) ? Path.GetFullPath(cleaned) : null;
    }

    public string? DetectWebPDecoder(string? encoderPath)
    {
        var encoder = ExistingPath(encoderPath);
        var sibling = encoder == null ? null : ExistingPath(Path.Combine(Path.GetDirectoryName(encoder)!, "dwebp.exe"));
        return sibling ?? FindInPath("dwebp");
    }

    /// <summary>
    /// Runs an external executable with the given arguments, waiting for completion.
    /// Returns the exit code, or null if the process cannot complete.
    /// </summary>
    public int? Run(string executable, string arguments, int timeoutMs = 60_000)
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
            // Drain both pipes while waiting: encoders can otherwise block on a full buffer.
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return null;
            }
            process.WaitForExit();
            return process.ExitCode;
        }
        catch
        {
            return null;
        }
    }
}
