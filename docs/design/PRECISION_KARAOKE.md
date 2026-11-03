# Precision Karaoke · Enhanced LRC specification

The renderer uses an inline **two-color text sweep**, not a progress bar.

## Supported input

Plain LRC — approximate karaoke sweep across grapheme clusters over the line duration:

```lrc
[00:01.00]A regular timed lyric line
[00:05.00]A new phrase appears
```

Enhanced LRC — exact phrase timing with **absolute word/chunk start timestamps**:

```lrc
[ar:Demo]
[ti:Precision test]
[offset:0]
[00:01.00]<00:01.00>Снова <00:01.50>споём <00:02.20>дальше<00:03.00>
[00:04.00]<00:04.00>Stay <00:04.60>with <00:05.15>me<00:05.90>
```

The final optional `<timestamp>` closes the final word interval. Without it, the final word ends at the phrase end. Each timed chunk is a complete word (optionally with its following spaces); spaces are retained when reconstructing the text. The global `[offset:...]` shifts both line and word timestamps.

## Algorithm

1. Parse `[mm:ss.xx]` line timestamps and `<mm:ss.xx>` word timestamps on both the native C++ plugin and the managed LRC reader.
2. Preserve original text; serialize `words: [{startMs,endMs,text}]` alongside each line through Named Pipes.
3. The highlight engine uses the current song position and interpolates **within the current timed chunk only**. Previously elapsed chunks are highlighted in full, future chunks remain dim.
4. If exact word times are absent, color progresses approximately across the grapheme clusters of the entire line.
5. WPF displays **two adjacent Runs**. It updates the split only when a grapheme boundary changes, avoiding mass updates to unrelated song lines.

## Limitations

Exact word alignment is only as accurate as the supplied Enhanced LRC. This implementation does **not** analyze the audio or infer phoneme timing. Plain LRC uses an approximate sweep, visibly labeled as such. Simultaneous bilingual lines with the same timestamp are merged and do not claim precise alignment. Enhanced LRC may be loaded through the local sidecar file or manually from the LRC button. AIMP Lyrics Service may return only line-level timings; the fallback then remains approximate.

## Release gates

C++ parser test, managed parser + highlight engine tests, Windows WPF compilation, packaged .aimppack verification; visual WPF test and real AIMP x64 playback/seek/closure require an interactive Windows session with AIMP.
