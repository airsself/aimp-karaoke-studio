# AIMP Karaoke Studio v0.3 – installation checklist

This package contains source only until compiled on Windows. **Do not rename a source ZIP into `.aimppack`.**

1. Install Visual Studio 2022 with Desktop development with C++, CMake, and .NET 10 SDK.
2. Download AIMP 5.40 SDK (Sources/Cpp headers). Use a trusted official distribution; confirm `apiPlugin.h` exists.
3. In PowerShell from project root: `./build.ps1 -SdkDir 'C:\AIMP-SDK\Sources\Cpp' -Architecture x64` (or x86).
4. Run: `python tests/verify_package.py dist/AimpKaraokeStudio-x64.aimppack x64`.
5. In AIMP 5.40: Settings → Plugins → Install, select the freshly built `.aimppack`, then restart AIMP.
6. Use the player menu → Karaoke Studio, then verify lyrics sync and keyboard commands.
7. If the DLL or AIMP fails to initialize, inspect Windows debugger messages and `%LOCALAPPDATA%\AimpKaraokeStudio\karaoke.log`.

WARNING: SDK callback cancellation and plugin unload safety are not yet validated by running Windows/AIMP stress tests. Build a diagnostic configuration and test on a non-critical installation before distributing.
