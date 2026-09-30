/**
 * @fileoverview Popup UI Controller
 * Site-agnostic: detects the active tab's site via sites.js's registry and
 * gates the UI (quality options, subtitles, playlist, sponsor, compat mode)
 * to whatever that site actually supports.
 * @version 6.2.0
 */

(function() {
  'use strict';

  const elements = {
    updateNotice: document.getElementById('updateNotice'),
    siteBanner: document.getElementById('siteBanner'),
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
    compatMode: document.getElementById('compatMode'),
    customCommand: document.getElementById('customCommand'),
    concurrentDownloads: document.getElementById('concurrentDownloads'),
    siteManagerList: document.getElementById('siteManagerList'),

    status: document.getElementById('status')
  };

  let currentSite = null;      // the YTDLP_SITES entry matching the active tab's URL, or null
  let currentSiteEnabled = false;

  function isNewerVersion(latest, current) {
    const latestParts = latest.split('.').map(n => parseInt(n, 10) || 0);
    const currentParts = current.split('.').map(n => parseInt(n, 10) || 0);
    const len = Math.max(latestParts.length, currentParts.length);
    for (let i = 0; i < len; i++) {
      const l = latestParts[i] || 0;
      const c = currentParts[i] || 0;
      if (l > c) return true;
      if (l < c) return false;
    }
    return false;
  }

  function initialize() {
    checkForUpdates();
    loadSavedPreferences();
    attachEventListeners();
    validateCurrentTab();
    renderSiteManager();
  }

  async function checkForUpdates() {
    try {
      const currentVersion = chrome.runtime.getManifest().version;
      const res = await fetch('https://api.github.com/repos/muzammilAwan-dev/yt-downloader-pro/releases/latest');
      if (!res.ok) return;
      const data = await res.json();
      const latestVersion = data.tag_name.replace('v', '');

      if (isNewerVersion(latestVersion, currentVersion)) {
        elements.updateNotice.innerHTML = `Update available: v${latestVersion}! <a href="${data.html_url}" target="_blank">Download here</a>`;
        elements.updateNotice.style.display = 'block';
      }
    } catch (e) { /* Fail silently */ }
  }

  async function loadSavedPreferences() {
    try {
      const prefs = await chrome.storage.sync.get([
        'savePath', 'resolution', 'audioFormat', 'embedSubs', 'downloadPlaylist',
        'playlistItems', 'useCookies', 'concurrentDownloads',
        'flagMetadata', 'flagThumbnail', 'flagSponsor', 'compatMode'
      ]);

      if (prefs.savePath) elements.savePath.value = prefs.savePath;

      if (prefs.resolution) {
          elements.resolution.value = prefs.resolution;
          toggleAudioDropdown(prefs.resolution);
      }
      if (prefs.audioFormat) elements.audioFormat.value = prefs.audioFormat;

      elements.flagMetadata.checked = prefs.flagMetadata === true;
      elements.flagThumbnail.checked = prefs.flagThumbnail !== false;
      elements.flagSponsor.checked = prefs.flagSponsor === true;
      elements.compatMode.checked = prefs.compatMode === true;

      elements.concurrentDownloads.value = prefs.concurrentDownloads || "4";

      if (prefs.embedSubs) elements.subsToggle.checked = prefs.embedSubs;
      if (prefs.downloadPlaylist) {
          elements.playlistToggle.checked = prefs.downloadPlaylist;
          elements.playlistOptions.style.display = 'block';
      }
      if (prefs.playlistItems) elements.playlistItems.value = prefs.playlistItems;
      if (prefs.useCookies) elements.cookiesToggle.checked = prefs.useCookies;
    } catch (error) { }
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
        flagSponsor: elements.flagSponsor.checked,
        compatMode: elements.compatMode.checked
      });
    } catch (error) { }
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

    [elements.savePath, elements.audioFormat, elements.subsToggle, elements.playlistItems, elements.cookiesToggle, elements.concurrentDownloads, elements.flagMetadata, elements.flagThumbnail, elements.flagSponsor, elements.compatMode]
      .forEach(el => el.addEventListener('change', savePreferences));
  }

  // --- Site detection + feature gating (replaces the old YouTube-only isValidYouTubeUrl gate) ---

  async function validateCurrentTab() {
    try {
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      currentSite = (typeof getSiteForUrl === 'function') ? getSiteForUrl(tab?.url) : null;

      if (!currentSite) {
        currentSiteEnabled = false;
        showSiteBanner('unsupported', `This page isn't a supported site yet. See "Supported Sites" below for what's available.`);
        elements.downloadBtn.disabled = true;
        return;
      }

      const { disabledSites = [] } = await chrome.storage.sync.get('disabledSites');
      currentSiteEnabled = !disabledSites.includes(currentSite.id);

      if (!currentSiteEnabled) {
        showSiteBanner('disabled', `${currentSite.label} support is turned off in Settings.`, {
          label: `Turn on`,
          onClick: async () => {
            await chrome.runtime.sendMessage({ action: 'set_site_enabled', site: currentSite.id, enabled: true });
            currentSiteEnabled = true;
            hideSiteBanner();
            elements.downloadBtn.disabled = false;
            applyFeatureGating(currentSite);
            renderSiteManager();
            showStatus(`${currentSite.label} turned on. Reload the page for the on-page button to appear.`, 'success');
          }
        });
        elements.downloadBtn.disabled = true;
        return;
      }

      hideSiteBanner();
      elements.downloadBtn.disabled = false;
      applyFeatureGating(currentSite);
    } catch (error) {
      showStatus('Unable to access current tab', 'error');
    }
  }

  function showSiteBanner(kind, text, action) {
    const el = elements.siteBanner;
    el.className = kind;
    el.style.display = kind === 'disabled' ? 'flex' : 'block';
    el.innerHTML = '';
    const span = document.createElement('span');
    span.textContent = text;
    el.appendChild(span);
    if (action) {
      const btn = document.createElement('button');
      btn.className = 'enable-btn';
      btn.textContent = action.label;
      btn.addEventListener('click', action.onClick);
      el.appendChild(btn);
    }
  }

  function hideSiteBanner() {
    elements.siteBanner.style.display = 'none';
    elements.siteBanner.innerHTML = '';
  }

  /**
   * Hides UI for yt-dlp features the current site doesn't meaningfully
   * support (e.g. SponsorBlock only has YouTube data; most sites have no
   * real subtitle tracks) instead of showing a control that would just
   * silently do nothing when the command runs.
   */
  function applyFeatureGating(site) {
    const subsRow = elements.subsToggle.closest('.toggle-group');
    if (subsRow) subsRow.style.display = site.features.subtitles ? '' : 'none';

    const playlistRow = elements.playlistToggle.closest('.toggle-group');
    if (playlistRow) playlistRow.style.display = site.features.playlist ? '' : 'none';
    if (!site.features.playlist) elements.playlistOptions.style.display = 'none';

    const sponsorRow = elements.flagSponsor.closest('.adv-checkbox-label');
    if (sponsorRow) sponsorRow.style.display = site.features.sponsorBlock ? '' : 'none';

    const compatRow = elements.compatMode.closest('.adv-checkbox-label');
    if (compatRow) compatRow.style.display = site.features.compatMode ? '' : 'none';

    // Audio-only sites (e.g. SoundCloud): collapse the quality picker down
    // to just the audio option, there is no video track to choose from.
    Array.from(elements.resolution.options).forEach(opt => {
      if (opt.value === 'audio' || opt.value === 'custom') { opt.hidden = false; return; }
      if (site.audioOnly) { opt.hidden = true; return; }
      if (opt.value === '2160') { opt.hidden = !site.features.quality2160; return; }
      if (opt.value === '1440') { opt.hidden = !site.features.quality1440; return; }
      opt.hidden = false;
    });
    if (site.audioOnly && elements.resolution.value !== 'audio' && elements.resolution.value !== 'custom') {
      elements.resolution.value = 'audio';
      toggleAudioDropdown('audio');
    }
  }

  async function handleDownload() {
    const btn = elements.downloadBtn;
    const originalText = btn.innerHTML;
    try {
      if (!currentSite || !currentSiteEnabled) throw new Error('This site is not enabled. See Settings.');
      btn.disabled = true;
      btn.innerHTML = `<svg viewBox="0 0 24 24" style="animation: spin 1s linear infinite"><path d="M12 2v4m0 12v4M4.93 4.93l2.83 2.83m8.48 8.48l2.83 2.83M2 12h4m12 0h4M4.93 19.07l2.83-2.83m8.48-8.48l2.83-2.83"/></svg> Processing...`;
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });

      await executeCommand(buildYtDlpCommand(tab.url.split('&')[0]));

      // AUTO-UNCHECK FIX: Silently reset highly-situational toggles
      elements.cookiesToggle.checked = false;
      elements.playlistToggle.checked = false;

      // ADDED: Clears crop times since they are strictly video-specific
      elements.startTime.value = '';
      elements.endTime.value = '';

      await chrome.storage.sync.set({
          useCookies: false,
          downloadPlaylist: false,
          useCrop: false
      });

      showStatus('Download started! If Chrome asks to open YT Downloader Pro, click Open - the app will close this popup automatically.', 'success');
      // FIX: this used to auto-close the popup after 2s. Closing the popup
      // window can tear down Chrome's pending "Open App?" confirmation
      // before the user has a chance to click it, silently cancelling the
      // launch - which matches exactly what was being reported (the
      // prompt "disappearing" when using the toolbar icon specifically).
      // Leaving the popup open costs a little convenience; losing the
      // launch silently costs a lot more.
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

    const site = currentSite;
    const settings = {
      savePath: elements.savePath.value.trim(),
      resolution: elements.resolution.value,
      audioFormat: elements.audioFormat.value,
      embedSubs: elements.subsToggle.checked && site.features.subtitles,
      isPlaylist: elements.playlistToggle.checked && site.features.playlist,
      playlistItems: elements.playlistItems.value.trim(),
      concurrentDownloads: elements.concurrentDownloads.value || "4"
    };

    let saveDir = settings.savePath || '~/Downloads/YTDownloaderPro/';
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
        ? '--write-subs --write-auto-subs --embed-subs --sub-langs "en.*" --sleep-subtitles 5 '
        : '';

    let playlistOptions = '--no-playlist ';
    if (settings.isPlaylist) {
        playlistOptions = '--yes-playlist ';
        if (settings.playlistItems) {
            let cleanItems = settings.playlistItems.toLowerCase().replace(/\s+(to|through)\s+/g, '-').replace(/[^0-9,\-]/g, '').replace(/,+/g, ',').replace(/(^,)|(,$)/g, '');
            if (cleanItems) playlistOptions += `--playlist-items "${cleanItems}" `;
        }
    }

    const metaFlag = (elements.flagMetadata.checked && site.features.metadata) ? '--embed-metadata' : '';

    const thumbFlag = (elements.flagThumbnail.checked && site.features.thumbnail && formatConfig.extension !== 'wav')
        ? '--embed-thumbnail'
        : '';

    const sponsorFlag = (elements.flagSponsor.checked && site.features.sponsorBlock) ? '--sponsorblock-remove all' : '';

    const compatFlag = (elements.compatMode.checked && site.features.compatMode) ? '-S "vcodec:h264,res,acodec:m4a"' : '';

    return [
      'yt-dlp',
      '-f', `"${formatConfig.formatString}"`,
      compatFlag,
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
            chrome.runtime.sendMessage({ action: "get_cookies", site: currentSite?.id }, (response) => {
                if (chrome.runtime.lastError) {
                    return reject(new Error("Cookie error. Please refresh the page."));
                }
                resolve(response);
            });
        });
        if (cookieBase64) encodedCommand += `||${cookieBase64}`;
    }

    const protocolUrl = `ytdlp://${encodeURIComponent(encodedCommand)}`;

    const iframe = document.createElement('iframe');
    iframe.src = protocolUrl;
    iframe.style.display = 'none';
    document.body.appendChild(iframe);

    setTimeout(() => iframe.remove(), 4000);
  }

  // --- Supported Sites manager ---

  async function renderSiteManager() {
    const list = elements.siteManagerList;
    if (!list) return;
    list.innerHTML = '<div style="font-size:12px;color:var(--text-muted);">Loading...</div>';

    let sites;
    try {
      sites = await chrome.runtime.sendMessage({ action: 'list_sites' });
    } catch {
      list.innerHTML = '<div style="font-size:12px;color:var(--text-muted);">Could not load site list.</div>';
      return;
    }

    list.innerHTML = '';
    (sites || []).forEach(site => {
      const row = document.createElement('div');
      row.className = 'site-row';

      const labelWrap = document.createElement('div');
      const label = document.createElement('div');
      label.className = 'site-label';
      label.textContent = site.label;
      labelWrap.appendChild(label);
      if (site.note) {
        const note = document.createElement('div');
        note.className = 'site-note';
        note.textContent = site.note;
        labelWrap.appendChild(note);
      }
      row.appendChild(labelWrap);

      if (site.id === 'youtube') {
        const badge = document.createElement('span');
        badge.className = 'site-builtin-badge';
        badge.textContent = 'Always on';
        row.appendChild(badge);
      } else {
        const switchLabel = document.createElement('label');
        switchLabel.className = 'switch';
        const input = document.createElement('input');
        input.type = 'checkbox';
        input.checked = site.enabled;
        input.setAttribute('aria-label', `Toggle ${site.label} support`);
        const slider = document.createElement('span');
        slider.className = 'slider';
        switchLabel.appendChild(input);
        switchLabel.appendChild(slider);

        input.addEventListener('change', async (e) => {
          const wantEnabled = e.target.checked;
          input.disabled = true;
          try {
            await chrome.runtime.sendMessage({ action: 'set_site_enabled', site: site.id, enabled: wantEnabled });
            await validateCurrentTab(); // refresh banner/gating if this was the active site
          } finally {
            input.disabled = false;
          }
        });

        row.appendChild(switchLabel);
      }

      list.appendChild(row);
    });
  }

  function showStatus(msg, type = 'info') {
    const el = elements.status;
    el.textContent = msg; el.className = `status ${type} show`;
    if (type === 'success') setTimeout(() => el.classList.remove('show'), 5000);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize);
  else initialize();
})();