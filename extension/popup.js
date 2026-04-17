/**
 * @fileoverview Popup UI Controller
 * Integrates Audio Formats, Timestamp Cropper, Advanced Settings, and Auto-Update Checker.
 * @version 5.4.0
 */

(function() {
  'use strict';

  const elements = {
    updateNotice: document.getElementById('updateNotice'),
    downloadBtn: document.getElementById('downloadBtn'),
    settingsBtn: document.getElementById('settingsBtn'), 
    settingsPanel: document.getElementById('settingsPanel'), 
    
    savePath: document.getElementById('savePath'),
    resolution: document.getElementById('resolution'),
    audioFormatWrapper: document.getElementById('audioFormatWrapper'),
    audioFormat: document.getElementById('audioFormat'),
    startTime: document.getElementById('startTime'),
    endTime: document.getElementById('endTime'),
    
    subsToggle: document.getElementById('subsToggle'),
    playlistToggle: document.getElementById('playlistToggle'),
    playlistOptions: document.getElementById('playlistOptions'),
    playlistItems: document.getElementById('playlistItems'),
    cookiesToggle: document.getElementById('cookiesToggle'),
    
    flagMetadata: document.getElementById('flagMetadata'),
    flagThumbnail: document.getElementById('flagThumbnail'),
    flagSponsor: document.getElementById('flagSponsor'),
    customCommand: document.getElementById('customCommand'),
    concurrentDownloads: document.getElementById('concurrentDownloads'), 
    
    status: document.getElementById('status')
  };

  function initialize() {
    checkForUpdates();
    loadSavedPreferences();
    attachEventListeners();
    validateCurrentTab();
  }

  async function checkForUpdates() {
    try {
      const currentVersion = chrome.runtime.getManifest().version;
      const res = await fetch('https://api.github.com/repos/muzammilAwan-dev/yt-downloader-pro/releases/latest');
      const data = await res.json();
      const latestVersion = data.tag_name.replace('v', '');
      
      if (latestVersion > currentVersion) {
        elements.updateNotice.innerHTML = `Update available: v${latestVersion}! <a href="${data.html_url}" target="_blank">Download here</a>`;
        elements.updateNotice.style.display = 'block';
      }
    } catch (e) { console.warn('Update check failed quietly.', e); }
  }

  async function loadSavedPreferences() {
    try {
      const prefs = await chrome.storage.sync.get([
        'savePath', 'resolution', 'audioFormat', 'embedSubs', 'downloadPlaylist',
        'playlistItems', 'useCookies', 'concurrentDownloads', 
        'flagMetadata', 'flagThumbnail', 'flagSponsor'
      ]);
      
      if (prefs.savePath) elements.savePath.value = prefs.savePath;
      
      if (prefs.resolution) {
          elements.resolution.value = prefs.resolution;
          toggleAudioDropdown(prefs.resolution);
      }
      if (prefs.audioFormat) elements.audioFormat.value = prefs.audioFormat;

      if (prefs.flagMetadata !== undefined) elements.flagMetadata.checked = prefs.flagMetadata;
      if (prefs.flagThumbnail !== undefined) elements.flagThumbnail.checked = prefs.flagThumbnail;
      if (prefs.flagSponsor !== undefined) elements.flagSponsor.checked = prefs.flagSponsor;
      elements.concurrentDownloads.value = prefs.concurrentDownloads || "4"; 

      if (prefs.embedSubs) elements.subsToggle.checked = prefs.embedSubs;
      if (prefs.downloadPlaylist) {
          elements.playlistToggle.checked = prefs.downloadPlaylist;
          elements.playlistOptions.style.display = 'block';
      }
      if (prefs.playlistItems) elements.playlistItems.value = prefs.playlistItems;
      if (prefs.useCookies) elements.cookiesToggle.checked = prefs.useCookies;
    } catch (error) { console.warn('Preferences load failed:', error); }
  }

  async function savePreferences() {
    try {
      await chrome.storage.sync.set({
        savePath: elements.savePath.value,
        resolution: elements.resolution.value,
        audioFormat: elements.audioFormat.value,
        embedSubs: elements.subsToggle.checked,
        downloadPlaylist: elements.playlistToggle.checked,
        playlistItems: elements.playlistItems.value.trim(),
        useCookies: elements.cookiesToggle.checked,
        concurrentDownloads: elements.concurrentDownloads.value,
        flagMetadata: elements.flagMetadata.checked,
        flagThumbnail: elements.flagThumbnail.checked,
        flagSponsor: elements.flagSponsor.checked
      });
    } catch (error) { console.warn('Preferences save failed:', error); }
  }

  function toggleAudioDropdown(resolutionVal) {
      if (resolutionVal === 'audio') {
          elements.audioFormatWrapper.style.display = 'block';
      } else {
          elements.audioFormatWrapper.style.display = 'none';
      }
  }

  function attachEventListeners() {
    elements.downloadBtn.addEventListener('click', handleDownload);
    
    elements.settingsBtn.addEventListener('click', () => {
        elements.settingsPanel.style.display = elements.settingsPanel.style.display === 'block' ? 'none' : 'block';
    });

    elements.resolution.addEventListener('change', (e) => {
        toggleAudioDropdown(e.target.value);
        savePreferences();
    });

    elements.playlistToggle.addEventListener('change', (e) => {
      elements.playlistOptions.style.display = e.target.checked ? 'block' : 'none';
      savePreferences();
    });
    
    [elements.savePath, elements.audioFormat, elements.subsToggle, elements.playlistItems, elements.cookiesToggle, elements.concurrentDownloads, elements.flagMetadata, elements.flagThumbnail, elements.flagSponsor]
      .forEach(el => el.addEventListener('change', savePreferences));
  }

  async function validateCurrentTab() {
    try {
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      if (!isValidYouTubeUrl(tab?.url)) {
        showStatus('Please navigate to a YouTube video first', 'error');
        elements.downloadBtn.disabled = true;
      }
    } catch (error) { showStatus('Unable to access current tab', 'error'); }
  }

  function isValidYouTubeUrl(url) {
    if (!url) return false;
    return [/^https?:\/\/(www\.)?youtube\.com\/watch\?v=[\w-]+/, /^https?:\/\/(www\.)?youtube\.com\/playlist\?list=[\w-]+/, /^https?:\/\/youtu\.be\/[\w-]+/, /^https?:\/\/(www\.)?youtube\.com\/shorts\/[\w-]+/].some(p => p.test(url));
  }

  async function handleDownload() {
    const btn = elements.downloadBtn;
    const originalText = btn.innerHTML;
    try {
      btn.disabled = true;
      btn.innerHTML = `<svg viewBox="0 0 24 24" style="animation: spin 1s linear infinite"><path d="M12 2v4m0 12v4M4.93 4.93l2.83 2.83m8.48 8.48l2.83 2.83M2 12h4m12 0h4M4.93 19.07l2.83-2.83m8.48-8.48l2.83-2.83"/></svg> Processing...`;
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      if (!isValidYouTubeUrl(tab.url)) throw new Error('Invalid YouTube URL.');

      await executeCommand(buildYtDlpCommand(tab.url));
      showStatus('Download started! Check your terminal window.', 'success');
      setTimeout(() => window.close(), 2000);
    } catch (error) {
      showStatus(error.message, 'error');
      btn.disabled = false;
      btn.innerHTML = originalText;
    }
  }

  function buildYtDlpCommand(videoUrl) {
    const customCmd = elements.customCommand.value.trim();
    if (customCmd !== '' || elements.resolution.value === 'custom') {
        let finalCustom = customCmd ? customCmd : 'yt-dlp';
        if (!finalCustom.includes(videoUrl)) finalCustom += ` "${videoUrl}"`;
        return finalCustom;
    }

    const settings = {
      savePath: elements.savePath.value.trim(),
      resolution: elements.resolution.value,
      audioFormat: elements.audioFormat.value,
      embedSubs: elements.subsToggle.checked,
      isPlaylist: elements.playlistToggle.checked,
      playlistItems: elements.playlistItems.value.trim(),
      concurrentDownloads: elements.concurrentDownloads.value || "4"
    };

    let saveDir = settings.savePath || '~/Downloads/YT-Downloads/';
    saveDir = saveDir.replace(/[/\\]$/, '') + '\\';
    saveDir = saveDir.replace(/\\/g, '\\\\');

    const formatConfig = buildFormatConfig(settings.resolution, settings.audioFormat);
    const outputTemplate = buildOutputTemplate(saveDir, settings.isPlaylist, formatConfig.extension, settings.resolution);
    
    let cropperOptions = '';
    const startT = elements.startTime.value.trim();
    const endT = elements.endTime.value.trim();
    if (startT || endT) {
        const s = startT ? startT : '00:00:00';
        const e = endT ? endT : 'inf';
        cropperOptions = `--download-sections "*${s}-${e}" --force-keyframes-at-cuts `;
    }

    const subOptions = (settings.embedSubs && settings.resolution !== 'audio') 
        ? '--write-subs --write-auto-subs --embed-subs --sub-langs "en.*" --sleep-subtitles 2 ' 
        : '';
    
    let playlistOptions = '--no-playlist ';
    if (settings.isPlaylist) {
        playlistOptions = '--yes-playlist ';
        if (settings.playlistItems) {
            let cleanItems = settings.playlistItems.toLowerCase().replace(/\s+(to|through)\s+/g, '-').replace(/[^0-9,\-]/g, '').replace(/,+/g, ',').replace(/(^,)|(,$)/g, '');
            if (cleanItems) playlistOptions += `--playlist-items "${cleanItems}" `;
        }
    }

    const metaFlag = elements.flagMetadata.checked ? '--embed-metadata' : '';
    
    // SMART WAV FIX: Disable thumbnail embedding if format is WAV
    const thumbFlag = (elements.flagThumbnail.checked && formatConfig.extension !== 'wav') 
        ? '--embed-thumbnail --convert-thumbnails jpg' 
        : '';
        
    const sponsorFlag = elements.flagSponsor.checked ? '--sponsorblock-remove all' : '';
    
    return [
      'yt-dlp',
      '-f', `"${formatConfig.formatString}"`,
      formatConfig.mediaOptions,
      `-N ${settings.concurrentDownloads}`,
      cropperOptions,
      subOptions,
      playlistOptions,
      sponsorFlag,
      '--restrict-filenames',
      metaFlag,
      thumbFlag,
      '--no-warnings',
      '--progress',
      outputTemplate,
      `"${videoUrl}"`
    ].filter(Boolean).join(' ');
  }

  function buildFormatConfig(res, audioFmt) {
    if (res === 'audio') {
      let quality = audioFmt === 'flac' || audioFmt === 'wav' ? '' : '--audio-quality 0';
      return { formatString: 'ba', mediaOptions: `--extract-audio --audio-format ${audioFmt} ${quality}`.trim(), extension: audioFmt };
    }
    if (res === '1440' || res === '2160') {
      return { formatString: `bv*[height<=${res}]+ba/b[height<=${res}]/bv*+ba/b`, mediaOptions: '--merge-output-format mkv', extension: 'mkv' };
    }
    return { formatString: `bv*[height<=${res}]+ba/b[height<=${res}]/bv*+ba/b`, mediaOptions: '--merge-output-format mp4', extension: 'mp4' };
  }

  function buildOutputTemplate(dir, isPl, ext, res) {
    const resLabel = res === 'audio' ? 'Audio' : `${res}p`;
    return isPl ? `-o "${dir}%(playlist_title)s/%(playlist_index)03d_%(title)s_${resLabel}.${ext}"` : `-o "${dir}%(title)s_${resLabel}.${ext}"`;
  }

  async function executeCommand(command) {
    let encodedCommand = btoa(unescape(encodeURIComponent(command)));
    if (elements.cookiesToggle.checked) {
        const cookieBase64 = await new Promise((resolve, reject) => {
            chrome.runtime.sendMessage({ action: "get_cookies" }, (response) => {
                if (chrome.runtime.lastError) {
                    console.error("Background Error:", chrome.runtime.lastError);
                    return reject(new Error("Cookie error: " + chrome.runtime.lastError.message));
                }
                resolve(response);
            });
        });
        if (cookieBase64) encodedCommand += `||${cookieBase64}`;
    }
    const protocolUrl = `ytdlp://${encodedCommand}`;
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    await chrome.tabs.update(tab.id, { url: protocolUrl });
  }

  function showStatus(msg, type = 'info') {
    const el = elements.status;
    el.textContent = msg; el.className = `status ${type} show`;
    if (type === 'success') setTimeout(() => el.classList.remove('show'), 5000);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize);
  else initialize();
})();