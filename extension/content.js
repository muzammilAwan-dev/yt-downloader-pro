/**
 * @fileoverview DOM Injection Controller
 * Integrates glassmorphism download overlay with Playlist Ranges.
 * Includes Anti-Lag Debouncing and YouTube Hotkey Hijack Blocker.
 * @version 5.3.0
 */

(function() {
  'use strict';

  const CONFIG = {
    CONTAINER_ID: 'yt-dlp-container',
    CHECK_INTERVAL: 1000, 
    PLAYER_SELECTOR: '#movie_player'
  };

  function initialize() {
    setInterval(enforceButtonPresence, CONFIG.CHECK_INTERVAL);
    enforceButtonPresence();
  }

  function enforceButtonPresence() {
    const isWatchPage = window.location.href.includes('/watch');
    const player = document.querySelector(CONFIG.PLAYER_SELECTOR);
    const existingButton = document.getElementById(CONFIG.CONTAINER_ID);

    if (isWatchPage && player) {
        if (!existingButton) injectDownloadButton(player);
    } else {
        if (existingButton) existingButton.remove();
    }
  }

  function injectDownloadButton(player) {
    const container = document.createElement('div');
    container.id = CONFIG.CONTAINER_ID;
    
    const button = document.createElement('button');
    button.className = 'yt-dlp-btn';
    button.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true" style="width:18px;height:18px;fill:currentColor;"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg><span>Download</span>`;
    
    const dropdown = createDropdown();
    
    button.addEventListener('click', (e) => {
      e.stopPropagation();
      const isExpanded = dropdown.classList.contains('show');
      document.querySelectorAll('.yt-dlp-dropdown').forEach(d => d.classList.remove('show'));
      if (!isExpanded) dropdown.classList.add('show');
    });
    
    container.appendChild(button);
    container.appendChild(dropdown);
    player.appendChild(container);
  }

  function createDropdown() {
    const dropdown = document.createElement('div');
    dropdown.className = 'yt-dlp-dropdown';
    dropdown.addEventListener('click', e => e.stopPropagation());
    
    const fragment = document.createDocumentFragment();

    const qualities = [
      { value: 'audio', label: '🎵 Audio Only' },
      { value: '2160', label: '4K (2160p)' },
      { value: '1440', label: '2K (1440p)' },
      { value: '1080', label: 'HD (1080p)', default: true },
      { value: '720', label: 'HD (720p)' },
      { value: '480', label: 'SD (480p)' },
      { value: '360', label: 'SD (360p)' }
    ];
    
    qualities.forEach(q => {
      const btn = document.createElement('button');
      btn.className = 'yt-dlp-option';
      btn.innerHTML = `<span class="option-label">${q.label}</span>${q.default ? '<span class="option-badge">Default</span>' : ''}`;
      
      btn.addEventListener('click', () => { 
        dropdown.classList.remove('show');
        handleQualitySelect(q.value); 
      });
      
      fragment.appendChild(btn);
    });
    
    fragment.appendChild(createSeparator());
    
    const togglesContainer = document.createElement('div');
    togglesContainer.className = 'yt-dlp-toggles';
    
    const buildToggle = (id, label, storageKey) => {
        const lbl = document.createElement('label');
        lbl.className = 'yt-dlp-toggle-label';
        lbl.innerHTML = `<input type="checkbox" id="${id}"><span class="toggle-text">${label}</span>`;
        const cb = lbl.querySelector('input');
        chrome.storage.sync.get([storageKey], res => { if (res[storageKey]) cb.checked = true; });
        cb.addEventListener('change', e => chrome.storage.sync.set({ [storageKey]: e.target.checked }));
        return lbl;
    };

    togglesContainer.appendChild(buildToggle('float-subs', 'Embed Subtitles', 'embedSubs'));
    
    const plToggle = buildToggle('float-playlist', 'Full Playlist', 'downloadPlaylist');
    togglesContainer.appendChild(plToggle);

    const plInputContainer = document.createElement('div');
    plInputContainer.className = 'yt-dlp-playlist-input-container';
    plInputContainer.innerHTML = `
        <input type="text" id="float-playlist-items" placeholder="e.g. 1-5, 8">
        <div class="helper-text">Specific videos (optional)</div>
    `;
    
    const playlistInputBox = plInputContainer.querySelector('input');

    chrome.storage.sync.get(['downloadPlaylist', 'playlistItems'], res => {
        if (!res.downloadPlaylist) plInputContainer.style.display = 'none';
        if (res.playlistItems) playlistInputBox.value = res.playlistItems;
    });

    plToggle.querySelector('input').addEventListener('change', e => {
        plInputContainer.style.display = e.target.checked ? 'block' : 'none';
    });

    // --- THE HOTKEY FIX ---
    // This physically stops YouTube from seeing your keystrokes while typing in the box.
    // It prevents the video from seeking and instantly cures the "lag".
    ['keydown', 'keyup', 'keypress'].forEach(eventType => {
        playlistInputBox.addEventListener(eventType, e => e.stopPropagation());
    });
    // ----------------------
    
    let saveTimeout;
    playlistInputBox.addEventListener('input', e => {
        clearTimeout(saveTimeout);
        saveTimeout = setTimeout(() => {
            chrome.storage.sync.set({ playlistItems: e.target.value.trim() });
        }, 500);
    });
    
    togglesContainer.appendChild(plInputContainer);
    togglesContainer.appendChild(buildToggle('float-cookies', 'Bypass Age & Bot issue', 'useCookies'));
    
    fragment.appendChild(togglesContainer);
    dropdown.appendChild(fragment);
    
    document.addEventListener('click', () => dropdown.classList.remove('show'));
    
    return dropdown;
  }

  function createSeparator() {
      const sep = document.createElement('div');
      sep.className = 'yt-dlp-separator';
      return sep;
  }

  function sanitizePlaylistItems(rawInput) {
      return rawInput.toLowerCase()
          .replace(/\s+(to|through)\s+/g, '-')
          .replace(/[^0-9,\-]/g, '')
          .replace(/,+/g, ',')
          .replace(/(^,)|(,$)/g, '');
  }

  async function handleQualitySelect(resolution) {
    const wantsSubs = document.getElementById('float-subs')?.checked || false;
    const wantsPlaylist = document.getElementById('float-playlist')?.checked || false;
    const wantsCookies = document.getElementById('float-cookies')?.checked || false;
    const wantsItems = document.getElementById('float-playlist-items')?.value.trim() || '';
    
    try {
      await launchDownload(resolution, wantsSubs, wantsPlaylist, wantsCookies, wantsItems);
      showToast('Download started! Check terminal window.');
    } catch (error) { showToast('Failed to start download.', 'error'); }
  }

  async function launchDownload(resolution, wantsSubs, wantsPlaylist, wantsCookies, playlistItems) {
    const videoUrl = window.location.href;
    const prefs = await chrome.storage.sync.get(['savePath', 'concurrentDownloads']);
    
    let saveDir = prefs.savePath ? prefs.savePath.trim().replace(/[/\\]$/, '') + '\\' : '~/Downloads/YT-Downloads/';
    saveDir = saveDir.replace(/\\/g, '\\\\');

    const speed = prefs.concurrentDownloads || '4';

    const fmt = resolution === 'audio' 
        ? { f: 'ba', opt: '--extract-audio --audio-format mp3 --audio-quality 0', ext: 'mp3' }
        : { f: `bv*[height<=${resolution}]+ba/b[height<=${resolution}]/bv*+ba/b`, opt: '--merge-output-format mp4', ext: 'mp4' };
    
    const resLabel = resolution === 'audio' ? 'Audio' : `${resolution}p`;
    const subCmd = (wantsSubs && resolution !== 'audio') ? '--write-subs --write-auto-subs --embed-subs --sub-langs "en.*" ' : '';
    
    let plCmd = '--no-playlist ';
    if (wantsPlaylist) {
        plCmd = '--yes-playlist ';
        if (playlistItems) {
            const clean = sanitizePlaylistItems(playlistItems);
            if (clean) plCmd += `--playlist-items "${clean}" `;
        }
    }
    
    const outTemplate = wantsPlaylist
      ? `-o "${saveDir}%(playlist_title)s/%(playlist_index)03d_%(title)s_${resLabel}.${fmt.ext}"`
      : `-o "${saveDir}%(title)s_${resLabel}.${fmt.ext}"`;
    
   const command = ['yt-dlp', '-f', `"${fmt.f}"`, fmt.opt, `-N ${speed}`, subCmd, plCmd, '--sponsorblock-remove all', '--restrict-filenames', '--embed-metadata', '--embed-thumbnail', '--progress', outTemplate, `"${videoUrl}"`].join(' ');
    
    let encoded = btoa(unescape(encodeURIComponent(command)));
    if (wantsCookies) {
        const cookies = await new Promise(res => chrome.runtime.sendMessage({ action: "get_cookies" }, res));
        if (cookies) encoded += `||${cookies}`;
    }
    window.location.href = `ytdlp://${encoded}`;
  }

  function showToast(msg, type = 'success') {
    const existing = document.querySelector('.yt-dlp-toast');
    if (existing) existing.remove();
    const t = document.createElement('div');
    t.className = `yt-dlp-toast ${type}`; t.textContent = msg;
    document.body.appendChild(t);
    requestAnimationFrame(() => t.classList.add('show'));
    setTimeout(() => { t.classList.remove('show'); setTimeout(() => t.remove(), 300); }, 3000);
  }

  initialize();
})();