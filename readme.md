# YT Downloader Pro

[![License](https://img.shields.io/github/license/muzammilAwan-dev/yt-downloader-pro?style=flat-square)](LICENSE)
[![Manifest Version](https://img.shields.io/badge/Manifest-V3-blue?style=flat-square)](https://developer.chrome.com/docs/extensions/mv3/intro/)
[![yt-dlp](https://img.shields.io/badge/yt--dlp-latest-green?style=flat-square)](https://github.com/yt-dlp/yt-dlp)
[![Platform](https://img.shields.io/badge/Platform-Windows_10%2F11-0078D6?style=flat-square&logo=windows)](https://microsoft.com)

A premium, Windows-exclusive Chrome extension that integrates natively with [yt-dlp](https://github.com/yt-dlp/yt-dlp) to download YouTube videos in various qualities, featuring a modern in-page overlay, real-time terminal output, and native age-restriction bypass.

![Extension Screenshot](docs/demo.gif)

---

## ✨ Features

| Feature | Description |
|---------|-------------|
| **🎨 Modern UI** | Glassmorphism design with YouTube-native aesthetic |
| **⚡ Dual Interface** | Popup for full control + In-page overlay for quick access |
| **🎬 Quality Selection** | 360p to 4K (2160p), plus Audio-only (MP3) |
| **🔞 Age & Bot Bypass** | Seamlessly extract local browser session cookies to download age-restricted and Members-Only videos also fixes Sign in to confirm you are not a bot issue |
| **📜 Subtitles** | Auto-embed manual and auto-generated English captions |
| **📂 Playlists** | Download entire playlists with organized folders |
| **🛡️ SponsorBlock** | Automatically remove sponsor segments |
| **🔄 SPA Compatible** | Works seamlessly with YouTube's dynamic page navigation |
| **📊 Real-time Output** | Native terminal shows live download progress, speed, and ETA |

---

## 🚀 Installation

### Step 1: Install Native Component (Windows Backend)

1. Go to the **[Releases](../../releases/latest)** page.
2. Download `setup.bat`.
3. Double-click `setup.bat`. 
   * *Note: The script will automatically request Administrator privileges. It uses Microsoft's native **WinGet** to silently install `yt-dlp` and `FFmpeg` globally on your system and configures the `ytdlp://` protocol handler.*

### Step 2: Install Chrome Extension

Because this extension interfaces directly with your PC's command line and extracts session cookies, it must be installed locally via Developer Mode:

1. Download `yt-downloader-extension.zip` from the **[Releases](../../releases/latest)** page.
2. Extract the folder to a safe place on your computer.
3. Open Chrome and navigate to `chrome://extensions/`.
4. Turn on **Developer mode** (toggle in the top-right corner).
5. Click **Load unpacked** in the top-left corner.
6. Select the folder you just extracted.

---

## 📖 Usage Guide

### Method 1: In-Page Overlay (Quick Access)
1. Navigate to any YouTube video.
2. Hover over the video player and click the floating **Download** button in the top-right corner.
3. Select your desired quality from the dropdown. 
4. Check the toggles for Subtitles, Playlists, or Age Bypass if needed.
5. The terminal opens immediately and begins downloading to your default folder.

### Method 2: Popup Interface (Full Control)
1. Click the **YT Downloader Pro** puzzle piece icon in your Chrome toolbar.
2. Configure your custom save location, default quality, and preferred toggles.
3. Click **Launch Download**.

---

## ⚙️ Configuration & Tips

**Custom Save Paths**
Leave the save path blank in the extension menu to use the default `~/Downloads/YT-Downloads/` folder. For custom paths, use standard Windows formatting:
```batch
C:\Users\YourName\Videos\
D:\Downloads\YouTube\
```

**Age-Restricted Content**
If a video requires you to log in to verify your age, simply toggle **Bypass Age or Bot Restriction**. The extension will securely pull your active YouTube session cookie, temporarily pass it to the downloader, and instantly delete it once the download starts.

---

## 🔒 Privacy & Security Policy

YT Downloader Pro operates with a strict **local-only** philosophy.

**What We Don't Do:**
- ❌ No analytics, telemetry, or download tracking.
- ❌ No external server connections (completely serverless).
- ❌ Cookies are **never** exported permanently or sent across the internet. They are passed directly to your local terminal via Base64 and destroyed immediately after initialization.

**Security Best Practices:**
- Uses a **Custom URI Protocol (`ytdlp://`)** to safely bridge the browser to your native OS shell.
- Installation utilizes official Microsoft `winget` repositories for dependency fetching.

---

## 📝 System Requirements

| Component | Minimum Version | Purpose |
|-----------|----------------|---------|
| **Windows** | 10 (1903+) | Native protocol and WinGet support |
| **Browser** | Chrome/Edge 88+ | Manifest V3 & Cookie API support |
| **WinGet** | 1.0+ | Dependency package manager |

---

## ❓ FAQ & Troubleshooting

**Q: `setup.bat` closes instantly or says WinGet is missing?** A: Ensure your Windows is up to date. You can manually install the Windows Package Manager by downloading "App Installer" from the Microsoft Store.

**Q: The terminal says "yt-dlp is not recognized"?** A: The script automatically refreshes your PATH variables, but in rare cases on older Windows builds, you may need to restart your browser once after running the setup script.

**Q: Why do subtitle downloads fail with HTTP 429 errors?** A: By default, the extension limits subtitle downloads to English (`en.*`) to prevent YouTube from rate-limiting your IP address for pulling too many automated captions simultaneously.

---

## 🙏 Acknowledgments

- **[yt-dlp](https://github.com/yt-dlp/yt-dlp)** - The incredibly powerful core download engine.
- **[FFmpeg](https://ffmpeg.org/)** - The multimedia framework powering the format conversions.
- **[SponsorBlock](https://sponsor.ajay.app/)** - The crowdsourced database enabling automatic sponsor skipping.

---

## ⚖️ Legal Notice

**YT Downloader Pro** is an independent tool and is:
- **NOT** affiliated with YouTube LLC or Google LLC.
- **NOT** affiliated with the yt-dlp project.
- **NOT** a DRM circumvention tool.

**Disclaimer:** This extension is for personal use only. Respect copyright laws and YouTube's Terms of Service. Downloading copyrighted content without authorization violates YouTube's Terms of Service and potentially copyright law in your jurisdiction. The developers assume no liability for misuse.
