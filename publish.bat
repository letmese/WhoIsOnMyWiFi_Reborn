@echo off
rem ============================================================
rem  Who Is On My WiFi Reborn - portable (self-contained) build
rem  Output: publish\  (runs on any Windows x64 PC, no .NET
rem  runtime required - copy the WHOLE folder)
rem ============================================================
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: the .NET 8 SDK was not found.
    echo Install it from https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
dotnet publish -c Release -r win-x64 --self-contained true -o publish
if errorlevel 1 (
    echo.
    echo PUBLISH FAILED - read the messages above.
    pause
    exit /b 1
)
echo.
echo PORTABLE BUILD OK: %~dp0publish\
echo Copy the whole "publish" folder to the target PC and run
echo WhoIsOnMyWiFi_Reborn.exe from inside it.
pause