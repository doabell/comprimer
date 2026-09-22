using System.Text.Json;
using Comprimer.Models;

namespace Comprimer.Services;

/// <summary>
/// Manages loading and saving application settings to a JSON file.
/// </summary>
public sealed class ConfigService
{
    private static readonly string AppDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Comprimer");

    private static readonly string SettingsPath =
        Path.Combine(AppDataFolder, "settings.json");

    private readonly string _settingsPath;

    public ConfigService(string? settingsPath = null) => _settingsPath = settingsPath ?? SettingsPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            using var document = JsonDocument.Parse(json);
            var settings = document.RootElement.Deserialize<AppSettings>(JsonOptions) ?? new AppSettings();
            if (!document.RootElement.TryGetProperty("sizeActions", out var actions) || actions.ValueKind == JsonValueKind.Null)
            {
                // Older Auto switches controlled both original-size and resized entries.
                settings.SizeActions = [];
                foreach (var size in settings.AvailableSizes.Where(size => size > 0).Distinct())
                    settings.SizeActions[size] = new SizeActionSettings { AutoResize = settings.AutoMode };
            }
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_settingsPath))!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public static string GetSettingsPath() => SettingsPath;
}
