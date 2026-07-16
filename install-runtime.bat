@echo off
REM ================================================================
REM install-runtime.bat
REM   Ensures the ".NET 9 Desktop Runtime" required by
REM   AIUsageOverlay.exe is installed. Run this once before the
REM   first launch. Console output is ASCII only.
REM
REM   Install order (auto fallback):
REM     1) bundled installer  windowsdesktop-runtime-*win-x64.exe
REM     2) winget
REM     3) download from Microsoft (https://aka.ms/dotnet/9.0/...)
REM ================================================================
setlocal enabledelayedexpansion

REM --- Self-elevate: installing a runtime requires administrator ---
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator privileges...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo.
echo ========================================
echo  .NET 9 Desktop Runtime Setup
echo ========================================
echo.

REM --- Already installed? ---
where dotnet >nul 2>&1
if %errorlevel%==0 (
    dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.WindowsDesktop.App 9." >nul
    if !errorlevel!==0 (
        echo [OK] .NET 9 Desktop Runtime is already installed.
        goto :done
    )
)

echo [INFO] .NET 9 Desktop Runtime not found. Installing...
echo.

set "DIR=%~dp0"

REM --- 1) Bundled installer beside this bat (offline) ---
set "BUNDLED="
for %%F in ("%DIR%windowsdesktop-runtime-*win-x64.exe") do set "BUNDLED=%%~fF"
if defined BUNDLED (
    echo [INFO] Using bundled installer: !BUNDLED!
    "!BUNDLED!" /install /quiet /norestart
    goto :verify
)

REM --- 2) winget ---
where winget >nul 2>&1
if %errorlevel%==0 (
    echo [INFO] Installing via winget...
    winget install --id Microsoft.DotNet.DesktopRuntime.9 --silent --accept-package-agreements --accept-source-agreements
    goto :verify
)

REM --- 3) Download the official installer ---
set "INSTALLER=%TEMP%\windowsdesktop-runtime-9-win-x64.exe"
echo [INFO] Downloading from Microsoft...
powershell -NoProfile -Command "try { Invoke-WebRequest -Uri 'https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe' -OutFile '%INSTALLER%' -UseBasicParsing } catch { exit 1 }"
if %errorlevel% neq 0 (
    echo [ERROR] Download failed. Please install manually from:
    echo   https://dotnet.microsoft.com/download/dotnet/9.0/runtime
    start "" "https://dotnet.microsoft.com/download/dotnet/9.0/runtime"
    goto :fail
)
echo [INFO] Running installer...
"%INSTALLER%" /install /quiet /norestart

:verify
echo.
dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.WindowsDesktop.App 9." >nul
if %errorlevel%==0 (
    echo [OK] Installation complete.
    goto :done
)
echo [WARN] Could not confirm the runtime. A restart may be required,
echo        or please install manually from:
echo   https://dotnet.microsoft.com/download/dotnet/9.0/runtime
goto :fail

:done
echo.
echo You can now start AIUsageOverlay.exe.
echo.
pause
exit /b 0

:fail
echo.
pause
exit /b 1
