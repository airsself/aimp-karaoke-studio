# AIMP Karaoke Studio v3.1 — performance-optimized Karaoke Studio

An AIMP 5.40 C++ plugin and a separate WPF 10 Windows lyrics window, styled to match the original AIMP dark UI.

## Features

- AIMP SDK `IAIMPServicePlayer` and `IAIMPServiceLyrics` integration, and local `.lrc` fallback.
- PID-scoped UTF-8 Named Pipe, safe serialized messages, generation-based stale update filtering.
- Three lyrics modes (Reading, Karaoke, Stage), **Vocal Focus** layout, virtualized lyric text, inline karaoke fill, font scaling, smooth line scroll, full-screen, manual reading and live controls.
- Offline embedded album artwork via AIMP AlbumArt (for PNG/JPEG encoded image container data up to 450 kB), asynchronous local cover fallback.
- Windows configuration persistence, parser tests, build/package scripts and diagnostic logs.

## Performance update v3.1

- Precalculate Unicode grapheme and Enhanced-LRC word boundaries once
  per active lyric line. Only update bound lyric text when the visible
  character boundary changes (instead of allocating on each timer tick).
- Adaptive WPF redraw cadence: ~30 Hz when playing/smooth-scrolling,
  ~7 Hz when paused and ~3 Hz while minimized. AIMP's monotonic
  playback position and lyric timestamps remain unchanged.
- Cache ambient artwork in a reduced-scale WPF bitmap and lower the
  expensive blur radius. Original sharp album artwork remains intact.
- Avoid refreshing all lyric ViewModels for every minor SizeChanged event.
- Group online translation cache disk writes (every 20 newly translated
  lines and on completion/cancellation), perform JSON serialization off
  the UI thread and yield periodically when replaying a warm cache.
- Dispose translation cancellation tokens after the async operation
  finishes. All playback buttons, Enhanced LRC timing, Russian
  translation and Lyrics Discovery remain supported.

Measured benchmark and AIMP smoke diagnostics are in the GitHub Actions
build log; metrics reflect the CI host and do not guarantee identical
results on every laptop/GPU. See docs/performance-v3.1.md.

## Lyrics discovery and download (v3.0)

The user-provided AIMP Lyrics Bridge 2.5.0 RC2 archive documents a localhost
HTTP marker protocol. The Bridge provider EXE, CLI and third-party WebLyrics DLL
are binary-only and are NOT redistributed by Karaoke Studio.

Karaoke Studio searches:
1. An already running old Lyrics Bridge on 127.0.0.1 ports 17321–17340,
   using /lyrics with artist/title/album and the AIMPLB marker protocol.
2. LRCLIB /api/get (exact metadata and duration), then /api/search.
   Synchronized LRC is retained with line timestamps.
3. lyrics.ovh as a plain-lyrics fallback when no good synchronized LRC exists.

Click **Найти** in the footer to search, preview, apply and optionally
**Сохранить LRC…** in a separate .lrc file. Fix artist/title manually where
the AIMP metadata is incorrect. The original **LRC** button continues to
open local files. New Settings switches allow turning off automatic
missing-text searches and the old local Bridge adapter.

Automatic lookup may populate missing text if AIMP and local LRC find none.
The app doesn't auto-apply ambiguous matches. Successful matches are cached
locally for 30 days at:
%LOCALAPPDATA%/AimpKaraokeStudio/lyrics-cache-v3.json

Existing audio files and tags are NEVER written by this feature. Only a
user-selected .lrc sidecar can be saved. Overwrite confirmation is explicit.
Automatic queries send artist/title to external online providers; disable
the setting to opt out.

Provider details, tests and old-code provenance are in
docs/design/07_LYRICS_DISCOVERY_V3.md.

## Bilingual Lyrics (v2.0)

Click **RU · Перевод** to split the lyric canvas into synchronized original
and Russian translation columns. Both columns share the same virtualized
row, timestamp and scroll position; the original retains its karaoke
word-timed highlighting. AIMP native DLL and IPC are unchanged.

Translation begins automatically only after the user enables the panel
(and on later tracks while the panel remains enabled). The app sends
**only lyric text**, not audio files, filenames or identity data, over HTTPS
to the MyMemory public translation API. Repeated translations use a local
cache at %LOCALAPPDATA%/AimpKaraokeStudio/translations.v1.json.

MyMemory requires no key for anonymous usage, but limits requests to
500 bytes per segment and 5,000 characters/day. The app chunks long lines,
prioritizes the current and next phrases, and reports quota/network errors
without disrupting playback. Automatic language selection detects scripts;
English is the Latin-script default and other input languages can be
selected in Settings.

Official provider docs: https://mymemory.translated.net/doc/spec.php
and https://mymemory.translated.net/doc/usagelimits.php

## Word-timed karaoke

Enhanced LRC supports `<mm:ss.xx>` word timestamps within `[mm:ss.xx]` lines. Exact timestamps are serialized to WPF and rendered with a left-to-right sung-color highlight. For ordinary LRC a clearly labeled **approximate** highlight is used. The old under-line progress bar has been removed. Example and limitations: `docs/design/PRECISION_KARAOKE.md`.

## Status

**Windows compilation and parser tests must be confirmed by the current GitHub Actions run. Real AIMP interactive testing has not been performed.** See `docs/REMEDIATION_REPORT.md` for fixed items and validation risks. The user's original v0.2 source remains untouched.

## Build on Windows

Prerequisites: Visual Studio 2022 C++ tools + CMake, .NET 10 SDK, AIMP 5.40 C++ SDK.

```powershell
./build.ps1 -SdkDir 'C:\AIMP-SDK\Sources\Cpp' -Architecture x64
```

A successful build writes `dist/AimpKaraokeStudio-x64.aimppack`. The package uses `AimpKaraokeStudio/x64/AimpKaraokeStudio.dll` and the adjacent WPF files. Install in AIMP → Settings → Plugins → Install.

The standalone WPF application can also run as `KaraokeStudio.exe --demo` once compiled.

## Tests

Portable native LRC tests:

```bash
cmake -S tests -B build/parser && cmake --build build/parser && ctest --test-dir build/parser --output-on-failure
python tests/verify_design_bridge.py
```

Managed LRC tests (Windows/.NET 10):

```powershell
dotnet run --project tests/LrcReaderTests/LrcReaderTests.csproj -c Release
python tests/verify_package.py dist/AimpKaraokeStudio-x64.aimppack x64
```

Logs from the renderer: `%LOCALAPPDATA%\AimpKaraokeStudio\karaoke.log`. Native diagnostic messages go to Windows debugger OutputDebugString.

## Code layout

`plugin/` native bridge and parsers; `app/` WPF renderer, data models and IPC client; `tests/` portable/native/managed static test sources; `docs/` architecture, design, verification, remediation, original visual target.

## License

MIT (project-authored code). AIMP SDK remains third-party and is fetched separately; SDK binaries/headers are not bundled in the source archive.
