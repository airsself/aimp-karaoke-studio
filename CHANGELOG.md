# Changelog

## 4.1.1 — Fix applying a selected lyrics result

- Fix WPF search dialog's disposed CancellationTokenSource reference.
  Previously, successful search disposed the CTS without clearing the
  shared field; Apply set DialogResult=true, which closed the dialog
  and called Cancel() on the already-disposed CTS.
- Clear the shared reference BEFORE disposing a completed lookup.
  The active search task remains sole owner of the source lifetime.
- Close and repeat-search cancel only the current in-flight lookup.
- Add real Windows WPF dialog regression covering first search,
  repeated search, selection -> Apply and closing during an active request.
- Keep v4.1 Go Bridge, batch commands, word-timed lyrics, translation,
  performance optimizations and metadata writer unchanged.


## 4.1.0 — Open Go Bridge Server with independent modules

- Add a clean-room Go 1.23.2 source implementation of the documented
  v2.5 localhost marker protocol; no server source was included in the
  supplied binary reference.
- Modularize HTTP API, online providers, metadata scoring, persistent TTL
  cache, Enhanced LRC parser, JSON configuration and orchestration.
- Preserve native v4 batch hotkeys, metadata-safe Lyrics tag writing and
  one registered AIMP DLL.
- Replace the active WPF in-process HTTP listener with a separately compiled
  Go executable launched and stopped by Karaoke Studio.
- Add /healthz and /api/v1/search without asserting undocumented legacy
  endpoints. Bind only to localhost with strict Host verification.
- Add Go offline unit tests, vet gate and build from source in Windows CI.
- Package KaraokeBridge.exe and bridge.config.json in the same .aimppack
  as AimpKaraokeStudio.dll and KaraokeStudio.exe.


## 4.0.0 — Karaoke Studio + Lyrics Bridge Batch RC2 merge

- Native single-DLL registration of current / selected-track / cancel actions.
- Preserve previous Ctrl+Alt+L and Ctrl+Alt+Shift+L hotkeys/action IDs.
- Snapshot and deduplicate selected playlist items; sequential non-blocking
  lookup and main-thread writes, progress notifications and cancellation.
- Metadata-safe per-tag Lyrics change; no SetToAll or unrelated tag edits.
- Fallback to non-overwriting .lrc sidecar for files without safe tags.
- Built-in loopback-only Lyrics Bridge marker protocol backed by LRCLIB /
  lyrics.ovh, with no closed-source Bridge binaries redistributed.
- Keep v3.1 renderer and bilingual translation functionality unchanged.
- Extend Windows CI with in-process HTTP protocol and source guard tests.


## 3.1.0 — Performance optimization (2026-11-04)

- Reduce per-tick memory allocation and CPU time in karaoke word highlighting:
  Unicode/grapheme and word-timing ranges are computed only once per active line.
- Bind highlighted text only when the visible character changes; remove
  unbound Progress PropertyChanged notifications on each frame.
- Adaptive playback UI cadence: 33ms active, 150ms paused, 300ms minimized;
  smoother, bounded scroll easing without 60-Hz WPF invalidation.
- Cache ambient cover rendering with quarter-resolution BitmapCache and
  replace the expensive full-surface BlurEffect radius 72 with radius 18.
- Skip unnecessary full-library ViewModel updates on small layout changes.
- Batch MyMemory translation disk writes, move cache serialization off the
  dispatcher, yield on large cached lyrics and flush on cancellation.
- Add managed highlight parity/alloc benchmarks, WPF view-model event-count
  tests, translation batch-cache regressions and live AIMP CPU/RAM telemetry.
- Preserve transport commands, lyrics retrieval, translation, LRC formats,
  API/protocol contracts and previous user settings.


## 3.0.0 — Lyrics Discovery / Lyrics Bridge integration (2026-11-04)

- Integrate the protocol of AIMP Lyrics Bridge 2.5.0 RC2 for localhost lookup.
- Standalone LRCLIB exact/search provider with confidence and duration match;
  lyrics.ovh fallback for plain, unsynchronized lyrics.
- Automatically search for missing lyrics without overriding existing text.
- WPF native finder window: edit artist/title, select, preview, apply, Save LRC.
- Cache successful lookups (30-day TTL), cancel outdated search on track change.
- Saving only a separate .lrc file and NEVER editing audio metadata/tags.
- Retain bilingual Russian translation, Enhanced LRC highlighting and transport.
- Deterministic mock HTTP, legacy protocol and audio-integrity tests.


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
