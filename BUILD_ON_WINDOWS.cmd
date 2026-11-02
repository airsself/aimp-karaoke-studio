@echo off
setlocal
cd /d "%~dp0"
echo.
echo  AIMP KARAOKE STUDIO v0.3 - WINDOWS RELEASE BUILDER
echo  =====================================================
echo  This is a SOURCE BUILD, not a prebuilt installer.
echo  Needs Visual Studio 2022 C++ Build Tools, .NET 10 SDK, internet for AIMP SDK.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_release_windows.ps1" -Architecture x64
if errorlevel 1 (
  echo.
  echo BUILD FAILED. See diagnostics above and build\windows-release-*.log
  pause
  exit /b 1
)
echo.
echo SUCCESS - check dist\AimpKaraokeStudio-x64.aimppack
pause
