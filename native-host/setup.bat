@echo off
setlocal EnableDelayedExpansion
title YT Downloader Pro - Setup
color 0B
cls

echo.
echo ╔══════════════════════════════════════════════════════════╗
echo ║        YT Downloader Pro - Native Component Setup        ║
echo ║              Version 5.1.0 - Windows Installer             ║
echo ╚══════════════════════════════════════════════════════════╝
echo.

:: Configuration
set "APP_NAME=YT-Downloader-Pro"
:: [FIXED] Added missing backslash
set "INSTALL_DIR=%LOCALAPPDATA%\%APP_NAME%"
set "LAUNCHER_NAME=launcher.bat"
set "PROTOCOL_NAME=ytdlp"

:: Check for administrator privileges
net session >nul 2>&1
set "IS_ADMIN=0"
if %errorlevel% equ 0 set "IS_ADMIN=1"
if "%IS_ADMIN%"=="0" (
    echo [!] Running without administrator privileges.
    echo     Global installation may fail if required.
    echo.
)

:: Create installation directory
echo [*] Creating installation directory...
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%" 2>nul
    if !errorlevel! neq 0 (
        echo [X] ERROR: Failed to create directory: %INSTALL_DIR%
        pause
        exit /b 1
    )
)
echo [OK] Directory ready: %INSTALL_DIR%
echo.

:: ============================================================
:: DEPENDENCIES
:: ============================================================
echo --- CHECKING DEPENDENCIES ---
echo.

call :CheckYtDlp
if !errorlevel! neq 0 (
    echo [X] Failed to setup yt-dlp
    pause
    exit /b 1
)

call :CheckFfmpeg
if !errorlevel! neq 0 (
    echo [X] Failed to setup FFmpeg
    pause
    exit /b 1
)

echo.
echo --- CONFIGURING SYSTEM INTEGRATION ---
echo.

call :CreateLauncher
if !errorlevel! neq 0 (
    echo [X] Failed to create launcher
    pause
    exit /b 1
)

call :RegisterProtocol
if !errorlevel! neq 0 (
    echo [X] Failed to register protocol
    pause
    exit /b 1
)

call :OptimizeConsole

echo.
echo ╔══════════════════════════════════════════════════════════╗
echo ║                 INSTALLATION COMPLETE                    ║
echo ╠══════════════════════════════════════════════════════════╣
echo ║ The native component has been installed successfully.    ║
echo ║                                                        ║
echo ║ You can now use the Chrome extension to download         ║
echo ║ YouTube videos directly to your PC.                      ║
echo ║                                                        ║
echo ║ Installation Directory: %INSTALL_DIR%
echo ╚══════════════════════════════════════════════════════════╝
echo.
pause
exit /b 0

:: ============================================================
:: SUBROUTINES
:: ============================================================

:CheckYtDlp
echo [ ] Checking for yt-dlp...
where yt-dlp >nul 2>&1
if %errorlevel% equ 0 (
    for /f "tokens=*" %%a in ('yt-dlp --version') do set "YTDLP_VERSION=%%a"
    echo [OK] Global yt-dlp detected (v!YTDLP_VERSION!)
    exit /b 0
)

echo [*] yt-dlp not found. Installing globally...
if "%IS_ADMIN%"=="0" (
    echo [X] Admin privileges required to install yt-dlp globally.
    exit /b 1
)
set "TEMP_YTDLP=%TEMP%\yt-dlp.exe"
curl -# -L -o "%TEMP_YTDLP%" "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" >nul 2>&1
if %errorlevel% neq 0 (
    echo [X] Download failed
    exit /b 1
)
move /Y "%TEMP_YTDLP%" "%SystemRoot%\System32\yt-dlp.exe" >nul 2>&1
if %errorlevel% neq 0 (
    echo [X] Failed to install yt-dlp globally
    exit /b 1
)
echo [OK] yt-dlp installed globally
exit /b 0

:CheckFfmpeg
echo [ ] Checking for FFmpeg...
where ffmpeg >nul 2>&1
if %errorlevel% equ 0 (
    for /f "tokens=*" %%a in ('ffmpeg -version 2^>^&1 ^| findstr "ffmpeg version"') do set "FFMPEG_VERSION=%%a"
    echo [OK] Global FFmpeg detected (!FFMPEG_VERSION:~0,30!...)
    exit /b 0
)

echo [*] FFmpeg not found. Installing globally...
if "%IS_ADMIN%"=="0" (
    echo [X] Admin privileges required to install FFmpeg globally.
    exit /b 1
)
set "FFMPEG_ZIP=%TEMP%\ffmpeg.zip"
set "FFMPEG_DIR=%TEMP%\ffmpeg_extract"

:: [FIXED] Added missing backslashes in Move-Item paths
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
"try { Invoke-WebRequest -Uri 'https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip' -OutFile '%FFMPEG_ZIP%'; Expand-Archive -Path '%FFMPEG_ZIP%' -DestinationPath '%FFMPEG_DIR%' -Force; Move-Item -Path '%FFMPEG_DIR%\*\bin\ffmpeg.exe' -Destination '%SystemRoot%\System32' -Force; Move-Item -Path '%FFMPEG_DIR%\*\bin\ffprobe.exe' -Destination '%SystemRoot%\System32' -Force; Remove-Item '%FFMPEG_ZIP%' -Force; Remove-Item '%FFMPEG_DIR%' -Recurse -Force; exit 0 } catch { exit 1 }" >nul 2>&1

if %errorlevel% neq 0 (
    echo [X] FFmpeg installation failed
    exit /b 1
)
echo [OK] FFmpeg installed globally
exit /b 0

:CreateLauncher
echo [*] Creating launcher script...
:: [FIXED] Added missing backslash
set "LAUNCHER_PATH=%INSTALL_DIR%\%LAUNCHER_NAME%"

echo @echo off > "%LAUNCHER_PATH%"
echo title YT Downloader Pro - Terminal >> "%LAUNCHER_PATH%"
echo color 0A >> "%LAUNCHER_PATH%"
echo cd /d "%%~dp0" >> "%LAUNCHER_PATH%"
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
echo [OK] Launcher created
exit /b 0

:RegisterProtocol
echo [*] Registering custom protocol handler...
:: [FIXED] Added missing backslashes to Registry keys and launcher path
reg add "HKCU\Software\Classes\%PROTOCOL_NAME%" /ve /d "URL:YT Downloader Protocol" /f >nul 2>&1
reg add "HKCU\Software\Classes\%PROTOCOL_NAME%" /v "URL Protocol" /d "" /f >nul 2>&1
reg add "HKCU\Software\Classes\%PROTOCOL_NAME%\shell\open\command" /ve /d "\"%INSTALL_DIR%\%LAUNCHER_NAME%\" \"%%1\"" /f >nul 2>&1
if !errorlevel! neq 0 exit /b 1
echo [OK] Protocol registered
exit /b 0

:OptimizeConsole
echo [*] Optimizing terminal settings...
reg add "HKCU\Console\YT Downloader Pro" /v QuickEdit /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKCU\Console\YT Downloader Pro" /v WindowSize /t REG_DWORD /d 0x00190050 /f >nul 2>&1
exit /b 0
