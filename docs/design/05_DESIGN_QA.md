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
