# AIMP Karaoke Studio v4.1.1 — Go Bridge and Lyrics Apply fix

An AIMP 5.40 C++ plugin and a separate WPF 10 Windows lyrics window, styled to match the original AIMP dark UI.

## Design system v1 (WPF)

The app now uses shared WPF design tokens and component templates for the main
karaoke window, settings and lyrics discovery. The 4-DIP grid, 36-DIP controls,
40/64/64-DIP chrome, 320-DIP responsive settings drawer, coherent selected/focus
states and typography are documented in [DESIGN_SYSTEM.md](docs/DESIGN_SYSTEM.md).

The implementation and source-of-truth files:
[Tokens.xaml](app/Styles/Tokens.xaml),
[Components.xaml](app/Styles/Components.xaml),
[DesignMetrics.cs](app/Design/DesignMetrics.cs) and
[design-system QA checklist](docs/design-system/QA_MATRIX.md).
CI validates resource resolution, token parity and color contrast; visual QA
is still required on different Windows display scaling settings.

## Features

- AIMP SDK `IAIMPServicePlayer` and `IAIMPServiceLyrics` integration, and local `.lrc` fallback.
- PID-scoped UTF-8 Named Pipe, safe serialized messages, generation-based stale update filtering.
- Three lyrics modes (Reading, Karaoke, Stage), **Vocal Focus** layout, virtualized lyric text, inline karaoke fill, font scaling, smooth line scroll, full-screen, manual reading and live controls.
- Offline embedded album artwork via AIMP AlbumArt (for PNG/JPEG encoded image container data up to 450 kB), asynchronous local cover fallback.
- Windows configuration persistence, parser tests, build/package scripts and diagnostic logs.

## v4.1 — fully open-source Go Bridge Server

The archived v2.5.0 binary was examined as an **interface reference only**;
it contains NO Go server source. v4.1 is a new independently developed Go
implementation licensed with this project under MIT. The server is built from
source as `KaraokeBridge.exe` and packaged alongside the ONE native AIMP
plugin DLL and the WPF Karaoke Studio app.

Modular Go source in `bridge-go/`:
- `internal/httpapi`: legacy `GET /lyrics`, `GET /healthz`, and new
  versioned `GET /api/v1/search` with JSON results.
- `internal/provider`: independent, mocked-HTTP-testable LRCLIB exact/search
  and lyrics.ovh adapters. Bandcamp/SongLyrics scraper behavior from the
  unavailable proprietary server is **not** claimed or included.
- `internal/scoring`: deterministic artist/title/duration match and synchronized
  LRC preference.
- `internal/cache`: hashed track keys, configurable TTL, optional negative
  cache and atomic on-disk persistence.
- `internal/lrc`: plain, synced and Enhanced LRC parser with timestamps and
  word segments; raw timed content is preserved without conversion.
- `internal/config`: typed, validated JSON config derived from the documented
  v2.5 contract, with safe loopback-only binding.
- `internal/engine`: provider orchestration, ranking and optional caching.
- `internal/process`: the Go server stops when its WPF parent closes, including
  unexpected Windows process termination.

**Native compatibility:** old RC2 batch actions and the WebLyrics provider
continue using `GET /lyrics?artist=...&title=...&album=...&mode=best`, with
`AIMPLB-*-BEGIN/END` markers and `AIMPLB-LINEBREAK-7F3C` separators.
No binary-only legacy server, Go CLI or third-party WebLyrics DLL is bundled.

The server starts automatically when the Karaoke Studio WPF window opens,
probes ports 17321–17340 on loopback, and is shut down with the window.
It is not exposed to the network. Artist/title data is transmitted to
LRCLIB and lyrics.ovh when searching. There is no remote management API.

**Build/test:** Install Go 1.23.2+ alongside the existing Visual Studio,
CMake and .NET 10 prerequisites. `build.ps1` runs
`go test ./...`, `go vet ./...` and compiles a standalone Windows
`KaraokeBridge.exe` from the open Go sources.

See `bridge-go/README.md` and `docs/GO_BRIDGE_V4_1.md`.

## v4.0 — one unified AIMP plug-in

**Single native plugin DLL:** AimpKaraokeStudio.dll hosts both the original
Karaoke Studio capabilities and the AIMP Lyrics Bridge 2.5.0 Batch RC2
hotkey / batch actions. The WPF window remains the only user interface.

**Hotkeys and commands:**
- Ctrl+Alt+L: refresh the current track's lyrics.
- Ctrl+Alt+Shift+L: download lyrics for selected playlist tracks after
  confirmation (also available in the AIMP playlist context menu).
- Cancel Lyrics Update: separate assignable action in AIMP hotkey settings.
- The former action IDs are retained; disable/uninstall the old standalone
  Lyrics Bridge hotkey plugin BEFORE installing this unified plugin,
  otherwise duplicate action identifiers may prevent registration.

**Standalone provider:** a lightweight loopback HTTP endpoint running only
while the Karaoke Studio WPF window is open implements the old
AIMPLB /lyrics marker protocol. The native action opens the same window
automatically. This endpoint reuses the integrated LRCLIB and lyrics.ovh
search, so NO separate old Bridge.exe, CLI, or aimp_webLyrics.dll is needed.
If the user's old Bridge server still occupies its default port, the
native worker may use it first; new lookup does not require that server.

**Safe writing:** compatible existing audio tags get only their Lyrics field
updated, skipping ID3v1; no TagEditor.SetToAll or wholesale metadata copy.
Unsupported/untagged formats fall back to a UTF-8 .lrc sidecar beside
the audio, without overwriting an existing .lrc. Save operations run
sequentially, batch selections are capped at 5000 and deduplicated.
Back up important audio files before first use of native AIMP Tag Editor.

**All existing functionality remains:** v3.1 performance optimizations,
karaoke timing, Russian translation, dual-column display, search/download,
playback controls, settings and manual LRC.

Technical integration details: docs/BRIDGE_MERGE_V4.md.

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
