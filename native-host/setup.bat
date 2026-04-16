@echo off
setlocal EnableDelayedExpansion
title YT Downloader Pro - Setup
color 0B
cls

echo.
echo  ╔══════════════════════════════════════════════════════════╗
echo  ║        YT Downloader Pro - Native Component Setup        ║
echo  ║              Version 5.1.0 - Windows Installer             ║
echo  ╚══════════════════════════════════════════════════════════╝
echo.

:: Configuration
set "APP_NAME=YT-Downloader-Pro"
set "INSTALL_DIR=%LOCALAPPDATA%\%APP_NAME%"
set "LAUNCHER_NAME=launcher.bat"
set "PROTOCOL_NAME=ytdlp"

:: Create installation directory
echo  [*] Creating installation directory...
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%" 2>nul
    if !errorlevel! neq 0 (
        echo  [X] ERROR: Failed to create directory: %INSTALL_DIR%
        pause
        exit /b 1
    )
)
echo  [OK] Directory ready: %INSTALL_DIR%
echo.

:: ============================================================
:: DEPENDENCIES
:: ============================================================
echo  --- CHECKING DEPENDENCIES ---
echo.

call :CheckYtDlp
if !errorlevel! neq 0 (
    echo  [X] Failed to setup yt-dlp
    pause
    exit /b 1
)

call :CheckFfmpeg
if !errorlevel! neq 0 (
    echo  [X] Failed to setup FFmpeg
    pause
    exit /b 1
)

:: Automatically add to Environment Variables
call :AddToPath

echo.
echo  --- CONFIGURING SYSTEM INTEGRATION ---
echo.

call :CreateLauncher
if !errorlevel! neq 0 (
    echo  [X] Failed to create launcher
    pause
    exit /b 1
)

call :RegisterProtocol
if !errorlevel! neq 0 (
    echo  [X] Failed to register protocol
    pause
    exit /b 1
)

call :OptimizeConsole

echo.
echo  ╔══════════════════════════════════════════════════════════╗
echo  ║                 INSTALLATION COMPLETE                    ║
echo  ╠══════════════════════════════════════════════════════════╣
echo  ║ The tools have been installed and added to your system   ║
echo  ║ Environment Variables automatically.                     ║
echo  ║                                                          ║
echo  ║ You can now use the Chrome extension to download         ║
echo  ║ YouTube videos directly to your PC!                      ║
echo  ╚══════════════════════════════════════════════════════════╝
echo.
pause
exit /b 0

:: ============================================================
:: SUBROUTINES
:: ============================================================

:CheckYtDlp
echo  [ ] Checking for yt-dlp...

:: 1. Check global Windows PATH first
where yt-dlp >nul 2>&1
if !errorlevel! equ 0 (
    for /f "tokens=*" %%a in ('yt-dlp --version') do set "YTDLP_VERSION=%%a"
    echo  [OK] Global yt-dlp detected (v!YTDLP_VERSION!)
    exit /b 0
)

:: 2. Check local AppData folder
if exist "%INSTALL_DIR%\yt-dlp.exe" (
    for /f "tokens=*" %%a in ('"%INSTALL_DIR%\yt-dlp.exe" --version') do set "YTDLP_VERSION=%%a"
    echo  [OK] Local yt-dlp found (v!YTDLP_VERSION!)
    exit /b 0
)

echo  [*] yt-dlp not found. Downloading latest version...
set "TEMP_YTDLP=%TEMP%\yt-dlp.exe"
curl -# -L -o "%TEMP_YTDLP%" "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" >nul 2>&1
if !errorlevel! neq 0 (
    echo  [X] Download failed. Check your internet connection.
    exit /b 1
)
move /Y "%TEMP_YTDLP%" "%INSTALL_DIR%\yt-dlp.exe" >nul 2>&1
echo  [OK] yt-dlp installed successfully
exit /b 0

:CheckFfmpeg
echo  [ ] Checking for FFmpeg...

:: 1. Check global Windows PATH first
where ffmpeg >nul 2>&1
if !errorlevel! equ 0 (
    for /f "tokens=*" %%a in ('ffmpeg -version 2^>^&1 ^| findstr "ffmpeg version"') do set "FFMPEG_VERSION=%%a"
    echo  [OK] Global FFmpeg detected (!FFMPEG_VERSION:~0,30!...)
    exit /b 0
)

:: 2. Check local AppData folder
if exist "%INSTALL_DIR%\ffmpeg.exe" (
    echo  [OK] Local FFmpeg found
    exit /b 0
)

echo  [*] FFmpeg not found. Downloading globally (this may take a moment)...
set "FFMPEG_ZIP=%TEMP%\ffmpeg.zip"
set "FFMPEG_DIR=%TEMP%\ffmpeg_extract"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
"try { Invoke-WebRequest -Uri 'https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip' -OutFile '%FFMPEG_ZIP%'; Expand-Archive -Path '%FFMPEG_ZIP%' -DestinationPath '%FFMPEG_DIR%' -Force; Move-Item -Path '%FFMPEG_DIR%\*\bin\ffmpeg.exe' -Destination '%INSTALL_DIR%' -Force; Move-Item -Path '%FFMPEG_DIR%\*\bin\ffprobe.exe' -Destination '%INSTALL_DIR%' -Force; Remove-Item '%FFMPEG_ZIP%' -Force; Remove-Item '%FFMPEG_DIR%' -Recurse -Force; exit 0 } catch { exit 1 }" >nul 2>&1

