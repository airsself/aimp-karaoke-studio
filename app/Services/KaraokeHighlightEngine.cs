using System.Globalization;
using KaraokeStudio.Models;

namespace KaraokeStudio.Services;

/// <summary>
/// Timed Enhanced LRC: follow the supplied timestamps for each text chunk.
/// Plain LRC: approximate left-to-right sweep across Unicode text elements.
/// Returns a UTF-16 boundary, never an interior code unit of a surrogate pair.
/// </summary>
public static class KaraokeHighlightEngine
{
    public static int GetHighlightLength(string text, IReadOnlyList<WordTimingDto>? words,
        int lineStartMs, int lineEndMs, double progressPercent)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        double normalized = double.IsFinite(progressPercent) ? Math.Clamp(progressPercent / 100.0, 0, 1) : 0;
        if (words is { Count: > 0 } && words.Count <= 256 && string.Concat(words.Select(w => w.Text)) == text)
        {
            double time = lineStartMs + normalized * Math.Max(1, lineEndMs - lineStartMs);
            int prefix = 0;
            foreach (var word in words)
            {
                if (time >= word.EndMs) prefix += word.Text.Length;
                else if (time > word.StartMs)
                {
                    double fraction = (time - word.StartMs) / Math.Max(1, word.EndMs - word.StartMs);
                    prefix += GraphemeCut(word.Text, fraction);
                    break;
                }
                else break;
            }
            return Math.Clamp(prefix, 0, text.Length);
        }
        return GraphemeCut(text, normalized);
    }

    private static int GraphemeCut(string text, double progress)
    {
        if (progress <= 0 || text.Length == 0) return 0;
        if (progress >= 1) return text.Length;
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int count = Math.Clamp((int)Math.Floor(starts.Length * progress), 0, starts.Length);
        return count >= starts.Length ? text.Length : starts[count];
    }
}
