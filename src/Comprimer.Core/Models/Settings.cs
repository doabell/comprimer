using System.Text.Json.Serialization;

namespace Comprimer.Models;

/// <summary>
/// Root settings for the application, persisted to settings.json.
/// </summary>
public sealed class AppSettings
{
    public bool NestedMenu { get; set; } = true;
    public bool OverwriteOriginal { get; set; }
    public int DownscaleSize { get; set; } = 1000;
    public List<int> AvailableSizes { get; set; } = [1000];

    public FormatSettings Jpg { get; set; } = new()
    {
        Downscale = true,
        ConvertToWebP = true,
    };

    public FormatSettings Png { get; set; } = new()
    {
        Downscale = true,
        ConvertToWebP = true,
        ConvertToJpg = true,
    };

    public FormatSettings WebP { get; set; } = new()
    {
        Downscale = true,
    };

    public ExecutableSettings Executables { get; set; } = new();
}

/// <summary>
/// Per-format operation toggles.
/// </summary>
public sealed class FormatSettings
{
    public bool Downscale { get; set; }
    public bool ConvertToWebP { get; set; }
    public bool ConvertToJpg { get; set; }
}

/// <summary>
/// Paths to external image processing executables.
/// </summary>
public sealed class ExecutableSettings
{
    public string? PngquantPath { get; set; }
    public string? Img2WebPPath { get; set; }
    public string? CjpegPath { get; set; }
}
