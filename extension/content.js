/**
 * @fileoverview DOM Injection Controller
 * Integrates glassmorphism download overlay directly into YouTube player hierarchy.
 * Uses Infinite State Enforcement to guarantee survival against YouTube's SPA routing.
 * @version 5.4.0
 */

(function() {
  'use strict';

  const CONFIG = {
    CONTAINER_ID: 'yt-dlp-container',
    CHECK_INTERVAL: 1000, 
    PLAYER_SELECTOR: '#movie_player'
  };

  /**
   * Bootstraps the Infinite State Enforcement loop.
   */
  function initialize() {
    // Run the enforcer every 1 second, forever.
    setInterval(enforceButtonPresence, CONFIG.CHECK_INTERVAL);
    enforceButtonPresence();
  }

  /**
   * The core logic loop. Relentlessly ensures the button exists on video pages,
   * and cleans it up if the user navigates away to the homepage.
   */
  function enforceButtonPresence() {
    const isWatchPage = window.location.href.includes('/watch');
    const player = document.querySelector(CONFIG.PLAYER_SELECTOR);
    const existingButton = document.getElementById(CONFIG.CONTAINER_ID);

    if (isWatchPage && player) {
        // If we are watching a video but our button is missing, inject it!
        if (!existingButton) {
            injectDownloadButton(player);
        }
    } else {
        // If we navigated back to the YouTube homepage, clean up the button
        if (existingButton) {
            existingButton.remove();
        }
    }
  }

  /**
   * Constructs and attaches DOM tree.
   * @param {Element} player 
   */
  function injectDownloadButton(player) {
    const container = createContainer();
    const button = createMainButton();
    const dropdown = createDropdown();
    
    container.appendChild(button);
    container.appendChild(dropdown);
    player.appendChild(container);
  }

  function createContainer() {
    const container = document.createElement('div');
    container.id = CONFIG.CONTAINER_ID;
    container.setAttribute('role', 'region');
    container.setAttribute('aria-label', 'Video download options');
    return container;
  }

  function createMainButton() {
    const button = document.createElement('button');
    button.className = 'yt-dlp-btn';
    button.setAttribute('type', 'button');
    button.setAttribute('aria-haspopup', 'true');
    button.setAttribute('aria-expanded', 'false');
    button.innerHTML = `
      <svg viewBox="0 0 24 24" aria-hidden="true">
        <path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/>
      </svg>
      <span>Download</span>
    `;
    
    button.addEventListener('click', (e) => {
      e.stopPropagation();
      const dropdown = button.nextElementSibling;
      const isExpanded = dropdown.classList.contains('show');
      
      document.querySelectorAll('.yt-dlp-dropdown').forEach(d => {
        d.classList.remove('show');
      });
      
      dropdown.classList.toggle('show');
      button.setAttribute('aria-expanded', !isExpanded);
    });
    
    return button;
  }

  function createDropdown() {
    const dropdown = document.createElement('div');
    dropdown.className = 'yt-dlp-dropdown';
    dropdown.setAttribute('role', 'menu');
    
    const qualities = [
      { value: 'audio', label: '🎵 Audio Only (MP3)', desc: 'Best quality audio' },
      { value: '2160', label: '4K (2160p)', desc: 'Ultra HD' },
      { value: '1440', label: '2K (1440p)', desc: 'Quad HD' },
      { value: '1080', label: 'HD (1080p)', desc: 'Full HD', default: true },
      { value: '720', label: 'HD (720p)', desc: 'Standard HD' },
      { value: '480', label: 'SD (480p)', desc: 'Standard quality' },
      { value: '360', label: 'SD (360p)', desc: 'Low quality' }
    ];
    
    qualities.forEach(quality => {
      const btn = document.createElement('button');
      btn.className = 'yt-dlp-option';
      btn.setAttribute('role', 'menuitem');
      btn.setAttribute('data-res', quality.value);
      btn.innerHTML = `
        <span class="option-label">${quality.label}</span>
        ${quality.default ? '<span class="option-badge">Default</span>' : ''}
      `;
      
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        handleQualitySelect(quality.value);
      });
      
      dropdown.appendChild(btn);
    });
    
    const separator = document.createElement('div');
    separator.className = 'yt-dlp-separator';
    dropdown.appendChild(separator);
    
    const toggles = createToggleOptions();
    dropdown.appendChild(toggles);
    
    document.addEventListener('click', () => {
      dropdown.classList.remove('show');
      const btn = dropdown.previousElementSibling;
      if (btn) btn.setAttribute('aria-expanded', 'false');
    });
    
    return dropdown;
  }

  function createToggleOptions() {
    const container = document.createElement('div');
    container.className = 'yt-dlp-toggles';
    
    const toggles = [
      { id: 'float-subs', label: 'Embed Subtitles', storageKey: 'embedSubs' },
      { id: 'float-playlist', label: 'Full Playlist', storageKey: 'downloadPlaylist' },
      { id: 'float-cookies', label: 'Bypass Age Lock', storageKey: 'useCookies' }
    ];
    
    toggles.forEach(toggle => {
      const label = document.createElement('label');
      label.className = 'yt-dlp-toggle-label';
      label.innerHTML = `
        <input type="checkbox" id="${toggle.id}">
        <span class="toggle-text">${toggle.label}</span>
      `;
      
      const checkbox = label.querySelector('input');
      
      chrome.storage.sync.get([toggle.storageKey], (result) => {
        if (result[toggle.storageKey]) checkbox.checked = true;
      });

      checkbox.addEventListener('change', (e) => {
        chrome.storage.sync.set({ [toggle.storageKey]: e.target.checked });
      });

      container.appendChild(label);
    });
    
    return container;
  }

  /**
   * Gathers active state and triggers execution.
   * @param {string} resolution 
   */
  async function handleQualitySelect(resolution) {
    const wantsSubs = document.getElementById('float-subs')?.checked || false;
    const wantsPlaylist = document.getElementById('float-playlist')?.checked || false;
    const wantsCookies = document.getElementById('float-cookies')?.checked || false;
    
    try {
      await launchDownload(resolution, wantsSubs, wantsPlaylist, wantsCookies);
      showToast('Download started! Check terminal window.');
    } catch (error) {
      console.error('Download execution failed:', error);
      showToast('Failed to start download. Check URL.', 'error');
    }
  }

  /**
   * Compiles execution payload and triggers native handler.
   * @param {string} resolution 
   * @param {boolean} wantsSubs 
   * @param {boolean} wantsPlaylist 
   * @param {boolean} wantsCookies 
   */
  async function launchDownload(resolution, wantsSubs, wantsPlaylist, wantsCookies) {
    const videoUrl = window.location.href;
    
    if (!videoUrl.match(/youtube\.com|youtu\.be/)) {
      throw new Error('Invalid YouTube Context');
    }
    
    const prefs = await chrome.storage.sync.get(['savePath']);
    let saveDir = prefs.savePath ? prefs.savePath.trim() : '~/Downloads/YT-Downloads/';
    
    saveDir = saveDir.replace(/[/\\]$/, '') + '\\';
    saveDir = saveDir.replace(/\\/g, '\\\\');

    const formatConfig = getFormatConfig(resolution);
    const resLabel = resolution === 'audio' ? 'Audio' : `${resolution}p`;
    
    const subCommand = (wantsSubs && resolution !== 'audio') 
      ? '--write-subs --write-auto-subs --embed-subs --sub-langs "en.*" ' 
      : '';
      
    const playlistCommand = wantsPlaylist ? '--yes-playlist ' : '--no-playlist ';
    
    const outputTemplate = wantsPlaylist
      ? `-o "${saveDir}%(playlist_title)s/%(playlist_index)03d_%(title)s_${resLabel}.${formatConfig.ext}"`
      : `-o "${saveDir}%(title)s_${resLabel}.${formatConfig.ext}"`;
    
    const command = [
      'yt-dlp',
      '-f', `"${formatConfig.format}"`,
      formatConfig.mediaOptions,
      subCommand,
      playlistCommand,
      '--sponsorblock-remove all',
      '--restrict-filenames',
      '--embed-metadata',
      '--progress',
      outputTemplate,
      `"${videoUrl}"`
    ].join(' ');
    
    let encoded = btoa(unescape(encodeURIComponent(command)));

    // Request session cookies from background worker if bypass is required
    if (wantsCookies) {
        const cookieBase64 = await new Promise(resolve => {
            chrome.runtime.sendMessage({ action: "get_cookies" }, response => resolve(response));
        });
        
        if (cookieBase64) {
            encoded += `||${cookieBase64}`;
        }
    }

    window.location.href = `ytdlp://${encoded}`;
  }

  /**
   * Maps UI resolution values to yt-dlp format selection flags.
   * @param {string} resolution 
   * @returns {Object} 
   */
  function getFormatConfig(resolution) {
    if (resolution === 'audio') {
      return {
        format: 'ba',
        mediaOptions: '--extract-audio --audio-format mp3 --audio-quality 0',
        ext: 'mp3'
      };
    }
    
    return {
      format: `bv*[height<=${resolution}]+ba/b[height<=${resolution}]/bv*+ba/b`,
      mediaOptions: '--merge-output-format mp4',
      ext: 'mp4'
    };
  }

  /**
   * Renders non-blocking UI notifications.
   * @param {string} message 
   * @param {string} type 
   */
  function showToast(message, type = 'success') {
    const existing = document.querySelector('.yt-dlp-toast');
    if (existing) existing.remove();
    
    const toast = document.createElement('div');
    toast.className = `yt-dlp-toast ${type}`;
    toast.textContent = message;
    toast.setAttribute('role', 'status');
    toast.setAttribute('aria-live', 'polite');
    
    document.body.appendChild(toast);
    
    requestAnimationFrame(() => {
      toast.classList.add('show');
    });
    
    setTimeout(() => {
      toast.classList.remove('show');
      setTimeout(() => toast.remove(), 300);
    }, 3000);
  }

  // Initialization Hook
  initialize();
})();