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

:: Check for administrator privileges (not required, but warn)
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo  [!] Note: Running without administrator privileges.
    echo      Installation is limited to current user only.
    echo.
)

:: Create installation directory
echo  [*] Creating installation directory...
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%" 2>nul
    if !errorlevel! neq 0 (
        echo  [X] ERROR: Failed to create directory: %INSTALL_DIR%
        echo      Please check permissions and try again.
        pause
        exit /b 1
    )
)
echo  [OK] Directory ready: %INSTALL_DIR%
echo.

:: Check dependencies section
echo  --- CHECKING DEPENDENCIES ---
echo.

:: Check for yt-dlp (global or local)
call :CheckYtDlp
if !errorlevel! equ 0 (
    echo  [OK] yt-dlp is ready
) else (
    echo  [X] Failed to setup yt-dlp
    pause
    exit /b 1
)

:: Check for FFmpeg (global or local)
call :CheckFfmpeg
if !errorlevel! equ 0 (
    echo  [OK] FFmpeg is ready
) else (
    echo  [X] Failed to setup FFmpeg
    pause
    exit /b 1
)

echo.
echo  --- CONFIGURING SYSTEM INTEGRATION ---
echo.

:: Create launcher script
call :CreateLauncher
if !errorlevel! neq 0 (
    echo  [X] Failed to create launcher
    pause
    exit /b 1
)

:: Register protocol handler
call :RegisterProtocol
if !errorlevel! neq 0 (
    echo  [X] Failed to register protocol
    pause
    exit /b 1
)

:: Optimize console settings
call :OptimizeConsole

echo.
echo  ╔══════════════════════════════════════════════════════════╗
echo  ║              INSTALLATION COMPLETE                         ║
echo  ╠══════════════════════════════════════════════════════════╣
echo  ║  The native component has been installed successfully.     ║
echo  ║                                                          ║
echo  ║  You can now use the Chrome extension to download        ║
echo  ║  YouTube videos directly to your PC.                       ║
echo  ║                                                          ║
echo  ║  Installation Directory: %INSTALL_DIR%
echo  ╚══════════════════════════════════════════════════════════╝
echo.
pause
exit /b 0

:: ============================================================
:: SUBROUTINES
:: ============================================================

:CheckYtDlp
echo  [ ] Checking for yt-dlp...
where yt-dlp >nul 2>&1
if %errorlevel% equ 0 (
    for /f "tokens=*" %%a in ('yt-dlp --version') do set "YTDLP_VERSION=%%a"
    echo  [OK] Global yt-dlp detected (v!YTDLP_VERSION!)
    exit /b 0
)

if exist "%INSTALL_DIR%\yt-dlp.exe" (
    for /f "tokens=*" %%a in ('"%INSTALL_DIR%\yt-dlp.exe" --version') do set "YTDLP_VERSION=%%a"
    echo  [OK] Local yt-dlp found (v!YTDLP_VERSION!)
    exit /b 0
)

echo  [*] Downloading yt-dlp portable...
curl -# -L -o "%INSTALL_DIR%\yt-dlp.exe" "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" 2>nul
if !errorlevel! neq 0 (
    echo  [X] Download failed. Please check internet connection.
    exit /b 1
)
echo  [OK] yt-dlp downloaded successfully
exit /b 0

:CheckFfmpeg
echo  [ ] Checking for FFmpeg...
where ffmpeg >nul 2>&1
if %errorlevel% equ 0 (
    for /f "tokens=*" %%a in ('ffmpeg -version 2^>^&1 ^| findstr "ffmpeg version"') do set "FFMPEG_VERSION=%%a"
    echo  [OK] Global FFmpeg detected (!FFMPEG_VERSION:~0,30!...)
    exit /b 0
)

if exist "%INSTALL_DIR%\ffmpeg.exe" (
    echo  [OK] Local FFmpeg found
    exit /b 0
)

echo  [*] Downloading FFmpeg (this may take a moment)...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "try { Invoke-WebRequest -Uri 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip' -OutFile '%INSTALL_DIR%\ffmpeg.zip' -UseBasicParsing; Expand-Archive -Path '%INSTALL_DIR%\ffmpeg.zip' -DestinationPath '%INSTALL_DIR%\ffmpeg_temp' -Force; Move-Item -Path '%INSTALL_DIR%\ffmpeg_temp\*\bin\ffmpeg.exe' -Destination '%INSTALL_DIR%\' -Force; Move-Item -Path '%INSTALL_DIR%\ffmpeg_temp\*\bin\ffprobe.exe' -Destination '%INSTALL_DIR%\' -Force; Remove-Item -Path '%INSTALL_DIR%\ffmpeg.zip' -Force; Remove-Item -Path '%INSTALL_DIR%\ffmpeg_temp' -Recurse -Force; exit 0 } catch { exit 1 }" 2>nul

if !errorlevel! neq 0 (
    echo  [X] FFmpeg download failed. You may need to install manually.
    exit /b 1
)
echo  [OK] FFmpeg installed successfully
exit /b 0

:CreateLauncher
echo  [*] Creating launcher script...
set "LAUNCHER_PATH=%INSTALL_DIR%\%LAUNCHER_NAME%"

:: We use line-by-line appending (>>) to prevent bracket parsing errors
echo @echo off > "%LAUNCHER_PATH%"
echo title YT Downloader Pro - Terminal >> "%LAUNCHER_PATH%"
echo color 0A >> "%LAUNCHER_PATH%"
echo cd /d "%%~dp0" >> "%LAUNCHER_PATH%"
echo. >> "%LAUNCHER_PATH%"
echo :: Decode Base64 command from protocol URL >> "%LAUNCHER_PATH%"
echo set "encoded=%%~1" >> "%LAUNCHER_PATH%"
echo set "encoded=%%encoded:%PROTOCOL_NAME%://=%%" >> "%LAUNCHER_PATH%"
echo if "%%encoded:~-1%%"=="/" set "encoded=%%encoded:~0,-1%%" >> "%LAUNCHER_PATH%"
echo. >> "%LAUNCHER_PATH%"
echo :: Validate input >> "%LAUNCHER_PATH%"
echo if "%%encoded%%"=="" ( >> "%LAUNCHER_PATH%"
echo     echo [X] Error: No command provided >> "%LAUNCHER_PATH%"
echo     pause >> "%LAUNCHER_PATH%"
echo     exit /b 1 >> "%LAUNCHER_PATH%"
echo ^) >> "%LAUNCHER_PATH%"
echo. >> "%LAUNCHER_PATH%"
echo :: Decode and execute >> "%LAUNCHER_PATH%"
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

if !errorlevel! neq 0 (
    echo  [X] Registry modification failed
    exit /b 1
)
echo  [OK] Protocol '%PROTOCOL_NAME%://' registered
exit /b 0

:OptimizeConsole
echo  [*] Optimizing terminal settings...
reg add "HKCU\Console\YT Downloader Pro" /v QuickEdit /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKCU\Console\YT Downloader Pro" /v WindowSize /t REG_DWORD /d 0x00190050 /f >nul 2>&1
exit /b 0