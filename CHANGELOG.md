# Changelog

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
