@echo off
setlocal DisableDelayedExpansion
set "WIS_LAUNCH_EXE=%~dp0WinImageStudio.exe"
if not exist "%WIS_LAUNCH_EXE%" (
    echo Extract the entire ZIP before starting the application.
    pause
    exit /b 1
)
powershell.exe -NoLogo -NoProfile -NonInteractive -Command "try { Start-Process -FilePath $env:WIS_LAUNCH_EXE -Verb RunAs -ErrorAction Stop } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"
if errorlevel 1 (
    echo Application startup was not approved or failed. No image was modified by this launcher.
    pause
    exit /b 1
)
exit /b 0
