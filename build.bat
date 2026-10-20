@echo off
rem ============================================================
rem  Who Is On My WiFi Reborn - quick build
rem  Output: bin\Release\net8.0-windows\WhoIsOnMyWiFi_Reborn.exe
rem  NOTE: run the app from that folder (do not copy the bare
rem  .exe without the dlls and runtimes\ folder).
rem ============================================================
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: the .NET 8 SDK was not found.
    echo Install it from https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
dotnet build -c Release
if errorlevel 1 (
    echo.
    echo BUILD FAILED - read the messages above.
    pause
    exit /b 1
)
echo.
echo BUILD OK.
echo App folder: %~dp0bin\Release\net8.0-windows\
echo To make a portable no-runtime-needed build, run publish.bat
pause