@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ----------------------------------------------------------------
REM build-release.bat
REM Publishes AIUsageOverlay as a framework-dependent exe.
REM Output: publish\                     (exe + native DLLs)
REM         AIUsageOverlay_release.zip   (distribution zip)
REM Usage : build-release.bat [version]  (example: 2.0.0)
REM
REM Framework-dependent: the .NET runtime is NOT bundled, so the exe
REM is small (~1-2MB instead of ~70MB). The target PC must have the
REM ".NET 9 Desktop Runtime" installed. install-runtime.bat (bundled
REM in the zip) installs it on the first run if missing.
REM ----------------------------------------------------------------

set ROOT=%~dp0
set PROJECT=%ROOT%AIUsageOverlay\AIUsageOverlay.csproj
set PROJDIR=%ROOT%AIUsageOverlay
set OUTPUT=%ROOT%publish
set "BUILD_VERSION=%~1"
set "VERSION_ARG="

REM Accept SemVer only before passing the value to MSBuild.
if defined BUILD_VERSION (
    powershell -NoProfile -Command "if ($env:BUILD_VERSION -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') { exit 1 }"
    if !ERRORLEVEL! neq 0 (
        echo [ERROR] Version must use SemVer without a leading v.
        exit /b 1
    )
    set "VERSION_ARG=-p:Version=!BUILD_VERSION!"
)

echo.
echo ========================================
echo  AI Usage Overlay - Release Build
echo ========================================
echo.

REM Check .NET SDK is installed
where dotnet > nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET SDK not found.
    echo Please install from: https://dotnet.microsoft.com/download/dotnet/9.0
    pause
    exit /b 1
)

echo [INFO] .NET SDK version:
dotnet --version
echo.

REM ----------------------------------------------------------------
REM Clean previous build artifacts
REM   Removes bin/, obj/, and publish/ to avoid stale file issues.
REM ----------------------------------------------------------------
echo [INFO] Cleaning previous build artifacts...

REM Intermediate outputs: AIUsageOverlay\bin
if exist "%PROJDIR%\bin" (
    echo   - removing "%PROJDIR%\bin"
    rmdir /s /q "%PROJDIR%\bin"
)

REM Intermediate outputs: AIUsageOverlay\obj
if exist "%PROJDIR%\obj" (
    echo   - removing "%PROJDIR%\obj"
    rmdir /s /q "%PROJDIR%\obj"
)

REM Previous publish output: publish\
if exist "%OUTPUT%" (
    echo   - removing "%OUTPUT%"
    rmdir /s /q "%OUTPUT%"
)
echo.

REM Restore NuGet packages
echo [INFO] Restoring packages...
dotnet restore "%PROJECT%"
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Package restore failed.
    pause
    exit /b 1
)

REM ----------------------------------------------------------------
REM Publish: native DLLs are placed beside the exe (not embedded).
REM   Omitting IncludeNativeLibrariesForSelfExtract outputs
REM   WebView2Loader.dll / wpfgfx_cor3.dll etc. next to the exe.
REM ----------------------------------------------------------------
echo [INFO] Publishing...
dotnet publish "%PROJECT%" ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -p:PublishSingleFile=true ^
  -p:DebugType=none ^
  !VERSION_ARG! ^
  -o "%OUTPUT%"

if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Build failed. See messages above.
    pause
    exit /b 1
)

REM ----------------------------------------------------------------
REM Create zip: exclude .pdb (debug symbols) and .xml (IntelliSense).
REM Copy to a temp dir first to reliably filter unwanted files.
REM ----------------------------------------------------------------
set ZIPNAME=%ROOT%AIUsageOverlay_release.zip
echo.
echo [INFO] Creating zip: %ZIPNAME%

if exist "%ZIPNAME%" del "%ZIPNAME%"

powershell -NoProfile -Command ^
  "$tmp = Join-Path $env:TEMP 'aiusage_zip_tmp';" ^
  "Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue;" ^
  "New-Item $tmp -ItemType Directory | Out-Null;" ^
  "Copy-Item '%OUTPUT%\*' $tmp -Recurse -Exclude @('*.pdb','*.xml');" ^
  "Copy-Item '%ROOT%install-runtime.bat' $tmp;" ^
  "Compress-Archive -Path \"$tmp\*\" -DestinationPath '%ZIPNAME%' -Force;" ^
  "Remove-Item $tmp -Recurse -Force;" ^
  "Write-Host '[INFO] Zip created successfully.'"

if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Zip creation failed.
    pause
    exit /b 1
)

echo.
echo ========================================
echo  Build complete!
echo  exe + DLLs : %OUTPUT%\
echo  Release zip: %ZIPNAME%
echo ========================================
echo.

REM Open output folder in Explorer
explorer "%OUTPUT%"

endlocal
pause
