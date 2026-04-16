@echo off
setlocal EnableDelayedExpansion

:: --- Auto-Elevate to Administrator ---
if not "%1"=="am_admin" (
    powershell -Command "Start-Process -FilePath '%0' -ArgumentList 'am_admin' -Verb runAs"
    exit /b
)

title YT Downloader Pro - System Setup
color 0B
cls

echo ==========================================================
echo    YT Downloader Pro - Native Component Setup
echo ==========================================================
echo.

:: --- Check for Winget ---
echo [*] Verifying Winget...
where winget >nul 2>&1
if %errorlevel% neq 0 (
    echo [X] Winget not found. Please install "App Installer" from the MS Store.
    pause
    exit /b
)
echo [OK] Winget found.

:: --- Upgrade Winget Engine ---
echo [*] Ensuring Windows Package Manager is up to date...
winget upgrade Microsoft.AppInstaller --accept-package-agreements --accept-source-agreements --silent >nul 2>&1

:: --- Setup Directory ---
set "INSTALL_DIR=%LOCALAPPDATA%\YT-Downloader-Pro"
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

:: --- Install Dependencies ---
echo [*] Checking yt-dlp...
where yt-dlp >nul 2>&1
if %errorlevel% neq 0 (
    echo [*] Installing yt-dlp...
    winget install --id yt-dlp.yt-dlp --exact --accept-package-agreements --accept-source-agreements --silent
) else ( echo [OK] yt-dlp exists. )

echo [*] Checking FFmpeg...
where ffmpeg >nul 2>&1
if %errorlevel% neq 0 (
    echo [*] Installing FFmpeg...
    winget install --id Gyan.FFmpeg --exact --accept-package-agreements --accept-source-agreements --silent
) else ( echo [OK] FFmpeg exists. )

:: --- Create Launcher ---
echo [*] Generating Launcher...
set "L_PATH=%INSTALL_DIR%\launcher.bat"

:: We use >> to safely build the file without parsing errors
echo @echo off > "%L_PATH%"
echo title YT Downloader Pro - Terminal >> "%L_PATH%"
echo cd /d "%%~dp0" >> "%L_PATH%"
echo. >> "%L_PATH%"

:: Dynamically fetch the absolute latest PATH at runtime to prevent the "Time Capsule" bug
echo for /f "tokens=*" %%%%a in ('powershell -NoProfile -Command "[System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User')"') do set "PATH=%%%%a" >> "%L_PATH%"
echo. >> "%L_PATH%"

:: Sanitize the ytdlp:// protocol wrapper
echo set "encoded=%%~1" >> "%L_PATH%"
echo set "encoded=%%encoded:ytdlp://=%%" >> "%L_PATH%"
echo. >> "%L_PATH%"

:: The Master Execution Command (Includes URL Decoding and Cookie Extraction Logic)
echo powershell -NoProfile -Command "$env:Path = [System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User'); $raw='%%encoded%%'; $raw=[System.Uri]::UnescapeDataString($raw).TrimEnd('/'); $parts=$raw -split '\|\|'; $cmd=[System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($parts[0])); if ($parts.Length -gt 1) { [IO.File]::WriteAllText('cookies.txt', [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($parts[1]))); $cmd += ' --cookies cookies.txt'; Write-Host '[*] Applying session cookies for age-restricted bypass...' -ForegroundColor Yellow; } Write-Host '[~] Executing command stream:' -ForegroundColor Cyan; Write-Host $cmd -ForegroundColor Gray; Write-Host ''; Invoke-Expression $cmd; if (Test-Path 'cookies.txt') { Remove-Item 'cookies.txt' }" >> "%L_PATH%"
echo pause >> "%L_PATH%"

:: --- Registry Protocol ---
echo [*] Registering Protocol Handler...
reg add "HKCU\Software\Classes\ytdlp" /ve /d "URL:YT Downloader Protocol" /f >nul
reg add "HKCU\Software\Classes\ytdlp" /v "URL Protocol" /d "" /f >nul
reg add "HKCU\Software\Classes\ytdlp\shell\open\command" /ve /d "\"%L_PATH%\" \"%%1\"" /f >nul

echo.
echo ==========================================================
echo    SETUP COMPLETE!
echo    You can now close this window.
echo ==========================================================
pause
exit /b