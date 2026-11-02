# Changelog

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
