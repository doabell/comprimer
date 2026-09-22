using Comprimer.Models;
using Microsoft.Win32;

namespace Comprimer.Services;

/// <summary>
/// Manages Windows Explorer context menu registration via the registry.
/// </summary>
public interface IRegistryService
{
    void Register(AppSettings settings, string currentExePath);
    void Unregister();
    bool IsRegistered();
    string? GetRegisteredExePath();
}

public sealed class RegistryService : IRegistryService
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
        // Resolve and validate before removing any existing shortcuts.
        exePath = GetRegistrationPath(settings, exePath, GetRegisteredExePath());
        settings.ComprimerPath = exePath;
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

    internal static string GetRegistrationPath(AppSettings settings, string currentExePath, string? registeredExePath)
    {
        var selected = settings.ComprimerPath ?? registeredExePath ?? currentExePath;
        return ExecutableService.ExistingPath(selected)
            ?? throw new FileNotFoundException("Choose Comprimer.exe or use Detect.", selected);
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
        foreach (var ext in JpgExtensions.Concat(PngExtensions).Concat(WebPExtensions))
        {
            using var shell = Registry.CurrentUser.OpenSubKey($@"{BaseKeyPath}\{ext}\shell");
            if (shell?.GetSubKeyNames().Any(name => name.Equals(MenuName, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith($"{MenuName}.", StringComparison.OrdinalIgnoreCase)) == true) return true;
        }
        return false;
    }

    /// <summary>Finds the configured command across every supported extension and menu style.</summary>
    public string? GetRegisteredExePath()
    {
        foreach (var ext in JpgExtensions.Concat(PngExtensions).Concat(WebPExtensions))
        {
            using var shell = Registry.CurrentUser.OpenSubKey($@"{BaseKeyPath}\{ext}\shell");
            if (shell == null) continue;
            using var nested = shell.OpenSubKey($@"{MenuName}\shell");
            if (nested != null)
            {
                foreach (var name in nested.GetSubKeyNames())
                {
                    using var command = nested.OpenSubKey($@"{name}\command");
                    if (command?.GetValue("") is string text && ExtractExePath(text) is string path) return path;
                }
            }
            foreach (var name in shell.GetSubKeyNames().Where(name => name.StartsWith($"{MenuName}.", StringComparison.OrdinalIgnoreCase)))
            {
                using var command = shell.OpenSubKey($@"{name}\command");
                if (command?.GetValue("") is string text && ExtractExePath(text) is string path) return path;
            }
        }
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

    internal static List<MenuEntry> BuildMenuEntries(AppSettings settings)
    {
        var entries = new List<MenuEntry>();
        bool fr = settings.Language == "fr";

        string Tr(string en, string frText) => fr ? frText : en;
        var sizes = settings.AvailableSizes.Where(size => size > 0).Distinct().Order().ToArray();
        var extensions = JpgExtensions.Concat(PngExtensions).Concat(WebPExtensions).ToArray();

        if (settings.AutoMode)
        {
            entries.Add(new MenuEntry
            {
                Id = "Auto",
                Label = Tr("Auto · original size", "Auto · taille d'origine"),
                Command = "--auto",
                Extensions = extensions,
            });
        }
        foreach (var size in sizes.Where(size => settings.GetSizeActions(size).AutoResize))
        {
            entries.Add(new MenuEntry
            {
                Id = $"Auto{size}",
                Label = Tr($"Auto + resize {size}px", $"Auto + réduire {size}px"),
                Command = $"--auto {size}",
                Extensions = extensions,
            });
        }

        // JPG operations
        if (settings.Jpg.Downscale)
        {
            foreach (var size in sizes.Where(size => settings.GetSizeActions(size).Resize))
            {
                entries.Add(new MenuEntry
                {
                    Id = $"Downscale{size}",
                    Label = Tr($"Resize only {size}px", $"Réduire seulement {size}px"),
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
                Label = Tr("Optimize (mozjpeg)", "Optimiser (mozjpeg)"),
                Command = "--mozjpeg",
                Extensions = JpgExtensions,
            });
        }
        if (settings.Jpg.ConvertToWebP)
        {
            entries.Add(new MenuEntry
            {
                Id = "ToWebP",
                Label = Tr("Convert to WebP (libwebp)", "Convertir en WebP (libwebp)"),
                Command = "--to-webp",
                Extensions = JpgExtensions,
            });
        }

        // PNG operations
        if (settings.Png.Downscale)
        {
            foreach (var size in sizes.Where(size => settings.GetSizeActions(size).Resize))
            {
                entries.Add(new MenuEntry
                {
                    Id = $"Downscale{size}",
                    Label = Tr($"Resize only {size}px", $"Réduire seulement {size}px"),
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
                Label = Tr("Optimize (pngquant)", "Optimiser (pngquant)"),
                Command = "--pngquant",
                Extensions = PngExtensions,
            });
        }
        if (settings.Png.ConvertToWebP)
        {
            entries.Add(new MenuEntry
            {
                Id = "ToWebP",
                Label = Tr("Convert to WebP (libwebp)", "Convertir en WebP (libwebp)"),
                Command = "--to-webp",
                Extensions = PngExtensions,
            });
        }
        if (settings.Png.ConvertToJpg)
        {
            entries.Add(new MenuEntry
            {
                Id = "ToJpg",
                Label = Tr("Convert to JPG (mozjpeg)", "Convertir en JPG (mozjpeg)"),
                Command = "--to-jpg",
                Extensions = PngExtensions,
            });
        }

        // WebP operations
        if (settings.WebP.Downscale)
        {
            foreach (var size in sizes.Where(size => settings.GetSizeActions(size).Resize))
            {
                entries.Add(new MenuEntry
                {
                    Id = $"Downscale{size}",
                    Label = Tr($"Resize only {size}px", $"Réduire seulement {size}px"),
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
        menuKey.SetValue("Icon", $"\"{exePath}\",0");
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
        key.SetValue("Icon", $"\"{exePath}\",0");

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

    internal sealed class MenuEntry
    {
        public required string Id { get; init; }
        public required string Label { get; init; }
        public required string Command { get; init; }
        public required string[] Extensions { get; init; }
    }
}
