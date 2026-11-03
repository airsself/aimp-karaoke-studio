# AIMP Karaoke Studio v0.4.1 — Transport Fix + Vocal Focus

An AIMP 5.40 C++ plugin and a separate WPF 10 Windows lyrics window, styled to match the original AIMP dark UI.

## Features

- AIMP SDK `IAIMPServicePlayer` and `IAIMPServiceLyrics` integration, and local `.lrc` fallback.
- PID-scoped UTF-8 Named Pipe, safe serialized messages, generation-based stale update filtering.
- Three lyrics modes (Reading, Karaoke, Stage), **Vocal Focus** layout, virtualized lyric text, inline karaoke fill, font scaling, smooth line scroll, full-screen, manual reading and live controls.
- Offline embedded album artwork via AIMP AlbumArt (for PNG/JPEG encoded image container data up to 450 kB), asynchronous local cover fallback.
- Windows configuration persistence, parser tests, build/package scripts and diagnostic logs.

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
