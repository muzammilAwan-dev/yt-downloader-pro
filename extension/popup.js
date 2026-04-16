/**
 * @fileoverview Popup UI Controller
 * Manages state binding, validation, and command generation for the extension popup.
 * @version 5.3.0
 */

(function() {
  'use strict';

  const elements = {
    downloadBtn: document.getElementById('downloadBtn'),
    settingsBtn: document.getElementById('settingsBtn'), // NEW
    settingsPanel: document.getElementById('settingsPanel'), // NEW
    concurrentDownloads: document.getElementById('concurrentDownloads'), // NEW
    savePath: document.getElementById('savePath'),
    resolution: document.getElementById('resolution'),
    subsToggle: document.getElementById('subsToggle'),
    playlistToggle: document.getElementById('playlistToggle'),
    playlistOptions: document.getElementById('playlistOptions'),
    playlistItems: document.getElementById('playlistItems'),
    cookiesToggle: document.getElementById('cookiesToggle'),
    status: document.getElementById('status')
  };

  function initialize() {
    loadSavedPreferences();
    attachEventListeners();
    validateCurrentTab();
  }

  async function loadSavedPreferences() {
    try {
      const prefs = await chrome.storage.sync.get([
        'savePath', 'resolution', 'embedSubs', 'downloadPlaylist',
        'playlistItems', 'useCookies', 'concurrentDownloads'
      ]);
      
      if (prefs.savePath) elements.savePath.value = prefs.savePath;
      if (prefs.resolution) elements.resolution.value = prefs.resolution;
      if (prefs.embedSubs) elements.subsToggle.checked = prefs.embedSubs;
      if (prefs.downloadPlaylist) {
          elements.playlistToggle.checked = prefs.downloadPlaylist;
          elements.playlistOptions.style.display = 'block';
      }
      if (prefs.playlistItems) elements.playlistItems.value = prefs.playlistItems;
      if (prefs.useCookies) elements.cookiesToggle.checked = prefs.useCookies;
      if (prefs.concurrentDownloads) elements.concurrentDownloads.value = prefs.concurrentDownloads;
    } catch (error) {
      console.warn('Preferences load failed:', error);
    }
  }

  async function savePreferences() {
    try {
      await chrome.storage.sync.set({
        savePath: elements.savePath.value,
        resolution: elements.resolution.value,
        embedSubs: elements.subsToggle.checked,
        downloadPlaylist: elements.playlistToggle.checked,
        playlistItems: elements.playlistItems.value.trim(),
        useCookies: elements.cookiesToggle.checked,
        concurrentDownloads: elements.concurrentDownloads.value
      });
    } catch (error) {
      console.warn('Preferences save failed:', error);
    }
  }

  function attachEventListeners() {
    elements.downloadBtn.addEventListener('click', handleDownload);
    
    // Toggle Settings Panel
    elements.settingsBtn.addEventListener('click', () => {
        elements.settingsPanel.style.display = elements.settingsPanel.style.display === 'block' ? 'none' : 'block';
    });

    elements.playlistToggle.addEventListener('change', (e) => {
      elements.playlistOptions.style.display = e.target.checked ? 'block' : 'none';
      savePreferences();
    });
    
    [elements.savePath, elements.resolution, elements.subsToggle, elements.playlistItems, elements.cookiesToggle, elements.concurrentDownloads]
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
    return [/^https?:\/\/(www\.)?youtube\.com\/watch\?v=[\w-]+/, /^https?:\/\/(www\.)?youtube\.com\/playlist\?list=[\w-]+/, /^https?:\/\/youtu\.be\/[\w-]+/].some(p => p.test(url));
  }

  async function handleDownload() {
    const btn = elements.downloadBtn;
    const originalText = btn.innerHTML;
    try {
      btn.disabled = true;
      btn.innerHTML = `<svg viewBox="0 0 24 24" style="animation: spin 1s linear infinite"><path d="M12 2v4m0 12v4M4.93 4.93l2.83 2.83m8.48 8.48l2.83 2.83M2 12h4m12 0h4M4.93 19.07l2.83-2.83m8.48-8.48l2.83-2.83"/></svg> Processing...`;
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      if (!isValidYouTubeUrl(tab.url)) throw new Error('Invalid YouTube URL. Please open a YouTube video.');

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
    const settings = {
      savePath: elements.savePath.value.trim(),
      resolution: elements.resolution.value,
      embedSubs: elements.subsToggle.checked,
      isPlaylist: elements.playlistToggle.checked,
      playlistItems: elements.playlistItems.value.trim(),
      concurrentDownloads: elements.concurrentDownloads.value || "4" // Default to 4
    };

    let saveDir = settings.savePath || '~/Downloads/YT-Downloads/';
    saveDir = saveDir.replace(/[/\\]$/, '') + '\\';
    saveDir = saveDir.replace(/\\/g, '\\\\');

    const formatConfig = buildFormatConfig(settings.resolution);
    const outputTemplate = buildOutputTemplate(saveDir, settings.isPlaylist, formatConfig.extension, settings.resolution);
    
    const subOptions = (settings.embedSubs && settings.resolution !== 'audio') ? '--write-subs --write-auto-subs --embed-subs --sub-langs "en.*" ' : '';
    
    let playlistOptions = '--no-playlist ';
    if (settings.isPlaylist) {
        playlistOptions = '--yes-playlist ';
        if (settings.playlistItems) {
            let cleanItems = settings.playlistItems.toLowerCase().replace(/\s+(to|through)\s+/g, '-').replace(/[^0-9,\-]/g, '').replace(/,+/g, ',').replace(/(^,)|(,$)/g, '');
            if (cleanItems) playlistOptions += `--playlist-items "${cleanItems}" `;
        }
    }
    
    return [
      'yt-dlp',
      '-f', `"${formatConfig.formatString}"`,
      formatConfig.mediaOptions,
      `-N ${settings.concurrentDownloads}`, // IDM Speed Booster Flag
      subOptions,
      playlistOptions,
      '--sponsorblock-remove all',
      '--restrict-filenames',
      '--embed-metadata',
      '--embed-thumbnail', // <--- ADD THIS LINE HERE!
      '--no-warnings',
      '--progress',
      outputTemplate,
      `"${videoUrl}"`
    ].filter(Boolean).join(' ');
  }

  function buildFormatConfig(res) {
    if (res === 'audio') return { formatString: 'ba', mediaOptions: '--extract-audio --audio-format mp3 --audio-quality 0', extension: 'mp3' };
    return { formatString: `bv*[height<=${res}]+ba/b[height<=${res}]/bv*+ba/b`, mediaOptions: '--merge-output-format mp4', extension: 'mp4' };
  }

  function buildOutputTemplate(dir, isPl, ext, res) {
    const resLabel = res === 'audio' ? 'Audio' : `${res}p`;
    return isPl ? `-o "${dir}%(playlist_title)s/%(playlist_index)03d_%(title)s_${resLabel}.${ext}"` : `-o "${dir}%(title)s_${resLabel}.${ext}"`;
  }

  async function executeCommand(command) {
    let encodedCommand = btoa(unescape(encodeURIComponent(command)));
    if (elements.cookiesToggle.checked) {
        const cookieBase64 = await new Promise(res => chrome.runtime.sendMessage({ action: "get_cookies" }, res));
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