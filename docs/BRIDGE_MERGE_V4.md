# Lyrics Bridge 2.5.0 Batch RC2 -> Karaoke Studio v4.0 source integration

Input archive: AIMP-Lyrics-Bridge-v2.5.0-batch-rc2-metadata-safe-source(1).zip.
46 archive entries, source: hotkey-plugin/plugin.cpp, plus installer/Go
launcher/tests/docs. No source or redistribution permissions for the
Bridge EXE, CLI or third-party WebLyrics DLL were present.

## Design
The RC2 batch action semantics are integrated as plugin/BatchLyrics.cpp
inside AimpKaraokeStudio.dll, not installed as a second AIMP plugin.
Original action IDs and default hotkeys are preserved.
The plugin snapshots active playlist selection, caps at 5000 items,
deduplicates by filepath, asks confirmation and downloads serially
on AIMP worker threads. Cancellation, notifications and main-thread
Tag Editor writes follow the RC2 threading discipline.

## Safety
Native writes use GetTag and SetValueAsObject(Lyrics) for existing
lyrics-capable tags only; ID3v1 is skipped. No SetToAll call is
allowed, because RC1 erased unrelated file metadata. If no supported
tag exists, the module writes a separate UTF-8 LRC file atomically
without overwriting an existing sidecar. The AIMP Tag Editor may
rewrite tag regions during Save, so backup copies are advisable
before a large batch operation. Old RC1 data loss cannot be undone.

## Built-in Bridge
The WPF window starts EmbeddedLyricsBridge on 127.0.0.1:17321–17340.
It implements the exact AIMPLB /lyrics marker and newline tokens,
delegating external lookup to the existing LRCLIB and lyrics.ovh service
without recursively calling itself. There is no second .NET executable
and no third-party WebLyrics binary. The native hotkey opens the
existing WPF UI if needed.

## Verification
- Automated test: native-compatible loopback HTTP markers, Unicode,
  not-found and malformed queries.
- Safety gate: one plugin entry point, native action IDs, no SetToAll.
- Build: native Windows x64 DLL, .NET 10 WPF renderer, .aimppack.
- AIMP portable smoke: DLL loading, IPC, pause/resume/seek, lyrics
  finder and bilingual layout.
- Manual checks still required: execute hotkeys and selected batch
  against disposable audio files; verify tags/artwork, sidecar
  fallback, cancellation and supported formats.

Known limit: Bandcamp/SongLyrics scrapers from the unavailable old
Bridge server cannot be reconstructed from these uploaded sources.
Only LRCLIB / lyrics.ovh providers are built in.
