using System.IO;
using System.Text.Json;

namespace KaraokeStudio.Services;

public sealed class StudioSettings
{
    public int SchemaVersion { get; set; } = 6;
    public bool AutoFindLyrics { get; set; } = true;
    public bool UseLegacyBridge { get; set; } = true;
    public bool TranslationEnabled { get; set; }
    public string TranslationSourceLanguage { get; set; } = "auto";
    public string TranslationProvider { get; set; } = "auto";
    public string LibreTranslateUrl { get; set; } = "";
    public double FontSize { get; set; } = 48;
    public double LineSpacing { get; set; } = 1.36;
    public double VerseGap { get; set; } = 18;
    public double PlainTextScale { get; set; } = 1.0;
    public double NeighborContrast { get; set; } = 64;
    public string AccentColor { get; set; } = "#8A7BB7";
    public double AmbientPercent { get; set; } = 8;
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
                {
                    var existing = JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(Pathname)) ?? new();
                    if (existing.SchemaVersion < 3)
                    {
                        // Migrate only untouched defaults; preserve deliberate user choices.
                        if (Math.Abs(existing.FontSize - 42) < 0.1) existing.FontSize = 48;
                        if (Math.Abs(existing.LineSpacing - 1.48) < 0.01) existing.LineSpacing = 1.36;
                        if (Math.Abs(existing.VerseGap - 14) < 0.1) existing.VerseGap = 18;
                        if (Math.Abs(existing.NeighborContrast - 43) < 0.1) existing.NeighborContrast = 64;
                        existing.SchemaVersion = 3;
                    }
                    if (existing.SchemaVersion < 5) existing.SchemaVersion = 5;
                    if (existing.SchemaVersion < 6) existing.SchemaVersion = 6;
                    if (existing.TranslationProvider is not ("auto" or "mymemory" or "libre" or "deepl"))
                        existing.TranslationProvider = "auto";
                    return existing;
                }
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
