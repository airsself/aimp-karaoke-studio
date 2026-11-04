# AIMP Karaoke Studio v3 — Lyrics Discovery, source provenance, QA

## Input archive
Reviewed AIMP-Lyrics-Bridge-Codex-Handoff-v2.5.0-RC2.zip including
README_FIRST_RU, PROJECT_STATE_RU, project/docs/ARCHITECTURE_RU,
project/docs/BATCH_LYRICS_RU, project/docs/THIRD_PARTY,
project/installer/payload/config.default.json and
project/hotkey-plugin/plugin.cpp.

The main bridge server, CLI and third-party WebLyrics plugin are
**binary-only**. Only the hotkey/batch add-on, launcher and installer have
source. No binaries or licensed third-party DLLs from that archive are
redistributed here. This v3 implementation is original source code that
implements the documented existing localhost marker protocol.

## Legacy Bridge protocol adapter
- 127.0.0.1 ports 17321–17340, GET /lyrics.
- Parameters: artist, title, album, mode=best (forced refresh adds force=1
  and nocache=1).
- Response starts AIMPLB-LYRICS-BEGIN, ends AIMPLB-LYRICS-END.
- AIMPLB-LINEBREAK-7F3C is decoded to newline.
- Failure may be identified by AIMPLB-STATUS marker.
- The client probes listeners on loopback, doesn't execute code and doesn't
  install the original Bridge. Its absence is not an error.

## Standalone provider strategy
1. Legacy Bridge (when already installed and enabled).
2. LRCLIB /api/get with artist, title and optional album/duration.
3. LRCLIB /api/search, maximum 20 candidates per API request.
4. lyrics.ovh /v1/{artist}/{title} plain-text fallback.

The LRCLIB client provides User-Agent, processes 404/429 and bounds
payload size. Confidence uses normalized title+artist and duration.
Automatic playback requires >=0.93 confidence and preserves LRC timestamps.
Lower-confidence candidates must be selected manually. Provider errors
don't stop playback.

External docs: https://lrclib.net/docs and
https://github.com/NTag/lyrics.ovh

## UI/persistence
- Footer **Найти** opens LyricsSearchWindow.
- Manual edit title/artist, search, compare, preview, Apply.
- **Сохранить LRC…** opens a native SaveFileDialog, writes a separate
  UTF-8 .lrc atomically. It NEVER changes any part of the source MP3/FLAC.
- Existing **LRC** button remains for local file import.
- Settings expose automatic missing-text lookup and Legacy Bridge option.
- 30-day hashed-key positive cache in LocalAppData/AimpKaraokeStudio/
  lyrics-cache-v3.json.
- Async requests are cancelled when track changes / source is overridden.

## Metadata integrity
The supplied RC1 had a critical incident: IAIMPFileTagEditor.SetToAll with
partial IAIMPFileInfo erased metadata other than Lyrics. The v3 discovery
and save flow does NOT call AIMP TagEditor at all. Its only write target
is a user-selected .lrc sidecar, never an audio file. Dedicated tests
snapshot the input audio bytes and assert no changes.

## QA gates
- [ ] Managed mock HTTP tests for exact/search/fallback/matching
- [ ] Legacy marker decoding (including escaped newlines)
- [ ] Cancellation, cache TTL / on-disk cache tests
- [ ] Sidecar save, overwrite protection and audio-byte integrity tests
- [ ] Windows WPF compile and AIMP 5.40 native DLL/IPC smoke
- [ ] Manual chooser UI and downloaded LRC display verification
- [ ] Bilingual Russian translation on newly found lyrics
- [ ] Real-song and provider access review by the user

Limitations: No Bandcamp/SongLyrics scraping adapters (their source
wasn't included), no old binary redistribution, no bulk tag-writing,
and no claims for AIMP 6/x86 support.
