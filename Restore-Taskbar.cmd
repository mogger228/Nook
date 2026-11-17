@echo off
if not exist "%~dp0bin\Nook.exe" (
  echo Nook.exe was not found. Build the project first.
  pause
  exit /b 1
)
"%~dp0bin\Nook.exe" --restore-taskbar "--data-dir=%~dp0data"
