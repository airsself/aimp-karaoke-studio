AIMP Karaoke Studio v0.3 — Windows build kit (SOURCE, NOT COMPILED EXE)

There is NO prebuilt binary in this archive. The Linux environment used to
prepare the project did not contain MSVC, Windows SDK, .NET 10 SDK, or AIMP.
A Windows compile and real in-player verification are still required.

On Windows 10/11 x64:
1. Install Visual Studio 2022 with Desktop development with C++ and CMake.
2. Install .NET SDK 10.x.
3. Extract this archive, then double-click BUILD_ON_WINDOWS.cmd.
4. The builder downloads public AIMP SDK headers (internet required), runs the
   C++ and .NET parser tests and builds x64 DLL + self-contained WPF EXE.
5. If successful, install dist/AimpKaraokeStudio-x64.aimppack in
   AIMP > Settings > Plugins > Install.

Alternatively, run PowerShell:
  powershell -ExecutionPolicy Bypass -File .\build_release_windows.ps1 -Architecture both
  powershell -ExecutionPolicy Bypass -File .\build_release_windows.ps1 -Architecture x64 -SdkDir C:\SDK\Sources\Cpp

Log files: build/windows-release-*.log
Source: AIMP_Karaoke_Studio_v0.3_Audit_Fixes_Source.zip
