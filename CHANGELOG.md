# Changelog

## 2.0.0 — Bilingual Lyrics / auto Russian translation (2026-11-04)

- Add a toolbar toggle splitting the lyric canvas into original and Russian columns.
- Preserve synchronized lyrics and karaoke timings with one virtualized row per timestamp.
- Opt-in automatic Russian translation via the HTTPS MyMemory public API.
- Cache translations by hashed original text and language for repeat playback.
- Prioritize the current line and upcoming lyrics; show progress and quota/error messages.
- Allow manual source-language selection for non-English Latin-script songs.
- Cancel pending translation when changing songs, input language, or closing the pane.
- Keep native AIMP IPC and player controls untouched.
- Add translation mock-HTTP unit tests and Windows split-pane smoke verification.


## 0.4.1 — Transport controls and uniform secondary lyrics (2026-11-03)

- Remove the "Следующая строка" caption entirely.
- Only the currently sung line is large and accent-colored; all other lines use the same smaller typography.
- Replace unreliable AIMP init-thread HWND posted-command delivery with SDK `IAIMPServiceThreads::ExecuteInMainThread`.
- Add observable command acknowledgements and error logging for transport and seek.
- Seek to the pointer-release coordinate rather than the slider's stale pre-release value, and support keyboard seek on slider arrows.
- Verify seek and pause/resume on official AIMP 5.40 x64 via Windows smoke test.


## 0.4.0 — Vocal Focus / Precision Karaoke (2026-11-03)

- Implement B (Vocal Focus): compact toolbar, dark violet focus background, stronger active and next lyric hierarchy, anchor compensation for beginning/end of lyrics.
- Remove the thin line progress bar. Replace it with two-color, inline, left-to-right karaoke highlighting.
- Parse Enhanced LRC word timings in native C++ and managed C#; retain ordinary-LRC approximate fallback.
- Keep per-word timestamps in IPC while preserving the existing generation-based track/position protocol.
- Improve keyboard focus cues, responsive settings overlay, settings migration and tests.
- docs/design/05_DESIGN_QA.md tracks what CI confirms and what requires interactive AIMP testing.

## 0.3.0 — audit remediation candidate (2026-10-28)

- Correct AIMP x64 package layout and add package validator.
- Cancel tracked Lyrics and AlbumArt tasks with SDK WAITFOR and drain in-flight callbacks before unload.
- Add generation filtering to position/state/artwork messages.
- Bound IPC lyrics messages; reject oversized payloads with an explicit error instead of reconnect loops.
- Scope renderer windows and mutex to AIMP process PID.
- Parse LRC offset globally; merge bilingual lines with equal timestamps; preserve ANSI and UTF-16 formats.
- Honor end timestamps and instrumental gaps, render plain lyrics at full reading size.
- Virtualize lyrics row containers and update only affected item styles during playback.
- Implement borderless monitor fullscreen, manual browsing/re-follow, working-monitor restore, wrapped text line height.
- Decode safe, size-limited AIMP album-art byte buffers; asynchronous local sidecar fallback.
- Add IPC diagnostics, Windows CI configuration, tests, package validation, and explicit verification gates.

**Not yet verified:** MSVC/.NET build, installed AIMP runtime, and SDK callback/shutdown behavior on Windows.
