using Comprimer.Forms;
using Comprimer.Models;
using Comprimer.Services;

namespace Comprimer;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            // GUI mode: open settings
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SettingsForm());
            return;
        }

        // Silent action mode (invoked from context menu)
        var config = new ConfigService();
        var settings = config.Load();
        var exe = new ExecutableService();
        var imageService = new ImageService(exe, settings);

        var command = args[0].ToLowerInvariant();
        switch (command)
        {
            case "--downscale":
                HandleDownscale(args, imageService);
                break;
            case "--to-webp":
                HandleToWebP(args, imageService);
                break;
            case "--to-jpg":
                HandleToJpg(args, imageService);
                break;
            case "--toggle-overwrite":
                HandleToggleOverwrite(args, config, settings);
                break;
            default:
                // Unknown command, ignore silently
                break;
        }
    }

    private static void HandleDownscale(string[] args, ImageService imageService)
    {
        if (args.Length < 3) return;
        if (!int.TryParse(args[1], out var size)) return;
        var filePath = args[2];
        if (!File.Exists(filePath)) return;

        imageService.Downscale(filePath, size);
    }

    private static void HandleToWebP(string[] args, ImageService imageService)
    {
        if (args.Length < 2) return;
        var filePath = args[1];
        if (!File.Exists(filePath)) return;

        imageService.ConvertToWebP(filePath);
    }

    private static void HandleToJpg(string[] args, ImageService imageService)
    {
        if (args.Length < 2) return;
        var filePath = args[1];
        if (!File.Exists(filePath)) return;

        imageService.ConvertToJpg(filePath);
    }

    private static void HandleToggleOverwrite(string[] args, ConfigService config, AppSettings settings)
    {
        settings.OverwriteOriginal = !settings.OverwriteOriginal;
        config.Save(settings);

        // Re-register context menus with updated overwrite state
        var registry = new RegistryService();
        if (registry.IsRegistered())
        {
            var exePath = Environment.ProcessPath ?? "";
            registry.Register(settings, exePath);
        }
    }
}
