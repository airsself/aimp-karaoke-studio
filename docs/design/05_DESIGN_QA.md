# 05 — WPF design and precision karaoke QA

Status: **Windows CI build, C++/C# parser tests, and live AIMP 5.40 DLL/IPC/WPF startup verified in GitHub Actions. User playback/visual QA remains necessary.**

| Test | Status |
|---|---|
| Design B theme resources and typography | Code updated; runtime screenshot pending |
| Track/mode/settings/transport remain available | Source review; interactive runtime test pending |
| No progress bar beneath active line | Code updated |
| Active vs next lyric hierarchy | Code updated |
| Stable focus anchor including first and last phrase | Padding added; interactive scroll verification pending |
| Enhanced LRC real word timestamps | C++ and C# parser implementations; CI tests pending |
| Plain LRC approximate color sweep | C# highlight engine; CI tests pending |
| Unicode / emoji split boundaries | C# test authored |
| Small viewport settings overlay | Code updated; screenshot pending |
| Accessibility keyboard focus | Style updated; keyboard inspection pending |
| WPF executable compiled on Windows | PASS — GitHub Actions |
| Native AIMP plugin compiled on Windows | PASS — GitHub Actions |
| Actual AIMP 5.40 playback, DLL and IPC connection, WPF window creation | PASS in CI using verified portable AIMP 5.40 x64; first-run wizard still limits full visual validation |

No HTML prototype is described as a screenshot of the native WPF application. 

## v0.4.1 user-reported corrections (2026-11-03)

- Next-line caption removed from the template.
- Only current line uses a larger font. All non-current lines use a single small font size and tone.
- Player command transport marshalled through AIMP's official main-thread service, no dependency on an arbitrary HWND message loop.
- IPC command acknowledgement with state and position values. Seek click coordinate and keyboard arrows supported.
- CI smoke test will send seek and pause/resume through the real AIMP pipe, then inspect command results and actual player position.
