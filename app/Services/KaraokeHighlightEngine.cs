using System.Globalization;
using KaraokeStudio.Models;

namespace KaraokeStudio.Services;

/// <summary>
/// A UTF-16/Unicode-safe, allocation-free hot path after preparation.
/// Enhanced LRC word boundaries and grapheme positions are calculated once
/// per active line, not 30-60 times each second by the WPF frame timer.
/// </summary>
public static class KaraokeHighlightEngine
{
    public sealed class PreparedLine
    {
        private readonly string _text;
        private readonly int[] _wholeGraphemes;
        private readonly WordSpan[]? _segments;

        private readonly record struct WordSpan(
            int StartMs, int EndMs, int Prefix,
            int Length, int[] Graphemes);

        internal PreparedLine(string text, IReadOnlyList<WordTimingDto>? words)
        {
            _text = text;
            if (words is { Count: > 0 and <= 256 })
            {
                int total = 0;
                bool valid = true;
                foreach (var word in words)
                {
                    if (word.Text is null || total + word.Text.Length > text.Length ||
                        !text.AsSpan(total, word.Text.Length).SequenceEqual(word.Text.AsSpan()))
                    {
                        valid = false;
                        break;
                    }
                    total += word.Text.Length;
                }
                if (valid && total == text.Length)
                {
                    var segments = new WordSpan[words.Count];
                    int prefix = 0;
                    for (int i = 0; i < words.Count; i++)
                    {
                        var word = words[i];
                        segments[i] = new WordSpan(word.StartMs, word.EndMs,
                            prefix, word.Text.Length,
                            StringInfo.ParseCombiningCharacters(word.Text));
                        prefix += word.Text.Length;
                    }
                    _segments = segments;
                }
            }
            // The fallback's whole-line grapheme boundaries are also cached.
            _wholeGraphemes = _segments is null
                ? StringInfo.ParseCombiningCharacters(_text)
                : [];
        }

        public int GetHighlightLength(int lineStartMs, int lineEndMs,
            double progressPercent)
        {
            if (_text.Length == 0) return 0;
            double normalized = double.IsFinite(progressPercent)
                ? Math.Clamp(progressPercent / 100.0, 0, 1) : 0;
            if (_segments is { } words)
            {
                double time = lineStartMs + normalized *
                    Math.Max(1, lineEndMs - lineStartMs);
                int prefix = 0;
                foreach (var word in words)
                {
                    if (time >= word.EndMs) prefix += word.Length;
                    else if (time > word.StartMs)
                    {
                        double fraction = (time - word.StartMs) /
                            Math.Max(1, word.EndMs - word.StartMs);
                        prefix += GraphemeCut(word.Graphemes, word.Length, fraction);
                        break;
                    }
                    else break;
                }
                return Math.Clamp(prefix, 0, _text.Length);
            }
            return GraphemeCut(_wholeGraphemes, _text.Length, normalized);
        }
    }

    public static PreparedLine Prepare(string text, IReadOnlyList<WordTimingDto>? words)
        => new(text ?? "", words);

    // Keep the public API and timing semantics used by existing parser tests.
    public static int GetHighlightLength(string text,
        IReadOnlyList<WordTimingDto>? words, int lineStartMs, int lineEndMs,
        double progressPercent)
        => Prepare(text, words).GetHighlightLength(lineStartMs, lineEndMs, progressPercent);

    private static int GraphemeCut(int[] starts, int textLength, double fraction)
    {
        if (fraction <= 0 || textLength == 0) return 0;
        if (fraction >= 1) return textLength;
        int count = Math.Clamp((int)Math.Floor(starts.Length * fraction),
            0, starts.Length);
        return count >= starts.Length ? textLength : starts[count];
    }
}
