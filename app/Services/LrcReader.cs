using KaraokeStudio.Models;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace KaraokeStudio.Services;

public static partial class LrcReader
{
    [GeneratedRegex(@"\[([0-9]{1,3}):([0-9]{2})(?:\.([0-9]{1,3}))?\]")]
    private static partial Regex TimestampPattern();
    [GeneratedRegex(@"^\[offset:([+-]?[0-9]+)\]$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPattern();

    public static (List<LyricsDto> Lines, bool Synced) Parse(string content, int durationMs = 0)
    {
        var timed = new List<LyricsDto>();
        var plain = new List<LyricsDto>();
        var offset = 0;
        foreach (var row in content.Split('\n'))
        {
            var line = row.Trim().TrimStart('\uFEFF');
            if (line.Length == 0) continue;
            var tag = OffsetPattern().Match(line);
            if (tag.Success)
            {
                if (int.TryParse(tag.Groups[1].Value, out int parsed)) offset = Math.Clamp(parsed, -600000, 600000);
                continue;
            }
            // Matches must form a contiguous prefix; don't parse accidental
            // timestamps in the middle of ordinary lyric text.
            var stamps = TimestampPattern().Matches(line);
            var position = 0;
            var times = new List<int>();
            foreach (Match stamp in stamps)
            {
                if (stamp.Index != position) break;
                position += stamp.Length;
                var minutes = int.Parse(stamp.Groups[1].Value);
                var seconds = int.Parse(stamp.Groups[2].Value);
                if (seconds > 59) { times.Clear(); break; }
                var fraction = stamp.Groups[3].Value;
                var millis = fraction.Length > 0 ? int.Parse(fraction.PadRight(3, '0')) : 0;
                times.Add(((minutes * 60) + seconds) * 1000 + millis);
            }
            if (times.Count > 0)
            {
                var words = line[position..].Trim();
                foreach (var time in times)
                    timed.Add(new LyricsDto { StartMs = time, Text = words });
            }
            else if (!line.StartsWith('[')) plain.Add(new LyricsDto { Text = line });
        }
        if (timed.Count == 0) return (plain, false);
        foreach (var item in timed)
            item.StartMs = (int)Math.Clamp((long)item.StartMs + offset, 0, 86_400_000);
        timed = timed.OrderBy(x => x.StartMs).ToList();
        var merged = new List<LyricsDto>();
        foreach (var line in timed)
        {
            if (merged.Count > 0 && merged[^1].StartMs == line.StartMs)
            {
                if (!string.IsNullOrEmpty(line.Text))
                    merged[^1].Text += (merged[^1].Text.Length > 0 ? "\n" : "") + line.Text;
            }
            else merged.Add(line);
        }
        for (var i = 0; i < merged.Count; i++)
            merged[i].EndMs = i + 1 < merged.Count ? Math.Max(merged[i].StartMs + 1, merged[i + 1].StartMs)
                : Math.Max(merged[i].StartMs + 4000, durationMs);
        return (merged, true);
    }

    public static (List<LyricsDto> Lines, bool Synced) Read(string path, int durationMs)
    {
        if (new FileInfo(path).Length > 8_000_000) throw new InvalidDataException("LRC is larger than 8 MB.");
        byte[] raw = File.ReadAllBytes(path);
        string content;
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
            content = Encoding.Unicode.GetString(raw, 2, raw.Length - 2);
        else if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
            content = Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);
        else
        {
            int start = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF ? 3 : 0;
            try { content = new UTF8Encoding(false, true).GetString(raw, start, raw.Length - start); }
            catch (DecoderFallbackException)
            {
                // Match native plugin's system ANSI fallback for legacy files.
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                content = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(raw);
            }
        }
        return Parse(content, durationMs);
    }
}
