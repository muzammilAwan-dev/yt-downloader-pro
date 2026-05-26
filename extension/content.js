/**
 * @fileoverview DOM Injection Controller
 * Integrates Auto-Uncheck flows for highly situational variables.
 * @version 6.1.2
 */

(function() {
  'use strict';

  const CONFIG = {
    CONTAINER_ID: 'yt-dlp-container',
    CHECK_INTERVAL: 1000
  };

  function initialize() {
    setInterval(enforceButtonPresence, CONFIG.CHECK_INTERVAL);
    enforceButtonPresence();
  }

  function enforceButtonPresence() {
    const path = window.location.pathname;
    const isWatchPage = path.startsWith('/watch');
    const isShortsPage = path.startsWith('/shorts');

    const existingButton = document.getElementById(CONFIG.CONTAINER_ID);

    if (!isWatchPage && !isShortsPage) {
        if (existingButton) existingButton.remove();
        return;
    }

    if (isWatchPage) {
        const player = document.querySelector('#movie_player');
        if (player) {
            if (existingButton && existingButton.parentElement !== player) {
                existingButton.remove();
                injectDownloadButton(player, false);
            } else if (!existingButton) {
                injectDownloadButton(player, false);
            }
        }
    } else if (isShortsPage) {
        if (existingButton && existingButton.parentElement !== document.body) {
            existingButton.remove();
            injectDownloadButton(document.body, true);
        } else if (!existingButton) {
            injectDownloadButton(document.body, true);
        }
    }
  }

  function injectDownloadButton(player, isShortsPage) {
    const container = document.createElement('div');
    container.id = CONFIG.CONTAINER_ID;
    
    if (isShortsPage) container.classList.add('shorts-mode');
    
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

    const customBtn = document.createElement('button');
    customBtn.className = 'yt-dlp-option custom-cmd';
    customBtn.innerHTML = `<span>⚙️ Run Custom Command</span>`;
    customBtn.addEventListener('click', () => { 
        dropdown.classList.remove('show');
        handleQualitySelect('custom'); 
    });
    fragment.appendChild(customBtn);

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

    const audioContainer = document.createElement('div');
    audioContainer.className = 'yt-dlp-input-container';
    audioContainer.innerHTML = `<label>Audio Format</label><select id="float-audio-format"><option value="mp3">MP3</option><option value="flac">FLAC</option><option value="wav">WAV</option><option value="m4a">M4A</option></select>`;
    const audioSelect = audioContainer.querySelector('select');
    chrome.storage.sync.get(['audioFormat'], res => { if(res.audioFormat) audioSelect.value = res.audioFormat; });
    audioSelect.addEventListener('change', e => chrome.storage.sync.set({ audioFormat: e.target.value }));
    togglesContainer.appendChild(audioContainer);

    togglesContainer.appendChild(buildToggle('float-subs', 'Embed Subtitles', 'embedSubs'));
    
    const cropToggle = buildToggle('float-crop', 'Crop Video section', 'useCrop');
    togglesContainer.appendChild(cropToggle);

    const timeContainer = document.createElement('div');
    timeContainer.className = 'yt-dlp-input-container';
    timeContainer.style.display = 'none';
    timeContainer.innerHTML = `
        <div class="yt-dlp-time-row">
            <input type="text" id="float-start-time" placeholder="Start (MM:SS)">
            <input type="text" id="float-end-time" placeholder="End (MM:SS)">
        </div>
    `;
    const startBox = timeContainer.querySelector('#float-start-time');
    const endBox = timeContainer.querySelector('#float-end-time');
    
    cropToggle.querySelector('input').addEventListener('change', e => {
        timeContainer.style.display = e.target.checked ? 'block' : 'none';
    });
    togglesContainer.appendChild(timeContainer);

    ['keydown', 'keyup', 'keypress'].forEach(eventType => {
        startBox.addEventListener(eventType, e => e.stopPropagation());
        endBox.addEventListener(eventType, e => e.stopPropagation());
    });

    const plToggle = buildToggle('float-playlist', 'Full Playlist', 'downloadPlaylist');
    togglesContainer.appendChild(plToggle);

    const plInputContainer = document.createElement('div');
    plInputContainer.className = 'yt-dlp-input-container';
    plInputContainer.innerHTML = `<input type="text" id="float-playlist-items" placeholder="e.g. 1-5, 8"><div class="helper-text">Specific videos (optional)</div>`;
    const playlistInputBox = plInputContainer.querySelector('input');

    chrome.storage.sync.get(['downloadPlaylist', 'playlistItems'], res => {
        if (!res.downloadPlaylist) plInputContainer.style.display = 'none';
        if (res.playlistItems) playlistInputBox.value = res.playlistItems;
    });

    plToggle.querySelector('input').addEventListener('change', e => {
        plInputContainer.style.display = e.target.checked ? 'block' : 'none';
    });

    ['keydown', 'keyup', 'keypress'].forEach(eventType => playlistInputBox.addEventListener(eventType, e => e.stopPropagation()));
    
    let saveTimeout;
    playlistInputBox.addEventListener('input', e => {
        clearTimeout(saveTimeout);
        saveTimeout = setTimeout(() => chrome.storage.sync.set({ playlistItems: e.target.value.trim() }), 500);
    });
    togglesContainer.appendChild(plInputContainer);
    
    togglesContainer.appendChild(buildToggle('float-cookies', 'Bypass Age & Bot Lock', 'useCookies'));
    
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
      return rawInput.toLowerCase().replace(/\s+(to|through)\s+/g, '-').replace(/[^0-9,\-]/g, '').replace(/,+/g, ',').replace(/(^,)|(,$)/g, '');
  }

  async function handleQualitySelect(resolution) {
    const wantsSubs = document.getElementById('float-subs')?.checked || false;
    const wantsPlaylist = document.getElementById('float-playlist')?.checked || false;
    const wantsCookies = document.getElementById('float-cookies')?.checked || false;
    const wantsItems = document.getElementById('float-playlist-items')?.value.trim() || '';
    
    const isCropped = document.getElementById('float-crop')?.checked || false;
    const sTime = document.getElementById('float-start-time')?.value.trim() || '';
    const eTime = document.getElementById('float-end-time')?.value.trim() || '';

    try {
      await launchDownload(resolution, wantsSubs, wantsPlaylist, wantsCookies, wantsItems, isCropped, sTime, eTime);
      
      // AUTO-UNCHECK FIX: Visually clear situational checkboxes
      const cookiesToggle = document.getElementById('float-cookies');
      const playlistToggle = document.getElementById('float-playlist');
      const cropToggle = document.getElementById('float-crop');
      const startInput = document.getElementById('float-start-time');
      const endInput = document.getElementById('float-end-time');

      if (cookiesToggle) cookiesToggle.checked = false;
      
      if (playlistToggle) {
          playlistToggle.checked = false;
          const plInputContainer = document.getElementById('float-playlist-items')?.parentElement;
          if (plInputContainer) plInputContainer.style.display = 'none';
      }
      
      if (cropToggle) {
          cropToggle.checked = false;
          const timeContainer = document.querySelector('.yt-dlp-time-row')?.parentElement;
          if (timeContainer) timeContainer.style.display = 'none';
      }
      
      if (startInput) startInput.value = '';
      if (endInput) endInput.value = '';
      
      // Reset persistent memory for situational toggles
      chrome.storage.sync.set({ 
          useCookies: false, 
          downloadPlaylist: false, 
          useCrop: false 
      });

      showToast('Download started! Check the application.');
    } catch (error) { 
      showToast(`Error: ${error.message}`, 'error'); 
    }
  }

  async function launchDownload(resolution, wantsSubs, wantsPlaylist, wantsCookies, playlistItems, isCropped, sTime, eTime) {
    const videoUrl = window.location.href.split('&')[0]; 
    const prefs = await chrome.storage.sync.get(['savePath', 'concurrentDownloads', 'customCommand', 'audioFormat', 'flagMetadata', 'flagThumbnail', 'flagSponsor', 'compatMode']);
    
    if (resolution === 'custom') {
        const rawCustom = prefs.customCommand ? prefs.customCommand.trim() : '';
        let finalCustom = rawCustom ? rawCustom : 'yt-dlp';
        if (!finalCustom.includes(videoUrl)) finalCustom += ` "${videoUrl}"`;
        await executeFinalCommand(finalCustom, wantsCookies);
        return;
    }

    let saveDir = prefs.savePath ? prefs.savePath.trim().replace(/[/\\]$/, '') + '\\' : '~/Downloads/YT-Downloads/';
    saveDir = saveDir.replace(/\\/g, '\\\\');
    const speed = prefs.concurrentDownloads || '4'; 
    const audioFmt = prefs.audioFormat || 'mp3';

    let fmt;
    if (resolution === 'audio') {
        let quality = audioFmt === 'flac' || audioFmt === 'wav' ? '' : '--audio-quality 0';
        fmt = { f: 'ba', opt: `--extract-audio --audio-format ${audioFmt} ${quality}`.trim(), ext: audioFmt };
    } else if (resolution === '1440' || resolution === '2160') {
        fmt = { f: `bv*[height<=${resolution}]+ba/b[height<=${resolution}]/bv*+ba/b`, opt: '--merge-output-format mkv', ext: 'mkv' };
    } else {
        fmt = { f: `bv*[height<=${resolution}]+ba/b[height<=${resolution}]/bv*+ba/b`, opt: '--merge-output-format mp4', ext: 'mp4' };
    }
    
    const resLabel = resolution === 'audio' ? 'Audio' : `${resolution}p`;
    
    let cropperCmd = '';
    if (isCropped && (sTime || eTime)) {
        const startSec = sTime ? sTime : '00:00:00';
        const endSec = eTime ? eTime : 'inf';
        cropperCmd = `--download-sections "*${startSec}-${endSec}" --force-keyframes-at-cuts `;
    }

    const subCmd = (wantsSubs && resolution !== 'audio') 
        ? '--write-subs --write-auto-subs --embed-subs --sub-langs "en.*" --sleep-subtitles 2 ' 
        : '';
    
    let plCmd = '--no-playlist ';
    if (wantsPlaylist) {
        plCmd = '--yes-playlist ';
        if (playlistItems) {
            const clean = sanitizePlaylistItems(playlistItems);
            if (clean) plCmd += `--playlist-items "${clean}" `;
        }
    }

    const metaFlag = (prefs.flagMetadata === true) ? '--embed-metadata' : '';
    
    const thumbFlag = (prefs.flagThumbnail !== false && fmt.ext !== 'wav') 
        ? '--embed-thumbnail' 
        : '';
        
    const sponsorFlag = (prefs.flagSponsor === true) ? '--sponsorblock-remove all' : '';
    
    const compatFlag = (prefs.compatMode === true) ? '-S "vcodec:h264,res,acodec:m4a"' : '';
    
    const outTemplate = wantsPlaylist
      ? `-o "${saveDir}%(playlist_title)s/%(playlist_index)03d_%(title)s_${resLabel}.${fmt.ext}"`
      : `-o "${saveDir}%(title)s_${resLabel}.${fmt.ext}"`;
    
    const command = ['yt-dlp', '-f', `"${fmt.f}"`, compatFlag, fmt.opt, `-N ${speed}`, cropperCmd, subCmd, plCmd, sponsorFlag, '--restrict-filenames', metaFlag, thumbFlag, '--no-warnings', '--progress', outTemplate, `"${videoUrl}"`].filter(Boolean).join(' ');
    
    await executeFinalCommand(command, wantsCookies);
  }

  async function executeFinalCommand(command, wantsCookies) {
      let encoded = btoa(unescape(encodeURIComponent(command)));
      if (wantsCookies) {
          const cookies = await new Promise((resolve, reject) => {
              chrome.runtime.sendMessage({ action: "get_cookies" }, (response) => {
                  if (chrome.runtime.lastError) {
                      return reject(new Error("Cookie mapping failed. Please refresh."));
                  }
                  resolve(response);
              });
          });
          if (cookies) encoded += `||${cookies}`;
      }
      
      const protocolUrl = `ytdlp://${encodeURIComponent(encoded)}`;
      
      const iframe = document.createElement('iframe');
      iframe.src = protocolUrl;
      iframe.style.display = 'none';
      document.body.appendChild(iframe);
      
      setTimeout(() => iframe.remove(), 1000);
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
