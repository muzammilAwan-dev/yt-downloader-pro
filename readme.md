# YT Downloader Pro (v6.0.0)

[![License](https://img.shields.io/github/license/muzammilAwan-dev/yt-downloader-pro?style=flat-square)](LICENSE)
[![Manifest Version](https://img.shields.io/badge/Manifest-V3-blue?style=flat-square)](https://developer.chrome.com/docs/extensions/mv3/intro/)
[![yt-dlp](https://img.shields.io/badge/yt--dlp-latest-green?style=flat-square)](https://github.com/yt-dlp/yt-dlp)
[![Platform](https://img.shields.io/badge/Platform-Windows_10%2F11-0078D6?style=flat-square&logo=windows)](https://microsoft.com)

A premium, Windows-exclusive Chrome extension that integrates natively with [yt-dlp](https://github.com/yt-dlp/yt-dlp) to download YouTube videos in various qualities. Featuring a modern in-page glassmorphism overlay, IDM-style concurrent download speeds, and a **brand new Native Windows GUI** to seamlessly manage your download queue without messy terminal windows.

![Extension Screenshot](docs/demo.gif) *(Update with a new GIF showing the WPF UI!)*

---

## ✨ Features

| Feature | Description |
|---------|-------------|
| **🖥️ Native Desktop GUI** | Sends downloads straight to a custom-built, dark-themed Windows WPF application. |
| **🚦 Queue Management** | Handles multiple downloads flawlessly with Pause, Resume, Cancel, and Auto-Clear. |
| **🎨 Modern Web UI** | Glassmorphism design with a YouTube-native aesthetic inside the browser. |
| **⚡ IDM-Style Speeds** | Bypass YouTube throttling using Concurrent Connections (Defaults to 4x). |
| **🎬 Quality Selection** | 360p to 4K (2160p), plus dynamic Audiophile formats (MP3, FLAC, WAV, M4A). |
| **✂️ Timestamp Cropper** | Download specific video sections natively by inputting start/end times. |
| **📱 YouTube Shorts** | IDM-style floating button dynamically tracks infinite-scrolling Shorts. |
| **🔞 Age & Bot Bypass** | Securely passes lightweight session cookies to bypass age/bot locks locally. |
| **📜 Subtitles** | Auto-embed manual and auto-generated English captions (with 502 error bypass). |
| **📂 Smart Playlists** | Download full playlists or select specific ranges (e.g., `1-5, 8`). |
| **🛡️ SponsorBlock** | Automatically remove sponsor segments seamlessly. |

---

## 🚀 Installation

### Step 1: Install the Native Windows Client

The extension requires our lightweight Windows Host to bridge the browser to your local file system.

1. Go to the **[Releases](../../releases/latest)** page.
2. Download and run **`YTDownloaderPro_Setup.exe`**.
4. The automated installer will safely:
   * Install the Native GUI Client.
   * Download the latest official `yt-dlp` and `FFmpeg` binaries in the background.
   * Register the secure `ytdlp://` protocol handler to your Windows Registry.
5.  Download and Extract (Extract Here) the **`yt-downloader-extension.rar`**. 

### Step 2: Install Chrome Extension

Because this extension interfaces directly with your PC's native client and handles session cookies, it must be installed locally via Developer Mode:

1. Open Chrome and navigate to `chrome://extensions/`.
2. Turn on **Developer mode** (toggle in the top-right corner).
3. Click **Load unpacked** in the top-left corner.
4. Select the extracted extension folder.

---

## 📖 Usage Guide

### Method 1: In-Page Overlay (Quick Access)
1. Navigate to any YouTube Video or Short.
2. Look for the floating **Download** button (top-right of standard videos, or floating IDM-style on the right for Shorts).
3. Click it to open the glassmorphism menu.
4. Select your desired quality, audio format, or input custom timestamps. 
5. The **YT Downloader Pro Windows App** will instantly open (or wake from the system tray) and begin processing your download in its queue.

### Method 2: Popup Interface (Full Control)
1. Click the **YT Downloader Pro** puzzle piece icon in your Chrome toolbar.
2. Open the **Settings Gear** to access Advanced Custom Commands, Metadata flags, and Concurrent Download Speeds.
3. Set your custom save location, default quality, and preferred toggles.
4. Click **Launch Download**.

---

## ⚙️ Configuration & Tips

**Custom Save Paths**
Leave the save path blank in the extension menu to use the default `~/Downloads` folder. For custom paths, use standard Windows formatting:
```batch
C:\Users\YourName\Videos\
D:\Downloads\YouTube\
```

**Timestamp Cropping**
If you only want a 10-second clip of a 2-hour podcast, enter a Start and End time (e.g., `01:15` to `01:25`). The extension will force `yt-dlp` to download *only* that chunk, saving massive amounts of bandwidth.

**Custom Command Overrides**
Click the Settings gear in the extension popup to access the Custom Command box. Type your favorite flags (e.g., `yt-dlp -f bestvideo+bestaudio --merge-output-format mkv`). 

**Multi-Part Downloading (Speed Booster)**
If you are downloading massive 4K videos, ensure your speed is set to **Fast (4x)** or **Extreme (8x)** in the Settings menu. This forces yt-dlp to open multiple connections to YouTube simultaneously.

---

## 🔒 Privacy & Security Policy

YT Downloader Pro operates with a strict **local-only** philosophy.

**What We Don't Do:**
- ❌ No analytics, telemetry, or download tracking.
- ❌ No external server connections (completely serverless).
- ❌ Cookies are **never** exported permanently or sent across the internet. They are extracted safely, passed locally via Base64, and destroyed immediately upon download completion.

**Security Best Practices:**
- Uses a **Custom URI Protocol (`ytdlp://`)** to safely bridge the browser to your native OS shell.
- The desktop host validates and sanitizes all incoming commands to prevent injection attacks.

---

## 📝 System Requirements

| Component | Minimum Version | Purpose |
|-----------|----------------|---------|
| **Windows** | 10 or 11 | Required for the Native WPF Desktop Client |
| **.NET Runtime**| .NET 8.0 Desktop | Powers the desktop GUI application |
| **Browser** | Chrome/Edge 88+ | Manifest V3 & Cookie API support |

---

## ❓ FAQ & Troubleshooting

**Q: "Uncaught Error: Extension context invalidated"** A: You reloaded the extension files in the background, but your YouTube tab is still running the old script. Simply press `Ctrl + F5` to hard-refresh the YouTube tab.

**Q: `YTDownloaderPro_Setup.exe` triggered Windows Defender / SmartScreen?** A: Because the installer is new and hasn't gathered thousands of downloads yet, Windows Defender may flag it simply for being "unrecognized." The installer and host app are 100% open-source. Click "More Info" -> "Run anyway".

**Q: Why do subtitle downloads fail with HTTP 502 / 429 errors?** A: YouTube rate-limits IPs that download subtitles too quickly. The extension automatically applies a 2-second sleep delay when fetching captions to seamlessly bypass this bot-trap.

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

**Disclaimer:** This extension is for educational purposes, personal archiving, and downloading royalty-free content. Respect copyright laws and YouTube's Terms of Service. Downloading copyrighted content without authorization violates Terms of Service and potentially copyright law in your jurisdiction. The developers assume no liability for misuse.
