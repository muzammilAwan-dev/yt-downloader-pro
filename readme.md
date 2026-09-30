# YT Downloader Pro

[![Build YTDLPHost](https://github.com/muzammilAwan-dev/yt-downloader-pro/actions/workflows/build.yml/badge.svg)](../../actions/workflows/build.yml)
[![License](https://img.shields.io/github/license/muzammilAwan-dev/yt-downloader-pro?style=flat-square)](LICENSE)
[![Manifest Version](https://img.shields.io/badge/Manifest-V3-blue?style=flat-square)](https://developer.chrome.com/docs/extensions/mv3/intro/)
[![yt-dlp](https://img.shields.io/badge/yt--dlp-latest-green?style=flat-square)](https://github.com/yt-dlp/yt-dlp)
[![Platform](https://img.shields.io/badge/Platform-Windows_10%2F11-0078D6?style=flat-square&logo=windows)](https://microsoft.com)

A premium, Windows-exclusive **Chrome extension + native desktop host**, built around [yt-dlp](https://github.com/yt-dlp/yt-dlp), for downloading YouTube videos in any quality. This repo contains both halves of the product:

| Component | What it is | Lives in |
|---|---|---|
| **Chrome Extension** | Manifest V3 extension: in-page glassmorphism download button, popup UI, cookie/session capture | [`extension/`](extension) |
| **Native Host (YTDLPHost)** | .NET 8 WPF desktop app: receives `ytdlp://` links, runs `yt-dlp`/`ffmpeg`/`deno` in a sandboxed queue, dark-themed GUI | [`host/`](host) |

They're released and versioned together from this single repo.

## Screenshots

<table border="0" cellpadding="0" cellspacing="12">
  <tr>
    <td><img src="extension/docs/1.webp" alt="Image 1" width="300"></td>
    <td><img src="extension/docs/2.webp" alt="Image 2" width="300"></td>
  </tr>
  <tr>
    <td><img src="extension/docs/3.webp" alt="Image 3" width="300"></td>
    <td><img src="extension/docs/4.webp" alt="Image 4" width="300"></td>
  </tr>
  <tr>
    <td colspan="2" align="center"><img src="extension/docs/5.webp" alt="Image 5" width="612"></td>
  </tr>
</table>

## ✨ Features

| Feature | Description |
|---|---|
| 🖥️ Native Desktop GUI | Sends downloads to a custom-built, dark-themed WPF host app |
| 🚦 Queue Management | IDM-style concurrent downloads with dynamic hot-swapping of speed/thread settings |
| 🎨 Modern Web UI | Glassmorphism in-page overlay with a YouTube-native aesthetic |
| ⚡ IDM-Style Speeds | Bypass YouTube throttling via concurrent connections (default 4x) |
| 🎬 Quality Selection | 360p up to 4K (2160p), plus MP3 / FLAC / WAV / M4A audio |
| 📺 Compatibility Mode | Forces H.264/AAC for playback on legacy TVs and phones |
| ✂️ Timestamp Cropper | Download only a specific start/end section of a video |
| 📱 YouTube Shorts | Floating button tracks infinite-scrolling Shorts |
| 📂 Smart Playlists | Full playlists or specific ranges (e.g. `1-5, 8`) |
| 🔁 Smart Auto-Recovery | Debounced JSON queue state survives crashes/restarts |
| 🛡️ Anti-Bot Shield | Bundles the Deno JS engine to bypass YouTube's signature puzzles |

## 🚀 Installation

### 1. Install the native host

1. Go to [Releases](../../releases/latest) and download `YTDownloaderPro_Setup.exe`.
2. Run it. This installs the WPF host to `C:\Program Files\YT Downloader Pro` and registers the `ytdlp://` protocol.
3. On first launch, the host silently fetches `yt-dlp`, `ffmpeg`, and `deno` into `%LOCALAPPDATA%\YTDownloaderProEngine`.

### 2. Install the Chrome extension

Because this extension talks to the native host and reads session cookies, it must be loaded unpacked:

1. Open `chrome://extensions/` and enable **Developer mode**.
2. Click **Load unpacked**.
3. Select the [`extension/`](extension) folder from this repo.

## 📖 Usage

**In-page overlay:** open any YouTube video/Short → click the floating **Download** button → pick quality/format/timestamps → the host app opens and starts the download.

**Popup:** click the toolbar icon → open **Settings** for advanced flags, compatibility mode, custom commands, and speed → **Launch Download**.

## ⚙️ Configuration tips

- **Compatibility Mode (H.264/AAC):** turn on if downloads show a black screen or no audio on older TVs/phones (modern YouTube serves VP9/AV1 by default).
- **Timestamp cropping:** enter start/end (e.g. `01:15`–`01:25`) to download only that clip instead of the whole video.
- **Multi-connection speed:** for large 4K files, set speed to Fast (4x) or Extreme (8x) in Settings.

## 🔍 How it works (Browser → OS → Host)

1. **Extension:** `popup.js`/`content.js` build a `yt-dlp ...` command string, `background.js` grabs the needed session cookies, both get Base64-encoded and joined as `ytdlp://<command>||<cookies>`, fired via a hidden `<iframe>`.
2. **OS handoff:** Windows resolves the `ytdlp://` protocol from the registry and launches `YTDLPHost.exe "<payload>"`.
3. **Single instance:** if the host is already running, the new process drops the payload into `%LOCALAPPDATA%\YT Downloader Pro\Payloads`, pings the running instance's named pipe as a "doorbell," and exits — no duplicate windows, no orphaned processes.
4. **Execution:** the primary instance decodes, validates (path/flag safety checks), and queues the task, then runs `yt-dlp.exe` in a sandboxed, console-free child process with live progress parsed straight into the WPF UI.

## 🔒 Privacy & Security

- **Local-only:** no analytics, telemetry, or external servers besides YouTube/yt-dlp/ffmpeg/deno downloads themselves.
- Session cookies are captured only when you enable "Bypass Age & Bot issue," passed locally via Base64, written to a temp file for the duration of that download, and deleted immediately after — they are never committed to the queue's persisted history.
- Command payloads are validated against a blocklist of dangerous `yt-dlp` flags (`--exec`, `--postprocessor-args`, etc.) and blocked path traversal before execution.

## 🗑️ Uninstallation

Settings → Apps → Installed Apps → uninstall **YT Downloader Pro**. This removes the host binaries, unregisters the protocol handler, and wipes the dynamic `%LOCALAPPDATA%` engine/payload folders. Remove the Chrome extension separately from `chrome://extensions/`.

## 🛠️ Building from source

```
git clone https://github.com/muzammilAwan-dev/yt-downloader-pro.git
cd yt-downloader-pro

# Host app (requires .NET 8 SDK, Windows)
dotnet build host/YTDLPHost.sln --configuration Release

# Installer (requires Inno Setup, Windows) - optional, only if packaging a setup.exe
iscc "host/installer/setup.iss"
```

CI (`.github/workflows/build.yml`) builds and publishes `YTDLPHost.exe` as a GitHub Actions artifact on every push to `host/**`. Build outputs (`bin/`, `obj/`, `publish/`, `installer/Output/`) are not committed — see `.gitignore`.

## 📁 Repository structure

```
yt-downloader-pro/
├── .github/workflows/build.yml   # CI: build + publish YTDLPHost.exe
├── extension/                    # Chrome extension (Manifest V3)
│   ├── docs/                     # Screenshots used in this README
│   ├── icons/
│   ├── background.js
│   ├── content.js / content.css
│   ├── manifest.json
│   └── popup.html / popup.js
├── host/                         # Native Windows GUI host
│   ├── YTDLPHost.sln
│   ├── YTDLPHost/
│   │   ├── Assets/                (icon.ico)
│   │   ├── Converters/
│   │   ├── Models/                (AppSettings, DownloadTask)
│   │   ├── Services/              (AppLogger, HistoryManager, ProtocolHandler,
│   │   │                           SingleInstanceManager, TrayIconService, YtDlpRunner)
│   │   ├── ViewModels/            (MainViewModel, DownloadItemViewModel)
│   │   ├── App.xaml(.cs)
│   │   ├── MainWindow.xaml(.cs)
│   │   └── YTDLPHost.csproj
│   └── installer/                 # Inno Setup packaging (renamed from "YTDLP_Installer maker")
│       ├── Assets/icon.ico
│       └── setup.iss
├── .gitignore
├── LICENSE
└── README.md
```

## ⚖️ Legal Notice

**YT Downloader Pro** is an independent tool, **not affiliated** with YouTube LLC, Google LLC, or the yt-dlp project, and is **not** a DRM circumvention tool.

For educational purposes, personal archiving, and royalty-free content only. Respect copyright law and YouTube's Terms of Service — downloading copyrighted content without authorization may violate both. The developers assume no liability for misuse.

## License

[MIT](LICENSE) © 2026 Muhammad Muzammil