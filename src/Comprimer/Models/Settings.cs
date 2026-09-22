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
    public Dictionary<int, SizeActionSettings> SizeActions { get; set; } = [];
    public string Language { get; set; } = "en";
    public bool AutoMode { get; set; } = true;
    public string? ComprimerPath { get; set; }
    public EncoderSettings Encoders { get; set; } = new();

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

    internal SizeActionSettings GetSizeActions(int size)
    {
        if (!SizeActions.TryGetValue(size, out var actions))
            SizeActions[size] = actions = new SizeActionSettings();
        return actions;
    }
}

/// <summary>Independent Explorer shortcuts for a shared size preset.</summary>
public sealed class SizeActionSettings
{
    public bool AutoResize { get; set; } = true;
    public bool Resize { get; set; } = true;
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
    public string? DwebpPath { get; set; }
}

/// <summary>Quality settings shared by resizing, optimization, conversion and Auto.</summary>
public sealed class EncoderSettings
{
    private int _pngMinQuality = 65;
    private int _pngQuality = 80;
    private int _jpgQuality = 85;
    private int _webPQuality = 80;

    public int PngMinQuality { get => _pngMinQuality; set => _pngMinQuality = Math.Clamp(value, 0, 100); }
    public int PngQuality { get => _pngQuality; set => _pngQuality = Math.Clamp(value, 0, 100); }
    public int JpgQuality { get => _jpgQuality; set => _jpgQuality = Math.Clamp(value, 0, 100); }
    public int WebPQuality { get => _webPQuality; set => _webPQuality = Math.Clamp(value, 0, 100); }
    internal int EffectivePngMinQuality => Math.Min(PngMinQuality, PngQuality);
}
