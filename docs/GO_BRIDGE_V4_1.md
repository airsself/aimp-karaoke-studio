# Open-Source Go Bridge Server — v4.1

## Reference/provenance
The user supplied AIMP-Lyrics-Bridge-Server-Binary-Reference-v2.5.0(1).zip.
It contains four precompiled Windows binaries (amd64 and arm64 server+CLI),
a config JSON, a WebLyrics INI, manifest and protocol note. Go 1.23.2
build metadata identifies the old module but does NOT disclose its source.
All bridge-go/*.go files in v4.1 are independent open-source implementations
of the DOCUMENTED HTTP protocol, not recovered or decompiled original code.

## Architecture
`MainWindow.xaml.cs` -> starts Go child `KaraokeBridge.exe` with parent PID.
`plugin/BatchLyrics.cpp` -> WinHTTP loopback GET /lyrics, existing hotkeys.
`bridge-go/cmd/aimp-lyrics-bridge` -> config, provider wiring, HTTP server.
`internal/httpapi` -> GET /lyrics with legacy markers, /healthz, /api/v1/search.
`internal/engine` -> strategy, score ordering, TTL caching.
`internal/provider` -> LRCLIB and lyrics.ovh with bounded HTTP responses.
`internal/scoring` -> predictable artist/title/duration score.
`internal/cache` -> hashed-key JSON persistence with atomic writes and TTL.
`internal/lrc` -> LRC / Enhanced-LRC timing inspection.
`internal/config` -> defaults, validation, 127.0.0.1 binding.
`internal/process` -> parent watchdog (Windows kernel process handle).

## Legacy compatibility
GET http://127.0.0.1:17321/lyrics?artist=A&title=B&album=C&mode=best
RC2 force and nocache query parameters are honored.
Old AIMP WebLyrics INI consumes AIMPLB-ARTIST-BEGIN/END,
AIMPLB-TITLE-BEGIN/END, AIMPLB-ALBUM-BEGIN/END,
AIMPLB-LYRICS-BEGIN/END and AIMPLB-LINEBREAK-7F3C.
AIMP will use the first available port 17321–17340. Server refuses
non-loopback Host values and never listens on a public interface.

## New API
GET /healthz -> JSON {status:ok,engine:go,version:4.1}.
GET /api/v1/search?artist=...&title=...&album=...&duration=...
  -> typed JSON best/candidates/cached/diagnostic.
These new endpoints are NOT advertised as original v2.5 interfaces.

## Configuration
Packaged file: bridge.config.json, copied from bridge-go/config.default.json.
Editable JSON with the documented provider order, score thresholds,
positive/negative TTL, max cache entries, timeouts and loopback listen.
The original reference contains options for Bandcamp, SongLyrics, proxy,
management_token, dashboard and circuit breaker. Those original binary
behaviors are NOT implemented here; unsupported provider names are skipped
with log messages. There is no management endpoint.

## Performance/safety
Go server is separate from WPF dispatcher and AIMP audio thread.
Cache writes use temporary files and atomic rename; lookup cancellation
observes request context, provider body size capped to 1 MiB.
Native Metadata-Safe Tag Editor implementation is UNCHANGED from v4.
Original music files should be backed up before using batch metadata writes.

## Verification
- Go: go test ./... and go vet ./... in bridge-go.
- Windows: Go source compiled to KaraokeBridge.exe, packaged into aimppack.
- Native: one AIMPPluginGetHeader, RC2 action IDs and no unsafe SetToAll.
- AIMP portable smoke: DLL and IPC, karaoke, lyrics finder, Go /healthz
  engine=go, legacy markers and native action registration.
- User acceptance: real provider network results and batch tag writes
  on disposable files, then full library only after backups.
