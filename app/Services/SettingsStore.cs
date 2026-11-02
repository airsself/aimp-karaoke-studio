using System.IO;
using System.Text.Json;

namespace KaraokeStudio.Services;

public sealed class StudioSettings
{
    public int SchemaVersion { get; set; } = 2;
    public double FontSize { get; set; } = 42;
    public double LineSpacing { get; set; } = 1.48;
    public double VerseGap { get; set; } = 14;
    public double PlainTextScale { get; set; } = 1.0;
    public double NeighborContrast { get; set; } = 43;
    public string AccentColor { get; set; } = "#8A7BB7";
    public double AmbientPercent { get; set; } = 10;
    public bool SmoothScroll { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public string Mode { get; set; } = "Reading";
    public int OffsetMs { get; set; }
    public bool AlwaysOnTop { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 1194;
    public double WindowHeight { get; set; } = 760;
}

public static class SettingsStore
{
    private static readonly string Pathname = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AimpKaraokeStudio", "settings.json");

    public static StudioSettings Load()
    {
        try
        {
            if (File.Exists(Pathname))
                return JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(Pathname)) ?? new();
        }
        catch (Exception) { /* Damaged or outdated settings must not prevent launch. */ }
        return new();
    }
    public static void Save(StudioSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pathname)!);
            // Write through a temporary file; interrupted writes do not corrupt an existing settings file.
            string temp = Pathname + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, Pathname, overwrite: true);
        }
        catch (Exception) { /* Keep live settings if profile is read-only. */ }
    }
}
