# Go Lyrics Bridge Server

Original MIT-licensed implementation for AIMP Karaoke Studio v4.1.
Uses no third-party Go modules; builds with Go 1.23.2+.

## Run tests

cd bridge-go
go test ./... -count=1
go vet ./...

## Build Windows x64

cd bridge-go
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o KaraokeBridge.exe ./cmd/aimp-lyrics-bridge

## Run

KaraokeBridge.exe --config bridge.config.json --parent-pid 12345

The WPF app manages the server process automatically during normal
operation. Start manually only for development; never expose port 17321
beyond localhost. It will try successive ports through 17340 if occupied.

GET /lyrics?artist=...&title=...&album=...&mode=best — legacy markers.
GET /healthz — liveness JSON.
GET /api/v1/search?artist=...&title=... — typed JSON candidates.

## Packages

- cmd/aimp-lyrics-bridge: executable entrypoint and lifecycle
- internal/httpapi: HTTP validation/legacy markers/versioned API
- internal/provider: LRCLIB and lyrics.ovh adapters
- internal/scoring: match confidence
- internal/cache: bounded TTL JSON cache
- internal/lrc: standard and Enhanced LRC parsing
- internal/config: typed file defaults/validation
- internal/engine: orchestration and ranking
- internal/model: shared pure record types
- internal/process: parent process liveness

Only the known protocol from v2.5 binary reference is reproduced.
Undocumented CLI options and proprietary scraping are not reconstructed.
