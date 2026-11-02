# v0.3 Audit remediation — evidence and remaining validation

Source baseline: v0.2 integrated project. The changes address the 25 audit findings. **These are code changes with static/portable tests, not evidence that the AIMP plugin runs on Windows.**

| Finding | Change in v0.3 | Verification |
|---|---|---|
| P0-01 x64 package | Architecture-specific `x64/` placement, `verify_package.py` | Needs actual Windows package |
| P0-02 async callback lifetime | Track task handles; cancel with `WAITFOR`; epoch invalidation; in-flight callback barrier | Source review; **must stress test on AIMP**; SDK callback guarantee unverified |
| P1-01 fullscreen | Monitor bounds, DPI conversion, borderless/chrome preservation | Source review; live multi-monitor test pending |
| P1-02 end timestamp | Active index `-1` for gaps/past end | Source review; live UI test pending |
| P1-03 offset | Apply final `[offset:]` to all entries in both parsers | C++ parser tests passed; C# tests authored, not executed |
| P1-04 large lists | WPF virtualizing `ListBox`, incremental per-line restyling | Static/XAML verified; profile pending |
| P1-05 oversize IPC | Limit output JSON to ~800 kB; explicit error payload, no repeated invalid frames | Source review |
| P1-06 duplicate windows | PID-qualified caption; named mutex per pipe; validate pipe argument | Source review |
| P1-07 embedded cover | Offline AIMP `IAIMPServiceAlbumArt.Get2` with PNG/JPEG signature + 450 kB cap; sidecar async fallback | SDK header reviewed; depends on image container encoding; runtime test pending |
| P1-08 message-only HWND | Added a bounded at-most-once command queue drained by both `HWND_MESSAGE` wake events and AIMP CoreMessage fallback, with debugger diagnostics | **Not fully closed**: needs live command-delivery/thread-affinity test; stopped-state command delivery still depends on the HWND message pump and must be measured on Windows |
| P2-01 placeholder | `loading` separated from `missing` and `error` | Static |
| P2-02 charset | UTF-8/UTF-16/legacy ANSI fallback in both parsers | Native tested, managed not executed |
| P2-03 duplicate timestamp | Merge same-time lines | C++ tested; managed test authored |
| P2-04 manual scrolling | Pause follow on wheel; explicit return button | XAML static verified |
| P2-05 offscreen restore | Check visible rectangle against monitor working areas | Static; mixed-DPI runtime pending |
| P2-06 art blocking | Background decode with cancel, bounded local file size, refuse UNC | Static |
| P2-07 client cleanup | `finally` resets writer, controlled cancellation and logs | Static; forced disconnect scenario pending |
| P2-08 generations | Generation on `state`, `position`, `artwork` and UI filters | Static |
| P2-09 AIMP lyrics offset | Read property, deliberately avoid unverified double-adjustment | **Needs AIMP contract comparison** |
| P2-10 wrapped spacing | Bind `TextBlock.LineHeight` to text metrics; separate verse gap | Static/XAML |
| P2-11 plain text size | Full font size at scale 1.0 | Static |
| P3-01 tests | Windows managed parser test target, expanded native tests and test plan | Native tests passed |
| P3-02 `NDEBUG` | Test checks throw instead of `assert` | Native Release-style compilation passed |
| P3-03 CI and logs | GitHub Actions Windows x86/x64 matrix, structured WPF file logging, native debugger tracing | Workflow not yet executed |
| P3-04 release docs | MIT LICENSE, CHANGELOG and settings schema | Files present |

## Gated Windows verification

1. Build x86 and x64 with MSVC/Visual Studio 2022 and .NET 10.
2. Install generated `.aimppack` in clean AIMP 5.40. Verify correct assembly paths and menus.
3. Play FLAC/MP3/CUE with embedded and adjacent lyrics; verify line timing, offset and cover images.
4. Rapid track switching, AIMP process exit with outstanding lyrics/art callbacks, restart, UI crash, pipe disconnect.
5. Check command delivery in playing, paused and stopped states.
6. Test 5,000-row LRC and 800-kB payload boundary; profile memory/frame timings.
7. Multi-monitor/DPI placement and fullscreen behavior.

Blocking unresolved live checks: callback cancellation/unload guarantee, AIMP command-window pump, the SDK lyrics offset convention, and album-art buffer encoding. Do not advertise v0.3 as a stable release before these checks.
