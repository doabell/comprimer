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

    private const string CheckedPrefix = "✓ ";
    private const string UncheckedPrefix = "   ";

    private static readonly string[] JpgExtensions = [".jpg", ".jpeg"];
    private static readonly string[] PngExtensions = [".png"];
    private static readonly string[] WebPExtensions = [".webp"];

    // System icons
    private static readonly string SystemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
    private static readonly string PifmgrPath = Path.Combine(SystemDir, "pifmgr.dll");
    private static readonly string ImageresPath = Path.Combine(SystemDir, "imageres.dll");
    private static readonly string BeachBallIcon = $"{PifmgrPath},-8";     // beach ball icon for top-level
    private static readonly string DownscaleIcon = $"{ImageresPath},-5306"; // resize icon
    private static readonly string ConvertIcon = $"{ImageresPath},-5381";   // convert/save-as icon

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
                    RegisterNestedEntry(ext, entry, exePath, settings);
                else
                    RegisterFlatEntry(ext, entry, exePath, settings);
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

    private static List<MenuEntry> BuildMenuEntries(AppSettings settings)
    {
        var entries = new List<MenuEntry>();

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
                    Icon = DownscaleIcon,
                });
            }
        }
        if (settings.Jpg.ConvertToWebP)
        {
            entries.Add(new MenuEntry
            {
                Id = "ToWebP",
                Label = "Convert to WebP",
                Command = "--to-webp",
                Extensions = JpgExtensions,
                Icon = ConvertIcon,
            });
        }

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
                    Icon = DownscaleIcon,
                });
            }
        }
        if (settings.Png.ConvertToWebP)
        {
            entries.Add(new MenuEntry
            {
                Id = "ToWebP",
                Label = "Convert to WebP",
                Command = "--to-webp",
                Extensions = PngExtensions,
                Icon = ConvertIcon,
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
                Icon = ConvertIcon,
            });
        }

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
                    Icon = DownscaleIcon,
                });
            }
        }

        return entries;
    }

    private static void RegisterNestedEntry(string ext, MenuEntry entry, string exePath, AppSettings settings)
    {
        var menuPath = $@"{BaseKeyPath}\{ext}\shell\{MenuName}";
        using var menuKey = Registry.CurrentUser.CreateSubKey(menuPath);
        menuKey.SetValue("MUIVerb", MenuName);
        menuKey.SetValue("Icon", BeachBallIcon);
        menuKey.SetValue("SubCommands", "");

        var shellPath = $@"{menuPath}\shell";

        var owPath = $@"{shellPath}\OverwriteToggle";
        using (var owKey = Registry.CurrentUser.CreateSubKey(owPath))
        {
            var prefix = settings.OverwriteOriginal ? CheckedPrefix : UncheckedPrefix;
            var label = $"{prefix}Overwrite original";
            owKey.SetValue("MUIVerb", label);
            using var owCmd = Registry.CurrentUser.CreateSubKey($@"{owPath}\command");
            owCmd.SetValue("", $"\"{exePath}\" --toggle-overwrite \"%1\"");
        }

        var sepPath = $@"{shellPath}\Sep1";
        using (var sepKey = Registry.CurrentUser.CreateSubKey(sepPath))
        {
            sepKey.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord);
            sepKey.SetValue("MUIVerb", "");
        }

        var entryPath = $@"{shellPath}\{entry.Id}";
        using (var entryKey = Registry.CurrentUser.CreateSubKey(entryPath))
        {
            entryKey.SetValue("MUIVerb", entry.Label);
            entryKey.SetValue("Icon", entry.Icon);

            using var cmdKey = Registry.CurrentUser.CreateSubKey($@"{entryPath}\command");
            cmdKey.SetValue("", $"\"{exePath}\" {entry.Command} \"%1\"");
        }
    }

    private static void RegisterFlatEntry(string ext, MenuEntry entry, string exePath, AppSettings settings)
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
        public required string Icon { get; init; }
    }
}
