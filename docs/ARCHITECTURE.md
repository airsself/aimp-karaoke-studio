# Architecture v0.3 — C++ AIMP bridge + WPF renderer

## Processing model

AIMP 5.40 Player/Lyrics/AlbumArt interfaces → native plugin `KaraokePlugin` → PID-scoped local Named Pipe → .NET WPF renderer. Native SDK methods are accessed from AIMP plugin code, not from the WPF client; incoming UI commands enter a bounded at-most-once queue; AIMP's message-only HWND and CoreMessage hooks both drain that queue from the plugin thread. This depends on the thread's Win32 message pump and **must be validated in AIMP**.

Native async lyric/art tasks store `TTaskHandle`. Track changes invalidate the generation and cancel requests using SDK WAITFOR flags; plugin Finalize also waits for callback activity to drain. Actual SDK unload/cancel semantics require an AIMP 5.40 stress test; never infer it from static inspection alone.

`PipeServer` uses per-user SDDL, rejects remote clients, serializes UTF-8 JSON lines, caps track/lyrics and artwork messages and collapses stale position updates. Only one WPF process per AIMP PID/pipe is launched; the app rejects arbitrary pipe names.

## Events

```
{"type":"track","generation":4,"title":"Song","artist":"Artist","album":"Album","filePath":"C:\\Music\\song.flac","durationMs":180000}
{"type":"lyrics","generation":4,"source":"loading","synced":false,"lines":[]}
{"type":"lyrics","generation":4,"source":"local-lrc","synced":true,"lines":[{"startMs":1200,"endMs":3400,"text":"example"}]}
{"type":"artwork","generation":4,"artworkData":"<base64 png/jpg>"}
{"type":"state","generation":4,"state":2}
{"type":"position","generation":4,"positionMs":1300}
```

The `artworkData` buffer is accepted only if the AIMP `IAIMPImageContainer` returns a small PNG/JPEG-encoded byte array. Otherwise WPF asynchronously scans local cover/folder/sidecar artwork. AIMP may use image containers with other encodings; validation on Windows is necessary.

UI commands use `{"command":"toggle"}`, `{"command":"next"}`, `{"command":"previous"}`, `{"command":"seek","positionMs":12000}`.

## Lyrics UX

The lyric list is a WPF `ListBox` backed by a recycling `VirtualizingStackPanel` with pixel scrolling. Only nearby rows are restyled during progression; all are restyled on explicit changes to appearance. Reading, Karaoke and Stage modes share the same model. `FindCurrentLine` honors `EndMs` and returns no active line during instrumental gaps. Users can manually scroll without the follow engine immediately returning to the active line, then press the follow button.

LRC parsing applies the final `[offset:]` globally, coalesces equal timestamps and supports UTF-8/UTF-16 plus legacy ANSI fallback on Windows. Word-level highlighting is not fabricated without enhanced word timings.

## Error handling

Native diagnostics: `OutputDebugStringW`; WPF logs: `%LOCALAPPDATA%\AimpKaraokeStudio\karaoke.log` (rotates at 2 MB). Missing/corrupt images are non-fatal; oversized lyrics produce explicit IPC errors. All UI events carry `generation` for stale-event rejection.

## Remaining integration gates

MSVC and WPF build, SDK callback cancellation, Win32 command-thread message pump, album-art encoding, lyrics offset interpretation, mixed DPI and performance must be tested on Windows with AIMP 5.40 before release.
