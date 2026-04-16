/**
 * YT Downloader Pro - Content Script
 * Injects download button into YouTube video player with MutationObserver
 * @version 5.1.1
 */

(function() {
  'use strict';

  const CONFIG = {
    CONTAINER_ID: 'yt-dlp-container',
    CHECK_INTERVAL: 1000, 
    MAX_RETRIES: 10,
    PLAYER_SELECTOR: '#movie_player, #player-container, .html5-video-player'
  };

  let observer = null;
  let retryCount = 0;
  let isInjected = false;

  function initialize() {
    setupMutationObserver();
    setInterval(checkAndInject, CONFIG.CHECK_INTERVAL);
    checkAndInject();
  }

  function setupMutationObserver() {
    if (observer) return;
    
    observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        if (mutation.type === 'childList') {
          for (const node of mutation.addedNodes) {
            if (node.nodeType === Node.ELEMENT_NODE) {
              if (node.matches?.(CONFIG.PLAYER_SELECTOR) || 
                  node.querySelector?.(CONFIG.PLAYER_SELECTOR)) {
                checkAndInject();
                return;
              }
            }
          }
        }
      }
    });
    
    observer.observe(document.body, { childList: true, subtree: true });
  }

  function checkAndInject() {
    if (isInjected || retryCount >= CONFIG.MAX_RETRIES) return;
    
    const player = document.querySelector(CONFIG.PLAYER_SELECTOR);
    if (player && !player.querySelector(`#${CONFIG.CONTAINER_ID}`)) {
      injectDownloadButton(player);
      isInjected = true;
    } else {
      retryCount++;
    }
  }

  function injectDownloadButton(player) {
    const container = createContainer();
    const button = createMainButton();
    const dropdown = createDropdown();
    
    container.appendChild(button);
    container.appendChild(dropdown);
    player.appendChild(container);
    
    setupNavigationHandler();
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
      { id: 'float-playlist', label: 'Full Playlist', storageKey: 'downloadPlaylist' }
    ];
    
    toggles.forEach(toggle => {
      const label = document.createElement('label');
      label.className = 'yt-dlp-toggle-label';
      label.innerHTML = `
        <input type="checkbox" id="${toggle.id}">
        <span class="toggle-text">${toggle.label}</span>
      `;
      
      const checkbox = label.querySelector('input');
      
      // Load saved state from global memory
      chrome.storage.sync.get([toggle.storageKey], (result) => {
        if (result[toggle.storageKey]) checkbox.checked = true;
      });

      // Save state back to global memory if clicked
      checkbox.addEventListener('change', (e) => {
        chrome.storage.sync.set({ [toggle.storageKey]: e.target.checked });
      });

      container.appendChild(label);
    });
    
    return container;
  }

  async function handleQualitySelect(resolution) {
    const wantsSubs = document.getElementById('float-subs')?.checked || false;
    const wantsPlaylist = document.getElementById('float-playlist')?.checked || false;
    
    try {
      await launchDownload(resolution, wantsSubs, wantsPlaylist);
      showToast('Download started! Check terminal window.');
    } catch (error) {
      console.error('Download failed:', error);
      showToast('Failed to start download. Check URL.', 'error');
    }
  }

  async function launchDownload(resolution, wantsSubs, wantsPlaylist) {
    const videoUrl = window.location.href;
    
    if (!videoUrl.match(/youtube\.com|youtu\.be/)) {
      throw new Error('Not a valid YouTube URL');
    }
    
    // Dynamically fetch user's saved path, or fallback to default
    const prefs = await chrome.storage.sync.get(['savePath']);
    let saveDir = prefs.savePath ? prefs.savePath.trim() : '~/Downloads/YT-Downloads/';
    
    // Format path for Windows Batch safety
    saveDir = saveDir.replace(/[/\\]$/, '') + '\\';
    saveDir = saveDir.replace(/\\/g, '\\\\');

    const formatConfig = getFormatConfig(resolution);
    const subCommand = (wantsSubs && resolution !== 'audio') 
      ? '--write-subs --write-auto-subs --embed-subs --sub-langs "en.*,all" ' 
      : '';
    const playlistCommand = wantsPlaylist ? '--yes-playlist ' : '--no-playlist ';
    
    const outputTemplate = wantsPlaylist
      ? `-o "${saveDir}%(playlist_title)s/%(playlist_index)03d - %(title)s.${formatConfig.ext}"`
      : `-o "${saveDir}%(title)s.${formatConfig.ext}"`;
    
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
    
    // Safely encode command
    const encoded = btoa(unescape(encodeURIComponent(command)));
    window.location.href = `ytdlp://${encoded}`;
  }

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

  function setupNavigationHandler() {
    let lastUrl = location.href;
    
    new MutationObserver(() => {
      const currentUrl = location.href;
      if (currentUrl !== lastUrl) {
        lastUrl = currentUrl;
        isInjected = false;
        retryCount = 0;
        
        const existing = document.getElementById(CONFIG.CONTAINER_ID);
        if (existing) existing.remove();
        
        setTimeout(checkAndInject, 500);
      }
    }).observe(document, { subtree: true, childList: true });
  }

  initialize();
})();