/**
 * YT Downloader Pro - Popup Script
 * Handles user interactions in the extension popup and generates yt-dlp commands
 * @version 5.1.1
 */

(function() {
  'use strict';

  const elements = {
    downloadBtn: document.getElementById('downloadBtn'),
    savePath: document.getElementById('savePath'),
    resolution: document.getElementById('resolution'),
    subsToggle: document.getElementById('subsToggle'),
    playlistToggle: document.getElementById('playlistToggle'),
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
        'savePath', 
        'resolution', 
        'embedSubs', 
        'downloadPlaylist'
      ]);
      
      if (prefs.savePath) elements.savePath.value = prefs.savePath;
      if (prefs.resolution) elements.resolution.value = prefs.resolution;
      if (prefs.embedSubs) elements.subsToggle.checked = prefs.embedSubs;
      if (prefs.downloadPlaylist) elements.playlistToggle.checked = prefs.downloadPlaylist;
    } catch (error) {
      console.warn('Failed to load preferences:', error);
    }
  }

  async function savePreferences() {
    try {
      await chrome.storage.sync.set({
        savePath: elements.savePath.value,
        resolution: elements.resolution.value,
        embedSubs: elements.subsToggle.checked,
        downloadPlaylist: elements.playlistToggle.checked
      });
    } catch (error) {
      console.warn('Failed to save preferences:', error);
    }
  }

  function attachEventListeners() {
    elements.downloadBtn.addEventListener('click', handleDownload);
    
    [elements.savePath, elements.resolution, elements.subsToggle, elements.playlistToggle]
      .forEach(el => el.addEventListener('change', savePreferences));
  }

  async function validateCurrentTab() {
    try {
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      
      if (!isValidYouTubeUrl(tab?.url)) {
        showStatus('Please navigate to a YouTube video first', 'error');
        elements.downloadBtn.disabled = true;
      }
    } catch (error) {
      showStatus('Unable to access current tab', 'error');
    }
  }

  function isValidYouTubeUrl(url) {
    if (!url) return false;
    const patterns = [
      /^https?:\/\/(www\.)?youtube\.com\/watch\?v=[\w-]+/,
      /^https?:\/\/(www\.)?youtube\.com\/playlist\?list=[\w-]+/,
      /^https?:\/\/youtu\.be\/[\w-]+/
    ];
    return patterns.some(pattern => pattern.test(url));
  }

  async function handleDownload() {
    const btn = elements.downloadBtn;
    const originalText = btn.innerHTML;
    
    try {
      btn.disabled = true;
      btn.innerHTML = `<svg viewBox="0 0 24 24" style="animation: spin 1s linear infinite"><path d="M12 2v4m0 12v4M4.93 4.93l2.83 2.83m8.48 8.48l2.83 2.83M2 12h4m12 0h4M4.93 19.07l2.83-2.83m8.48-8.48l2.83-2.83"/></svg> Processing...`;
      
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      
      if (!isValidYouTubeUrl(tab.url)) {
        throw new Error('Invalid YouTube URL. Please open a YouTube video.');
      }

      const command = buildYtDlpCommand(tab.url);
      await executeCommand(command);
      
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
      isPlaylist: elements.playlistToggle.checked
    };

    let saveDir = settings.savePath || '~/Downloads/YT-Downloads/';
    
    // Format path for Windows Batch safety
    saveDir = saveDir.replace(/[/\\]$/, '') + '\\';
    saveDir = saveDir.replace(/\\/g, '\\\\');

    const formatConfig = buildFormatConfig(settings.resolution);
    const outputTemplate = buildOutputTemplate(saveDir, settings.isPlaylist, formatConfig.extension);
    
    const subOptions = (settings.embedSubs && settings.resolution !== 'audio') 
      ? '--write-subs --write-auto-subs --embed-subs --sub-langs "en.*,all" ' 
      : '';
    
    const playlistOptions = settings.isPlaylist ? '--yes-playlist ' : '--no-playlist ';
    
    const command = [
      'yt-dlp',
      '-f', `"${formatConfig.formatString}"`,
      formatConfig.mediaOptions,
      subOptions,
      playlistOptions,
      '--sponsorblock-remove all',
      '--restrict-filenames',
      '--embed-metadata',
      '--no-warnings',
      '--progress',
      outputTemplate,
      `"${videoUrl}"`
    ].filter(Boolean).join(' ');

    return command;
  }

  function buildFormatConfig(resolution) {
    if (resolution === 'audio') {
      return {
        formatString: 'ba',
        mediaOptions: '--extract-audio --audio-format mp3 --audio-quality 0',
        extension: 'mp3'
      };
    }
    
    return {
      formatString: `bv*[height<=${resolution}]+ba/b[height<=${resolution}]/bv*+ba/b`,
      mediaOptions: '--merge-output-format mp4',
      extension: 'mp4'
    };
  }

  function buildOutputTemplate(saveDir, isPlaylist, extension) {
    if (isPlaylist) {
      return `-o "${saveDir}%(playlist_title)s/%(playlist_index)03d - %(title)s.${extension}"`;
    }
    return `-o "${saveDir}%(title)s.${extension}"`;
  }

  async function executeCommand(command) {
    // Safely encode command to Base64 (avoids Call Stack limits of TextEncoder arrays)
    const encodedCommand = btoa(unescape(encodeURIComponent(command)));
    const protocolUrl = `ytdlp://${encodedCommand}`;
    
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    await chrome.tabs.update(tab.id, { url: protocolUrl });
  }

  function showStatus(message, type = 'info') {
    const statusEl = elements.status;
    statusEl.textContent = message;
    statusEl.className = `status ${type} show`;
    
    if (type === 'success') {
      setTimeout(() => {
        statusEl.classList.remove('show');
      }, 5000);
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initialize);
  } else {
    initialize();
  }
})();