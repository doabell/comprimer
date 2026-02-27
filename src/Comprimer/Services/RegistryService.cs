using Comprimer.Models;
using Microsoft.Win32;

namespace Comprimer.Services;

/// <summary>
/// Manages Windows Explorer context menu registration via the registry.
/// </summary>
public sealed class RegistryService
{
    private const string BaseKeyPath = @"Software\Classes\SystemFileAssociations";
    private const string MenuName = "Comprimer";

    private static readonly string[] JpgExtensions = [".jpg", ".jpeg"];
    private static readonly string[] PngExtensions = [".png"];
    private static readonly string[] WebPExtensions = [".webp"];

    // System icons
    private static readonly string SystemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
    private static readonly string PifmgrPath = Path.Combine(SystemDir, "pifmgr.dll");
    private static readonly string BeachBallIcon = $"{PifmgrPath},-8";     // beach ball icon for top-level

    /// <summary>
    /// Registers context menu entries for all enabled formats and operations.
    /// </summary>
    public void Register(AppSettings settings, string exePath)
    {
        Unregister();

        var allEntries = BuildMenuEntries(settings);

        foreach (var entry in allEntries)
        {
            foreach (var ext in entry.Extensions)
            {
                if (settings.NestedMenu)
                    RegisterNestedEntry(ext, entry, exePath);
                else
                    RegisterFlatEntry(ext, entry, exePath);
            }
        }
    }

    /// <summary>
    /// Removes all Comprimer context menu entries from the registry.
    /// </summary>
    public void Unregister()
    {
        var allExts = JpgExtensions.Concat(PngExtensions).Concat(WebPExtensions);
        foreach (var ext in allExts)
        {
            var shellPath = $@"{BaseKeyPath}\{ext}\shell";
            try
            {
                using var shellKey = Registry.CurrentUser.OpenSubKey(shellPath, writable: true);
                if (shellKey == null) continue;

                DeleteSubKeyTreeSafe(shellKey, MenuName);

                foreach (var subName in shellKey.GetSubKeyNames())
                {
                    if (subName.StartsWith($"{MenuName}.", StringComparison.OrdinalIgnoreCase))
                        DeleteSubKeyTreeSafe(shellKey, subName);
                }
            }
            catch
            {
                // Ignore permission errors
            }
        }
    }

