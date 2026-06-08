@echo off
setlocal

REM ----------------------------------------------------------------
REM build-release.bat
REM Publishes AIUsageOverlay as a self-contained single exe.
REM Output: publish\AIUsageOverlay.exe
REM ----------------------------------------------------------------

set ROOT=%~dp0
set PROJECT=%ROOT%AIUsageOverlay\AIUsageOverlay.csproj
set OUTPUT=%ROOT%publish

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

REM Clean previous output
if exist "%OUTPUT%" (
    echo [INFO] Cleaning previous build...
    rmdir /s /q "%OUTPUT%"
)

REM Restore NuGet packages
echo [INFO] Restoring packages...
dotnet restore "%PROJECT%"
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Package restore failed.
    pause
    exit /b 1
)

REM Publish as single self-contained exe
echo [INFO] Publishing...
dotnet publish "%PROJECT%" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o "%OUTPUT%"

if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Build failed. See messages above.
    pause
    exit /b 1
)

echo.
echo ========================================
echo  Build complete!
echo  Output: %OUTPUT%\AIUsageOverlay.exe
echo ========================================
echo.

REM Open output folder in Explorer
explorer "%OUTPUT%"

endlocal
pause
