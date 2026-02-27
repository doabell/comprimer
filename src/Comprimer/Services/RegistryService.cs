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

    /// <summary>
    /// Registers context menu entries for all enabled formats and operations.
    /// </summary>
    public void Register(AppSettings settings, string exePath)
    {
        Unregister(); // Clean slate

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

                // Remove nested menu
                DeleteSubKeyTreeSafe(shellKey, MenuName);

                // Remove flat entries
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

        // Check flat entries
        var shellPath = $@"{BaseKeyPath}\.jpg\shell";
        using var shellKey = Registry.CurrentUser.OpenSubKey(shellPath);
        if (shellKey == null) return false;
        return shellKey.GetSubKeyNames().Any(n =>
            n.StartsWith($"{MenuName}.", StringComparison.OrdinalIgnoreCase));
    }

    private static List<MenuEntry> BuildMenuEntries(AppSettings settings)
    {
        var entries = new List<MenuEntry>();

        // JPG/JPEG operations
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
                    IconIndex = 1,
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
                IconIndex = 2,
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
                    IconIndex = 1,
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
                IconIndex = 2,
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
                IconIndex = 3,
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
                    IconIndex = 1,
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
        menuKey.SetValue("Icon", $"\"{exePath}\",0");
        menuKey.SetValue("SubCommands", "");

        var shellPath = $@"{menuPath}\shell";

        // Add overwrite toggle
        var owPath = $@"{shellPath}\OverwriteToggle";
        using (var owKey = Registry.CurrentUser.CreateSubKey(owPath))
        {
            var label = settings.OverwriteOriginal ? "✓ Overwrite original" : "  Overwrite original";
            owKey.SetValue("MUIVerb", label);
            using var owCmd = Registry.CurrentUser.CreateSubKey($@"{owPath}\command");
            owCmd.SetValue("", $"\"{exePath}\" --toggle-overwrite \"%1\"");
        }

        // Add separator after toggle
        var sepPath = $@"{shellPath}\Sep1";
        using (var sepKey = Registry.CurrentUser.CreateSubKey(sepPath))
        {
            sepKey.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord);
            sepKey.SetValue("MUIVerb", "");
        }

        // Add operation entry
        var entryPath = $@"{shellPath}\{entry.Id}";
        using (var entryKey = Registry.CurrentUser.CreateSubKey(entryPath))
        {
            entryKey.SetValue("MUIVerb", entry.Label);
            entryKey.SetValue("Icon", $"\"{exePath}\",{entry.IconIndex}");

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
        key.SetValue("Icon", $"\"{exePath}\",{entry.IconIndex}");

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
        public int IconIndex { get; init; }
    }
}