    /// <summary>
    /// Checks whether context menu entries are currently registered.
    /// </summary>
    public bool IsRegistered()
    {
        var testPath = $@"{BaseKeyPath}\.jpg\shell\{MenuName}";
        using var key = Registry.CurrentUser.OpenSubKey(testPath);
        if (key != null) return true;

        var shellPath = $@"{BaseKeyPath}\.jpg\shell";
        using var shellKey = Registry.CurrentUser.OpenSubKey(shellPath);
        if (shellKey == null) return false;
        return shellKey.GetSubKeyNames().Any(n =>
            n.StartsWith($"{MenuName}.", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the exe path currently registered in the context menu, if any.
    /// </summary>
    public string? GetRegisteredExePath()
    {
        // Check nested first
        var nestedCmd = $@"{BaseKeyPath}\.jpg\shell\{MenuName}\shell";
        try
        {
            using var shellKey = Registry.CurrentUser.OpenSubKey(nestedCmd);
            if (shellKey != null)
            {
                foreach (var subName in shellKey.GetSubKeyNames())
                {
                    var cmdPath = $@"{nestedCmd}\{subName}\command";
                    using var cmdKey = Registry.CurrentUser.OpenSubKey(cmdPath);
                    var val = cmdKey?.GetValue("") as string;
                    if (val != null)
                        return ExtractExePath(val);
                }
            }
        }
        catch { }

        // Check flat entries
        var flatShell = $@"{BaseKeyPath}\.jpg\shell";
        try
        {
            using var shellKey = Registry.CurrentUser.OpenSubKey(flatShell);
            if (shellKey == null) return null;
            foreach (var subName in shellKey.GetSubKeyNames())
            {
                if (!subName.StartsWith($"{MenuName}.", StringComparison.OrdinalIgnoreCase))
                    continue;
                var cmdPath = $@"{flatShell}\{subName}\command";
                using var cmdKey = Registry.CurrentUser.OpenSubKey(cmdPath);
                var val = cmdKey?.GetValue("") as string;
                if (val != null)
                    return ExtractExePath(val);
            }
        }
        catch { }

        return null;
    }

    private static string? ExtractExePath(string commandLine)
    {
        // Format: "C:\path\to\Comprimer.exe" --args "%1"
        if (commandLine.StartsWith('"'))
        {
            var end = commandLine.IndexOf('"', 1);
            if (end > 1)
                return commandLine[1..end];
        }
        return null;
    }

    private static List<MenuEntry> BuildMenuEntries(AppSettings settings)
    {
        var entries = new List<MenuEntry>();

        // JPG operations
        if (settings.Jpg.Downscale)
        {
            foreach (var size in settings.AvailableSizes)
            {
                entries.Add(new MenuEntry
                {
                    Id = $"Downscale{size}",
                    Label = $"Downscale to {size}px",
                    Command = $"--downscale {size}",
                    Extensions = JpgExtensions,
                });
            }
        }
        if (settings.Jpg.Optimize)
        {
            entries.Add(new MenuEntry
            {
                Id = "Mozjpeg",
                Label = "Optimize (mozjpeg)",
                Command = "--mozjpeg",
                Extensions = JpgExtensions,
            });
        }
        if (settings.Jpg.ConvertToWebP)
        {
            entries.Add(new MenuEntry
            {
                Id = "ToWebP",
                Label = "Convert to WebP",
                Command = "--to-webp",
                Extensions = JpgExtensions,
            });
        }

        // PNG operations
        if (settings.Png.Downscale)
        {
            foreach (var size in settings.AvailableSizes)
            {
                entries.Add(new MenuEntry
                {
                    Id = $"Downscale{size}",
                    Label = $"Downscale to {size}px",
                    Command = $"--downscale {size}",
                    Extensions = PngExtensions,
                });
            }
        }
        if (settings.Png.Optimize)
        {
            entries.Add(new MenuEntry
            {
                Id = "Pngquant",
                Label = "Optimize (pngquant)",
                Command = "--pngquant",
                Extensions = PngExtensions,
            });
        }
        if (settings.Png.ConvertToWebP)
        {
            entries.Add(new MenuEntry
            {
                Id = "ToWebP",
                Label = "Convert to WebP",
                Command = "--to-webp",
                Extensions = PngExtensions,
            });
        }
        if (settings.Png.ConvertToJpg)
        {
            entries.Add(new MenuEntry
            {
                Id = "ToJpg",
                Label = "Convert to JPG (mozjpeg)",
                Command = "--to-jpg",
                Extensions = PngExtensions,
            });
        }

        // WebP operations
        if (settings.WebP.Downscale)
        {
            foreach (var size in settings.AvailableSizes)
            {
                entries.Add(new MenuEntry
                {
                    Id = $"Downscale{size}",
                    Label = $"Downscale to {size}px",
                    Command = $"--downscale {size}",
                    Extensions = WebPExtensions,
                });
            }
        }

        return entries;
    }

    private static void RegisterNestedEntry(string ext, MenuEntry entry, string exePath)
    {
        var menuPath = $@"{BaseKeyPath}\{ext}\shell\{MenuName}";
        using var menuKey = Registry.CurrentUser.CreateSubKey(menuPath);
        menuKey.SetValue("MUIVerb", MenuName);
        menuKey.SetValue("Icon", BeachBallIcon);
        menuKey.SetValue("SubCommands", "");

        var shellPath = $@"{menuPath}\shell";

        var entryPath = $@"{shellPath}\{entry.Id}";
        using var entryKey = Registry.CurrentUser.CreateSubKey(entryPath);
        entryKey.SetValue("MUIVerb", entry.Label);

        using var cmdKey = Registry.CurrentUser.CreateSubKey($@"{entryPath}\command");
        cmdKey.SetValue("", $"\"{exePath}\" {entry.Command} \"%1\"");
    }

    private static void RegisterFlatEntry(string ext, MenuEntry entry, string exePath)
    {
        var keyName = $"{MenuName}.{entry.Id}";
        var keyPath = $@"{BaseKeyPath}\{ext}\shell\{keyName}";

        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue("", $"{MenuName}: {entry.Label}");
        key.SetValue("Icon", BeachBallIcon);

        using var cmdKey = Registry.CurrentUser.CreateSubKey($@"{keyPath}\command");
        cmdKey.SetValue("", $"\"{exePath}\" {entry.Command} \"%1\"");
    }

    private static void DeleteSubKeyTreeSafe(RegistryKey parent, string subKeyName)
    {
        try
        {
            parent.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);
        }
        catch
        {
            // Ignore
        }
    }

    private sealed class MenuEntry
    {
        public required string Id { get; init; }
        public required string Label { get; init; }
        public required string Command { get; init; }
        public required string[] Extensions { get; init; }
    }
}
