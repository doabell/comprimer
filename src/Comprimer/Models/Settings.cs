using System.Text.Json.Serialization;

namespace Comprimer.Models;

/// <summary>
/// Controls which dimension the downscale limit applies to.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DownscaleMode
{
    /// <summary>Limit applies to whichever side is longer.</summary>
    LongestSide,
    /// <summary>Limit applies to the width only.</summary>
    Width,
    /// <summary>Limit applies to the height only.</summary>
    Height,
}

/// <summary>
/// Root settings for the application, persisted to settings.json.
/// </summary>
public sealed class AppSettings
{
    public bool NestedMenu { get; set; } = true;
    public bool OverwriteOriginal { get; set; }
    public int DownscaleSize { get; set; } = 1024;
    public DownscaleMode DownscaleMode { get; set; } = DownscaleMode.LongestSide;
    public List<int> AvailableSizes { get; set; } = [512, 1024];
    public string Language { get; set; } = "en";
    public bool DarkMode { get; set; } = false;

    public FormatSettings Jpg { get; set; } = new()
    {
        Downscale = true,
        ConvertToWebP = true,
        Optimize = true,
    };

    public FormatSettings Png { get; set; } = new()
    {
        Downscale = true,
        ConvertToWebP = true,
        ConvertToJpg = true,
        Optimize = true,
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
    public bool Optimize { get; set; }
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
