@echo off
setlocal EnableDelayedExpansion

:: --- 1. Robust Admin Elevation ---
openfiles >nul 2>&1
if %errorlevel% neq 0 (
    echo [*] Elevating to Administrator...
    powershell -Command "Start-Process -FilePath '%~f0' -ArgumentList 'am_admin' -Verb runAs"
    exit /b
)

title YT Downloader Pro - Universal Setup
color 0B
cls

echo ==========================================================
echo     YT Downloader Pro - Global System Setup
echo ==========================================================
echo.

:: --- 2. Define Global Binary Directory ---
:: We use C:\bin as it's short, standard, and avoids 'Program Files' permission quirks
set "GLOBAL_BIN=C:\bin"
set "INSTALL_DIR=%LOCALAPPDATA%\YT-Downloader-Pro"

if not exist "%GLOBAL_BIN%" mkdir "%GLOBAL_BIN%"
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

:: --- 3. Direct Binary Bootstrap (Reliable Method) ---
echo [*] Checking Native Components...

:: yt-dlp check/download
where yt-dlp >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] yt-dlp not found. Downloading latest release...
    powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile '%GLOBAL_BIN%\yt-dlp.exe'"
) else ( echo [OK] yt-dlp already installed. )

:: ffmpeg check/download
where ffmpeg >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] FFmpeg not found. Downloading static build...
    powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; $url='https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip'; Invoke-WebRequest -Uri $url -OutFile '%TEMP%\ffmpeg.zip'; Expand-Archive -Path '%TEMP%\ffmpeg.zip' -DestinationPath '%TEMP%\ffmpeg_ext' -Force; Get-ChildItem -Path '%TEMP%\ffmpeg_ext' -Filter 'ffmpeg.exe' -Recurse | Select-Object -First 1 | Move-Item -Destination '%GLOBAL_BIN%\ffmpeg.exe' -Force; Get-ChildItem -Path '%TEMP%\ffmpeg_ext' -Filter 'ffprobe.exe' -Recurse | Select-Object -First 1 | Move-Item -Destination '%GLOBAL_BIN%\ffprobe.exe' -Force; Remove-Item '%TEMP%\ffmpeg.zip'; Remove-Item '%TEMP%\ffmpeg_ext' -Recurse"
) else ( echo [OK] FFmpeg already installed. )

:: --- 4. Permanent PATH Update ---
:: Safely checks if GLOBAL_BIN is already in PATH to avoid duplicates
echo !PATH! | findstr /i "%GLOBAL_BIN%" >nul
if %errorlevel% neq 0 (
    echo [*] Adding %GLOBAL_BIN% to System PATH...
    setx /M PATH "%GLOBAL_BIN%;!PATH!"
    set "PATH=%GLOBAL_BIN%;!PATH!"
)

:: --- 5. Generate Advanced Launcher ---
echo [*] Generating Protocol Launcher...
set "L_PATH=%INSTALL_DIR%\launcher.bat"

:: Using line-by-line redirection to prevent parenthesis block crashing
echo @echo off > "%L_PATH%"
echo title YT Downloader Pro - Terminal >> "%L_PATH%"
echo cd /d "%%~dp0" >> "%L_PATH%"
echo. >> "%L_PATH%"
echo :: Ensure environment is fresh >> "%L_PATH%"
echo for /f "tokens=*" %%%%a in ('powershell -NoProfile -Command "[System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User')"') do set "PATH=%%%%a" >> "%L_PATH%"
echo. >> "%L_PATH%"
echo set "encoded=%%~1" >> "%L_PATH%"
echo set "encoded=%%encoded:ytdlp://=%%" >> "%L_PATH%"
echo. >> "%L_PATH%"

:: THE FIX IS HERE (Added Write-Host $full_cmd to print the command before execution)
echo powershell -NoProfile -Command "$raw='%%encoded%%'; $raw=[System.Uri]::UnescapeDataString($raw).TrimEnd('/'); $parts=$raw -split '\|\|'; $full_cmd=[System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($parts[0])); if ($parts.Length -gt 1) { [IO.File]::WriteAllText('cookies.txt', [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($parts[1]))); $full_cmd += ' --cookies cookies.txt'; Write-Host '[*] Session cookies applied.' -ForegroundColor Yellow; } Write-Host '[~] Executing Command Stream:' -ForegroundColor Cyan; Write-Host $full_cmd -ForegroundColor Gray; Write-Host ''; Invoke-Expression $full_cmd; if (Test-Path 'cookies.txt') { Remove-Item 'cookies.txt' }" >> "%L_PATH%"

echo pause >> "%L_PATH%"

:: --- 6. Registry Protocol Configuration ---
echo [*] Registering ytdlp:// Protocol...
reg add "HKCU\Software\Classes\ytdlp" /ve /d "URL:YT Downloader Protocol" /f >nul
reg add "HKCU\Software\Classes\ytdlp" /v "URL Protocol" /d "" /f >nul
reg add "HKCU\Software\Classes\ytdlp\shell\open\command" /ve /d "\"%L_PATH%\" \"%%1\"" /f >nul

echo.
echo ==========================================================
echo    SETUP SUCCESSFUL! 
echo    Everything is installed and configured perfectly.
echo    You can now close this window and use the extension.
echo ==========================================================
pause
exit /b