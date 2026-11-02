# Verification — v0.3 source candidate

| Check | Result |
|---|---|
| Native C++ LRC parser (CMake/CTest) | PASS on Linux clang++ |
| Native parser with `-DNDEBUG -Werror` | PASS |
| WPF XAML well-formed XML | PASS |
| WPF XAML resource/event/name static check | PASS |
| Managed C# parser tests | Authored, not run (.NET 10 unavailable in container) |
| MSVC C++ plugin build | NOT RUN (Windows/MSVC unavailable) |
| WPF executable build | NOT RUN (Windows/.NET SDK unavailable) |
| Actual `.aimppack` installation | NOT RUN |
| AIMP callback lifetime during shutdown | NOT RUN |
| AIMP album-art data format | NOT RUN |
| AIMP UI command routing | NOT RUN |
| SDK lyrics offset contract | NOT RUN |
| Multi-monitor DPI and virtualization FPS | NOT RUN |

This is **not a stable Windows binary release**. `tests/verify_package.py` requires a real Windows-built `.aimppack`, and `.github/workflows/windows-build.yml` provides the intended Windows CI build/test matrix; neither has been run here.
