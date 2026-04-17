# 🚀 YT Downloader Pro (v5.4.0)

![Version](https://img.shields.io/badge/version-5.4.0-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Chrome%20%7C%20Edge-lightgrey.svg)
![License](https://img.shields.io/badge/license-MIT-green.svg)
![Dependencies](https://img.shields.io/badge/dependencies-yt--dlp%20%7C%20FFmpeg-orange.svg)

A premium, zero-server Chromium Extension that bridges the gap between your browser and the incredible power of `yt-dlp`. 

Instead of relying on slow, ad-ridden web converters, this extension communicates directly with your local Windows terminal using a secure, custom `ytdlp://` protocol. Get 4K downloads, lossless audiophile formats, and age-restriction bypasses directly from the YouTube video player.

---

## 📸 Screenshots
*(Add your screenshots here!)*
> `![Popup Menu](link_to_image)` | `![Floating Shorts Button](link_to_image)`

---

## ✨ What's New in Version 5.4.0 (Major Update)
* **✂️ Timestamp Cropper:** Only want a 10-second clip? Input start and end times to download specific video sections natively, saving massive amounts of bandwidth.
* **🎵 Audiophile Formats:** Expanded audio support to include FLAC (Lossless), WAV (Uncompressed), and M4A alongside standard MP3. *(Includes a Smart WAV Fix to prevent FFmpeg metadata crashes).*
* **📱 YouTube Shorts Support:** The floating glassmorphism button now dynamically tracks and injects itself into infinite-scrolling YouTube Shorts!
* **⚙️ Custom Command Override:** Terminal junkies can now write custom `yt-dlp` commands in the extension settings and execute them directly via the UI.
* **📡 Smart Auto-Updater:** The extension now checks GitHub APIs natively and notifies you when a new release is available!
* **🍪 "Cookie Diet" Bypass:** Optimized the age/bot bypass logic to strictly fetch essential authentication tokens, completely eliminating Windows OS 2048-character URL limit crashes.

---

## 🌟 Key Features
* **Zero-Touch Architecture:** No intermediate servers. The extension sends commands directly to your local PC.
* **In-Page Floating UI:** A sleek, glassmorphism dropdown menu injected right into the YouTube video player.
* **4K & 2K Video Support:** Automatically merges the highest quality video and audio streams using FFmpeg into MKV or MP4 containers.
* **Full Playlist Support:** Download entire playlists or specific ranges (e.g., `1-5, 8, 11-13`).
* **Anti-Bot & Age Restriction Bypass:** Seamlessly grabs your active YouTube session cookies to bypass age gates and bot-verification locks.
* **Rich Metadata:** Auto-embeds English subtitles, video thumbnails, and YouTube chapter metadata directly into the media file.
* **SponsorBlock Integration:** Automatically removes baked-in sponsor segments from downloaded videos.

---

## 🛑 Prerequisites
Before installing, ensure your system meets the following requirements:
* **Operating System:** Windows 10 or Windows 11 (Required for the `setup.bat` native bridge).
* **Browser:** Google Chrome, Microsoft Edge, Brave, or any Chromium-based browser.
* **Permissions:** Administrator privileges on your Windows PC (only needed once during installation).

---

## 🛠️ Installation Guide

Because this extension executes native Windows commands, installation is a quick two-step process:

### Step 1: Install the Windows Bridge (`setup.bat`)
Chrome cannot run local `.exe` files for security reasons. We solve this by registering a custom Windows URL protocol (`ytdlp://`).
1. Download the latest `.zip` from the [Releases](https://github.com/muzammilAwan-dev/yt-downloader-pro/releases) page.
2. Extract the folder to a permanent location.
3. Right-click **`setup.bat`** and select **Run as Administrator**.
   * *Note: This script safely creates a `C:\bin` directory, downloads the latest official `yt-dlp.exe` and `FFmpeg` binaries via PowerShell, and updates your System PATH and Registry.*

### Step 2: Load the Chrome Extension
1. Open your browser and navigate to `chrome://extensions/`.
2. Toggle **Developer mode** ON (top right corner).
3. Click **Load unpacked** (top left).
4. Select the extracted extension folder.

---

## 📖 How to Use
1. Navigate to any YouTube Video, Playlist, or Short.
2. Look for the floating **Download** button in the top right corner of the video player.
3. Click it to open the glassmorphism menu.
4. Select your desired resolution, toggle any advanced options (like Crop Video or Full Playlist), and watch your terminal spring to life!
5. *(Optional)* Click the extension puzzle piece icon in your browser toolbar to access global defaults and Advanced Custom Command settings.

---

## 💡 Troubleshooting & FAQ

**"Uncaught Error: Extension context invalidated"**
* **Cause:** You reloaded the extension code in the background while developing.
* **Fix:** Simply press `Ctrl + F5` to hard-refresh the YouTube tab. 

**"The setup.bat file triggered Windows Defender / SmartScreen"**
* **Cause:** The script uses PowerShell to download `.exe` files (`yt-dlp` and `FFmpeg`) from GitHub, which triggers generic antivirus warnings. 
* **Fix:** The code is 100% open-source. You can open `setup.bat` in Notepad to verify it. Click "More Info" -> "Run anyway".

**"4K MKV files don't show thumbnails in Windows Explorer"**
* **Cause:** The Windows OS natively lacks the codec to read embedded metadata cover art inside `.mkv` files.
* **Fix:** The extension embeds the thumbnail perfectly. Use a modern media player like VLC, MPC-HC, or Plex, and the cover art will display beautifully.

**"My custom command failed to execute"**
* **Fix:** Ensure you did not type the YouTube URL in the custom command box. Just type your flags (e.g., `yt-dlp -f bestaudio`). The extension automatically appends the current video URL for you!

---

## 🗺️ Roadmap
- [ ] Add support for Twitter/X and Reddit video downloads.
- [ ] Implement Light/Dark mode syncing with system preferences.
- [ ] Add an in-extension Download History log.
- [ ] Direct MP3 tagging with Spotify/Apple Music metadata APIs.

---

## 💻 Tech Stack
* **Frontend:** Vanilla JavaScript, HTML5, CSS3 (Glassmorphism UI)
* **Backend / System:** Windows Batch (`.bat`), PowerShell, Windows Registry
* **Core Engine:** `yt-dlp`, FFmpeg
* **APIs:** Chrome Extension Manifest V3 (`chrome.storage`, `chrome.cookies`, `chrome.tabs`)

---

## ⚠️ Disclaimer
This tool is built strictly for educational purposes, personal archiving, and downloading royalty-free or creative commons content. The developer does not condone or support the piracy of copyrighted material. By using this software, you agree to bear full responsibility for your actions and to comply with the Terms of Service of the platforms you download from.

---

## 🤝 Contributing
Feel free to open issues or submit pull requests. If you want to add support for a new site, improve the floating UI, or optimize the `yt-dlp` arguments, contributions are highly welcome!

## 📜 License
This project is open-source and available under the **MIT License**.