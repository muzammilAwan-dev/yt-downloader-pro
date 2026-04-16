/**
 * @fileoverview Popup UI Controller
 * Manages state binding, validation, and command generation for the extension popup.
 * @version 5.4.0
 */

(function() {
  'use strict';

  /** @type {Object<string, HTMLElement>} Cached DOM elements */
  const elements = {
    downloadBtn: document.getElementById('downloadBtn'),
    savePath: document.getElementById('savePath'),
    resolution: document.getElementById('resolution'),
    subsToggle: document.getElementById('subsToggle'),
    playlistToggle: document.getElementById('playlistToggle'),
    cookiesToggle: document.getElementById('cookiesToggle'),
    status: document.getElementById('status')
  };

  /**
   * Bootstraps UI state and event listeners.
   */
  function initialize() {
    loadSavedPreferences();
    attachEventListeners();
    validateCurrentTab();
  }

  /**
   * Retrieves persistent user preferences from Chrome Storage API.
   */
  async function loadSavedPreferences() {
    try {
      const prefs = await chrome.storage.sync.get([
        'savePath', 
        'resolution', 
        'embedSubs', 
        'downloadPlaylist',
        'useCookies'
      ]);
      
      if (prefs.savePath) elements.savePath.value = prefs.savePath;
      if (prefs.resolution) elements.resolution.value = prefs.resolution;
      if (prefs.embedSubs) elements.subsToggle.checked = prefs.embedSubs;
      if (prefs.downloadPlaylist) elements.playlistToggle.checked = prefs.downloadPlaylist;
      if (prefs.useCookies) elements.cookiesToggle.checked = prefs.useCookies;
    } catch (error) {
      console.warn('Preferences load failed:', error);
    }
  }

  /**
   * Persists current UI state to Chrome Storage API.
   */
  async function savePreferences() {
    try {
      await chrome.storage.sync.set({
        savePath: elements.savePath.value,
        resolution: elements.resolution.value,
        embedSubs: elements.subsToggle.checked,
        downloadPlaylist: elements.playlistToggle.checked,
        useCookies: elements.cookiesToggle.checked
      });
    } catch (error) {
      console.warn('Preferences save failed:', error);
    }
  }

  /**
   * Binds UI interaction events.
   */
  function attachEventListeners() {
    elements.downloadBtn.addEventListener('click', handleDownload);
    
    [elements.savePath, elements.resolution, elements.subsToggle, elements.playlistToggle, elements.cookiesToggle]
      .forEach(el => el.addEventListener('change', savePreferences));
  }

  /**
   * Validates if the active tab contains a supported YouTube URL.
   */
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

  /**
   * Regex validation for standard YouTube video and playlist schemas.
   * @param {string} url 
   * @returns {boolean}
   */
  function isValidYouTubeUrl(url) {
    if (!url) return false;
    const patterns = [
      /^https?:\/\/(www\.)?youtube\.com\/watch\?v=[\w-]+/,
      /^https?:\/\/(www\.)?youtube\.com\/playlist\?list=[\w-]+/,
      /^https?:\/\/youtu\.be\/[\w-]+/
    ];
    return patterns.some(pattern => pattern.test(url));
  }

  /**
   * Entry point for download execution flow.
   */
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

  /**
   * Constructs the base shell command for yt-dlp execution.
   * @param {string} videoUrl 
   * @returns {string} 
   */
  function buildYtDlpCommand(videoUrl) {
    const settings = {
      savePath: elements.savePath.value.trim(),
      resolution: elements.resolution.value,
      embedSubs: elements.subsToggle.checked,
      isPlaylist: elements.playlistToggle.checked
    };

    let saveDir = settings.savePath || '~/Downloads/YT-Downloads/';
    saveDir = saveDir.replace(/[/\\]$/, '') + '\\';
    saveDir = saveDir.replace(/\\/g, '\\\\');

    const formatConfig = buildFormatConfig(settings.resolution);
    const outputTemplate = buildOutputTemplate(saveDir, settings.isPlaylist, formatConfig.extension, settings.resolution);
    
    // Limits subtitles to English variants to prevent HTTP 429 rate-limiting
    const subOptions = (settings.embedSubs && settings.resolution !== 'audio') 
      ? '--write-subs --write-auto-subs --embed-subs --sub-langs "en.*" ' 
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

  /**
   * Maps UI resolution values to yt-dlp format selection flags.
   * @param {string} resolution 
   * @returns {Object} 
   */
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

  /**
   * Handles directory routing for playlists vs single files with dynamic resolution tags.
   * @param {string} saveDir 
   * @param {boolean} isPlaylist 
   * @param {string} extension 
   * @param {string} resolution
   * @returns {string} 
   */
  function buildOutputTemplate(saveDir, isPlaylist, extension, resolution) {
    const resLabel = resolution === 'audio' ? 'Audio' : `${resolution}p`;
    
    if (isPlaylist) {
      return `-o "${saveDir}%(playlist_title)s/%(playlist_index)03d_%(title)s_${resLabel}.${extension}"`;
    }
    return `-o "${saveDir}%(title)s_${resLabel}.${extension}"`;
  }

  /**
   * Orchestrates payload transmission to native host via custom protocol.
   * Integrates background cookie extraction if bypass is enabled.
   * @param {string} command 
   */
  async function executeCommand(command) {
    let encodedCommand = btoa(unescape(encodeURIComponent(command)));
    
    if (elements.cookiesToggle.checked) {
        const cookieBase64 = await new Promise(resolve => {
            chrome.runtime.sendMessage({ action: "get_cookies" }, response => resolve(response));
        });
        
        if (cookieBase64) {
            encodedCommand += `||${cookieBase64}`;
        }
    }

    const protocolUrl = `ytdlp://${encodedCommand}`;
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    await chrome.tabs.update(tab.id, { url: protocolUrl });
  }

  /**
   * Displays localized status alerts.
   * @param {string} message 
   * @param {string} type 
   */
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

  // Initialization Hook
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initialize);
  } else {
    initialize();
  }
})();