if !errorlevel! neq 0 (
    echo  [X] FFmpeg installation failed
    exit /b 1
)
echo  [OK] FFmpeg installed successfully
exit /b 0

:AddToPath
echo  [*] Checking Environment Variables...
:: Safely grab the current User PATH from the Windows Registry
for /f "usebackq tokens=2,*" %%A in (`reg query HKCU\Environment /v PATH 2^>nul`) do set "USER_PATH=%%B"
if not defined USER_PATH set "USER_PATH="

:: Check if our installation directory is already in the PATH
echo !USER_PATH! | find /i "%INSTALL_DIR%" >nul
if !errorlevel! neq 0 (
    echo  [*] Adding %INSTALL_DIR% to global PATH...
    if "!USER_PATH!"=="" (
        setx PATH "%INSTALL_DIR%" >nul
    ) else (
        setx PATH "%INSTALL_DIR%;!USER_PATH!" >nul
    )
    echo  [OK] Successfully added to Environment Variables!
) else (
    echo  [OK] Path is already in Environment Variables.
)
exit /b 0

:CreateLauncher
echo  [*] Creating launcher script...
set "LAUNCHER_PATH=%INSTALL_DIR%\%LAUNCHER_NAME%"

echo @echo off > "%LAUNCHER_PATH%"
echo title YT Downloader Pro - Terminal >> "%LAUNCHER_PATH%"
echo color 0A >> "%LAUNCHER_PATH%"
echo cd /d "%%~dp0" >> "%LAUNCHER_PATH%"
echo. >> "%LAUNCHER_PATH%"
:: Failsafe: Force the current session to recognize the new path instantly without a reboot
echo set "PATH=%INSTALL_DIR%;%%PATH%%" >> "%LAUNCHER_PATH%"
echo. >> "%LAUNCHER_PATH%"
echo set "encoded=%%~1" >> "%LAUNCHER_PATH%"
echo set "encoded=%%encoded:%PROTOCOL_NAME%://=%%" >> "%LAUNCHER_PATH%"
echo if "%%encoded:~-1%%"=="/" set "encoded=%%encoded:~0,-1%%" >> "%LAUNCHER_PATH%"
echo. >> "%LAUNCHER_PATH%"
echo if "%%encoded%%"=="" ( >> "%LAUNCHER_PATH%"
echo     echo [X] Error: No command provided >> "%LAUNCHER_PATH%"
echo     pause >> "%LAUNCHER_PATH%"
echo     exit /b 1 >> "%LAUNCHER_PATH%"
echo ^) >> "%LAUNCHER_PATH%"
echo. >> "%LAUNCHER_PATH%"
echo powershell -NoProfile -Command "$cmd = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('%%encoded%%')); Write-Host '[~] Executing:' -ForegroundColor Cyan; Write-Host $cmd -ForegroundColor Gray; Write-Host ''; Invoke-Expression $cmd" >> "%LAUNCHER_PATH%"
echo. >> "%LAUNCHER_PATH%"
echo if %%errorlevel%% equ 0 ( >> "%LAUNCHER_PATH%"
echo     echo [OK] Download completed successfully >> "%LAUNCHER_PATH%"
echo ^) else ( >> "%LAUNCHER_PATH%"
echo     echo [X] Download failed >> "%LAUNCHER_PATH%"
echo ^) >> "%LAUNCHER_PATH%"
echo echo. >> "%LAUNCHER_PATH%"
echo pause >> "%LAUNCHER_PATH%"

if not exist "%LAUNCHER_PATH%" exit /b 1
echo  [OK] Launcher created
exit /b 0

:RegisterProtocol
echo  [*] Registering custom protocol handler...
reg add "HKCU\Software\Classes\%PROTOCOL_NAME%" /ve /d "URL:YT Downloader Protocol" /f >nul 2>&1
reg add "HKCU\Software\Classes\%PROTOCOL_NAME%" /v "URL Protocol" /d "" /f >nul 2>&1
reg add "HKCU\Software\Classes\%PROTOCOL_NAME%\shell\open\command" /ve /d "\"%INSTALL_DIR%\%LAUNCHER_NAME%\" \"%%1\"" /f >nul 2>&1
if !errorlevel! neq 0 exit /b 1
echo  [OK] Protocol registered
exit /b 0

:OptimizeConsole
echo  [*] Optimizing terminal settings...
reg add "HKCU\Console\YT Downloader Pro" /v QuickEdit /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKCU\Console\YT Downloader Pro" /v WindowSize /t REG_DWORD /d 0x00190050 /f >nul 2>&1
exit /b 0
