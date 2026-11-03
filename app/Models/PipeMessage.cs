namespace KaraokeStudio.Models;
public sealed class PipeMessage
{
    public string Type { get; set; } = "";
    public int Generation { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Album { get; set; } = "";
    public int DurationMs { get; set; }
    public int PositionMs { get; set; }
    public int State { get; set; }
    public bool Synced { get; set; }
    public string Source { get; set; } = "";
    public string Error { get; set; } = "";
    public string ArtworkPath { get; set; } = "";
    public string ArtworkData { get; set; } = "";
    public List<LyricsDto> Lines { get; set; } = [];
}
public sealed class WordTimingDto
{
    public int StartMs { get; set; }
    public int EndMs { get; set; }
    public string Text { get; set; } = "";
}
public sealed class LyricsDto
{
    public int StartMs { get; set; }
    public int EndMs { get; set; }
    public string Text { get; set; } = "";
    public List<WordTimingDto> Words { get; set; } = [];
}
